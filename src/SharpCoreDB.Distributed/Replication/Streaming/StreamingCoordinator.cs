// <copyright file="StreamingCoordinator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Coordinates multiple streaming pipelines across different WAL files.
/// Provides high-level management of distributed streaming operations.
/// C# 14: Primary constructors, collection expressions, async coordination.
/// </summary>
public sealed class StreamingCoordinator : IAsyncDisposable
{
    private readonly Dictionary<string, StreamingPipeline> _pipelines = [];
    private readonly WALPositionTracker _globalPositionTracker;
    private readonly ILogger<StreamingCoordinator>? _logger;

    private readonly CancellationTokenSource _cts = new();

    private bool _isRunning;

    /// <summary>Gets the number of active pipelines.</summary>
    public int ActivePipelineCount => _pipelines.Count;

    /// <summary>Gets all pipeline identifiers.</summary>
    public IReadOnlyCollection<string> PipelineIds => [.. _pipelines.Keys];

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingCoordinator"/> class.
    /// </summary>
    /// <param name="globalPositionTracker">The global position tracker.</param>
    /// <param name="logger">Optional logger.</param>
    public StreamingCoordinator(WALPositionTracker globalPositionTracker, ILogger<StreamingCoordinator>? logger = null)
    {
        _globalPositionTracker = globalPositionTracker ?? throw new ArgumentNullException(nameof(globalPositionTracker));
        _logger = logger;
    }

    /// <summary>
    /// Starts the streaming coordinator.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartCoordinatorAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;

        _logger?.LogInformation("Starting streaming coordinator");

        // Start all pipelines
        var startTasks = _pipelines.Values.Select(p => p.StartPipelineAsync(cancellationToken)).ToArray();
        await Task.WhenAll(startTasks);

        _logger?.LogInformation("Streaming coordinator started with {Count} pipelines", _pipelines.Count);
    }

    /// <summary>
    /// Stops the streaming coordinator.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopCoordinatorAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _logger?.LogInformation("Stopping streaming coordinator");

        _isRunning = false;
        _cts.Cancel();

        // Stop all pipelines
        var stopTasks = _pipelines.Values.Select(p => p.StopPipelineAsync()).ToArray();
        await Task.WhenAll(stopTasks);

        _logger?.LogInformation("Streaming coordinator stopped");
    }

    /// <summary>
    /// Creates a new streaming pipeline for a WAL file.
    /// </summary>
    /// <param name="pipelineId">Unique pipeline identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <returns>The created pipeline.</returns>
    public StreamingPipeline CreatePipeline(string pipelineId, string walFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(walFilePath);

        if (_pipelines.ContainsKey(pipelineId))
        {
            throw new InvalidOperationException($"Pipeline '{pipelineId}' already exists.");
        }

        var pipeline = new StreamingPipeline(_globalPositionTracker, (ILogger<StreamingPipeline>?)_logger);
        _pipelines[pipelineId] = pipeline;

        _logger?.LogInformation("Created streaming pipeline {PipelineId} for WAL file {WalFilePath}",
            pipelineId, walFilePath);

        return pipeline;
    }

    /// <summary>
    /// Removes a streaming pipeline.
    /// </summary>
    /// <param name="pipelineId">The pipeline identifier.</param>
    /// <returns>True if the pipeline was removed.</returns>
    public async Task<bool> RemovePipelineAsync(string pipelineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);

        if (_pipelines.Remove(pipelineId, out var pipeline))
        {
            await pipeline.StopPipelineAsync();
            _logger?.LogInformation("Removed streaming pipeline {PipelineId}", pipelineId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a streaming pipeline by ID.
    /// </summary>
    /// <param name="pipelineId">The pipeline identifier.</param>
    /// <returns>The pipeline, or null if not found.</returns>
    public StreamingPipeline? GetPipeline(string pipelineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);

        return _pipelines.TryGetValue(pipelineId, out var pipeline) ? pipeline : null;
    }

    /// <summary>
    /// Gets all streaming pipelines.
    /// </summary>
    /// <returns>Collection of all pipelines.</returns>
    public IReadOnlyCollection<StreamingPipeline> GetAllPipelines()
    {
        return [.. _pipelines.Values];
    }

    /// <summary>
    /// Adds a replica to a specific pipeline.
    /// </summary>
    /// <param name="pipelineId">The pipeline identifier.</param>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <param name="replicationState">The replication state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddReplicaToPipelineAsync(
        string pipelineId,
        string replicaId,
        string walFilePath,
        ReplicationState replicationState,
        CancellationToken cancellationToken = default)
    {
        var pipeline = GetPipeline(pipelineId);
        if (pipeline is null)
        {
            throw new InvalidOperationException($"Pipeline '{pipelineId}' not found.");
        }

        await pipeline.AddSessionAsync(replicaId, walFilePath, replicationState, cancellationToken);

        _logger?.LogInformation("Added replica {ReplicaId} to pipeline {PipelineId}", replicaId, pipelineId);
    }

    /// <summary>
    /// Removes a replica from a pipeline.
    /// </summary>
    /// <param name="pipelineId">The pipeline identifier.</param>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveReplicaFromPipelineAsync(
        string pipelineId,
        string replicaId,
        CancellationToken cancellationToken = default)
    {
        var pipeline = GetPipeline(pipelineId);
        if (pipeline is null)
        {
            throw new InvalidOperationException($"Pipeline '{pipelineId}' not found.");
        }

        await pipeline.RemoveSessionAsync(replicaId, cancellationToken);

        _logger?.LogInformation("Removed replica {ReplicaId} from pipeline {PipelineId}", replicaId, pipelineId);
    }

    /// <summary>
    /// Acknowledges a position for a replica in a pipeline.
    /// </summary>
    /// <param name="pipelineId">The pipeline identifier.</param>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="position">The acknowledged position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AcknowledgePositionAsync(
        string pipelineId,
        string replicaId,
        WALPosition position,
        CancellationToken cancellationToken = default)
    {
        var pipeline = GetPipeline(pipelineId);
        if (pipeline is null)
        {
            throw new InvalidOperationException($"Pipeline '{pipelineId}' not found.");
        }

        await pipeline.AcknowledgePositionAsync(replicaId, position, cancellationToken);
    }

    /// <summary>
    /// Gets coordinator statistics.
    /// </summary>
    /// <returns>Coordinator statistics.</returns>
    public StreamingCoordinatorStats GetStats()
    {
        var pipelineStats = _pipelines.Select(p => new PipelineStats(p.Key, p.Value.GetStats())).ToArray();

        return new StreamingCoordinatorStats
        {
            IsRunning = _isRunning,
            TotalPipelines = _pipelines.Count,
            ActivePipelines = pipelineStats.Count(p => p.Stats.IsRunning),
            TotalSessions = pipelineStats.Sum(p => p.Stats.TotalSessions),
            ActiveSessions = pipelineStats.Sum(p => p.Stats.ActiveSessions),
            HealthySessions = pipelineStats.Sum(p => p.Stats.HealthySessions),
            PipelineStats = pipelineStats,
            GlobalPositionStats = _globalPositionTracker.GetStats()
        };
    }

    /// <summary>
    /// Performs health check on all pipelines and sessions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with health status.</returns>
    public async Task<StreamingCoordinatorHealthStatus> PerformHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var pipelineHealthChecks = new List<Task<PipelineHealthCheck>>();

        foreach (var (pipelineId, pipeline) in _pipelines)
        {
            pipelineHealthChecks.Add(CheckPipelineHealthAsync(pipelineId, pipeline, cancellationToken));
        }

        var results = await Task.WhenAll(pipelineHealthChecks);

        var healthyPipelines = results.Count(r => r.IsHealthy);
        var totalSessions = results.Sum(r => r.TotalSessions);
        var healthySessions = results.Sum(r => r.HealthySessions);

        return new StreamingCoordinatorHealthStatus
        {
            IsCoordinatorRunning = _isRunning,
            TotalPipelines = _pipelines.Count,
            HealthyPipelines = healthyPipelines,
            TotalSessions = totalSessions,
            HealthySessions = healthySessions,
            PipelineHealthChecks = [.. results]
        };
    }

    /// <summary>
    /// Checks the health of a single pipeline.
    /// </summary>
    /// <param name="pipelineId">The pipeline identifier.</param>
    /// <param name="pipeline">The pipeline instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with health check result.</returns>
    private async Task<PipelineHealthCheck> CheckPipelineHealthAsync(
        string pipelineId,
        StreamingPipeline pipeline,
        CancellationToken cancellationToken)
    {
        var stats = pipeline.GetStats();
        var sessions = pipeline.GetAllSessions();

        var healthySessions = sessions.Count(s => s.GetStats().IsHealthy);

        return new PipelineHealthCheck
        {
            PipelineId = pipelineId,
            IsHealthy = stats.HealthStatus is StreamingPipelineHealthStatus.Healthy or StreamingPipelineHealthStatus.Degraded,
            TotalSessions = stats.TotalSessions,
            HealthySessions = healthySessions,
            Status = stats.HealthStatus.ToString()
        };
    }

    /// <summary>
    /// Disposes the streaming coordinator asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopCoordinatorAsync();
        _cts.Dispose();
    }
}

/// <summary>
/// Statistics for streaming coordinator operations.
/// </summary>
public class StreamingCoordinatorStats
{
    /// <summary>Gets whether the coordinator is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the total number of pipelines.</summary>
    public int TotalPipelines { get; init; }

    /// <summary>Gets the number of active pipelines.</summary>
    public int ActivePipelines { get; init; }

    /// <summary>Gets the total number of sessions across all pipelines.</summary>
    public int TotalSessions { get; init; }

    /// <summary>Gets the number of active sessions.</summary>
    public int ActiveSessions { get; init; }

    /// <summary>Gets the number of healthy sessions.</summary>
    public int HealthySessions { get; init; }

    /// <summary>Gets the individual pipeline statistics.</summary>
    public IReadOnlyCollection<PipelineStats> PipelineStats { get; init; } = [];

    /// <summary>Gets the global position tracking statistics.</summary>
    public WALPositionStats GlobalPositionStats { get; init; } = new();

    /// <summary>Gets the coordinator health status.</summary>
    public StreamingCoordinatorHealthStatus HealthStatus => new()
    {
        IsCoordinatorRunning = IsRunning,
        TotalPipelines = TotalPipelines,
        HealthyPipelines = ActivePipelines, // Simplified
        TotalSessions = TotalSessions,
        HealthySessions = HealthySessions,
        PipelineHealthChecks = [] // Would need to be populated separately
    };
}

/// <summary>
/// Pipeline statistics wrapper.
/// </summary>
public class PipelineStats(string pipelineId, StreamingPipelineStats stats)
{
    /// <summary>Gets the pipeline identifier.</summary>
    public string PipelineId { get; } = pipelineId;

    /// <summary>Gets the pipeline statistics.</summary>
    public StreamingPipelineStats Stats { get; } = stats;
}

/// <summary>
/// Health status for the streaming coordinator.
/// </summary>
public class StreamingCoordinatorHealthStatus
{
    /// <summary>Gets whether the coordinator is running.</summary>
    public bool IsCoordinatorRunning { get; init; }

    /// <summary>Gets the total number of pipelines.</summary>
    public int TotalPipelines { get; init; }

    /// <summary>Gets the number of healthy pipelines.</summary>
    public int HealthyPipelines { get; init; }

    /// <summary>Gets the total number of sessions.</summary>
    public int TotalSessions { get; init; }

    /// <summary>Gets the number of healthy sessions.</summary>
    public int HealthySessions { get; init; }

    /// <summary>Gets the individual pipeline health checks.</summary>
    public IReadOnlyCollection<PipelineHealthCheck> PipelineHealthChecks { get; init; } = [];

    /// <summary>Gets the overall health status.</summary>
    public string OverallStatus
    {
        get
        {
            if (!IsCoordinatorRunning) return "Stopped";
            if (HealthyPipelines == TotalPipelines && HealthySessions == TotalSessions) return "Healthy";
            if (HealthyPipelines > 0 || HealthySessions > 0) return "Degraded";
            return "Unhealthy";
        }
    }
}

/// <summary>
/// Health check result for a single pipeline.
/// </summary>
public class PipelineHealthCheck
{
    /// <summary>Gets the pipeline identifier.</summary>
    public string PipelineId { get; init; } = string.Empty;

    /// <summary>Gets whether the pipeline is healthy.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Gets the total number of sessions in the pipeline.</summary>
    public int TotalSessions { get; init; }

    /// <summary>Gets the number of healthy sessions.</summary>
    public int HealthySessions { get; init; }

    /// <summary>Gets the pipeline status description.</summary>
    public string Status { get; init; } = string.Empty;
}
