// <copyright file="LifecycleMetrics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Performance metrics for buffer lifecycle operations.
/// Provides detailed timing and throughput analysis.
/// C# 14: Collection expressions, pattern matching, performance counters.
/// </summary>
public sealed class LifecycleMetrics
{
    private readonly CircularBuffer<LifecycleOperation> _operationHistory;
    private readonly Dictionary<string, OperationMetrics> _operationMetrics = [];
    private readonly Lock _metricsLock = new();

    private long _totalOperations;
    private TimeSpan _totalOperationTime;

    /// <summary>Gets the total number of operations recorded.</summary>
    public long TotalOperations => _totalOperations;

    /// <summary>Gets the total time spent on all operations.</summary>
    public TimeSpan TotalOperationTime => _totalOperationTime;

    /// <summary>Gets the average operation time.</summary>
    public TimeSpan AverageOperationTime => _totalOperations > 0
        ? TimeSpan.FromTicks(_totalOperationTime.Ticks / _totalOperations)
        : TimeSpan.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="LifecycleMetrics"/> class.
    /// </summary>
    /// <param name="historySize">Number of operations to keep in history.</param>
    public LifecycleMetrics(int historySize = 10000)
    {
        _operationHistory = new CircularBuffer<LifecycleOperation>(historySize);
    }

    /// <summary>
    /// Records the timing of a lifecycle operation.
    /// </summary>
    /// <param name="operationType">The type of operation.</param>
    /// <param name="duration">The operation duration.</param>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="size">The buffer size (if applicable).</param>
    /// <param name="source">The operation source.</param>
    public void RecordOperation(
        LifecycleOperationType operationType,
        TimeSpan duration,
        string bufferId,
        int? size = null,
        string? source = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        var operation = new LifecycleOperation
        {
            OperationType = operationType,
            Duration = duration,
            BufferId = bufferId,
            Size = size,
            Source = source,
            Timestamp = DateTimeOffset.UtcNow
        };

        lock (_metricsLock)
        {
            // Update global counters
            Interlocked.Increment(ref _totalOperations);
            _totalOperationTime += duration;

            // Update operation-specific metrics
            var key = operationType.ToString();
            if (!_operationMetrics.TryGetValue(key, out var metrics))
            {
                metrics = new OperationMetrics { OperationType = operationType };
                _operationMetrics[key] = metrics;
            }

            metrics.RecordOperation(duration, size);

            // Add to history
            _operationHistory.Add(operation);
        }
    }

    /// <summary>
    /// Gets metrics for a specific operation type.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <returns>The operation metrics, or null if not found.</returns>
    public OperationMetrics? GetOperationMetrics(LifecycleOperationType operationType)
    {
        lock (_metricsLock)
        {
            return _operationMetrics.TryGetValue(operationType.ToString(), out var metrics) ? metrics : null;
        }
    }

    /// <summary>
    /// Gets all operation metrics.
    /// </summary>
    /// <returns>Collection of all operation metrics.</returns>
    public IReadOnlyCollection<OperationMetrics> GetAllOperationMetrics()
    {
        lock (_metricsLock)
        {
            return [.. _operationMetrics.Values];
        }
    }

    /// <summary>
    /// Gets recent operation history.
    /// </summary>
    /// <param name="count">Number of recent operations to retrieve.</param>
    /// <returns>Collection of recent operations.</returns>
    public IReadOnlyCollection<LifecycleOperation> GetRecentOperations(int count = 100)
    {
        lock (_metricsLock)
        {
            var operations = new List<LifecycleOperation>();
            var startIndex = Math.Max(0, _operationHistory.Count - count);

            for (var i = startIndex; i < _operationHistory.Count; i++)
            {
                operations.Add(_operationHistory[i]);
            }

            return operations;
        }
    }

    /// <summary>
    /// Gets comprehensive lifecycle performance statistics.
    /// </summary>
    /// <returns>Lifecycle performance statistics.</returns>
    public LifecyclePerformanceStats GetPerformanceStats()
    {
        lock (_metricsLock)
        {
            var operationMetrics = GetAllOperationMetrics();
            var throughput = CalculateThroughput();
            var bottlenecks = IdentifyBottlenecks(operationMetrics);
            var trends = AnalyzeTrends();

            return new LifecyclePerformanceStats
            {
                TotalOperations = TotalOperations,
                TotalOperationTime = TotalOperationTime,
                AverageOperationTime = AverageOperationTime,
                OperationsPerSecond = throughput,
                OperationMetrics = operationMetrics,
                PerformanceBottlenecks = bottlenecks,
                PerformanceTrends = trends,
                HealthScore = CalculateHealthScore(operationMetrics, throughput)
            };
        }
    }

    /// <summary>
    /// Gets operations that exceed a performance threshold.
    /// </summary>
    /// <param name="threshold">The performance threshold.</param>
    /// <returns>Collection of slow operations.</returns>
    public IReadOnlyCollection<LifecycleOperation> GetSlowOperations(TimeSpan threshold)
    {
        lock (_metricsLock)
        {
            var slowOps = new List<LifecycleOperation>();

            foreach (var operation in _operationHistory)
            {
                if (operation.Duration > threshold)
                {
                    slowOps.Add(operation);
                }
            }

            return slowOps;
        }
    }

    /// <summary>
    /// Gets operation frequency by time window.
    /// </summary>
    /// <param name="timeWindow">The time window to analyze.</param>
    /// <returns>Operation frequency by time.</returns>
    public IReadOnlyDictionary<DateTimeOffset, int> GetOperationFrequency(TimeSpan timeWindow)
    {
        lock (_metricsLock)
        {
            var frequency = new Dictionary<DateTimeOffset, int>();
            var now = DateTimeOffset.UtcNow;
            var windowStart = now - timeWindow;

            foreach (var operation in _operationHistory)
            {
                if (operation.Timestamp >= windowStart)
                {
                    var timeBucket = new DateTimeOffset(
                        operation.Timestamp.Ticks / timeWindow.Ticks * timeWindow.Ticks,
                        operation.Timestamp.Offset);

                    frequency[timeBucket] = frequency.GetValueOrDefault(timeBucket) + 1;
                }
            }

            return frequency;
        }
    }

    /// <summary>
    /// Resets all metrics and history.
    /// </summary>
    public void Reset()
    {
        lock (_metricsLock)
        {
            _totalOperations = 0;
            _totalOperationTime = TimeSpan.Zero;
            _operationMetrics.Clear();

            // Note: We don't clear history as it might be needed for analysis
        }
    }

    /// <summary>
    /// Calculates the current throughput in operations per second.
    /// </summary>
    /// <returns>Operations per second.</returns>
    private double CalculateThroughput()
    {
        if (_operationHistory.Count < 2)
        {
            return 0;
        }

        var earliest = _operationHistory[0].Timestamp;
        var latest = _operationHistory[^1].Timestamp;
        var timeSpan = latest - earliest;

        if (timeSpan.TotalSeconds == 0)
        {
            return 0;
        }

        return _operationHistory.Count / timeSpan.TotalSeconds;
    }

    /// <summary>
    /// Identifies performance bottlenecks.
    /// </summary>
    /// <param name="operationMetrics">The operation metrics.</param>
    /// <returns>List of identified bottlenecks.</returns>
    private IReadOnlyList<string> IdentifyBottlenecks(IReadOnlyCollection<OperationMetrics> operationMetrics)
    {
        var bottlenecks = new List<string>();

        foreach (var metrics in operationMetrics)
        {
            if (metrics.AverageDuration > TimeSpan.FromMilliseconds(10))
            {
                bottlenecks.Add($"{metrics.OperationType} operations are slow (avg: {metrics.AverageDuration.TotalMilliseconds:F2}ms)");
            }

            if (metrics.P95Duration > TimeSpan.FromMilliseconds(50))
            {
                bottlenecks.Add($"{metrics.OperationType} operations have high P95 latency ({metrics.P95Duration.TotalMilliseconds:F2}ms)");
            }
        }

        if (CalculateThroughput() < 1000)
        {
            bottlenecks.Add("Overall throughput is low (< 1000 ops/sec)");
        }

        return bottlenecks;
    }

    /// <summary>
    /// Analyzes performance trends over time.
    /// </summary>
    /// <returns>Performance trend analysis.</returns>
    private LifecyclePerformanceTrends AnalyzeTrends()
    {
        if (_operationHistory.Count < 10)
        {
            return new LifecyclePerformanceTrends
            {
                TrendDirection = PerformanceTrend.Stable,
                TrendMagnitude = 0,
                Description = "Insufficient data for trend analysis"
            };
        }

        // Simple trend analysis - compare first half vs second half
        var midpoint = _operationHistory.Count / 2;
        var firstHalf = _operationHistory.Take(midpoint);
        var secondHalf = _operationHistory.Skip(midpoint);

        var firstHalfAvg = firstHalf.Average(op => op.Duration.TotalMilliseconds);
        var secondHalfAvg = secondHalf.Average(op => op.Duration.TotalMilliseconds);

        var changePercent = (secondHalfAvg - firstHalfAvg) / firstHalfAvg * 100;
        var trend = Math.Abs(changePercent) < 5 ? PerformanceTrend.Stable :
                   changePercent > 0 ? PerformanceTrend.Degrading : PerformanceTrend.Improving;

        return new LifecyclePerformanceTrends
        {
            TrendDirection = trend,
            TrendMagnitude = Math.Abs(changePercent),
            Description = $"{trend} performance ({changePercent:F1}% change)"
        };
    }

    /// <summary>
    /// Calculates an overall health score (0-100).
    /// </summary>
    /// <param name="operationMetrics">The operation metrics.</param>
    /// <param name="throughput">The current throughput.</param>
    /// <returns>Health score.</returns>
    private double CalculateHealthScore(IReadOnlyCollection<OperationMetrics> operationMetrics, double throughput)
    {
        var score = 100.0;

        // Deduct points for slow operations
        foreach (var metrics in operationMetrics)
        {
            if (metrics.AverageDuration > TimeSpan.FromMilliseconds(5))
            {
                score -= 10;
            }
            if (metrics.P95Duration > TimeSpan.FromMilliseconds(20))
            {
                score -= 15;
            }
        }

        // Deduct points for low throughput
        if (throughput < 5000) score -= 20;
        else if (throughput < 1000) score -= 40;

        return Math.Max(0, Math.Min(100, score));
    }
}

/// <summary>
/// Metrics for a specific type of lifecycle operation.
/// </summary>
public class OperationMetrics
{
    private readonly List<TimeSpan> _durations = [];
    private readonly List<int?> _sizes = [];
    private readonly Lock _operationLock = new();

    /// <summary>Gets the operation type.</summary>
    public LifecycleOperationType OperationType { get; init; }

    /// <summary>Gets the number of operations recorded.</summary>
    public int OperationCount => _durations.Count;

    /// <summary>Gets the average operation duration.</summary>
    public TimeSpan AverageDuration => _durations.Any()
        ? TimeSpan.FromTicks((long)_durations.Average(d => d.Ticks))
        : TimeSpan.Zero;

    /// <summary>Gets the minimum operation duration.</summary>
    public TimeSpan MinDuration => _durations.Any() ? _durations.Min() : TimeSpan.Zero;

    /// <summary>Gets the maximum operation duration.</summary>
    public TimeSpan MaxDuration => _durations.Any() ? _durations.Max() : TimeSpan.Zero;

    /// <summary>Gets the 95th percentile duration.</summary>
    public TimeSpan P95Duration => CalculatePercentile(95);

    /// <summary>Gets the average buffer size (if applicable).</summary>
    public double AverageSize => _sizes.Where(s => s.HasValue).Any()
        ? _sizes.Where(s => s.HasValue).Average(s => s!.Value)
        : 0;

    /// <summary>
    /// Records an operation.
    /// </summary>
    /// <param name="duration">The operation duration.</param>
    /// <param name="size">The buffer size (if applicable).</param>
    public void RecordOperation(TimeSpan duration, int? size = null)
    {
        lock (_operationLock)
        {
            _durations.Add(duration);
            _sizes.Add(size);

            // Keep only recent operations to avoid unbounded growth
            if (_durations.Count > 1000)
            {
                _durations.RemoveAt(0);
                _sizes.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Calculates the percentile duration.
    /// </summary>
    /// <param name="percentile">The percentile to calculate (0-100).</param>
    /// <returns>The percentile duration.</returns>
    private TimeSpan CalculatePercentile(int percentile)
    {
        lock (_operationLock)
        {
            if (!_durations.Any())
            {
                return TimeSpan.Zero;
            }

            var sorted = _durations.OrderBy(d => d).ToList();
            var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Count) - 1;
            index = Math.Max(0, Math.Min(index, sorted.Count - 1));

            return sorted[index];
        }
    }
}

/// <summary>
/// Represents a single lifecycle operation.
/// </summary>
public class LifecycleOperation
{
    /// <summary>Gets the operation type.</summary>
    public LifecycleOperationType OperationType { get; init; }

    /// <summary>Gets the operation duration.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the buffer size (if applicable).</summary>
    public int? Size { get; init; }

    /// <summary>Gets the operation source.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the operation timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Lifecycle operation types.
/// </summary>
public enum LifecycleOperationType
{
    /// <summary>Buffer allocation operation.</summary>
    Allocate,

    /// <summary>Buffer rental operation.</summary>
    Rent,

    /// <summary>Buffer return operation.</summary>
    Return,

    /// <summary>Buffer deallocation operation.</summary>
    Deallocate,

    /// <summary>Buffer validation operation.</summary>
    Validate,

    /// <summary>Buffer cleanup operation.</summary>
    Cleanup
}

/// <summary>
/// Comprehensive lifecycle performance statistics.
/// </summary>
public class LifecyclePerformanceStats
{
    /// <summary>Gets the total number of operations.</summary>
    public long TotalOperations { get; init; }

    /// <summary>Gets the total operation time.</summary>
    public TimeSpan TotalOperationTime { get; init; }

    /// <summary>Gets the average operation time.</summary>
    public TimeSpan AverageOperationTime { get; init; }

    /// <summary>Gets the operations per second.</summary>
    public double OperationsPerSecond { get; init; }

    /// <summary>Gets the operation-specific metrics.</summary>
    public IReadOnlyCollection<OperationMetrics> OperationMetrics { get; init; } = [];

    /// <summary>Gets the identified performance bottlenecks.</summary>
    public IReadOnlyList<string> PerformanceBottlenecks { get; init; } = [];

    /// <summary>Gets the performance trends.</summary>
    public LifecyclePerformanceTrends PerformanceTrends { get; init; } = new();

    /// <summary>Gets the overall health score (0-100).</summary>
    public double HealthScore { get; init; }

    /// <summary>Gets a human-readable performance summary.</summary>
    public string PerformanceSummary
    {
        get
        {
            var health = HealthScore switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good",
                >= 60 => "Fair",
                >= 40 => "Poor",
                _ => "Critical"
            };

            return $"{health} performance ({OperationsPerSecond:F0} ops/sec, {AverageOperationTime.TotalMilliseconds:F2}ms avg)";
        }
    }
}

/// <summary>
/// Performance trend analysis.
/// </summary>
public class LifecyclePerformanceTrends
{
    /// <summary>Gets the trend direction.</summary>
    public PerformanceTrend TrendDirection { get; init; }

    /// <summary>Gets the trend magnitude (percentage change).</summary>
    public double TrendMagnitude { get; init; }

    /// <summary>Gets the trend description.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Performance trend directions.
/// </summary>
public enum PerformanceTrend
{
    /// <summary>Performance is improving.</summary>
    Improving,

    /// <summary>Performance is stable.</summary>
    Stable,

    /// <summary>Performance is degrading.</summary>
    Degrading
}
