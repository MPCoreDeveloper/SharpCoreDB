// <copyright file="BucketManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// Manages time-series bucket lifecycle.
/// C# 14: Lock class, primary constructors, modern patterns.
/// 
/// ✅ SCDB Phase 8.2: Bucket Storage System
/// 
/// Purpose:
/// - Automatic bucket creation and rotation
/// - Tier management (Hot → Warm → Cold)
/// - Bucket lookup and retrieval
/// - Integration with Phase 8.1 compression
/// </summary>
public sealed class BucketManager : IDisposable
{
    private readonly ConcurrentDictionary<string, TimeSeriesBucket> _buckets = new();
    private readonly ConcurrentDictionary<string, TimeSeriesBatch> _hotData = new();
    private readonly ConcurrentDictionary<string, TimeSeriesCompression.CompressedData> _warmTimestamps = new();
    private readonly ConcurrentDictionary<string, TimeSeriesCompression.CompressedData> _warmValues = new();
    private readonly Lock _bucketLock = new();
    private readonly BucketGranularity _defaultGranularity;
    private readonly TimeSpan _hotToColdThreshold;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BucketManager"/> class.
    /// </summary>
    /// <param name="defaultGranularity">Default bucket granularity.</param>
    /// <param name="hotToWarmThreshold">Time after which hot data becomes warm (default: 1 hour).</param>
    public BucketManager(
        BucketGranularity defaultGranularity = BucketGranularity.Hour,
        TimeSpan? hotToWarmThreshold = null)
    {
        _defaultGranularity = defaultGranularity;
        _hotToColdThreshold = hotToWarmThreshold ?? TimeSpan.FromHours(1);
    }

    /// <summary>Gets the number of buckets.</summary>
    public int BucketCount => _buckets.Count;

    /// <summary>Gets the default granularity.</summary>
    public BucketGranularity DefaultGranularity => _defaultGranularity;

    /// <summary>
    /// Gets or creates a bucket for the given timestamp.
    /// </summary>
    public TimeSeriesBucket GetOrCreateBucket(string tableName, DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var bucketId = BucketPartitioner.GetBucketId(tableName, timestamp, _defaultGranularity);

        return _buckets.GetOrAdd(bucketId, _ =>
        {
            var bucketStart = BucketPartitioner.GetBucketStart(timestamp, _defaultGranularity);
            var bucketEnd = BucketPartitioner.GetBucketEnd(bucketStart, _defaultGranularity);

            return new TimeSeriesBucket
            {
                BucketId = bucketId,
                TableName = tableName,
                StartTime = bucketStart,
                EndTime = bucketEnd,
                Granularity = _defaultGranularity,
                Tier = BucketTier.Hot
            };
        });
    }

    /// <summary>
    /// Inserts data points into the appropriate bucket.
    /// </summary>
    public void Insert(string tableName, IEnumerable<TimeSeriesDataPoint> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(points);

        // Group points by bucket
        var grouped = points.GroupBy(p =>
            BucketPartitioner.GetBucketId(tableName, p.Timestamp, _defaultGranularity));

        foreach (var group in grouped)
        {
            var bucketId = group.Key;
            var bucket = GetOrCreateBucket(tableName, group.First().Timestamp);

            // Add to hot data
            var batch = TimeSeriesBatch.FromDataPoints(group);
            InsertBatch(bucketId, bucket, batch);
        }
    }

    /// <summary>
    /// Inserts a batch of data into a bucket.
    /// </summary>
    public void InsertBatch(string tableName, DateTime timestamp, TimeSeriesBatch batch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(batch);

        var bucket = GetOrCreateBucket(tableName, timestamp);
        InsertBatch(bucket.BucketId, bucket, batch);
    }

    private void InsertBatch(string bucketId, TimeSeriesBucket bucket, TimeSeriesBatch batch)
    {
        lock (_bucketLock)
        {
            // Merge with existing hot data
            if (_hotData.TryGetValue(bucketId, out var existing))
            {
                // Merge batches
                var mergedTimestamps = existing.Timestamps.Concat(batch.Timestamps).OrderBy(t => t).ToArray();
                var mergedValues = new double[mergedTimestamps.Length];

                // Build lookup for merging
                var existingDict = existing.Timestamps.Zip(existing.Values).ToDictionary(x => x.First, x => x.Second);
                var newDict = batch.Timestamps.Zip(batch.Values).ToDictionary(x => x.First, x => x.Second);

                for (int i = 0; i < mergedTimestamps.Length; i++)
                {
                    var ts = mergedTimestamps[i];
                    mergedValues[i] = newDict.TryGetValue(ts, out var v) ? v : existingDict[ts];
                }

                batch = new TimeSeriesBatch
                {
                    Timestamps = mergedTimestamps,
                    Values = mergedValues
                };
            }

            _hotData[bucketId] = batch;

            // Update bucket metadata
            bucket.RowCount = batch.Count;
            bucket.UncompressedSize = batch.Count * (sizeof(long) + sizeof(double));

            if (batch.Count > 0)
            {
                bucket.MinTimestamp = new DateTime(batch.Timestamps.Min(), DateTimeKind.Utc);
                bucket.MaxTimestamp = new DateTime(batch.Timestamps.Max(), DateTimeKind.Utc);
            }

            bucket.ModifiedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Queries data points within a time range.
    /// </summary>
    public IEnumerable<TimeSeriesDataPoint> Query(
        string tableName,
        DateTime startTime,
        DateTime endTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var bucketIds = BucketPartitioner.GetBucketIdsForRange(
            tableName, startTime, endTime, _defaultGranularity);

        foreach (var bucketId in bucketIds)
        {
            if (!_buckets.TryGetValue(bucketId, out var bucket))
            {
                continue;
            }

            TimeSeriesBatch? batch = null;

            if (bucket.Tier == BucketTier.Hot)
            {
                // Read from hot data
                _hotData.TryGetValue(bucketId, out batch);
            }
            else if (bucket.Tier == BucketTier.Warm)
            {
                // Decompress from warm data
                batch = DecompressBucket(bucketId);
            }

            if (batch == null)
            {
                continue;
            }

            // Filter by time range
            var startTicks = startTime.Ticks;
            var endTicks = endTime.Ticks;

            for (int i = 0; i < batch.Count; i++)
            {
                var ts = batch.Timestamps[i];
                if (ts >= startTicks && ts < endTicks)
                {
                    yield return new TimeSeriesDataPoint
                    {
                        Timestamp = new DateTime(ts, DateTimeKind.Utc),
                        Value = batch.Values[i]
                    };
                }
            }
        }
    }

    /// <summary>
    /// Compresses a hot bucket to warm tier using Phase 8.1 codecs.
    /// </summary>
    public bool CompressBucket(string bucketId)
    {
        if (!_buckets.TryGetValue(bucketId, out var bucket))
        {
            return false;
        }

        if (bucket.Tier != BucketTier.Hot)
        {
            return false; // Already compressed
        }

        if (!_hotData.TryGetValue(bucketId, out var batch))
        {
            return false;
        }

        lock (_bucketLock)
        {
            // Compress using Phase 8.1 codecs
            var compressedTimestamps = TimeSeriesCompression.CompressTimestamps(batch.Timestamps);
            var compressedValues = TimeSeriesCompression.CompressValues(batch.Values);

            _warmTimestamps[bucketId] = compressedTimestamps;
            _warmValues[bucketId] = compressedValues;

            // Update bucket metadata
            bucket.Tier = BucketTier.Warm;
            bucket.CompressedSize = compressedTimestamps.Data.Length + compressedValues.Data.Length;
            bucket.IsSealed = true;
            bucket.ModifiedAt = DateTime.UtcNow;

            // Remove hot data
            _hotData.TryRemove(bucketId, out _);
        }

        return true;
    }

    /// <summary>
    /// Decompresses a warm bucket back to a batch.
    /// </summary>
    private TimeSeriesBatch? DecompressBucket(string bucketId)
    {
        if (!_warmTimestamps.TryGetValue(bucketId, out var compressedTs) ||
            !_warmValues.TryGetValue(bucketId, out var compressedVals))
        {
            return null;
        }

        var timestamps = TimeSeriesCompression.DecompressTimestamps(compressedTs);
        var values = TimeSeriesCompression.DecompressValues(compressedVals);

        return new TimeSeriesBatch
        {
            Timestamps = timestamps,
            Values = values
        };
    }

    /// <summary>
    /// Compresses all eligible hot buckets.
    /// </summary>
    public int CompressEligibleBuckets()
    {
        var now = DateTime.UtcNow;
        int compressed = 0;

        foreach (var bucket in _buckets.Values.Where(b => b.Tier == BucketTier.Hot))
        {
            // Compress if bucket end time is past the threshold
            if (now - bucket.EndTime > _hotToColdThreshold)
            {
                if (CompressBucket(bucket.BucketId))
                {
                    compressed++;
                }
            }
        }

        return compressed;
    }

    /// <summary>
    /// Gets all buckets for a table.
    /// </summary>
    public IEnumerable<TimeSeriesBucket> GetBuckets(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        return _buckets.Values
            .Where(b => b.TableName == tableName)
            .OrderBy(b => b.StartTime);
    }

    /// <summary>
    /// Gets a bucket by ID.
    /// </summary>
    public TimeSeriesBucket? GetBucket(string bucketId)
    {
        return _buckets.TryGetValue(bucketId, out var bucket) ? bucket : null;
    }

    /// <summary>
    /// Gets statistics for all buckets.
    /// </summary>
    public BucketManagerStats GetStats()
    {
        var buckets = _buckets.Values.ToList();

        return new BucketManagerStats
        {
            TotalBuckets = buckets.Count,
            HotBuckets = buckets.Count(b => b.Tier == BucketTier.Hot),
            WarmBuckets = buckets.Count(b => b.Tier == BucketTier.Warm),
            ColdBuckets = buckets.Count(b => b.Tier == BucketTier.Cold),
            TotalRows = buckets.Sum(b => b.RowCount),
            TotalCompressedSize = buckets.Sum(b => b.CompressedSize),
            TotalUncompressedSize = buckets.Sum(b => b.UncompressedSize)
        };
    }

    /// <summary>
    /// Disposes the bucket manager.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _buckets.Clear();
        _hotData.Clear();
        _warmTimestamps.Clear();
        _warmValues.Clear();

        _disposed = true;
    }
}

/// <summary>
/// Bucket manager statistics.
/// </summary>
public sealed record BucketManagerStats
{
    /// <summary>Total number of buckets.</summary>
    public int TotalBuckets { get; init; }

    /// <summary>Number of hot buckets.</summary>
    public int HotBuckets { get; init; }

    /// <summary>Number of warm buckets.</summary>
    public int WarmBuckets { get; init; }

    /// <summary>Number of cold buckets.</summary>
    public int ColdBuckets { get; init; }

    /// <summary>Total row count across all buckets.</summary>
    public long TotalRows { get; init; }

    /// <summary>Total compressed size in bytes.</summary>
    public long TotalCompressedSize { get; init; }

    /// <summary>Total uncompressed size in bytes.</summary>
    public long TotalUncompressedSize { get; init; }

    /// <summary>Overall compression ratio.</summary>
    public double CompressionRatio => TotalCompressedSize > 0
        ? (double)TotalUncompressedSize / TotalCompressedSize
        : 1.0;
}
