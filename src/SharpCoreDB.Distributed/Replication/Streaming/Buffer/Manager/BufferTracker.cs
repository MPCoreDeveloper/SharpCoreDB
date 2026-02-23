// <copyright file="BufferTracker.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Comprehensive tracker for buffer usage patterns and statistics.
/// Provides detailed insights into buffer allocation, usage, and performance.
/// C# 14: Collection expressions, pattern matching, advanced analytics.
/// </summary>
public sealed class BufferTracker
{
    private readonly Dictionary<string, BufferUsageStats> _bufferStats = [];
    private readonly Dictionary<string, List<BufferUsageRecord>> _usageHistory = [];
    private readonly Lock _trackerLock = new();

    private readonly int _maxHistorySize;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferTracker"/> class.
    /// </summary>
    /// <param name="maxHistorySize">Maximum number of historical records to keep per buffer.</param>
    public BufferTracker(int maxHistorySize = 1000)
    {
        _maxHistorySize = maxHistorySize;
    }

    /// <summary>
    /// Records a buffer allocation.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="size">The buffer size.</param>
    /// <param name="source">The allocation source.</param>
    public void RecordAllocation(string bufferId, int size, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var record = new BufferUsageRecord
        {
            BufferId = bufferId,
            EventType = BufferEventType.Allocated,
            Timestamp = DateTimeOffset.UtcNow,
            Size = size,
            Source = source
        };

        lock (_trackerLock)
        {
            // Update stats
            if (!_bufferStats.TryGetValue(bufferId, out var stats))
            {
                stats = new BufferUsageStats { BufferId = bufferId };
                _bufferStats[bufferId] = stats;
            }

            stats.TotalAllocations++;
            stats.LastAllocationTime = record.Timestamp;
            stats.CurrentSize = size;

            // Add to history
            if (!_usageHistory.TryGetValue(bufferId, out var history))
            {
                history = [];
                _usageHistory[bufferId] = history;
            }

            history.Add(record);
            TrimHistory(history);
        }
    }

    /// <summary>
    /// Records a buffer rental.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="source">The rental source.</param>
    public void RecordRental(string bufferId, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var record = new BufferUsageRecord
        {
            BufferId = bufferId,
            EventType = BufferEventType.Rented,
            Timestamp = DateTimeOffset.UtcNow,
            Source = source
        };

        lock (_trackerLock)
        {
            if (_bufferStats.TryGetValue(bufferId, out var stats))
            {
                stats.TotalRentals++;
                stats.LastRentalTime = record.Timestamp;
                stats.IsCurrentlyRented = true;
                stats.RentalStartTime = record.Timestamp;
            }

            // Add to history
            if (_usageHistory.TryGetValue(bufferId, out var history))
            {
                history.Add(record);
                TrimHistory(history);
            }
        }
    }

    /// <summary>
    /// Records a buffer return.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="source">The return source.</param>
    /// <param name="usageDuration">The usage duration.</param>
    public void RecordReturn(string bufferId, string source, TimeSpan usageDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var record = new BufferUsageRecord
        {
            BufferId = bufferId,
            EventType = BufferEventType.Returned,
            Timestamp = DateTimeOffset.UtcNow,
            Source = source,
            Duration = usageDuration
        };

        lock (_trackerLock)
        {
            if (_bufferStats.TryGetValue(bufferId, out var stats))
            {
                stats.TotalReturns++;
                stats.LastReturnTime = record.Timestamp;
                stats.IsCurrentlyRented = false;
                stats.TotalUsageTime += usageDuration;

                if (stats.RentalStartTime.HasValue)
                {
                    var actualDuration = record.Timestamp - stats.RentalStartTime.Value;
                    stats.LastUsageDuration = actualDuration;
                    stats.RentalStartTime = null;
                }
            }

            // Add to history
            if (_usageHistory.TryGetValue(bufferId, out var history))
            {
                history.Add(record);
                TrimHistory(history);
            }
        }
    }

    /// <summary>
    /// Records a buffer deallocation.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="source">The deallocation source.</param>
    /// <param name="totalLifetime">The total buffer lifetime.</param>
    public void RecordDeallocation(string bufferId, string source, TimeSpan totalLifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var record = new BufferUsageRecord
        {
            BufferId = bufferId,
            EventType = BufferEventType.Deallocated,
            Timestamp = DateTimeOffset.UtcNow,
            Source = source,
            Duration = totalLifetime
        };

        lock (_trackerLock)
        {
            if (_bufferStats.TryGetValue(bufferId, out var stats))
            {
                stats.IsDeallocated = true;
                stats.DeallocationTime = record.Timestamp;
                stats.TotalLifetime = totalLifetime;
            }

            // Add to history
            if (_usageHistory.TryGetValue(bufferId, out var history))
            {
                history.Add(record);
                // Don't trim history for deallocated buffers - keep full history
            }
        }
    }

    /// <summary>
    /// Gets usage statistics for a specific buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The buffer usage statistics, or null if not found.</returns>
    public BufferUsageStats? GetBufferStats(string bufferId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        lock (_trackerLock)
        {
            return _bufferStats.TryGetValue(bufferId, out var stats) ? stats : null;
        }
    }

    /// <summary>
    /// Gets usage history for a specific buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The buffer usage history.</returns>
    public IReadOnlyList<BufferUsageRecord> GetBufferHistory(string bufferId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        lock (_trackerLock)
        {
            return _usageHistory.TryGetValue(bufferId, out var history) ? [.. history] : [];
        }
    }

    /// <summary>
    /// Gets all buffer statistics.
    /// </summary>
    /// <returns>Collection of all buffer statistics.</returns>
    public IReadOnlyCollection<BufferUsageStats> GetAllStats()
    {
        lock (_trackerLock)
        {
            return [.. _bufferStats.Values];
        }
    }

    /// <summary>
    /// Gets comprehensive tracking statistics.
    /// </summary>
    /// <returns>Tracking statistics.</returns>
    public BufferTrackingStats GetTrackingStats()
    {
        lock (_trackerLock)
        {
            var allStats = _bufferStats.Values;
            var totalHistoryRecords = _usageHistory.Values.Sum(h => h.Count);

            return new BufferTrackingStats
            {
                TotalBuffersTracked = _bufferStats.Count,
                ActiveBuffers = allStats.Count(s => !s.IsDeallocated),
                DeallocatedBuffers = allStats.Count(s => s.IsDeallocated),
                CurrentlyRentedBuffers = allStats.Count(s => s.IsCurrentlyRented),
                TotalHistoryRecords = totalHistoryRecords,
                AverageHistoryPerBuffer = _bufferStats.Count > 0 ? (double)totalHistoryRecords / _bufferStats.Count : 0,
                MemoryUsage = CalculateMemoryUsage(allStats),
                TopUsageSources = GetTopUsageSources(allStats),
                UsagePatterns = AnalyzeUsagePatterns(allStats)
            };
        }
    }

    /// <summary>
    /// Finds buffers with unusual usage patterns.
    /// </summary>
    /// <param name="threshold">The threshold for considering usage unusual.</param>
    /// <returns>Collection of buffers with unusual usage.</returns>
    public IReadOnlyCollection<BufferUsageStats> FindUnusualUsage(double threshold = 2.0)
    {
        lock (_trackerLock)
        {
            var allStats = _bufferStats.Values;
            if (!allStats.Any())
            {
                return [];
            }

            var avgUsageTime = allStats.Average(s => s.TotalUsageTime.TotalMilliseconds);
            var stdDev = CalculateStandardDeviation(allStats.Select(s => s.TotalUsageTime.TotalMilliseconds));

            return allStats
                .Where(s => Math.Abs(s.TotalUsageTime.TotalMilliseconds - avgUsageTime) > threshold * stdDev)
                .ToList();
        }
    }

    /// <summary>
    /// Gets buffers that have been rented for too long.
    /// </summary>
    /// <param name="maxRentalTime">Maximum allowed rental time.</param>
    /// <returns>Collection of long-rented buffers.</returns>
    public IReadOnlyCollection<BufferUsageStats> GetLongRentedBuffers(TimeSpan maxRentalTime)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_trackerLock)
        {
            return _bufferStats.Values
                .Where(s => s.IsCurrentlyRented &&
                           s.RentalStartTime.HasValue &&
                           (now - s.RentalStartTime.Value) > maxRentalTime)
                .ToList();
        }
    }

    /// <summary>
    /// Cleans up tracking data for deallocated buffers older than the specified age.
    /// </summary>
    /// <param name="maxAge">Maximum age of deallocated buffer data to keep.</param>
    public void CleanupOldData(TimeSpan maxAge)
    {
        var cutoffTime = DateTimeOffset.UtcNow - maxAge;

        lock (_trackerLock)
        {
            var toRemove = _bufferStats
                .Where(kvp => kvp.Value.IsDeallocated &&
                             kvp.Value.DeallocationTime.HasValue &&
                             kvp.Value.DeallocationTime.Value < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var bufferId in toRemove)
            {
                _bufferStats.Remove(bufferId);
                _usageHistory.Remove(bufferId);
            }
        }
    }

    /// <summary>
    /// Trims the history list to the maximum size.
    /// </summary>
    /// <param name="history">The history list to trim.</param>
    private void TrimHistory(List<BufferUsageRecord> history)
    {
        if (history.Count > _maxHistorySize)
        {
            history.RemoveRange(0, history.Count - _maxHistorySize);
        }
    }

    /// <summary>
    /// Calculates the total memory usage of tracked buffers.
    /// </summary>
    /// <param name="stats">The buffer statistics.</param>
    /// <returns>Total memory usage in bytes.</returns>
    private static long CalculateMemoryUsage(IEnumerable<BufferUsageStats> stats)
    {
        return stats.Where(s => !s.IsDeallocated).Sum(s => s.CurrentSize);
    }

    /// <summary>
    /// Gets the top usage sources by frequency.
    /// </summary>
    /// <param name="stats">The buffer statistics.</param>
    /// <returns>Dictionary of source to usage count.</returns>
    private static IReadOnlyDictionary<string, int> GetTopUsageSources(IEnumerable<BufferUsageStats> stats)
    {
        var sourceCounts = new Dictionary<string, int>();

        foreach (var stat in stats)
        {
            // This would need to be implemented based on how we track sources
            // For now, return empty
        }

        return sourceCounts.OrderByDescending(kvp => kvp.Value).Take(10).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Analyzes usage patterns across all buffers.
    /// </summary>
    /// <param name="stats">The buffer statistics.</param>
    /// <returns>Usage pattern analysis.</returns>
    private static BufferUsagePatterns AnalyzeUsagePatterns(IEnumerable<BufferUsageStats> stats)
    {
        var statsList = stats.ToList();
        if (!statsList.Any())
        {
            return new BufferUsagePatterns();
        }

        var avgRentalFrequency = statsList.Average(s => s.TotalRentals);
        var avgUsageTime = statsList.Average(s => s.TotalUsageTime.TotalMilliseconds);
        var peakUsageTimes = AnalyzePeakUsageTimes(statsList);

        return new BufferUsagePatterns
        {
            AverageRentalFrequency = avgRentalFrequency,
            AverageUsageTimeMs = avgUsageTime,
            PeakUsageTimes = peakUsageTimes,
            UsageDistribution = CalculateUsageDistribution(statsList)
        };
    }

    /// <summary>
    /// Analyzes peak usage times.
    /// </summary>
    /// <param name="stats">The buffer statistics.</param>
    /// <returns>Peak usage time analysis.</returns>
    private static IReadOnlyDictionary<TimeSpan, int> AnalyzePeakUsageTimes(List<BufferUsageStats> stats)
    {
        // Simplified implementation - would analyze actual timestamps in real implementation
        return new Dictionary<TimeSpan, int>();
    }

    /// <summary>
    /// Calculates usage time distribution.
    /// </summary>
    /// <param name="stats">The buffer statistics.</param>
    /// <returns>Usage distribution buckets.</returns>
    private static IReadOnlyDictionary<string, int> CalculateUsageDistribution(List<BufferUsageStats> stats)
    {
        var distribution = new Dictionary<string, int>
        {
            ["< 1ms"] = 0,
            ["1-10ms"] = 0,
            ["10-100ms"] = 0,
            ["100ms-1s"] = 0,
            ["> 1s"] = 0
        };

        foreach (var stat in stats)
        {
            var usageMs = stat.TotalUsageTime.TotalMilliseconds;
            var bucket = usageMs switch
            {
                < 1 => "< 1ms",
                < 10 => "1-10ms",
                < 100 => "10-100ms",
                < 1000 => "100ms-1s",
                _ => "> 1s"
            };
            distribution[bucket]++;
        }

        return distribution;
    }

    /// <summary>
    /// Calculates the standard deviation of a sequence of values.
    /// </summary>
    /// <param name="values">The values.</param>
    /// <returns>The standard deviation.</returns>
    private static double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count <= 1)
        {
            return 0;
        }

        var avg = list.Average();
        var sumOfSquares = list.Sum(x => Math.Pow(x - avg, 2));
        return Math.Sqrt(sumOfSquares / (list.Count - 1));
    }
}

/// <summary>
/// Statistics for a specific buffer's usage.
/// </summary>
public class BufferUsageStats
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the current buffer size.</summary>
    public int CurrentSize { get; set; }

    /// <summary>Gets the total number of allocations.</summary>
    public int TotalAllocations { get; set; }

    /// <summary>Gets the total number of rentals.</summary>
    public int TotalRentals { get; set; }

    /// <summary>Gets the total number of returns.</summary>
    public int TotalReturns { get; set; }

    /// <summary>Gets the total usage time.</summary>
    public TimeSpan TotalUsageTime { get; set; }

    /// <summary>Gets the last allocation time.</summary>
    public DateTimeOffset? LastAllocationTime { get; set; }

    /// <summary>Gets the last rental time.</summary>
    public DateTimeOffset? LastRentalTime { get; set; }

    /// <summary>Gets the last return time.</summary>
    public DateTimeOffset? LastReturnTime { get; set; }

    /// <summary>Gets the last usage duration.</summary>
    public TimeSpan? LastUsageDuration { get; set; }

    /// <summary>Gets the rental start time (if currently rented).</summary>
    public DateTimeOffset? RentalStartTime { get; set; }

    /// <summary>Gets whether the buffer is currently rented.</summary>
    public bool IsCurrentlyRented { get; set; }

    /// <summary>Gets whether the buffer has been deallocated.</summary>
    public bool IsDeallocated { get; set; }

    /// <summary>Gets the deallocation time.</summary>
    public DateTimeOffset? DeallocationTime { get; set; }

    /// <summary>Gets the total buffer lifetime.</summary>
    public TimeSpan? TotalLifetime { get; set; }

    /// <summary>Gets the average usage time per rental.</summary>
    public TimeSpan AverageUsageTime => TotalRentals > 0
        ? TimeSpan.FromTicks(TotalUsageTime.Ticks / TotalRentals)
        : TimeSpan.Zero;

    /// <summary>Gets the buffer utilization efficiency (0-1).</summary>
    public double UtilizationEfficiency => TotalLifetime.HasValue && TotalLifetime.Value.TotalMilliseconds > 0
        ? TotalUsageTime.TotalMilliseconds / TotalLifetime.Value.TotalMilliseconds
        : 0;
}

/// <summary>
/// A record of buffer usage event.
/// </summary>
public class BufferUsageRecord
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the event type.</summary>
    public BufferEventType EventType { get; init; }

    /// <summary>Gets the event timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the buffer size (for allocation events).</summary>
    public int? Size { get; init; }

    /// <summary>Gets the event source.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the duration (for usage events).</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Buffer event types.
/// </summary>
public enum BufferEventType
{
    /// <summary>Buffer was allocated.</summary>
    Allocated,

    /// <summary>Buffer was rented.</summary>
    Rented,

    /// <summary>Buffer was returned.</summary>
    Returned,

    /// <summary>Buffer was deallocated.</summary>
    Deallocated
}

/// <summary>
/// Comprehensive tracking statistics.
/// </summary>
public class BufferTrackingStats
{
    /// <summary>Gets the total number of buffers tracked.</summary>
    public int TotalBuffersTracked { get; init; }

    /// <summary>Gets the number of active buffers.</summary>
    public int ActiveBuffers { get; init; }

    /// <summary>Gets the number of deallocated buffers.</summary>
    public int DeallocatedBuffers { get; init; }

    /// <summary>Gets the number of currently rented buffers.</summary>
    public int CurrentlyRentedBuffers { get; init; }

    /// <summary>Gets the total number of history records.</summary>
    public int TotalHistoryRecords { get; init; }

    /// <summary>Gets the average history records per buffer.</summary>
    public double AverageHistoryPerBuffer { get; init; }

    /// <summary>Gets the current memory usage in bytes.</summary>
    public long MemoryUsage { get; init; }

    /// <summary>Gets the top usage sources.</summary>
    public IReadOnlyDictionary<string, int> TopUsageSources { get; init; } = new Dictionary<string, int>();

    /// <summary>Gets the usage pattern analysis.</summary>
    public BufferUsagePatterns UsagePatterns { get; init; } = new();
}

/// <summary>
/// Analysis of buffer usage patterns.
/// </summary>
public class BufferUsagePatterns
{
    /// <summary>Gets the average rental frequency.</summary>
    public double AverageRentalFrequency { get; init; }

    /// <summary>Gets the average usage time in milliseconds.</summary>
    public double AverageUsageTimeMs { get; init; }

    /// <summary>Gets the peak usage times.</summary>
    public IReadOnlyDictionary<TimeSpan, int> PeakUsageTimes { get; init; } = new Dictionary<TimeSpan, int>();

    /// <summary>Gets the usage time distribution.</summary>
    public IReadOnlyDictionary<string, int> UsageDistribution { get; init; } = new Dictionary<string, int>();
}
