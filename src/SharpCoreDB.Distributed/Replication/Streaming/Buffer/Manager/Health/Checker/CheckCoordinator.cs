// <copyright file="CheckCoordinator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Coordinates health checks across multiple buffers with load balancing and resource management.
/// Provides intelligent distribution of check execution based on buffer priority and system load.
/// C# 14: Primary constructors, Channel<T> for coordination, pattern matching.
/// </summary>
public sealed class CheckCoordinator : IAsyncDisposable
{
    private readonly CheckExecutor _executor;
    private readonly HealthAlerts _alerts;
    private readonly ILogger<CheckCoordinator>? _logger;

    private readonly Channel<CoordinatedCheckRequest> _coordinationChannel = Channel.CreateBounded<CoordinatedCheckRequest>(1000);
    private readonly Dictionary<string, BufferCheckState> _bufferStates = [];
    private readonly Lock _coordinatorLock = new();

    private readonly CancellationTokenSource _cts = new();

    private Task? _coordinationTask;
    private bool _isRunning;

    /// <summary>Gets the number of coordinated buffers.</summary>
    public int CoordinatedBufferCount => _bufferStates.Count;

    /// <summary>Gets whether the coordinator is running.</summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckCoordinator"/> class.
    /// </summary>
    /// <param name="executor">The check executor.</param>
    /// <param name="alerts">The alerts system.</param>
    /// <param name="logger">Optional logger.</param>
    public CheckCoordinator(CheckExecutor executor, HealthAlerts alerts, ILogger<CheckCoordinator>? logger = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        _logger = logger;
    }

    /// <summary>
    /// Starts the coordination system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartCoordinationAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;

        _logger?.LogInformation("Starting check coordination for {Count} buffers", _bufferStates.Count);

        _coordinationTask = CoordinateChecksAsync(_cts.Token);
        await _alerts.StartProcessingAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the coordination system.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopCoordinationAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _logger?.LogInformation("Stopping check coordination");

        _isRunning = false;
        _cts.Cancel();

        if (_coordinationTask is not null)
        {
            await _coordinationTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await _alerts.StopProcessingAsync();
        _coordinationChannel.Writer.Complete();
    }

    /// <summary>
    /// Registers a buffer for coordination.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="health">The buffer health instance.</param>
    /// <param name="priority">The buffer priority.</param>
    /// <param name="checkInterval">The check interval.</param>
    /// <returns>True if the buffer was registered.</returns>
    public bool RegisterBuffer(
        string bufferId,
        BufferHealth health,
        CheckPriority priority = CheckPriority.Normal,
        TimeSpan? checkInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentNullException.ThrowIfNull(health);

        lock (_coordinatorLock)
        {
            if (_bufferStates.ContainsKey(bufferId))
            {
                return false;
            }

            var state = new BufferCheckState
            {
                BufferId = bufferId,
                Health = health,
                Priority = priority,
                CheckInterval = checkInterval ?? GetDefaultCheckInterval(priority),
                LastCheckTime = DateTimeOffset.MinValue,
                NextCheckTime = DateTimeOffset.UtcNow,
                CheckCount = 0,
                FailureCount = 0
            };

            _bufferStates[bufferId] = state;
        }

        _logger?.LogInformation("Registered buffer {BufferId} for coordination with priority {Priority}",
            bufferId, priority);

        return true;
    }

    /// <summary>
    /// Unregisters a buffer from coordination.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>True if the buffer was unregistered.</returns>
    public bool UnregisterBuffer(string bufferId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        lock (_coordinatorLock)
        {
            if (_bufferStates.Remove(bufferId, out _))
            {
                _logger?.LogInformation("Unregistered buffer {BufferId} from coordination", bufferId);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Requests an immediate coordinated check for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="priority">The check priority.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RequestCoordinatedCheckAsync(
        string bufferId,
        CheckPriority priority = CheckPriority.High,
        CancellationToken cancellationToken = default)
    {
        var request = new CoordinatedCheckRequest
        {
            BufferId = bufferId,
            RequestedPriority = priority,
            RequestTime = DateTimeOffset.UtcNow
        };

        await _coordinationChannel.Writer.WriteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Updates the priority of a registered buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="newPriority">The new priority.</param>
    /// <returns>True if the priority was updated.</returns>
    public bool UpdateBufferPriority(string bufferId, CheckPriority newPriority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        lock (_coordinatorLock)
        {
            if (_bufferStates.TryGetValue(bufferId, out var state))
            {
                state.Priority = newPriority;
                state.CheckInterval = GetDefaultCheckInterval(newPriority);
                _logger?.LogInformation("Updated priority for buffer {BufferId} to {Priority}",
                    bufferId, newPriority);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the coordination statistics.
    /// </summary>
    /// <returns>Coordination statistics.</returns>
    public CheckCoordinatorStats GetStats()
    {
        lock (_coordinatorLock)
        {
            var bufferStats = _bufferStates.ToDictionary(
                kvp => kvp.Key,
                kvp => new BufferCoordinationStats
                {
                    Priority = kvp.Value.Priority,
                    CheckInterval = kvp.Value.CheckInterval,
                    LastCheckTime = kvp.Value.LastCheckTime,
                    NextCheckTime = kvp.Value.NextCheckTime,
                    CheckCount = kvp.Value.CheckCount,
                    FailureCount = kvp.Value.FailureCount,
                    HealthStatus = kvp.Value.Health.CurrentStatus
                });

            return new CheckCoordinatorStats
            {
                IsRunning = _isRunning,
                CoordinatedBuffers = _bufferStates.Count,
                BufferStats = bufferStats,
                ExecutorStats = _executor.GetStats(),
                PendingRequests = _coordinationChannel.Reader.Count
            };
        }
    }

    /// <summary>
    /// Coordinates checks asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the coordination operation.</returns>
    private async Task CoordinateChecksAsync(CancellationToken cancellationToken)
    {
        var checkTasks = new List<Task>();

        try
        {
            await foreach (var request in _coordinationChannel.Reader.ReadAllAsync(cancellationToken))
            {
                // Handle immediate requests
                if (!string.IsNullOrEmpty(request.BufferId))
                {
                    var task = ProcessImmediateRequestAsync(request, cancellationToken);
                    checkTasks.Add(task);
                }

                // Also check for scheduled checks
                var scheduledRequests = GetDueScheduledChecks();
                foreach (var scheduledRequest in scheduledRequests)
                {
                    var task = ProcessScheduledRequestAsync(scheduledRequest, cancellationToken);
                    checkTasks.Add(task);
                }

                // Wait for some tasks to complete to avoid unbounded growth
                if (checkTasks.Count > 10)
                {
                    await Task.WhenAny(checkTasks);
                    checkTasks.RemoveAll(t => t.IsCompleted);
                }
            }

            // Wait for remaining tasks
            if (checkTasks.Count > 0)
            {
                await Task.WhenAll(checkTasks);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in check coordination");
        }
    }

    /// <summary>
    /// Processes an immediate check request.
    /// </summary>
    /// <param name="request">The check request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessImmediateRequestAsync(CoordinatedCheckRequest request, CancellationToken cancellationToken)
    {
        BufferCheckState? state = null;

        lock (_coordinatorLock)
        {
            _bufferStates.TryGetValue(request.BufferId, out state);
        }

        if (state is null)
        {
            _logger?.LogWarning("Received check request for unregistered buffer {BufferId}", request.BufferId);
            return;
        }

        // Use the higher priority
        var effectivePriority = (CheckPriority)Math.Max((int)state.Priority, (int)request.RequestedPriority);

        var assessment = await _executor.ExecuteCheckAsync(
            request.BufferId,
            state.Health,
            cancellationToken: cancellationToken);

        await ProcessAssessmentAsync(request.BufferId, assessment, state, effectivePriority);
    }

    /// <summary>
    /// Processes a scheduled check request.
    /// </summary>
    /// <param name="state">The buffer check state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessScheduledRequestAsync(BufferCheckState state, CancellationToken cancellationToken)
    {
        var assessment = await _executor.ExecuteCheckAsync(
            state.BufferId,
            state.Health,
            cancellationToken: cancellationToken);

        await ProcessAssessmentAsync(state.BufferId, assessment, state, state.Priority);
    }

    /// <summary>
    /// Processes a health assessment result.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="assessment">The health assessment.</param>
    /// <param name="state">The buffer check state.</param>
    /// <param name="priority">The check priority.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessAssessmentAsync(
        string bufferId,
        BufferHealthAssessment assessment,
        BufferCheckState state,
        CheckPriority priority)
    {
        // Update state
        lock (_coordinatorLock)
        {
            state.LastCheckTime = assessment.AssessmentTime;
            state.NextCheckTime = assessment.AssessmentTime + state.CheckInterval;
            state.CheckCount++;

            if (assessment.Status == BufferHealthStatus.Corrupted ||
                assessment.Issues.Any(i => i.Severity >= HealthIssueSeverity.Error))
            {
                state.FailureCount++;
            }
        }

        // Raise alerts for issues
        foreach (var issue in assessment.Issues)
        {
            await _alerts.RaiseAlertAsync(bufferId, issue, assessment);
        }

        _logger?.LogDebug("Processed health assessment for buffer {BufferId}: {Status}",
            bufferId, assessment.Status);
    }

    /// <summary>
    /// Gets scheduled checks that are due.
    /// </summary>
    /// <returns>Collection of due buffer check states.</returns>
    private IReadOnlyCollection<BufferCheckState> GetDueScheduledChecks()
    {
        var now = DateTimeOffset.UtcNow;
        var dueChecks = new List<BufferCheckState>();

        lock (_coordinatorLock)
        {
            foreach (var state in _bufferStates.Values)
            {
                if (state.NextCheckTime <= now)
                {
                    dueChecks.Add(state);
                }
            }
        }

        return dueChecks;
    }

    /// <summary>
    /// Gets the default check interval for a priority level.
    /// </summary>
    /// <param name="priority">The priority level.</param>
    /// <returns>The default check interval.</returns>
    private static TimeSpan GetDefaultCheckInterval(CheckPriority priority)
    {
        return priority switch
        {
            CheckPriority.Critical => TimeSpan.FromSeconds(10),
            CheckPriority.High => TimeSpan.FromSeconds(30),
            CheckPriority.Normal => TimeSpan.FromMinutes(2),
            CheckPriority.Low => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(2)
        };
    }

    /// <summary>
    /// Disposes the coordinator asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopCoordinationAsync();
        _cts.Dispose();
        _coordinationChannel.Writer.Complete();
    }
}

/// <summary>
/// Coordinated check request.
/// </summary>
public class CoordinatedCheckRequest
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the requested priority.</summary>
    public CheckPriority RequestedPriority { get; init; }

    /// <summary>Gets the request timestamp.</summary>
    public DateTimeOffset RequestTime { get; init; }
}

/// <summary>
/// Buffer check state.
/// </summary>
internal class BufferCheckState
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the buffer health instance.</summary>
    public BufferHealth Health { get; init; } = null!;

    /// <summary>Gets or sets the check priority.</summary>
    public CheckPriority Priority { get; set; }

    /// <summary>Gets or sets the check interval.</summary>
    public TimeSpan CheckInterval { get; set; }

    /// <summary>Gets or sets the last check time.</summary>
    public DateTimeOffset LastCheckTime { get; set; }

    /// <summary>Gets or sets the next check time.</summary>
    public DateTimeOffset NextCheckTime { get; set; }

    /// <summary>Gets or sets the total check count.</summary>
    public int CheckCount { get; set; }

    /// <summary>Gets or sets the failure count.</summary>
    public int FailureCount { get; set; }
}

/// <summary>
/// Coordinator statistics.
/// </summary>
public class CheckCoordinatorStats
{
    /// <summary>Gets whether the coordinator is running.</summary>
    public bool IsRunning { get; init; }

    /// <summary>Gets the number of coordinated buffers.</summary>
    public int CoordinatedBuffers { get; init; }

    /// <summary>Gets the buffer coordination statistics.</summary>
    public IReadOnlyDictionary<string, BufferCoordinationStats> BufferStats { get; init; } = new Dictionary<string, BufferCoordinationStats>();

    /// <summary>Gets the executor statistics.</summary>
    public CheckExecutorStats ExecutorStats { get; init; } = new();

    /// <summary>Gets the number of pending requests.</summary>
    public int PendingRequests { get; init; }
}

/// <summary>
/// Buffer coordination statistics.
/// </summary>
public class BufferCoordinationStats
{
    /// <summary>Gets the buffer priority.</summary>
    public CheckPriority Priority { get; init; }

    /// <summary>Gets the check interval.</summary>
    public TimeSpan CheckInterval { get; init; }

    /// <summary>Gets the last check time.</summary>
    public DateTimeOffset LastCheckTime { get; init; }

    /// <summary>Gets the next check time.</summary>
    public DateTimeOffset NextCheckTime { get; init; }

    /// <summary>Gets the total check count.</summary>
    public int CheckCount { get; init; }

    /// <summary>Gets the failure count.</summary>
    public int FailureCount { get; init; }

    /// <summary>Gets the current health status.</summary>
    public BufferHealthStatus HealthStatus { get; init; }

    /// <summary>Gets the time until next check.</summary>
    public TimeSpan TimeUntilNextCheck => NextCheckTime - DateTimeOffset.UtcNow;

    /// <summary>Gets the failure rate.</summary>
    public double FailureRate => CheckCount > 0 ? (double)FailureCount / CheckCount : 0;
}
