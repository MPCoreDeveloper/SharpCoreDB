// <copyright file="ShardMonitor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Sharding;

/// <summary>
/// Continuously monitors shard health and handles failover scenarios.
/// C# 14: PeriodicTimer, async streams, lock keyword.
/// </summary>
public sealed class ShardMonitor : IAsyncDisposable
{
    private readonly ShardManager _shardManager;
    private readonly ShardOperations _shardOperations;
    private readonly ILogger<ShardMonitor>? _logger;

    private readonly PeriodicTimer _healthCheckTimer;
    private readonly Lock _monitorLock = new();
    private readonly CancellationTokenSource _cts = new();

    private Task? _monitoringTask;
    private bool _isMonitoring;

    /// <summary>
    /// Gets the health check interval.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; }

    /// <summary>
    /// Gets the failover timeout.
    /// </summary>
    public TimeSpan FailoverTimeout { get; }

    /// <summary>
    /// Occurs when a shard status changes.
    /// </summary>
    public event EventHandler<ShardStatusChangedEventArgs>? ShardStatusChanged;

    /// <summary>
    /// Occurs when a failover operation is initiated.
    /// </summary>
    public event EventHandler<ShardFailoverEventArgs>? ShardFailoverInitiated;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardMonitor"/> class.
    /// </summary>
    /// <param name="shardManager">The shard manager.</param>
    /// <param name="shardOperations">The shard operations.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="healthCheckInterval">Interval between health checks.</param>
    /// <param name="failoverTimeout">Timeout for failover operations.</param>
    public ShardMonitor(
        ShardManager shardManager,
        ShardOperations shardOperations,
        ILogger<ShardMonitor>? logger = null,
        TimeSpan? healthCheckInterval = null,
        TimeSpan? failoverTimeout = null)
    {
        _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
        _shardOperations = shardOperations ?? throw new ArgumentNullException(nameof(shardOperations));
        _logger = logger;

        HealthCheckInterval = healthCheckInterval ?? TimeSpan.FromSeconds(30);
        FailoverTimeout = failoverTimeout ?? TimeSpan.FromMinutes(5);

        _healthCheckTimer = new PeriodicTimer(HealthCheckInterval);
    }

    /// <summary>
    /// Starts the shard monitoring process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        lock (_monitorLock)
        {
            if (_isMonitoring)
            {
                return;
            }

            _isMonitoring = true;
        }

        _logger?.LogInformation("Starting shard health monitoring with {Interval} interval",
            HealthCheckInterval);

        // Perform initial health check
        await PerformHealthChecksAsync(cancellationToken);

        // Start continuous monitoring
        _monitoringTask = MonitorShardsAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the shard monitoring process.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopMonitoringAsync()
    {
        lock (_monitorLock)
        {
            if (!_isMonitoring)
            {
                return;
            }

            _isMonitoring = false;
        }

        _cts.Cancel();

        if (_monitoringTask is not null)
        {
            try
            {
                await _monitoringTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("Shard monitoring did not stop within timeout");
            }
        }

        _logger?.LogInformation("Shard health monitoring stopped");
    }

    /// <summary>
    /// Continuously monitors shard health.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task MonitorShardsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_isMonitoring && !cancellationToken.IsCancellationRequested)
            {
                await _healthCheckTimer.WaitForNextTickAsync(cancellationToken);
                await PerformHealthChecksAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when monitoring is stopped
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during shard monitoring");
        }
    }

    /// <summary>
    /// Performs health checks on all shards.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        var allShards = _shardManager.GetAllShards();

        foreach (var shard in allShards)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await CheckShardHealthAsync(shard, cancellationToken);
        }
    }

    /// <summary>
    /// Checks the health of a single shard and handles status changes.
    /// </summary>
    /// <param name="shard">The shard metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CheckShardHealthAsync(ShardMetadata shard, CancellationToken cancellationToken)
    {
        var previousStatus = shard.Status;

        try
        {
            var healthCheck = await _shardOperations.CheckShardHealthAsync(shard.ShardId, cancellationToken);

            // Update shard status based on health check
            var newStatus = healthCheck.IsHealthy ? ShardStatus.Online : ShardStatus.Offline;
            var errorMessage = healthCheck.IsHealthy ? null : healthCheck.ErrorMessage;

            _shardManager.UpdateShardStatus(shard.ShardId, newStatus, errorMessage);

            // Notify about status changes
            if (previousStatus != newStatus)
            {
                OnShardStatusChanged(shard.ShardId, previousStatus, newStatus, errorMessage);

                // Handle failover for master shards that go offline
                if (previousStatus == ShardStatus.Online && newStatus == ShardStatus.Offline && shard.IsMaster)
                {
                    await HandleMasterFailoverAsync(shard, cancellationToken);
                }
            }

            // Update heartbeat
            _shardManager.UpdateHeartbeat(shard.ShardId);

            _logger?.LogDebug("Shard {ShardId} health check: {Status} ({ResponseTime}ms)",
                shard.ShardId, newStatus,
                healthCheck.ResponseTime?.TotalMilliseconds ?? 0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to check health for shard {ShardId}", shard.ShardId);

            // Mark as offline on health check failure
            _shardManager.UpdateShardStatus(shard.ShardId, ShardStatus.Offline, ex.Message);

            if (previousStatus != ShardStatus.Offline)
            {
                OnShardStatusChanged(shard.ShardId, previousStatus, ShardStatus.Offline, ex.Message);
            }
        }
    }

    /// <summary>
    /// Handles failover when a master shard goes offline.
    /// </summary>
    /// <param name="failedMaster">The failed master shard.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task HandleMasterFailoverAsync(ShardMetadata failedMaster, CancellationToken cancellationToken)
    {
        _logger?.LogWarning("Master shard {ShardId} failed, initiating failover", failedMaster.ShardId);

        OnShardFailoverInitiated(failedMaster.ShardId, "Master shard offline");

        try
        {
            // Find available replica shards
            var replicas = _shardManager.GetReplicaShards(failedMaster.ShardId);
            var availableReplica = replicas.FirstOrDefault(r => r.Status == ShardStatus.Online);

            if (availableReplica is not null)
            {
                // Promote replica to master
                await PromoteReplicaToMasterAsync(availableReplica, cancellationToken);

                _logger?.LogInformation("Successfully promoted replica {ReplicaId} to master for shard {ShardId}",
                    availableReplica.ShardId, failedMaster.ShardId);
            }
            else
            {
                _logger?.LogError("No available replicas found for failed master shard {ShardId}",
                    failedMaster.ShardId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failover failed for master shard {ShardId}", failedMaster.ShardId);
        }
    }

    /// <summary>
    /// Promotes a replica shard to master.
    /// </summary>
    /// <param name="replica">The replica shard to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PromoteReplicaToMasterAsync(ShardMetadata replica, CancellationToken cancellationToken)
    {
        // TODO: Implement replica promotion
        // - Stop replication from old master
        // - Allow writes on the replica
        // - Update routing configuration
        // - Notify other components of the change

        await Task.Delay(1000, cancellationToken); // Simulate promotion time

        _logger?.LogInformation("Promoted replica shard {ShardId} to master", replica.ShardId);
    }

    /// <summary>
    /// Raises the ShardStatusChanged event.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="oldStatus">The old status.</param>
    /// <param name="newStatus">The new status.</param>
    /// <param name="errorMessage">Optional error message.</param>
    private void OnShardStatusChanged(string shardId, ShardStatus oldStatus, ShardStatus newStatus, string? errorMessage)
    {
        _logger?.LogInformation("Shard {ShardId} status changed: {OldStatus} -> {NewStatus}",
            shardId, oldStatus, newStatus);

        ShardStatusChanged?.Invoke(this, new ShardStatusChangedEventArgs(
            shardId, oldStatus, newStatus, errorMessage));
    }

    /// <summary>
    /// Raises the ShardFailoverInitiated event.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="reason">The reason for failover.</param>
    private void OnShardFailoverInitiated(string shardId, string reason)
    {
        ShardFailoverInitiated?.Invoke(this, new ShardFailoverEventArgs(shardId, reason));
    }

    /// <summary>
    /// Gets monitoring statistics.
    /// </summary>
    /// <returns>Monitoring statistics.</returns>
    public ShardMonitorStats GetMonitorStats()
    {
        return new ShardMonitorStats
        {
            IsMonitoring = _isMonitoring,
            HealthCheckInterval = HealthCheckInterval,
            TotalShards = _shardManager.ShardCount,
            OnlineShards = _shardManager.OnlineShardCount,
            LastHealthCheck = DateTimeOffset.UtcNow // TODO: Track actual last check time
        };
    }

    /// <summary>
    /// Disposes the monitor asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopMonitoringAsync();
        _cts.Dispose();
        _healthCheckTimer.Dispose();
    }
}

/// <summary>
/// Event arguments for shard status changes.
/// </summary>
public class ShardStatusChangedEventArgs : EventArgs
{
    /// <summary>Gets the shard identifier.</summary>
    public string ShardId { get; }

    /// <summary>Gets the old status.</summary>
    public ShardStatus OldStatus { get; }

    /// <summary>Gets the new status.</summary>
    public ShardStatus NewStatus { get; }

    /// <summary>Gets the error message (if any).</summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardStatusChangedEventArgs"/> class.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="oldStatus">The old status.</param>
    /// <param name="newStatus">The new status.</param>
    /// <param name="errorMessage">The error message.</param>
    public ShardStatusChangedEventArgs(string shardId, ShardStatus oldStatus, ShardStatus newStatus, string? errorMessage)
    {
        ShardId = shardId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Event arguments for shard failover operations.
/// </summary>
public class ShardFailoverEventArgs : EventArgs
{
    /// <summary>Gets the shard identifier.</summary>
    public string ShardId { get; }

    /// <summary>Gets the reason for failover.</summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardFailoverEventArgs"/> class.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="reason">The reason for failover.</param>
    public ShardFailoverEventArgs(string shardId, string reason)
    {
        ShardId = shardId;
        Reason = reason;
    }
}

/// <summary>
/// Statistics for shard monitoring operations.
/// </summary>
public class ShardMonitorStats
{
    /// <summary>Gets whether monitoring is currently active.</summary>
    public bool IsMonitoring { get; init; }

    /// <summary>Gets the health check interval.</summary>
    public TimeSpan HealthCheckInterval { get; init; }

    /// <summary>Gets the total number of shards.</summary>
    public int TotalShards { get; init; }

    /// <summary>Gets the number of online shards.</summary>
    public int OnlineShards { get; init; }

    /// <summary>Gets the timestamp of the last health check.</summary>
    public DateTimeOffset LastHealthCheck { get; init; }
}
