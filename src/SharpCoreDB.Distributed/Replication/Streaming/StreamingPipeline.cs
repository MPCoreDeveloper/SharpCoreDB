// <copyright file="StreamingPipeline.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Orchestrates the end-to-end WAL streaming pipeline.
/// Manages the flow from WAL reading to replica transmission.
/// C# 14: Async streams, Channel<T> coordination, primary constructors.
/// </summary>
public sealed class StreamingPipeline : IAsyncDisposable
{
    private readonly Dictionary<string, StreamingSession> _sessions = [];
    private readonly WALPositionTracker _positionTracker;
    private readonly ILogger<StreamingPipeline>? _logger;

    private readonly Channel<StreamingCommand> _commandChannel = Channel.CreateBounded<StreamingCommand>(100);
    private readonly CancellationTokenSource _cts = new();

    private Task? _pipelineTask;
    private bool _isRunning;

    /// <summary>Gets the number of active sessions.</summary>
    public int ActiveSessionCount => _sessions.Count(s => s.Value.IsActive);

    /// <summary>Gets all session identifiers.</summary>
    public IReadOnlyCollection<string> SessionIds => [.. _sessions.Keys];

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingPipeline"/> class.
    /// </summary>
    /// <param name="positionTracker">The position tracker to use.</param>
    /// <param name="logger">Optional logger.</param>
    public StreamingPipeline(WALPositionTracker positionTracker, ILogger<StreamingPipeline>? logger = null)
    {
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _logger = logger;
    }

    /// <summary>
    /// Starts the streaming pipeline.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartPipelineAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;

        _logger?.LogInformation("Starting streaming pipeline");

        // Start command processor
        _pipelineTask = ProcessCommandsAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the streaming pipeline.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopPipelineAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _logger?.LogInformation("Stopping streaming pipeline");

        _isRunning = false;
        _cts.Cancel();

        // Stop all sessions
        var stopTasks = _sessions.Values.Select(s => s.StopSessionAsync()).ToArray();
        await Task.WhenAll(stopTasks);

        // Wait for pipeline task
        if (_pipelineTask is not null)
        {
            await _pipelineTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        _commandChannel.Writer.Complete();
    }

    /// <summary>
    /// Adds a new streaming session for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <param name="replicationState">The replication state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddSessionAsync(
        string replicaId,
        string walFilePath,
        ReplicationState replicationState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(walFilePath);

        var command = new StreamingCommand
        {
            Type = StreamingCommandType.AddSession,
            ReplicaId = replicaId,
            WalFilePath = walFilePath,
            ReplicationState = replicationState
        };

        await _commandChannel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <summary>
    /// Removes a streaming session.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveSessionAsync(string replicaId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        var command = new StreamingCommand
        {
            Type = StreamingCommandType.RemoveSession,
            ReplicaId = replicaId
        };

        await _commandChannel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <summary>
    /// Acknowledges a position for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="position">The acknowledged position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AcknowledgePositionAsync(
        string replicaId,
        WALPosition position,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        var command = new StreamingCommand
        {
            Type = StreamingCommandType.AcknowledgePosition,
            ReplicaId = replicaId,
            Position = position
        };

        await _commandChannel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <summary>
    /// Gets a streaming session by replica ID.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <returns>The streaming session, or null if not found.</returns>
    public StreamingSession? GetSession(string replicaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        return _sessions.TryGetValue(replicaId, out var session) ? session : null;
    }

    /// <summary>
    /// Gets all streaming sessions.
    /// </summary>
    /// <returns>Collection of all sessions.</returns>
    public IReadOnlyCollection<StreamingSession> GetAllSessions()
    {
        return [.. _sessions.Values];
    }

    /// <summary>
    /// Gets pipeline statistics.
    /// </summary>
    /// <returns>Pipeline statistics.</returns>
    public StreamingPipelineStats GetStats()
    {
        var sessionStats = _sessions.Values.Select(s => s.GetStats()).ToArray();

        return new StreamingPipelineStats
        {
            IsRunning = _isRunning,
            TotalSessions = _sessions.Count,
            ActiveSessions = sessionStats.Count(s => s.IsActive),
            HealthySessions = sessionStats.Count(s => s.IsHealthy),
            SessionStats = sessionStats,
            PositionStats = _positionTracker.GetStats()
        };
    }

    /// <summary>
    /// Processes streaming commands asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessCommandsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in _commandChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessCommandAsync(command, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing streaming commands");
        }
    }

    /// <summary>
    /// Processes a single streaming command.
    /// </summary>
    /// <param name="command">The command to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessCommandAsync(StreamingCommand command, CancellationToken cancellationToken)
    {
        try
        {
            switch (command.Type)
            {
                case StreamingCommandType.AddSession:
                    await AddSessionInternalAsync(
                        command.ReplicaId!,
                        command.WalFilePath!,
                        command.ReplicationState!,
                        cancellationToken);
                    break;

                case StreamingCommandType.RemoveSession:
                    await RemoveSessionInternalAsync(command.ReplicaId!, cancellationToken);
                    break;

                case StreamingCommandType.AcknowledgePosition:
                    AcknowledgePositionInternal(command.ReplicaId!, command.Position!.Value);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing streaming command {CommandType} for replica {ReplicaId}",
                command.Type, command.ReplicaId);
        }
    }

    /// <summary>
    /// Adds a session internally.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <param name="replicationState">The replication state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddSessionInternalAsync(
        string replicaId,
        string walFilePath,
        ReplicationState replicationState,
        CancellationToken cancellationToken)
    {
        if (_sessions.ContainsKey(replicaId))
        {
            _logger?.LogWarning("Session for replica {ReplicaId} already exists", replicaId);
            return;
        }

        var session = StreamingSessionFactory.CreateSession(
            replicaId, walFilePath, _positionTracker, replicationState, (ILogger<StreamingSession>?)_logger);

        _sessions[replicaId] = session;

        await session.StartSessionAsync(cancellationToken);

        _logger?.LogInformation("Added streaming session for replica {ReplicaId}", replicaId);
    }

    /// <summary>
    /// Removes a session internally.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RemoveSessionInternalAsync(string replicaId, CancellationToken cancellationToken)
    {
        if (_sessions.Remove(replicaId, out var session))
        {
            await session.StopSessionAsync();
            _logger?.LogInformation("Removed streaming session for replica {ReplicaId}", replicaId);
        }
    }

    /// <summary>
    /// Acknowledges a position internally.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="position">The acknowledged position.</param>
    private void AcknowledgePositionInternal(string replicaId, WALPosition position)
    {
        if (_sessions.TryGetValue(replicaId, out var session))
        {
            session.AcknowledgePosition(position);
        }
    }

    /// <summary>
    /// Disposes the streaming pipeline asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopPipelineAsync();
        _cts.Dispose();
        _commandChannel.Writer.Complete();
    }
}

/// <summary>
/// Statistics for streaming pipeline operations.
/// </summary>
public class StreamingPipelineStats
{
    /// <summary>Gets whether the pipeline is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the total number of sessions.</summary>
    public int TotalSessions { get; init; }

    /// <summary>Gets the number of active sessions.</summary>
    public int ActiveSessions { get; init; }

    /// <summary>Gets the number of healthy sessions.</summary>
    public int HealthySessions { get; init; }

    /// <summary>Gets the individual session statistics.</summary>
    public IReadOnlyCollection<StreamingSessionStats> SessionStats { get; init; } = [];

    /// <summary>Gets the position tracking statistics.</summary>
    public WALPositionStats PositionStats { get; init; } = new();

    /// <summary>Gets the pipeline health status.</summary>
    public StreamingPipelineHealthStatus HealthStatus
    {
        get
        {
            if (!IsRunning) return StreamingPipelineHealthStatus.Stopped;
            if (HealthySessions == TotalSessions) return StreamingPipelineHealthStatus.Healthy;
            if (ActiveSessions > 0) return StreamingPipelineHealthStatus.Degraded;
            return StreamingPipelineHealthStatus.Unhealthy;
        }
    }
}

/// <summary>
/// Streaming pipeline health status.
/// </summary>
public enum StreamingPipelineHealthStatus
{
    /// <summary>Pipeline is stopped.</summary>
    Stopped,

    /// <summary>All sessions are healthy.</summary>
    Healthy,

    /// <summary>Some sessions are unhealthy but pipeline is operational.</summary>
    Degraded,

    /// <summary>Pipeline is unhealthy.</summary>
    Unhealthy
}

/// <summary>
/// Streaming command types.
/// </summary>
internal enum StreamingCommandType
{
    AddSession,
    RemoveSession,
    AcknowledgePosition
}

/// <summary>
/// Internal streaming command.
/// </summary>
internal class StreamingCommand
{
    public StreamingCommandType Type { get; init; }
    public string? ReplicaId { get; init; }
    public string? WalFilePath { get; init; }
    public ReplicationState? ReplicationState { get; init; }
    public WALPosition? Position { get; init; }
}
