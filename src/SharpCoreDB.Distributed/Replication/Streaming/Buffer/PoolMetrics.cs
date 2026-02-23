// <copyright file="PoolMetrics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Metrics for buffer pool utilization and performance.
/// Provides comprehensive statistics for monitoring and optimization.
/// </summary>
public class PoolMetrics
{
    /// <summary>Gets the total number of buffers created since pool initialization.</summary>
    public int TotalBuffersCreated { get; init; }

    /// <summary>Gets the current number of buffers in the pool.</summary>
    public int CurrentBufferCount { get; init; }

    /// <summary>Gets the number of buffers currently available for rent.</summary>
    public int AvailableBuffers { get; init; }

    /// <summary>Gets the pool utilization as a percentage (0-100).</summary>
    public double PoolUtilizationPercent { get; init; }

    /// <summary>Gets the average buffer size in bytes.</summary>
    public double AverageBufferSize { get; init; }

    /// <summary>Gets the total memory usage of all buffers in the pool.</summary>
    public long TotalMemoryUsage { get; init; }

    /// <summary>Gets the timestamp when these metrics were collected.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets the number of buffers currently rented out.</summary>
    public int RentedBuffers => CurrentBufferCount - AvailableBuffers;

    /// <summary>Gets the memory usage of rented buffers.</summary>
    public long RentedMemoryUsage => (long)(TotalMemoryUsage * ((double)RentedBuffers / Math.Max(1, CurrentBufferCount)));

    /// <summary>Gets the memory usage of available buffers.</summary>
    public long AvailableMemoryUsage => TotalMemoryUsage - RentedMemoryUsage;

    /// <summary>Gets the pool efficiency rating (higher is better).</summary>
    public double EfficiencyRating
    {
        get
        {
            if (TotalBuffersCreated == 0)
            {
                return 1.0; // Perfect efficiency for no allocations
            }

            // Efficiency based on reuse: (total operations) / (buffers created)
            // This is a simplified metric - in practice you'd track actual rentals
            var reuseRatio = (double)(CurrentBufferCount + RentedBuffers) / TotalBuffersCreated;
            return Math.Min(reuseRatio, 1.0);
        }
    }

    /// <summary>Gets a human-readable status description.</summary>
    public string StatusDescription
    {
        get
        {
            var utilization = PoolUtilizationPercent;
            return utilization switch
            {
                < 25 => "Underutilized",
                < 75 => "Normal utilization",
                < 90 => "High utilization",
                _ => "Critically full"
            };
        }
    }

    /// <summary>
    /// Creates a snapshot of metrics with additional calculated values.
    /// </summary>
    /// <returns>A metrics snapshot with additional analysis.</returns>
    public PoolMetricsSnapshot CreateSnapshot()
    {
        return new PoolMetricsSnapshot
        {
            BaseMetrics = this,
            MemoryEfficiencyPercent = CalculateMemoryEfficiency(),
            AllocationRate = CalculateAllocationRate(),
            Recommendations = GenerateRecommendations()
        };
    }

    /// <summary>
    /// Calculates memory efficiency based on buffer sizes and utilization.
    /// </summary>
    /// <returns>Memory efficiency percentage.</returns>
    private double CalculateMemoryEfficiency()
    {
        if (CurrentBufferCount == 0 || AverageBufferSize == 0)
        {
            return 100.0;
        }

        // Efficiency based on how well buffer sizes match usage patterns
        // This is a simplified calculation - real implementation would track actual usage
        var utilizationFactor = PoolUtilizationPercent / 100.0;
        return Math.Min(utilizationFactor * 100.0, 100.0);
    }

    /// <summary>
    /// Calculates the buffer allocation rate (buffers created per minute).
    /// </summary>
    /// <returns>Allocation rate.</returns>
    private double CalculateAllocationRate()
    {
        // This would need historical data to calculate properly
        // For now, return a placeholder
        return TotalBuffersCreated > 0 ? TotalBuffersCreated / 10.0 : 0.0;
    }

    /// <summary>
    /// Generates optimization recommendations based on current metrics.
    /// </summary>
    /// <returns>List of recommendations.</returns>
    private IReadOnlyList<string> GenerateRecommendations()
    {
        var recommendations = new List<string>();

        if (PoolUtilizationPercent > 90)
        {
            recommendations.Add("Consider increasing MaxBuffers to reduce allocation pressure");
        }
        else if (PoolUtilizationPercent < 25)
        {
            recommendations.Add("Consider decreasing MaxBuffers to reduce memory usage");
        }

        if (EfficiencyRating < 0.5)
        {
            recommendations.Add("Buffer reuse is low - consider adjusting buffer sizes or access patterns");
        }

        if (TotalBuffersCreated > CurrentBufferCount * 2)
        {
            recommendations.Add("High buffer creation rate - consider increasing InitialBuffers");
        }

        return recommendations;
    }

    /// <summary>
    /// Formats the metrics as a human-readable string.
    /// </summary>
    /// <returns>Formatted metrics string.</returns>
    public override string ToString()
    {
        return $"BufferPool Metrics: {AvailableBuffers}/{CurrentBufferCount} available, " +
               $"{PoolUtilizationPercent:F1}% utilized, {TotalMemoryUsage / 1024}KB memory, " +
               $"{EfficiencyRating:P1} efficient";
    }
}

/// <summary>
/// Extended metrics snapshot with analysis and recommendations.
/// </summary>
public class PoolMetricsSnapshot
{
    /// <summary>Gets the base pool metrics.</summary>
    public PoolMetrics BaseMetrics { get; init; } = new();

    /// <summary>Gets the memory efficiency percentage.</summary>
    public double MemoryEfficiencyPercent { get; init; }

    /// <summary>Gets the buffer allocation rate (buffers/minute).</summary>
    public double AllocationRate { get; init; }

    /// <summary>Gets optimization recommendations.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>Gets whether the pool is in a healthy state.</summary>
    public bool IsHealthy
    {
        get
        {
            return BaseMetrics.PoolUtilizationPercent < 95 && // Not critically full
                   BaseMetrics.EfficiencyRating > 0.3 && // Reasonable reuse
                   Recommendations.Count < 3; // Not too many issues
        }
    }

    /// <summary>
    /// Gets a summary of the pool health.
    /// </summary>
    public string HealthSummary
    {
        get
        {
            if (IsHealthy)
            {
                return "Pool is operating normally";
            }

            var issues = Recommendations.Count;
            return issues switch
            {
                0 => "Pool is healthy but could be optimized",
                1 => "Pool has minor optimization opportunities",
                2 => "Pool needs attention",
                _ => "Pool requires immediate optimization"
            };
        }
    }
}

/// <summary>
/// Collector for aggregating pool metrics over time.
/// </summary>
public class PoolMetricsCollector
{
    private readonly CircularBuffer<PoolMetrics> _metricsHistory;
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PoolMetricsCollector"/> class.
    /// </summary>
    /// <param name="historySize">Number of metrics snapshots to keep.</param>
    public PoolMetricsCollector(int historySize = 100)
    {
        _metricsHistory = new CircularBuffer<PoolMetrics>(historySize);
    }

    /// <summary>
    /// Records a new metrics snapshot.
    /// </summary>
    /// <param name="metrics">The metrics to record.</param>
    public void RecordMetrics(PoolMetrics metrics)
    {
        lock (_lock)
        {
            _metricsHistory.Add(metrics);
        }
    }

    /// <summary>
    /// Gets the most recent metrics.
    /// </summary>
    /// <returns>The latest metrics, or null if no metrics recorded.</returns>
    public PoolMetrics? GetLatestMetrics()
    {
        lock (_lock)
        {
            return _metricsHistory.Count > 0 ? _metricsHistory[^1] : null;
        }
    }

    /// <summary>
    /// Gets metrics history.
    /// </summary>
    /// <returns>Array of historical metrics.</returns>
    public PoolMetrics[] GetMetricsHistory()
    {
        lock (_lock)
        {
            return [.. _metricsHistory];
        }
    }

    /// <summary>
    /// Gets aggregated statistics over the metrics history.
    /// </summary>
    /// <returns>Aggregated statistics.</returns>
    public PoolAggregatedStats GetAggregatedStats()
    {
        lock (_lock)
        {
            if (_metricsHistory.Count == 0)
            {
                return new PoolAggregatedStats();
            }

            var avgUtilization = _metricsHistory.Average(m => m.PoolUtilizationPercent);
            var maxUtilization = _metricsHistory.Max(m => m.PoolUtilizationPercent);
            var avgEfficiency = _metricsHistory.Average(m => m.EfficiencyRating);
            var totalAllocations = _metricsHistory.Sum(m => m.TotalBuffersCreated);

            return new PoolAggregatedStats
            {
                SampleCount = _metricsHistory.Count,
                AverageUtilizationPercent = avgUtilization,
                MaxUtilizationPercent = maxUtilization,
                AverageEfficiencyRating = avgEfficiency,
                TotalAllocations = totalAllocations,
                TimeSpan = _metricsHistory.Count > 1
                    ? _metricsHistory[^1].Timestamp - _metricsHistory[0].Timestamp
                    : TimeSpan.Zero
            };
        }
    }
}

/// <summary>
/// Aggregated statistics over a period of time.
/// </summary>
public class PoolAggregatedStats
{
    /// <summary>Gets the number of samples in the aggregation.</summary>
    public int SampleCount { get; init; }

    /// <summary>Gets the average pool utilization percentage.</summary>
    public double AverageUtilizationPercent { get; init; }

    /// <summary>Gets the maximum pool utilization percentage.</summary>
    public double MaxUtilizationPercent { get; init; }

    /// <summary>Gets the average efficiency rating.</summary>
    public double AverageEfficiencyRating { get; init; }

    /// <summary>Gets the total number of buffer allocations.</summary>
    public int TotalAllocations { get; init; }

    /// <summary>Gets the time span covered by the statistics.</summary>
    public TimeSpan TimeSpan { get; init; }

    /// <summary>Gets the allocation rate (allocations per minute).</summary>
    public double AllocationRatePerMinute =>
        TimeSpan.TotalMinutes > 0 ? TotalAllocations / TimeSpan.TotalMinutes : 0;
}

/// <summary>
/// Simple circular buffer implementation.
/// </summary>
/// <typeparam name="T">The type of items in the buffer.</typeparam>
internal class CircularBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private int _start;
    private int _count;

    /// <summary>Gets the number of items in the buffer.</summary>
    public int Count => _count;

    /// <summary>Gets the buffer capacity.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="CircularBuffer{T}"/> class.
    /// </summary>
    /// <param name="capacity">The buffer capacity.</param>
    public CircularBuffer(int capacity)
    {
        _buffer = new T[capacity];
    }

    /// <summary>Gets or sets the item at the specified index.</summary>
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _buffer[(_start + index) % Capacity];
        }
    }

    /// <summary>Adds an item to the buffer.</summary>
    public void Add(T item)
    {
        _buffer[(_start + _count) % Capacity] = item;

        if (_count < Capacity)
        {
            _count++;
        }
        else
        {
            _start = (_start + 1) % Capacity;
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the buffer.
    /// </summary>
    /// <returns>An enumerator for the buffer.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the buffer.
    /// </summary>
    /// <returns>An enumerator for the buffer.</returns>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
