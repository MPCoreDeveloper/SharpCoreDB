// <copyright file="HealthChecker.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Core health monitoring engine for continuous buffer health surveillance.
/// Provides automated health checking with intelligent scheduling and alerting.
/// C# 14: Primary constructors, Channel<T> for async coordination, pattern matching.
/// </summary>
public sealed class HealthChecker : IAsyncDisposable
{
    private readonly Dictionary<string, BufferHealth> _monitoredBuffers = [];
    private readonly HealthAlerts _alerts;
    private readonly CheckScheduler _scheduler;
    private readonly ILogger<HealthChecker>? _logger;

    private readonly Channel<HealthCheckRequest> _checkChannel = Channel.CreateBounded<HealthCheckRequest>(1000);
    private readonly CancellationTokenSource _cts = new();

    private Task? _monitoringTask;
    private Task? _schedulerTask;
    private bool _isRunning;

    /// <summary>Gets the number of monitored buffers.</summary>
    public int MonitoredBufferCount => _monitoredBuffers.Count;

    /// <summary>Gets whether the health checker is running.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthChecker"/> class.
    /// </summary>
    /// <param name="alerts">The alerts system.</param>
    /// <param name="scheduler">The check scheduler.</param>
    /// <param name="logger">Optional logger.</param>
    public HealthChecker(HealthAlerts alerts, CheckScheduler? scheduler = null, ILogger<HealthChecker>? logger = null)
    {
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        _scheduler = scheduler ?? new CheckScheduler();
        _logger = logger;
    }

    /// <summary>
    /// Starts the health monitoring system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;

        _logger?.LogInformation("Starting health monitoring for {Count} buffers", _monitoredBuffers.Count);

        // Start the monitoring and scheduler tasks
        _monitoringTask = MonitorBuffersAsync(_cts.Token);
        _schedulerTask = _scheduler.StartSchedulingAsync(_checkChannel.Writer, _cts.Token);

        await _alerts.StartProcessingAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the health monitoring system.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopMonitoringAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _logger?.LogInformation("Stopping health monitoring");

        _isRunning = false;
        _cts.Cancel();

        // Stop tasks
        var tasks = new List<Task>();
        if (_monitoringTask is not null) tasks.Add(_monitoringTask);
        if (_schedulerTask is not null) tasks.Add(_schedulerTask);

        await Task.WhenAll(tasks);

        await _alerts.StopProcessingAsync();
        _checkChannel.Writer.Complete();
    }

    /// <summary>
    /// Adds a buffer to health monitoring.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="buffer">The buffer segment.</param>
    /// <param name="checkInterval">The health check interval.</param>
    /// <returns>True if the buffer was added.</returns>
    public bool AddBuffer(string bufferId, BufferSegment buffer, TimeSpan? checkInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        if (_monitoredBuffers.ContainsKey(bufferId))
        {
            return false;
        }

        var health = new BufferHealth(buffer);
        _monitoredBuffers[bufferId] = health;

        var interval = checkInterval ?? GetDefaultCheckInterval(bufferId);
        _scheduler.ScheduleChecks(bufferId, interval);

        _logger?.LogInformation("Added buffer {BufferId} to health monitoring", bufferId);

        return true;
    }

    /// <summary>
    /// Removes a buffer from health monitoring.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>True if the buffer was removed.</returns>
    public bool RemoveBuffer(string bufferId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        if (_monitoredBuffers.Remove(bufferId, out _))
        {
            _scheduler.UnscheduleChecks(bufferId);
            _logger?.LogInformation("Removed buffer {BufferId} from health monitoring", bufferId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Performs an immediate health check on a specific buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health assessment result.</returns>
    public async Task<BufferHealthAssessment?> CheckBufferHealthAsync(
        string bufferId,
        CancellationToken cancellationToken = default)
    {
        if (!_monitoredBuffers.TryGetValue(bufferId, out var health))
        {
            return null;
        }

        var assessment = health.AssessHealth();

        // Process any issues found
        if (assessment.Issues.Count > 0)
        {
            foreach (var issue in assessment.Issues)
            {
                await _alerts.RaiseAlertAsync(bufferId, issue, assessment, cancellationToken);
            }
        }

        return assessment;
    }

    /// <summary>
    /// Gets the current health status of a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The current health status.</returns>
    public BufferHealthStatus? GetBufferHealthStatus(string bufferId)
    {
        return _monitoredBuffers.TryGetValue(bufferId, out var health)
            ? health.CurrentStatus
            : null;
    }

    /// <summary>
    /// Gets health statistics for all monitored buffers.
    /// </summary>
    /// <returns>Health statistics.</returns>
    public HealthCheckerStats GetStats()
    {
        var bufferStats = _monitoredBuffers.ToDictionary(
            kvp => kvp.Key,
            kvp => new BufferHealthSnapshot
            {
                Status = kvp.Value.CurrentStatus,
                LastCheck = kvp.Value.LastCheckTime,
                IssueCount = kvp.Value.Metrics.GetDetailedStats().TotalChecks > 0
                    ? kvp.Value.Metrics.GetDetailedStats().StatusDistribution
                        .Where(kvp => kvp.Key != BufferHealthStatus.Healthy)
                        .Sum(kvp => kvp.Value)
                    : 0
            });

        return new HealthCheckerStats
        {
            IsRunning = _isRunning,
            MonitoredBuffers = _monitoredBuffers.Count,
            BufferStats = bufferStats,
            SchedulerStats = _scheduler.GetStats(),
            AlertStats = _alerts.GetStats()
        };
    }

    /// <summary>
    /// Monitors buffers asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the monitoring operation.</returns>
    private async Task MonitorBuffersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var request in _checkChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessHealthCheckRequestAsync(request, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in buffer monitoring loop");
        }
    }

    /// <summary>
    /// Processes a health check request.
    /// </summary>
    /// <param name="request">The health check request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessHealthCheckRequestAsync(HealthCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var assessment = await CheckBufferHealthAsync(request.BufferId, cancellationToken);

            if (assessment is not null)
            {
                _logger?.LogDebug("Health check completed for buffer {BufferId}: {Status}",
                    request.BufferId, assessment.Status);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing health check for buffer {BufferId}", request.BufferId);
        }
    }

    /// <summary>
    /// Gets the default check interval for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The default check interval.</returns>
    private static TimeSpan GetDefaultCheckInterval(string bufferId)
    {
        // Could be made configurable based on buffer type, usage patterns, etc.
        // For now, use a conservative default
        return TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Disposes the health checker asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopMonitoringAsync();
        _cts.Dispose();
        _checkChannel.Writer.Complete();
    }
}

/// <summary>
/// Health check request.
/// </summary>
public class HealthCheckRequest
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the request timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the requested check priority.</summary>
    public CheckPriority Priority { get; init; } = CheckPriority.Normal;
}

/// <summary>
/// Health checker statistics.
/// </summary>
public class HealthCheckerStats
{
    /// <summary>Gets whether the checker is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the number of monitored buffers.</summary>
    public int MonitoredBuffers { get; init; }

    /// <summary>Gets the buffer health statistics.</summary>
    public IReadOnlyDictionary<string, BufferHealthSnapshot> BufferStats { get; init; } = new Dictionary<string, BufferHealthSnapshot>();

    /// <summary>Gets the scheduler statistics.</summary>
    public CheckSchedulerStats SchedulerStats { get; init; } = new();

    /// <summary>Gets the alert statistics.</summary>
    public AlertStats AlertStats { get; init; } = new();

    /// <summary>Gets the overall health summary.</summary>
    public string HealthSummary
    {
        get
        {
            var healthyBuffers = BufferStats.Count(s => s.Value.Status == BufferHealthStatus.Healthy);
            var unhealthyBuffers = MonitoredBuffers - healthyBuffers;

            return $"{healthyBuffers}/{MonitoredBuffers} buffers healthy, {unhealthyBuffers} unhealthy";
        }
    }
}

/// <summary>
/// Buffer health snapshot.
/// </summary>
public class BufferHealthSnapshot
{
    /// <summary>Gets the current health status.</summary>
    public BufferHealthStatus Status { get; init; }

    /// <summary>Gets the last check timestamp.</summary>
    public DateTimeOffset? LastCheck { get; init; }

    /// <summary>Gets the total number of issues found.</summary>
    public int IssueCount { get; init; }
}

/// <summary>
/// Check priority levels.
/// </summary>
public enum CheckPriority
{
    /// <summary>Low priority check.</summary>
    Low,

    /// <summary>Normal priority check.</summary>
    Normal,

    /// <summary>High priority check.</summary>
    High,

    /// <summary>Critical priority check.</summary>
    Critical
}
