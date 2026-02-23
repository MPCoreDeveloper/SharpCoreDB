// <copyright file="CheckExecutor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Efficient executor for health checks with parallel processing and resource management.
/// Provides optimized execution with timeout handling and load balancing.
/// C# 14: Primary constructors, parallel processing with PLINQ.
/// </summary>
public sealed class CheckExecutor
{
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly ILogger<CheckExecutor>? _logger;

    /// <summary>Gets the maximum concurrent checks.</summary>
    public int MaxConcurrency { get; }

    /// <summary>Gets the default check timeout.</summary>
    public TimeSpan DefaultTimeout { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckExecutor"/> class.
    /// </summary>
    /// <param name="maxConcurrency">Maximum concurrent health checks.</param>
    /// <param name="defaultTimeout">Default timeout for health checks.</param>
    /// <param name="logger">Optional logger.</param>
    public CheckExecutor(
        int maxConcurrency = 10,
        TimeSpan? defaultTimeout = null,
        ILogger<CheckExecutor>? logger = null)
    {
        MaxConcurrency = Math.Max(1, maxConcurrency);
        DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        _concurrencyLimiter = new SemaphoreSlim(MaxConcurrency);
        _logger = logger;
    }

    /// <summary>
    /// Executes a single health check with timeout and error handling.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="health">The buffer health instance.</param>
    /// <param name="timeout">Optional timeout override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health assessment result.</returns>
    public async Task<BufferHealthAssessment> ExecuteCheckAsync(
        string bufferId,
        BufferHealth health,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentNullException.ThrowIfNull(health);

        await _concurrencyLimiter.WaitAsync(cancellationToken);

        try
        {
            var checkTimeout = timeout ?? DefaultTimeout;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(checkTimeout);

            _logger?.LogDebug("Executing health check for buffer {BufferId}", bufferId);

            var startTime = DateTimeOffset.UtcNow;

            // Execute the health check with timeout
            var assessment = await Task.Run(() => health.AssessHealth(), linkedCts.Token);

            var duration = DateTimeOffset.UtcNow - startTime;

            _logger?.LogDebug("Health check completed for buffer {BufferId} in {Duration}ms: {Status}",
                bufferId, duration.TotalMilliseconds, assessment.Status);

            return assessment;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning("Health check timed out for buffer {BufferId}", bufferId);

            return new BufferHealthAssessment
            {
                Status = BufferHealthStatus.Degraded,
                Issues = [new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Warning,
                    Category = HealthIssueCategory.Performance,
                    Title = "Health Check Timeout",
                    Description = $"Health check exceeded timeout of {DefaultTimeout.TotalSeconds} seconds"
                }],
                AssessmentTime = DateTimeOffset.UtcNow,
                Recommendations = ["Consider increasing timeout or optimizing health checks"]
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing health check for buffer {BufferId}", bufferId);

            return new BufferHealthAssessment
            {
                Status = BufferHealthStatus.Corrupted,
                Issues = [new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Critical,
                    Category = HealthIssueCategory.System,
                    Title = "Health Check Failed",
                    Description = $"Exception during health check: {ex.Message}"
                }],
                AssessmentTime = DateTimeOffset.UtcNow,
                Recommendations = ["Investigate system health and check buffer integrity"]
            };
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Executes health checks for multiple buffers in parallel.
    /// </summary>
    /// <param name="checks">The health checks to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of buffer IDs to health assessments.</returns>
    public async Task<IReadOnlyDictionary<string, BufferHealthAssessment>> ExecuteBatchChecksAsync(
        IReadOnlyDictionary<string, BufferHealth> checks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (checks.Count == 0)
        {
            return new Dictionary<string, BufferHealthAssessment>();
        }

        _logger?.LogInformation("Executing batch health checks for {Count} buffers", checks.Count);

        var startTime = DateTimeOffset.UtcNow;

        // Execute checks in parallel with controlled concurrency
        var tasks = checks.Select(kvp =>
            ExecuteCheckAsync(kvp.Key, kvp.Value, cancellationToken: cancellationToken));

        var results = await Task.WhenAll(tasks);

        var resultDict = checks.Keys.Zip(results, (key, result) => new { key, result })
            .ToDictionary(x => x.key, x => x.result);

        var duration = DateTimeOffset.UtcNow - startTime;

        _logger?.LogInformation("Batch health checks completed in {Duration}ms", duration.TotalMilliseconds);

        return resultDict;
    }

    /// <summary>
    /// Executes health checks with priority ordering.
    /// </summary>
    /// <param name="priorityChecks">The prioritized health checks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of buffer IDs to health assessments.</returns>
    public async Task<IReadOnlyDictionary<string, BufferHealthAssessment>> ExecutePriorityChecksAsync(
        IReadOnlyDictionary<string, (BufferHealth Health, CheckPriority Priority)> priorityChecks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(priorityChecks);

        if (priorityChecks.Count == 0)
        {
            return new Dictionary<string, BufferHealthAssessment>();
        }

        // Group by priority and execute in priority order
        var groupedByPriority = priorityChecks
            .GroupBy(kvp => kvp.Value.Priority)
            .OrderByDescending(g => g.Key); // Critical first, then High, etc.

        var results = new Dictionary<string, BufferHealthAssessment>();

        foreach (var priorityGroup in groupedByPriority)
        {
            var groupChecks = priorityGroup.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Health);

            var groupResults = await ExecuteBatchChecksAsync(groupChecks, cancellationToken);

            foreach (var kvp in groupResults)
            {
                results[kvp.Key] = kvp.Value;
            }
        }

        return results;
    }

    /// <summary>
    /// Gets executor statistics.
    /// </summary>
    /// <returns>Executor statistics.</returns>
    public CheckExecutorStats GetStats()
    {
        return new CheckExecutorStats
        {
            MaxConcurrency = MaxConcurrency,
            CurrentConcurrency = MaxConcurrency - _concurrencyLimiter.CurrentCount,
            DefaultTimeout = DefaultTimeout
        };
    }

    /// <summary>
    /// Executes a health check with retry logic.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="health">The buffer health instance.</param>
    /// <param name="maxRetries">Maximum number of retries.</param>
    /// <param name="retryDelay">Delay between retries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The health assessment result.</returns>
    public async Task<BufferHealthAssessment> ExecuteCheckWithRetryAsync(
        string bufferId,
        BufferHealth health,
        int maxRetries = 3,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(100);

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await ExecuteCheckAsync(bufferId, health, cancellationToken: cancellationToken);

                // If successful or not a transient error, return result
                if (result.Status != BufferHealthStatus.Corrupted ||
                    result.Issues.All(i => i.Severity != HealthIssueSeverity.Critical))
                {
                    return result;
                }

                // If this was the last attempt, return the result
                if (attempt == maxRetries)
                {
                    return result;
                }

                _logger?.LogWarning("Health check failed for buffer {BufferId}, retrying (attempt {Attempt}/{MaxRetries})",
                    bufferId, attempt + 1, maxRetries + 1);

                await Task.Delay(delay, cancellationToken);
                delay = delay * 2; // Exponential backoff
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        // Should not reach here
        throw new InvalidOperationException("Unexpected end of retry loop");
    }
}

/// <summary>
/// Executor statistics.
/// </summary>
public class CheckExecutorStats
{
    /// <summary>Gets the maximum concurrency level.</summary>
    public int MaxConcurrency { get; init; }

    /// <summary>Gets the current concurrency level.</summary>
    public int CurrentConcurrency { get; init; }

    /// <summary>Gets the default timeout.</summary>
    public TimeSpan DefaultTimeout { get; init; }

    /// <summary>Gets the available concurrency slots.</summary>
    public int AvailableConcurrency => MaxConcurrency - CurrentConcurrency;

    /// <summary>Gets the concurrency utilization as a percentage.</summary>
    public double ConcurrencyUtilization => MaxConcurrency > 0
        ? (double)CurrentConcurrency / MaxConcurrency * 100
        : 0;
}

/// <summary>
/// Factory for creating check executors with different configurations.
/// </summary>
public static class CheckExecutorFactory
{
    /// <summary>
    /// Creates a high-performance executor optimized for speed.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A high-performance executor.</returns>
    public static CheckExecutor CreateHighPerformance(ILogger<CheckExecutor>? logger = null)
    {
        return new CheckExecutor(
            maxConcurrency: 50,
            defaultTimeout: TimeSpan.FromSeconds(5),
            logger: logger);
    }

    /// <summary>
    /// Creates a conservative executor optimized for resource usage.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A conservative executor.</returns>
    public static CheckExecutor CreateConservative(ILogger<CheckExecutor>? logger = null)
    {
        return new CheckExecutor(
            maxConcurrency: 5,
            defaultTimeout: TimeSpan.FromSeconds(60),
            logger: logger);
    }

    /// <summary>
    /// Creates a balanced executor with moderate settings.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A balanced executor.</returns>
    public static CheckExecutor CreateBalanced(ILogger<CheckExecutor>? logger = null)
    {
        return new CheckExecutor(
            maxConcurrency: 20,
            defaultTimeout: TimeSpan.FromSeconds(15),
            logger: logger);
    }
}
