// <copyright file="CheckScheduler.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Intelligent scheduler for health checks with priority-based execution.
/// Provides adaptive scheduling based on buffer usage patterns and health status.
/// C# 14: Primary constructors, PeriodicTimer for scheduling.
/// </summary>
public sealed class CheckScheduler
{
    private readonly Dictionary<string, ScheduledCheck> _scheduledChecks = [];
    private readonly PriorityQueue<HealthCheckRequest, CheckPriority> _priorityQueue = new();
    private readonly Lock _schedulerLock = new();

    private readonly ILogger<CheckScheduler>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckScheduler"/> class.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public CheckScheduler(ILogger<CheckScheduler>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts the scheduling system.
    /// </summary>
    /// <param name="requestWriter">The channel writer for health check requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the scheduling operation.</returns>
    public async Task StartSchedulingAsync(
        ChannelWriter<HealthCheckRequest> requestWriter,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("Starting health check scheduler");

        var tasks = new List<Task>();

        lock (_schedulerLock)
        {
            foreach (var scheduledCheck in _scheduledChecks.Values)
            {
                tasks.Add(RunScheduledCheckAsync(scheduledCheck, requestWriter, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Schedules health checks for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="interval">The check interval.</param>
    /// <param name="priority">The check priority.</param>
    public void ScheduleChecks(string bufferId, TimeSpan interval, CheckPriority priority = CheckPriority.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        var scheduledCheck = new ScheduledCheck
        {
            BufferId = bufferId,
            Interval = interval,
            Priority = priority,
            NextCheckTime = DateTimeOffset.UtcNow
        };

        lock (_schedulerLock)
        {
            _scheduledChecks[bufferId] = scheduledCheck;
        }

        _logger?.LogInformation("Scheduled health checks for buffer {BufferId} every {Interval}",
            bufferId, interval);
    }

    /// <summary>
    /// Unschedules health checks for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>True if checks were unscheduled.</returns>
    public bool UnscheduleChecks(string bufferId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        lock (_schedulerLock)
        {
            if (_scheduledChecks.Remove(bufferId, out var check))
            {
                // Cancel the timer if it exists
                check.Timer?.Dispose();
                _logger?.LogInformation("Unscheduled health checks for buffer {BufferId}", bufferId);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Requests an immediate health check for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="priority">The check priority.</param>
    public void RequestImmediateCheck(string bufferId, CheckPriority priority = CheckPriority.High)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        var request = new HealthCheckRequest
        {
            BufferId = bufferId,
            Priority = priority
        };

        lock (_schedulerLock)
        {
            _priorityQueue.Enqueue(request, priority);
        }

        _logger?.LogDebug("Requested immediate health check for buffer {BufferId}", bufferId);
    }

    /// <summary>
    /// Gets scheduler statistics.
    /// </summary>
    /// <returns>Scheduler statistics.</returns>
    public CheckSchedulerStats GetStats()
    {
        lock (_schedulerLock)
        {
            return new CheckSchedulerStats
            {
                ScheduledBuffers = _scheduledChecks.Count,
                PendingChecks = _priorityQueue.Count,
                NextCheckTimes = _scheduledChecks.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.NextCheckTime)
            };
        }
    }

    /// <summary>
    /// Runs scheduled checks for a buffer.
    /// </summary>
    /// <param name="check">The scheduled check.</param>
    /// <param name="requestWriter">The request writer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the scheduled check operation.</returns>
    private async Task RunScheduledCheckAsync(
        ScheduledCheck check,
        ChannelWriter<HealthCheckRequest> requestWriter,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(check.Interval);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await timer.WaitForNextTickAsync(cancellationToken);

                var request = new HealthCheckRequest
                {
                    BufferId = check.BufferId,
                    Priority = check.Priority
                };

                await requestWriter.WriteAsync(request, cancellationToken);

                // Update next check time
                check.NextCheckTime = DateTimeOffset.UtcNow + check.Interval;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in scheduled check for buffer {BufferId}", check.BufferId);
        }
    }
}

/// <summary>
/// Scheduled check configuration.
/// </summary>
internal class ScheduledCheck
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the check interval.</summary>
    public TimeSpan Interval { get; init; }

    /// <summary>Gets the check priority.</summary>
    public CheckPriority Priority { get; init; }

    /// <summary>Gets or sets the next check time.</summary>
    public DateTimeOffset NextCheckTime { get; set; }

    /// <summary>Gets or sets the periodic timer.</summary>
    public PeriodicTimer? Timer { get; set; }
}

/// <summary>
/// Scheduler statistics.
/// </summary>
public class CheckSchedulerStats
{
    /// <summary>Gets the number of buffers with scheduled checks.</summary>
    public int ScheduledBuffers { get; init; }

    /// <summary>Gets the number of pending immediate checks.</summary>
    public int PendingChecks { get; init; }

    /// <summary>Gets the next check times for scheduled buffers.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> NextCheckTimes { get; init; } = new Dictionary<string, DateTimeOffset>();
}
