// <copyright file="TimeSeriesTable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// High-level time-series table API.
/// C# 14: Primary constructors, async patterns, modern C#.
/// 
/// ✅ SCDB Phase 8.2: Bucket Storage System
/// 
/// Purpose:
/// - Simplified API for time-series data
/// - Automatic bucket management
/// - Aggregation queries
/// - Integration with Phase 8.1 compression
/// </summary>
public sealed class TimeSeriesTable : IDisposable
{
    private readonly string _tableName;
    private readonly BucketManager _bucketManager;
    private readonly Lock _insertLock = new();
    private readonly List<TimeSeriesDataPoint> _writeBuffer = [];
    private readonly int _writeBufferSize;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeSeriesTable"/> class.
    /// </summary>
    /// <param name="tableName">Name of the time-series table.</param>
    /// <param name="granularity">Bucket granularity (default: Hour).</param>
    /// <param name="writeBufferSize">Write buffer size before flush (default: 1000).</param>
    public TimeSeriesTable(
        string tableName,
        BucketGranularity granularity = BucketGranularity.Hour,
        int writeBufferSize = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        _tableName = tableName;
        _bucketManager = new BucketManager(granularity);
        _writeBufferSize = writeBufferSize;
    }

    /// <summary>Gets the table name.</summary>
    public string TableName => _tableName;

    /// <summary>Gets the bucket manager.</summary>
    public BucketManager BucketManager => _bucketManager;

    /// <summary>
    /// Inserts a single data point.
    /// </summary>
    public void Insert(DateTime timestamp, double value, Dictionary<string, string>? tags = null)
    {
        var point = new TimeSeriesDataPoint
        {
            Timestamp = timestamp,
            Value = value,
            Tags = tags
        };

        lock (_insertLock)
        {
            _writeBuffer.Add(point);

            if (_writeBuffer.Count >= _writeBufferSize)
            {
                FlushWriteBuffer();
            }
        }
    }

    /// <summary>
    /// Inserts multiple data points.
    /// </summary>
    public void InsertBatch(IEnumerable<TimeSeriesDataPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        lock (_insertLock)
        {
            _writeBuffer.AddRange(points);

            if (_writeBuffer.Count >= _writeBufferSize)
            {
                FlushWriteBuffer();
            }
        }
    }

    /// <summary>
    /// Inserts timestamps and values as parallel arrays.
    /// </summary>
    public void InsertBatch(long[] timestamps, double[] values)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(values);

        if (timestamps.Length != values.Length)
        {
            throw new ArgumentException("Timestamps and values arrays must have the same length");
        }

        var points = timestamps.Zip(values, (t, v) => new TimeSeriesDataPoint
        {
            Timestamp = new DateTime(t, DateTimeKind.Utc),
            Value = v
        });

        InsertBatch(points);
    }

    /// <summary>
    /// Flushes the write buffer to storage.
    /// </summary>
    public void Flush()
    {
        lock (_insertLock)
        {
            FlushWriteBuffer();
        }
    }

    private void FlushWriteBuffer()
    {
        if (_writeBuffer.Count == 0)
        {
            return;
        }

        _bucketManager.Insert(_tableName, _writeBuffer);
        _writeBuffer.Clear();
    }

    /// <summary>
    /// Queries data points within a time range.
    /// </summary>
    public IEnumerable<TimeSeriesDataPoint> Query(DateTime startTime, DateTime endTime)
    {
        // Flush pending writes first
        Flush();

        return _bucketManager.Query(_tableName, startTime, endTime);
    }

    /// <summary>
    /// Queries and returns as parallel arrays (more efficient).
    /// </summary>
    public (long[] Timestamps, double[] Values) QueryArrays(DateTime startTime, DateTime endTime)
    {
        var points = Query(startTime, endTime).ToList();

        var timestamps = points.Select(p => p.Timestamp.Ticks).ToArray();
        var values = points.Select(p => p.Value).ToArray();

        return (timestamps, values);
    }

    /// <summary>
    /// Counts data points in a time range.
    /// </summary>
    public long Count(DateTime startTime, DateTime endTime)
    {
        return Query(startTime, endTime).LongCount();
    }

    /// <summary>
    /// Calculates the sum of values in a time range.
    /// </summary>
    public double Sum(DateTime startTime, DateTime endTime)
    {
        return Query(startTime, endTime).Sum(p => p.Value);
    }

    /// <summary>
    /// Calculates the average of values in a time range.
    /// </summary>
    public double? Average(DateTime startTime, DateTime endTime)
    {
        var points = Query(startTime, endTime).ToList();
        return points.Count > 0 ? points.Average(p => p.Value) : null;
    }

    /// <summary>
    /// Gets the minimum value in a time range.
    /// </summary>
    public double? Min(DateTime startTime, DateTime endTime)
    {
        var points = Query(startTime, endTime).ToList();
        return points.Count > 0 ? points.Min(p => p.Value) : null;
    }

    /// <summary>
    /// Gets the maximum value in a time range.
    /// </summary>
    public double? Max(DateTime startTime, DateTime endTime)
    {
        var points = Query(startTime, endTime).ToList();
        return points.Count > 0 ? points.Max(p => p.Value) : null;
    }

    /// <summary>
    /// Gets the first data point in a time range.
    /// </summary>
    public TimeSeriesDataPoint? First(DateTime startTime, DateTime endTime)
    {
        return Query(startTime, endTime).FirstOrDefault();
    }

    /// <summary>
    /// Gets the last data point in a time range.
    /// </summary>
    public TimeSeriesDataPoint? Last(DateTime startTime, DateTime endTime)
    {
        return Query(startTime, endTime).LastOrDefault();
    }

    /// <summary>
    /// Gets aggregated statistics for a time range.
    /// </summary>
    public TimeSeriesStats GetStats(DateTime startTime, DateTime endTime)
    {
        var points = Query(startTime, endTime).ToList();

        if (points.Count == 0)
        {
            return new TimeSeriesStats
            {
                Count = 0,
                StartTime = startTime,
                EndTime = endTime
            };
        }

        var values = points.Select(p => p.Value).ToList();

        return new TimeSeriesStats
        {
            Count = points.Count,
            Sum = values.Sum(),
            Average = values.Average(),
            Min = values.Min(),
            Max = values.Max(),
            First = points.First().Value,
            Last = points.Last().Value,
            StartTime = points.First().Timestamp,
            EndTime = points.Last().Timestamp
        };
    }

    /// <summary>
    /// Groups data by time interval and aggregates.
    /// </summary>
    public IEnumerable<TimeSeriesStats> GroupBy(
        DateTime startTime,
        DateTime endTime,
        TimeSpan interval)
    {
        var points = Query(startTime, endTime).ToList();

        if (points.Count == 0)
        {
            yield break;
        }

        var current = startTime;

        while (current < endTime)
        {
            var intervalEnd = current + interval;

            var intervalPoints = points
                .Where(p => p.Timestamp >= current && p.Timestamp < intervalEnd)
                .ToList();

            if (intervalPoints.Count > 0)
            {
                var values = intervalPoints.Select(p => p.Value).ToList();

                yield return new TimeSeriesStats
                {
                    Count = intervalPoints.Count,
                    Sum = values.Sum(),
                    Average = values.Average(),
                    Min = values.Min(),
                    Max = values.Max(),
                    First = intervalPoints.First().Value,
                    Last = intervalPoints.Last().Value,
                    StartTime = current,
                    EndTime = intervalEnd
                };
            }

            current = intervalEnd;
        }
    }

    /// <summary>
    /// Compresses all eligible buckets to warm tier.
    /// </summary>
    public int Compact()
    {
        Flush();
        return _bucketManager.CompressEligibleBuckets();
    }

    /// <summary>
    /// Gets bucket information for this table.
    /// </summary>
    public IEnumerable<TimeSeriesBucket> GetBuckets()
    {
        return _bucketManager.GetBuckets(_tableName);
    }

    /// <summary>
    /// Disposes the time-series table.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        Flush();
        _bucketManager.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Time-series statistics for a time range.
/// </summary>
public sealed record TimeSeriesStats
{
    /// <summary>Number of data points.</summary>
    public long Count { get; init; }

    /// <summary>Sum of values.</summary>
    public double Sum { get; init; }

    /// <summary>Average value.</summary>
    public double Average { get; init; }

    /// <summary>Minimum value.</summary>
    public double Min { get; init; }

    /// <summary>Maximum value.</summary>
    public double Max { get; init; }

    /// <summary>First value in range.</summary>
    public double First { get; init; }

    /// <summary>Last value in range.</summary>
    public double Last { get; init; }

    /// <summary>Start of the time range.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>End of the time range.</summary>
    public DateTime EndTime { get; init; }
}
