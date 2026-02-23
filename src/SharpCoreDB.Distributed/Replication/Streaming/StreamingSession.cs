// <copyright file="StreamingSession.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Manages a streaming session for a single replica.
/// Handles connection lifecycle, heartbeat, and session state.
/// C# 14: Primary constructors, pattern matching, async streams.
/// </summary>
public sealed class StreamingSession : IAsyncDisposable
{
    private readonly WALStreamer _streamer;
    private readonly ReplicationState _replicationState;
    private readonly ILogger<StreamingSession>? _logger;

    private readonly PeriodicTimer _heartbeatTimer;
    private readonly CancellationTokenSource _cts = new();

    private Task? _sessionTask;
    private Task? _heartbeatTask;
    private bool _isActive;

    /// <summary>Gets the replica identifier for this session.</summary>
    public string ReplicaId => _replicationState.ReplicaNodeId;

    /// <summary>Gets whether the session is currently active.</summary>
    public bool IsActive => _isActive && !_cts.IsCancellationRequested;

    /// <summary>Gets the current session state.</summary>
    public ReplicationProtocol.ReplicationState State => _replicationState.State;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingSession"/> class.
    /// </summary>
    /// <param name="streamer">The WAL streamer for this session.</param>
    /// <param name="replicationState">The replication state.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="heartbeatInterval">Interval between heartbeats.</param>
    public StreamingSession(
        WALStreamer streamer,
        ReplicationState replicationState,
        ILogger<StreamingSession>? logger = null,
        TimeSpan? heartbeatInterval = null)
    {
        _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
        _replicationState = replicationState ?? throw new ArgumentNullException(nameof(replicationState));
        _logger = logger;

        var interval = heartbeatInterval ?? ReplicationProtocol.HeartbeatInterval;
        _heartbeatTimer = new PeriodicTimer(interval);
    }

    /// <summary>
    /// Starts the streaming session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _replicationState.ChangeState(ReplicationProtocol.ReplicationState.Starting);

        _logger?.LogInformation("Starting streaming session for replica {ReplicaId}", ReplicaId);

        // Start heartbeat monitoring
        _heartbeatTask = MonitorHeartbeatAsync(_cts.Token);

        // Start the streaming session
        _sessionTask = RunSessionAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the streaming session.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopSessionAsync()
    {
        if (!_isActive)
        {
            return;
        }

        _logger?.LogInformation("Stopping streaming session for replica {ReplicaId}", ReplicaId);

        _isActive = false;
        _cts.Cancel();

        // Stop streaming
        await _streamer.StopStreamingAsync();

        // Wait for tasks to complete
        var tasks = new List<Task>();
        if (_sessionTask is not null) tasks.Add(_sessionTask);
        if (_heartbeatTask is not null) tasks.Add(_heartbeatTask);

        await Task.WhenAll(tasks);

        _replicationState.ChangeState(ReplicationProtocol.ReplicationState.Stopped);
    }

    /// <summary>
    /// Acknowledges receipt of WAL entries up to the specified position.
    /// </summary>
    /// <param name="position">The acknowledged position.</param>
    public void AcknowledgePosition(WALPosition position)
    {
        _streamer.AcknowledgePosition(position);
        _replicationState.RecordAcknowledgment(position.Lsn, 1); // TODO: Calculate actual entry count
    }

    /// <summary>
    /// Gets session statistics.
    /// </summary>
    /// <returns>Session statistics.</returns>
    public StreamingSessionStats GetStats()
    {
        return new StreamingSessionStats
        {
            ReplicaId = ReplicaId,
            IsActive = IsActive,
            State = State,
            ReplicationStats = _replicationState.GetStatistics(),
            StreamerStats = _streamer.GetStats()
        };
    }

    /// <summary>
    /// Runs the streaming session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the session operation.</returns>
    private async Task RunSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Start streaming
            await _streamer.StartStreamingAsync(cancellationToken);

            _replicationState.ChangeState(ReplicationProtocol.ReplicationState.Streaming);

            // Keep session alive until cancelled
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in streaming session for replica {ReplicaId}", ReplicaId);
            _replicationState.RecordFailure(ex.Message);
        }
    }

    /// <summary>
    /// Monitors heartbeat and connection health.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the heartbeat monitoring operation.</returns>
    private async Task MonitorHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_isActive && !cancellationToken.IsCancellationRequested)
            {
                await _heartbeatTimer.WaitForNextTickAsync(cancellationToken);

                // Check if we should send a heartbeat or check connection health
                var timeSinceLastComm = DateTimeOffset.UtcNow - _replicationState.LastCommunication;

                if (timeSinceLastComm > ReplicationProtocol.HeartbeatInterval * 2)
                {
                    _logger?.LogWarning("No communication from replica {ReplicaId} for {Time}",
                        ReplicaId, timeSinceLastComm);

                    // If no communication for too long, mark as failed
                    if (timeSinceLastComm > TimeSpan.FromMinutes(5))
                    {
                        _replicationState.RecordFailure("Heartbeat timeout");
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in heartbeat monitoring for replica {ReplicaId}", ReplicaId);
        }
    }

    /// <summary>
    /// Handles session recovery after connection issues.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the recovery operation.</returns>
    public async Task RecoverSessionAsync(CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Attempting to recover session for replica {ReplicaId}", ReplicaId);

        try
        {
            // Reset failure state
            _replicationState.ChangeState(ReplicationProtocol.ReplicationState.Starting);

            // Restart the session
            await StartSessionAsync(cancellationToken);

            _logger?.LogInformation("Successfully recovered session for replica {ReplicaId}", ReplicaId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to recover session for replica {ReplicaId}", ReplicaId);
            _replicationState.RecordFailure($"Recovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes the streaming session asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopSessionAsync();
        _cts.Dispose();
        _heartbeatTimer.Dispose();
    }
}

/// <summary>
/// Statistics for streaming session operations.
/// </summary>
public class StreamingSessionStats
{
    /// <summary>Gets the replica identifier.</summary>
    public string ReplicaId { get; init; } = string.Empty;

    /// <summary>Gets whether the session is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets the current session state.</summary>
    public ReplicationProtocol.ReplicationState State { get; init; }

    /// <summary>Gets the replication statistics.</summary>
    public ReplicationStatistics ReplicationStats { get; init; } = new();

    /// <summary>Gets the streamer statistics.</summary>
    public WALStreamerStats StreamerStats { get; init; } = new();

    /// <summary>Gets the session uptime.</summary>
    public TimeSpan Uptime => ReplicationStats.Uptime;

    /// <summary>Gets whether the session is healthy.</summary>
    public bool IsHealthy => IsActive && ReplicationStats.IsInSync && ReplicationStats.ConsecutiveFailures == 0;
}

/// <summary>
/// Factory for creating streaming sessions.
/// </summary>
public static class StreamingSessionFactory
{
    /// <summary>
    /// Creates a new streaming session for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <param name="positionTracker">The position tracker.</param>
    /// <param name="replicationState">The replication state.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A new streaming session instance.</returns>
    public static StreamingSession CreateSession(
        string replicaId,
        string walFilePath,
        WALPositionTracker positionTracker,
        ReplicationState replicationState,
        ILogger<StreamingSession>? logger = null)
    {
        var streamer = WALStreamerFactory.CreateStreamer(replicaId, walFilePath, positionTracker);
        return new StreamingSession(streamer, replicationState, logger);
    }
}
