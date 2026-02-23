// <copyright file="ReplicationMonitor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Monitors replication synchronization metrics and performance.
/// Provides real-time statistics on replication health and performance.
/// C# 14: Primary constructors, collection expressions, interpolated strings.
/// </summary>
public sealed class ReplicationMonitor : IAsyncDisposable
{
    private readonly ILogger<ReplicationMonitor>? _logger;

    private readonly Dictionary<string, NodeMetrics> _nodeMetrics = [];
    private readonly Lock _metricsLock = new();

    private readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();
    private readonly PeriodicTimer _metricsTimer;

    private Task? _metricsCollectionTask;
    private bool _isRunning;

    /// <summary>Gets the total uptime of the monitor.</summary>
    public TimeSpan Uptime => _uptimeStopwatch.Elapsed;

    /// <summary>Gets the number of monitored nodes.</summary>
    public int MonitoredNodeCount => _nodeMetrics.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplicationMonitor"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for monitoring events.</param>
    public ReplicationMonitor(ILogger<ReplicationMonitor>? logger = null)
    {
        _logger = logger;
        _metricsTimer = new PeriodicTimer(TimeSpan.FromSeconds(30)); // Collect metrics every 30 seconds
    }

    /// <summary>
    /// Starts the replication monitor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _metricsCollectionTask = CollectMetricsAsync(cancellationToken);

        _logger?.LogInformation("Replication monitor started");
    }

    /// <summary>
    /// Stops the replication monitor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _metricsTimer.Dispose();

        if (_metricsCollectionTask is not null)
        {
            try
            {
                await _metricsCollectionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _logger?.LogInformation("Replication monitor stopped");
    }

    /// <summary>
    /// Registers a node for monitoring.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RegisterNodeAsync(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_metricsLock)
        {
            if (!_nodeMetrics.ContainsKey(nodeId))
            {
                _nodeMetrics[nodeId] = new NodeMetrics(nodeId);
                _logger?.LogInformation("Registered node {NodeId} for monitoring", nodeId);
            }
        }

        await Task.CompletedTask; // Placeholder for async registration
    }

    /// <summary>
    /// Unregisters a node from monitoring.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UnregisterNodeAsync(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_metricsLock)
        {
            if (_nodeMetrics.Remove(nodeId))
            {
                _logger?.LogInformation("Unregistered node {NodeId} from monitoring", nodeId);
            }
        }

        await Task.CompletedTask; // Placeholder for async unregistration
    }

    /// <summary>
    /// Records a replication event for monitoring.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="eventType">The event type.</param>
    /// <param name="metadata">Additional metadata.</param>
    public void RecordEvent(string nodeId, ReplicationEventType eventType, IReadOnlyDictionary<string, object>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_metricsLock)
        {
            if (_nodeMetrics.TryGetValue(nodeId, out var metrics))
            {
                metrics.RecordEvent(eventType, metadata);
            }
        }
    }

    /// <summary>
    /// Records synchronization latency.
    /// </summary>
    /// <param name="sourceNodeId">The source node identifier.</param>
    /// <param name="targetNodeId">The target node identifier.</param>
    /// <param name="latency">The synchronization latency.</param>
    public void RecordSyncLatency(string sourceNodeId, string targetNodeId, TimeSpan latency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetNodeId);

        lock (_metricsLock)
        {
            if (_nodeMetrics.TryGetValue(sourceNodeId, out var sourceMetrics))
            {
                sourceMetrics.RecordSyncLatency(targetNodeId, latency);
            }
        }
    }

    /// <summary>
    /// Records a conflict detection.
    /// </summary>
    /// <param name="nodeId">The node identifier where conflict was detected.</param>
    /// <param name="conflictType">The type of conflict.</param>
    public void RecordConflict(string nodeId, string conflictType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictType);

        lock (_metricsLock)
        {
            if (_nodeMetrics.TryGetValue(nodeId, out var metrics))
            {
                metrics.RecordConflict(conflictType);
            }
        }
    }

    /// <summary>
    /// Gets comprehensive replication metrics.
    /// </summary>
    /// <returns>The replication metrics.</returns>
    public ReplicationMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            var nodeMetrics = _nodeMetrics.Values.ToList();
            var totalEvents = nodeMetrics.Sum(m => m.TotalEvents);
            var totalConflicts = nodeMetrics.Sum(m => m.TotalConflicts);
            var avgSyncLatency = nodeMetrics
                .SelectMany(m => m.SyncLatencies.Values)
                .SelectMany(l => l)
                .DefaultIfEmpty(TimeSpan.Zero)
                .Average(t => t.TotalMilliseconds);

            return new ReplicationMetrics
            {
                Uptime = Uptime,
                MonitoredNodes = MonitoredNodeCount,
                TotalEvents = totalEvents,
                TotalConflicts = totalConflicts,
                AverageSyncLatencyMs = avgSyncLatency,
                NodeMetrics = [.. nodeMetrics],
                HealthStatus = DetermineHealthStatus(nodeMetrics)
            };
        }
    }

    /// <summary>
    /// Gets metrics for a specific node.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>The node metrics, or null if not found.</returns>
    public NodeMetrics? GetNodeMetrics(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_metricsLock)
        {
            return _nodeMetrics.TryGetValue(nodeId, out var metrics) ? metrics : null;
        }
    }

    /// <summary>
    /// Gets synchronization health between two nodes.
    /// </summary>
    /// <param name="nodeA">The first node identifier.</param>
    /// <param name="nodeB">The second node identifier.</param>
    /// <returns>The synchronization health metrics.</returns>
    public SyncHealth GetSyncHealth(string nodeA, string nodeB)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeA);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeB);

        lock (_metricsLock)
        {
            var metricsA = _nodeMetrics.GetValueOrDefault(nodeA);
            var metricsB = _nodeMetrics.GetValueOrDefault(nodeB);

            if (metricsA is null || metricsB is null)
            {
                return new SyncHealth(nodeA, nodeB, SyncHealthStatus.Unknown);
            }

            var latencyAtoB = metricsA.GetAverageLatencyTo(nodeB);
            var latencyBtoA = metricsB.GetAverageLatencyTo(nodeA);

            var avgLatency = (latencyAtoB + latencyBtoA) / 2;
            var status = avgLatency < TimeSpan.FromSeconds(1) ? SyncHealthStatus.Healthy :
                        avgLatency < TimeSpan.FromSeconds(5) ? SyncHealthStatus.Degraded :
                        SyncHealthStatus.Unhealthy;

            return new SyncHealth(nodeA, nodeB, status)
            {
                AverageLatency = avgLatency,
                LastSyncTime = DateTimeOffset.UtcNow // In real implementation, track actual sync times
            };
        }
    }

    /// <summary>
    /// Collects metrics periodically.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CollectMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (_isRunning && await _metricsTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await CollectNodeMetricsAsync(cancellationToken).ConfigureAwait(false);
                    LogHealthSummary();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error collecting replication metrics");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    /// <summary>
    /// Collects metrics from all nodes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CollectNodeMetricsAsync(CancellationToken cancellationToken)
    {
        // In a real implementation, this would query each node for metrics
        // For now, just update internal counters
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs a health summary.
    /// </summary>
    private void LogHealthSummary()
    {
        var metrics = GetMetrics();
        _logger?.LogInformation(
            "Replication health: {Nodes} nodes, {Events} events, {Conflicts} conflicts, {Latency:F2}ms avg latency, Status: {Status}",
            metrics.MonitoredNodes,
            metrics.TotalEvents,
            metrics.TotalConflicts,
            metrics.AverageSyncLatencyMs,
            metrics.HealthStatus);
    }

    /// <summary>
    /// Determines the overall health status.
    /// </summary>
    /// <param name="nodeMetrics">The node metrics.</param>
    /// <returns>The health status.</returns>
    private static ReplicationHealthStatus DetermineHealthStatus(IReadOnlyList<NodeMetrics> nodeMetrics)
    {
        if (nodeMetrics.Count == 0)
        {
            return ReplicationHealthStatus.Unknown;
        }

        var unhealthyNodes = nodeMetrics.Count(m => m.IsUnhealthy);
        var degradedNodes = nodeMetrics.Count(m => m.IsDegraded);

        if (unhealthyNodes > 0)
        {
            return ReplicationHealthStatus.Failed;
        }

        if (degradedNodes > 0)
        {
            return ReplicationHealthStatus.Lagging;
        }

        return ReplicationHealthStatus.Healthy;
    }

    /// <summary>
    /// Disposes the replication monitor.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Comprehensive replication metrics.
/// </summary>
public class ReplicationMetrics
{
    /// <summary>Gets the monitor uptime.</summary>
    public required TimeSpan Uptime { get; init; }

    /// <summary>Gets the number of monitored nodes.</summary>
    public required int MonitoredNodes { get; init; }

    /// <summary>Gets the total number of replication events.</summary>
    public required long TotalEvents { get; init; }

    /// <summary>Gets the total number of conflicts.</summary>
    public required long TotalConflicts { get; init; }

    /// <summary>Gets the average synchronization latency in milliseconds.</summary>
    public required double AverageSyncLatencyMs { get; init; }

    /// <summary>Gets the individual node metrics.</summary>
    public required IReadOnlyList<NodeMetrics> NodeMetrics { get; init; }

    /// <summary>Gets the overall health status.</summary>
    public required ReplicationHealthStatus HealthStatus { get; init; }
}

/// <summary>
/// Metrics for a specific node.
/// </summary>
public class NodeMetrics
{
    private readonly Dictionary<string, List<TimeSpan>> _syncLatencies = [];
    private readonly Dictionary<string, int> _conflictCounts = [];
    private readonly Dictionary<ReplicationEventType, int> _eventCounts = [];

    /// <summary>Gets the node identifier.</summary>
    public string NodeId { get; }

    /// <summary>Gets the total number of events.</summary>
    public long TotalEvents => _eventCounts.Values.Sum();

    /// <summary>Gets the total number of conflicts.</summary>
    public long TotalConflicts => _conflictCounts.Values.Sum();

    /// <summary>Gets the synchronization latencies to other nodes.</summary>
    public IReadOnlyDictionary<string, List<TimeSpan>> SyncLatencies => _syncLatencies;

    /// <summary>Gets whether the node is unhealthy.</summary>
    /// <summary>Gets whether the node is unhealthy.</summary>
    public bool IsUnhealthy => TotalConflicts > 100 || GetAverageLatency() > TimeSpan.FromSeconds(10);

    /// <summary>Gets whether the node is degraded.</summary>
    public bool IsDegraded => TotalConflicts > 10 || GetAverageLatency() > TimeSpan.FromSeconds(2);

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeMetrics"/> class.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    public NodeMetrics(string nodeId)
    {
        NodeId = nodeId;
    }

    /// <summary>
    /// Records a replication event.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="metadata">Additional metadata.</param>
    public void RecordEvent(ReplicationEventType eventType, IReadOnlyDictionary<string, object>? metadata = null)
    {
        _eventCounts[eventType] = _eventCounts.GetValueOrDefault(eventType) + 1;
    }

    /// <summary>
    /// Records synchronization latency to another node.
    /// </summary>
    /// <param name="targetNodeId">The target node identifier.</param>
    /// <param name="latency">The latency.</param>
    public void RecordSyncLatency(string targetNodeId, TimeSpan latency)
    {
        if (!_syncLatencies.TryGetValue(targetNodeId, out var latencies))
        {
            latencies = [];
            _syncLatencies[targetNodeId] = latencies;
        }

        latencies.Add(latency);

        // Keep only last 100 measurements
        if (latencies.Count > 100)
        {
            latencies.RemoveAt(0);
        }
    }

    /// <summary>
    /// Records a conflict.
    /// </summary>
    /// <param name="conflictType">The conflict type.</param>
    public void RecordConflict(string conflictType)
    {
        _conflictCounts[conflictType] = _conflictCounts.GetValueOrDefault(conflictType) + 1;
    }

    /// <summary>
    /// Gets the average latency to a specific node.
    /// </summary>
    /// <param name="targetNodeId">The target node identifier.</param>
    /// <returns>The average latency.</returns>
    public TimeSpan GetAverageLatencyTo(string targetNodeId)
    {
        if (_syncLatencies.TryGetValue(targetNodeId, out var latencies) && latencies.Count > 0)
        {
            return TimeSpan.FromTicks((long)latencies.Average(t => t.Ticks));
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Gets the overall average latency.
    /// </summary>
    /// <returns>The average latency.</returns>
    public TimeSpan GetAverageLatency()
    {
        var allLatencies = _syncLatencies.Values.SelectMany(l => l).ToList();
        if (allLatencies.Count > 0)
        {
            return TimeSpan.FromTicks((long)allLatencies.Average(t => t.Ticks));
        }

        return TimeSpan.Zero;
    }
}

/// <summary>
/// Synchronization health between two nodes.
/// </summary>
public class SyncHealth
{
    /// <summary>Gets the first node identifier.</summary>
    public string NodeA { get; }

    /// <summary>Gets the second node identifier.</summary>
    public string NodeB { get; }

    /// <summary>Gets the health status.</summary>
    public SyncHealthStatus Status { get; }

    /// <summary>Gets the average latency.</summary>
    public TimeSpan AverageLatency { get; init; }

    /// <summary>Gets the last synchronization time.</summary>
    public DateTimeOffset? LastSyncTime { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncHealth"/> class.
    /// </summary>
    /// <param name="nodeA">The first node identifier.</param>
    /// <param name="nodeB">The second node identifier.</param>
    /// <param name="status">The health status.</param>
    public SyncHealth(string nodeA, string nodeB, SyncHealthStatus status)
    {
        NodeA = nodeA;
        NodeB = nodeB;
        Status = status;
    }
}

/// <summary>
/// Synchronization health status.
/// </summary>
public enum SyncHealthStatus
{
    /// <summary>Unknown health status.</summary>
    Unknown,

    /// <summary>Healthy synchronization.</summary>
    Healthy,

    /// <summary>Degraded synchronization.</summary>
    Degraded,

    /// <summary>Unhealthy synchronization.</summary>
    Unhealthy
}
