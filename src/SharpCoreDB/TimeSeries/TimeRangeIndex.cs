// <copyright file="TimeRangeIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Sorted index for fast time range lookups.
/// C# 14: Modern patterns, SIMD-friendly, aggressive optimization.
/// 
/// ✅ SCDB Phase 8.3: Time Range Queries
/// 
/// Purpose:
/// - O(log n) range lookups via binary search
/// - Efficient iteration over sorted timestamps
/// - Memory-efficient sparse index
/// - Integration with bucket storage
/// 
/// Design:
/// - Stores sparse index entries (not every timestamp)
/// - Each entry points to a bucket/offset
/// - Binary search for range start
/// - Linear scan within range
/// </summary>
public sealed class TimeRangeIndex
{
    private readonly List<IndexEntry> _entries = [];
    private readonly Lock _lock = new();
    private bool _sorted = true;

    /// <summary>Gets the number of index entries.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets whether the index is empty.</summary>
    public bool IsEmpty => _entries.Count == 0;

    /// <summary>Gets the minimum timestamp in the index.</summary>
    public long? MinTimestamp => _entries.Count > 0 ? _entries[0].Timestamp : null;

    /// <summary>Gets the maximum timestamp in the index.</summary>
    public long? MaxTimestamp => _entries.Count > 0 ? _entries[^1].Timestamp : null;

    /// <summary>
    /// Adds an index entry.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(long timestamp, string bucketId, int offset = 0)
    {
        lock (_lock)
        {
            _entries.Add(new IndexEntry
            {
                Timestamp = timestamp,
                BucketId = bucketId,
                Offset = offset
            });
            _sorted = false;
        }
    }

    /// <summary>
    /// Adds an index entry for a DateTime.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(DateTime timestamp, string bucketId, int offset = 0)
    {
        Add(timestamp.Ticks, bucketId, offset);
    }

    /// <summary>
    /// Adds multiple entries from a bucket.
    /// </summary>
    public void AddBucket(string bucketId, long[] timestamps, int samplingRate = 100)
    {
        if (timestamps.Length == 0)
            return;

        lock (_lock)
        {
            // Add sparse entries (every Nth timestamp)
            for (int i = 0; i < timestamps.Length; i += samplingRate)
            {
                _entries.Add(new IndexEntry
                {
                    Timestamp = timestamps[i],
                    BucketId = bucketId,
                    Offset = i
                });
            }

            // Always add the last entry
            if (timestamps.Length > 1 && (timestamps.Length - 1) % samplingRate != 0)
            {
                _entries.Add(new IndexEntry
                {
                    Timestamp = timestamps[^1],
                    BucketId = bucketId,
                    Offset = timestamps.Length - 1
                });
            }

            _sorted = false;
        }
    }

    /// <summary>
    /// Ensures the index is sorted.
    /// </summary>
    public void EnsureSorted()
    {
        if (_sorted)
            return;

        lock (_lock)
        {
            if (_sorted)
                return;

            _entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            _sorted = true;
        }
    }

    /// <summary>
    /// Finds the first entry at or after the given timestamp.
    /// Uses binary search for O(log n) performance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IndexEntry? FindFirstAtOrAfter(long timestamp)
    {
        EnsureSorted();

        if (_entries.Count == 0)
            return null;

        int left = 0;
        int right = _entries.Count - 1;
        int result = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (_entries[mid].Timestamp >= timestamp)
            {
                result = mid;
                right = mid - 1;
            }
            else
            {
                left = mid + 1;
            }
        }

        return result >= 0 ? _entries[result] : null;
    }

    /// <summary>
    /// Finds the last entry at or before the given timestamp.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IndexEntry? FindLastAtOrBefore(long timestamp)
    {
        EnsureSorted();

        if (_entries.Count == 0)
            return null;

        int left = 0;
        int right = _entries.Count - 1;
        int result = -1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (_entries[mid].Timestamp <= timestamp)
            {
                result = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return result >= 0 ? _entries[result] : null;
    }

    /// <summary>
    /// Gets all entries within a time range.
    /// </summary>
    public IEnumerable<IndexEntry> GetRange(long startTimestamp, long endTimestamp)
    {
        EnsureSorted();

        if (_entries.Count == 0)
            yield break;

        // Find starting point with binary search
        int startIndex = FindStartIndex(startTimestamp);
        if (startIndex < 0)
            yield break;

        // Iterate while within range
        for (int i = startIndex; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.Timestamp >= endTimestamp)
                break;

            if (entry.Timestamp >= startTimestamp)
                yield return entry;
        }
    }

    /// <summary>
    /// Gets distinct bucket IDs that might contain data in the range.
    /// </summary>
    public IEnumerable<string> GetBucketsInRange(long startTimestamp, long endTimestamp)
    {
        return GetRange(startTimestamp, endTimestamp)
            .Select(e => e.BucketId)
            .Distinct();
    }

    /// <summary>
    /// Gets distinct bucket IDs for a DateTime range.
    /// </summary>
    public IEnumerable<string> GetBucketsInRange(DateTime startTime, DateTime endTime)
    {
        return GetBucketsInRange(startTime.Ticks, endTime.Ticks);
    }

    /// <summary>
    /// Estimates the number of data points in a range.
    /// </summary>
    public long EstimateCountInRange(long startTimestamp, long endTimestamp, int averagePointsPerEntry = 100)
    {
        var entries = GetRange(startTimestamp, endTimestamp).ToList();
        return entries.Count * averagePointsPerEntry;
    }

    /// <summary>
    /// Clears the index.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _sorted = true;
        }
    }

    /// <summary>
    /// Removes entries for a specific bucket.
    /// </summary>
    public int RemoveBucket(string bucketId)
    {
        lock (_lock)
        {
            int removed = _entries.RemoveAll(e => e.BucketId == bucketId);
            return removed;
        }
    }

    /// <summary>
    /// Gets index statistics.
    /// </summary>
    public TimeRangeIndexStats GetStats()
    {
        EnsureSorted();

        return new TimeRangeIndexStats
        {
            EntryCount = _entries.Count,
            MinTimestamp = MinTimestamp,
            MaxTimestamp = MaxTimestamp,
            BucketCount = _entries.Select(e => e.BucketId).Distinct().Count(),
            MemoryEstimateBytes = _entries.Count * 32 // Rough estimate
        };
    }

    // Private helpers

    private int FindStartIndex(long timestamp)
    {
        if (_entries.Count == 0)
            return -1;

        int left = 0;
        int right = _entries.Count - 1;

        while (left <= right)
        {
            int mid = left + (right - left) / 2;

            if (_entries[mid].Timestamp < timestamp)
            {
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        return left < _entries.Count ? left : -1;
    }
}

/// <summary>
/// Index entry pointing to a timestamp location.
/// </summary>
public readonly record struct IndexEntry
{
    /// <summary>Timestamp (ticks).</summary>
    public required long Timestamp { get; init; }

    /// <summary>Bucket containing this timestamp.</summary>
    public required string BucketId { get; init; }

    /// <summary>Offset within the bucket.</summary>
    public required int Offset { get; init; }

    /// <summary>Gets the timestamp as DateTime.</summary>
    public DateTime DateTime => new(Timestamp, DateTimeKind.Utc);
}

/// <summary>
/// Time range index statistics.
/// </summary>
public sealed record TimeRangeIndexStats
{
    /// <summary>Number of index entries.</summary>
    public required int EntryCount { get; init; }

    /// <summary>Minimum timestamp in index.</summary>
    public required long? MinTimestamp { get; init; }

    /// <summary>Maximum timestamp in index.</summary>
    public required long? MaxTimestamp { get; init; }

    /// <summary>Number of distinct buckets.</summary>
    public required int BucketCount { get; init; }

    /// <summary>Estimated memory usage.</summary>
    public required int MemoryEstimateBytes { get; init; }
}
