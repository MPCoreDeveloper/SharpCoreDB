// <copyright file="BucketTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.TimeSeries;

using System;
using System.Linq;
using SharpCoreDB.TimeSeries;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Tests for Phase 8.2: Bucket Storage System.
/// ✅ SCDB Phase 8.2: Verifies bucket partitioning, management, and queries.
/// </summary>
public sealed class BucketTests
{
    private readonly ITestOutputHelper _output;

    public BucketTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // BucketPartitioner Tests
    // ========================================

    [Fact]
    public void BucketPartitioner_GetBucketStart_Minute_TruncatesCorrectly()
    {
        // Arrange
        var timestamp = new DateTime(2026, 2, 15, 14, 35, 42, DateTimeKind.Utc);

        // Act
        var bucketStart = BucketPartitioner.GetBucketStart(timestamp, BucketGranularity.Minute);

        // Assert
        Assert.Equal(new DateTime(2026, 2, 15, 14, 35, 0, DateTimeKind.Utc), bucketStart);
        _output.WriteLine($"✓ Minute bucket: {bucketStart:yyyy-MM-dd HH:mm:ss}");
    }

    [Fact]
    public void BucketPartitioner_GetBucketStart_Hour_TruncatesCorrectly()
    {
        // Arrange
        var timestamp = new DateTime(2026, 2, 15, 14, 35, 42, DateTimeKind.Utc);

        // Act
        var bucketStart = BucketPartitioner.GetBucketStart(timestamp, BucketGranularity.Hour);

        // Assert
        Assert.Equal(new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc), bucketStart);
        _output.WriteLine($"✓ Hour bucket: {bucketStart:yyyy-MM-dd HH:mm:ss}");
    }

    [Fact]
    public void BucketPartitioner_GetBucketStart_Day_TruncatesCorrectly()
    {
        // Arrange
        var timestamp = new DateTime(2026, 2, 15, 14, 35, 42, DateTimeKind.Utc);

        // Act
        var bucketStart = BucketPartitioner.GetBucketStart(timestamp, BucketGranularity.Day);

        // Assert
        Assert.Equal(new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), bucketStart);
        _output.WriteLine($"✓ Day bucket: {bucketStart:yyyy-MM-dd}");
    }

    [Fact]
    public void BucketPartitioner_GetBucketStart_Week_ReturnsMonday()
    {
        // Arrange: February 15, 2026 is a Sunday
        var timestamp = new DateTime(2026, 2, 15, 14, 35, 42, DateTimeKind.Utc);

        // Act
        var bucketStart = BucketPartitioner.GetBucketStart(timestamp, BucketGranularity.Week);

        // Assert: Week starts on Monday, February 9
        Assert.Equal(DayOfWeek.Monday, bucketStart.DayOfWeek);
        Assert.Equal(new DateTime(2026, 2, 9, 0, 0, 0, DateTimeKind.Utc), bucketStart);
        _output.WriteLine($"✓ Week bucket: {bucketStart:yyyy-MM-dd} (Monday)");
    }

    [Fact]
    public void BucketPartitioner_GetBucketStart_Month_ReturnsFirstDay()
    {
        // Arrange
        var timestamp = new DateTime(2026, 2, 15, 14, 35, 42, DateTimeKind.Utc);

        // Act
        var bucketStart = BucketPartitioner.GetBucketStart(timestamp, BucketGranularity.Month);

        // Assert
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), bucketStart);
        _output.WriteLine($"✓ Month bucket: {bucketStart:yyyy-MM-dd}");
    }

    [Fact]
    public void BucketPartitioner_GetBucketEnd_CalculatesCorrectly()
    {
        // Arrange
        var start = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.Equal(start.AddMinutes(1), BucketPartitioner.GetBucketEnd(start, BucketGranularity.Minute));
        Assert.Equal(start.AddHours(1), BucketPartitioner.GetBucketEnd(start, BucketGranularity.Hour));
        Assert.Equal(start.AddDays(1), BucketPartitioner.GetBucketEnd(start, BucketGranularity.Day));
        Assert.Equal(start.AddDays(7), BucketPartitioner.GetBucketEnd(start, BucketGranularity.Week));

        _output.WriteLine("✓ All bucket end calculations correct");
    }

    [Fact]
    public void BucketPartitioner_GetBucketId_GeneratesUniqueId()
    {
        // Arrange
        var timestamp = new DateTime(2026, 2, 15, 14, 35, 42, DateTimeKind.Utc);

        // Act
        var hourId = BucketPartitioner.GetBucketId("metrics", timestamp, BucketGranularity.Hour);
        var dayId = BucketPartitioner.GetBucketId("metrics", timestamp, BucketGranularity.Day);

        // Assert
        Assert.Equal("metrics_Hour_2026021514", hourId);
        Assert.Equal("metrics_Day_20260215", dayId);
        _output.WriteLine($"✓ Hour ID: {hourId}");
        _output.WriteLine($"✓ Day ID: {dayId}");
    }

    [Fact]
    public void BucketPartitioner_GetBucketIdsForRange_ReturnsAllBuckets()
    {
        // Arrange: 3-hour range
        var start = new DateTime(2026, 2, 15, 10, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 2, 15, 13, 0, 0, DateTimeKind.Utc);

        // Act
        var bucketIds = BucketPartitioner.GetBucketIdsForRange("metrics", start, end, BucketGranularity.Hour).ToList();

        // Assert
        Assert.Equal(3, bucketIds.Count);
        Assert.Contains("metrics_Hour_2026021510", bucketIds);
        Assert.Contains("metrics_Hour_2026021511", bucketIds);
        Assert.Contains("metrics_Hour_2026021512", bucketIds);
        _output.WriteLine($"✓ Found {bucketIds.Count} buckets for 3-hour range");
    }

    [Fact]
    public void BucketPartitioner_RecommendGranularity_HighFrequency_ReturnsHour()
    {
        // Arrange: 1M points per day
        var queryRange = TimeSpan.FromDays(7);
        long pointsPerDay = 1_000_000;

        // Act
        var granularity = BucketPartitioner.RecommendGranularity(queryRange, pointsPerDay);

        // Assert
        Assert.Equal(BucketGranularity.Hour, granularity);
        _output.WriteLine($"✓ High frequency ({pointsPerDay}/day) → Hour granularity");
    }

    [Fact]
    public void BucketPartitioner_RecommendGranularity_LowFrequency_ReturnsWeekOrMonth()
    {
        // Arrange: 100 points per day
        var queryRange = TimeSpan.FromDays(90);
        long pointsPerDay = 100;

        // Act
        var granularity = BucketPartitioner.RecommendGranularity(queryRange, pointsPerDay);

        // Assert
        Assert.True(granularity == BucketGranularity.Week || granularity == BucketGranularity.Month);
        _output.WriteLine($"✓ Low frequency ({pointsPerDay}/day) → {granularity} granularity");
    }

    // ========================================
    // TimeSeriesBucket Tests
    // ========================================

    [Fact]
    public void TimeSeriesBucket_ContainsTimestamp_ReturnsCorrectly()
    {
        // Arrange
        var bucket = new TimeSeriesBucket
        {
            BucketId = "test_Hour_2026021514",
            TableName = "test",
            StartTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc),
            Granularity = BucketGranularity.Hour
        };

        // Act & Assert
        Assert.True(bucket.ContainsTimestamp(new DateTime(2026, 2, 15, 14, 30, 0, DateTimeKind.Utc)));
        Assert.True(bucket.ContainsTimestamp(new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc))); // Start inclusive
        Assert.False(bucket.ContainsTimestamp(new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc))); // End exclusive
        Assert.False(bucket.ContainsTimestamp(new DateTime(2026, 2, 15, 13, 59, 59, DateTimeKind.Utc)));

        _output.WriteLine("✓ Bucket timestamp containment works correctly");
    }

    [Fact]
    public void TimeSeriesBucket_OverlapsRange_ReturnsCorrectly()
    {
        // Arrange
        var bucket = new TimeSeriesBucket
        {
            BucketId = "test_Hour_2026021514",
            TableName = "test",
            StartTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc),
            Granularity = BucketGranularity.Hour
        };

        // Act & Assert
        Assert.True(bucket.OverlapsRange(
            new DateTime(2026, 2, 15, 14, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 15, 14, 45, 0, DateTimeKind.Utc)));

        Assert.True(bucket.OverlapsRange(
            new DateTime(2026, 2, 15, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 15, 14, 30, 0, DateTimeKind.Utc)));

        Assert.False(bucket.OverlapsRange(
            new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 15, 16, 0, 0, DateTimeKind.Utc)));

        _output.WriteLine("✓ Bucket range overlap detection works correctly");
    }

    // ========================================
    // BucketManager Tests
    // ========================================

    [Fact]
    public void BucketManager_GetOrCreateBucket_CreatesNewBucket()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var timestamp = new DateTime(2026, 2, 15, 14, 30, 0, DateTimeKind.Utc);

        // Act
        var bucket = manager.GetOrCreateBucket("metrics", timestamp);

        // Assert
        Assert.NotNull(bucket);
        Assert.Equal("metrics", bucket.TableName);
        Assert.Equal(BucketGranularity.Hour, bucket.Granularity);
        Assert.Equal(BucketTier.Hot, bucket.Tier);
        Assert.Equal(1, manager.BucketCount);

        _output.WriteLine($"✓ Created bucket: {bucket.BucketId}");
    }

    [Fact]
    public void BucketManager_GetOrCreateBucket_ReusesExistingBucket()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var timestamp1 = new DateTime(2026, 2, 15, 14, 15, 0, DateTimeKind.Utc);
        var timestamp2 = new DateTime(2026, 2, 15, 14, 45, 0, DateTimeKind.Utc);

        // Act
        var bucket1 = manager.GetOrCreateBucket("metrics", timestamp1);
        var bucket2 = manager.GetOrCreateBucket("metrics", timestamp2);

        // Assert
        Assert.Same(bucket1, bucket2); // Same bucket object
        Assert.Equal(1, manager.BucketCount);

        _output.WriteLine("✓ Reused existing bucket for same hour");
    }

    [Fact]
    public void BucketManager_Insert_StoresDataInBucket()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var points = Enumerable.Range(0, 100)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc).AddMinutes(i * 0.5),
                Value = 20.0 + i * 0.1
            })
            .ToList();

        // Act
        manager.Insert("metrics", points);

        // Assert
        var bucket = manager.GetBuckets("metrics").First();
        Assert.Equal(100, bucket.RowCount);
        Assert.NotNull(bucket.MinTimestamp);
        Assert.NotNull(bucket.MaxTimestamp);

        _output.WriteLine($"✓ Inserted {bucket.RowCount} points into bucket");
    }

    [Fact]
    public void BucketManager_Query_ReturnsCorrectPoints()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 60)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i
            })
            .ToList();

        manager.Insert("metrics", points);

        // Act: Query first 30 minutes
        var queryStart = baseTime;
        var queryEnd = baseTime.AddMinutes(30);
        var results = manager.Query("metrics", queryStart, queryEnd).ToList();

        // Assert
        Assert.Equal(30, results.Count);
        Assert.All(results, p => Assert.True(p.Timestamp >= queryStart && p.Timestamp < queryEnd));

        _output.WriteLine($"✓ Query returned {results.Count} points");
    }

    [Fact]
    public void BucketManager_CompressBucket_ChangesToWarmTier()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 100)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i * 0.5),
                Value = 20.0 + i * 0.1
            })
            .ToList();

        manager.Insert("metrics", points);
        var bucket = manager.GetBuckets("metrics").First();

        // Act
        var result = manager.CompressBucket(bucket.BucketId);

        // Assert
        Assert.True(result);
        Assert.Equal(BucketTier.Warm, bucket.Tier);
        Assert.True(bucket.IsSealed);
        Assert.True(bucket.CompressedSize > 0);

        _output.WriteLine($"✓ Bucket compressed: {bucket.UncompressedSize} → {bucket.CompressedSize} bytes ({bucket.CompressionRatio:F1}x)");
    }

    [Fact]
    public void BucketManager_Query_WorksWithCompressedData()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 100)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i * 0.5),
                Value = i
            })
            .ToList();

        manager.Insert("metrics", points);
        var bucket = manager.GetBuckets("metrics").First();
        manager.CompressBucket(bucket.BucketId);

        // Act: Query compressed data
        var results = manager.Query("metrics", baseTime, baseTime.AddHours(1)).ToList();

        // Assert
        Assert.Equal(100, results.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i, results[i].Value, precision: 5);
        }

        _output.WriteLine($"✓ Query from compressed bucket returned {results.Count} correct points");
    }

    [Fact]
    public void BucketManager_GetStats_ReturnsCorrectStatistics()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Insert across 3 hours
        for (int hour = 0; hour < 3; hour++)
        {
            var points = Enumerable.Range(0, 100)
                .Select(i => new TimeSeriesDataPoint
                {
                    Timestamp = baseTime.AddHours(hour).AddMinutes(i * 0.5),
                    Value = i
                })
                .ToList();

            manager.Insert("metrics", points);
        }

        // Act
        var stats = manager.GetStats();

        // Assert
        Assert.Equal(3, stats.TotalBuckets);
        Assert.Equal(300, stats.TotalRows);
        Assert.Equal(3, stats.HotBuckets);
        Assert.Equal(0, stats.WarmBuckets);

        _output.WriteLine($"✓ Stats: {stats.TotalBuckets} buckets, {stats.TotalRows} rows");
    }

    // ========================================
    // TimeSeriesTable Tests
    // ========================================

    [Fact]
    public void TimeSeriesTable_Insert_StoresData()
    {
        // Arrange
        using var table = new TimeSeriesTable("cpu_usage", BucketGranularity.Hour, writeBufferSize: 10);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Act
        for (int i = 0; i < 20; i++)
        {
            table.Insert(baseTime.AddMinutes(i), 50.0 + i);
        }

        table.Flush();

        // Assert
        var count = table.Count(baseTime, baseTime.AddHours(1));
        Assert.Equal(20, count);

        _output.WriteLine($"✓ Inserted and queried {count} points");
    }

    [Fact]
    public void TimeSeriesTable_Aggregations_CalculateCorrectly()
    {
        // Arrange
        using var table = new TimeSeriesTable("temperature", BucketGranularity.Hour, writeBufferSize: 100);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Insert 10 points: values 0, 1, 2, ..., 9
        for (int i = 0; i < 10; i++)
        {
            table.Insert(baseTime.AddMinutes(i), i);
        }

        table.Flush();

        // Act
        var queryStart = baseTime;
        var queryEnd = baseTime.AddHours(1);

        var count = table.Count(queryStart, queryEnd);
        var sum = table.Sum(queryStart, queryEnd);
        var avg = table.Average(queryStart, queryEnd);
        var min = table.Min(queryStart, queryEnd);
        var max = table.Max(queryStart, queryEnd);

        // Assert
        Assert.Equal(10, count);
        Assert.Equal(45.0, sum); // 0+1+2+...+9 = 45
        Assert.Equal(4.5, avg);
        Assert.Equal(0.0, min);
        Assert.Equal(9.0, max);

        _output.WriteLine($"✓ Aggregations: count={count}, sum={sum}, avg={avg}, min={min}, max={max}");
    }

    [Fact]
    public void TimeSeriesTable_GetStats_ReturnsCompleteStats()
    {
        // Arrange
        using var table = new TimeSeriesTable("metrics", BucketGranularity.Hour, writeBufferSize: 100);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < 100; i++)
        {
            table.Insert(baseTime.AddMinutes(i * 0.5), 10.0 + i);
        }

        table.Flush();

        // Act
        var stats = table.GetStats(baseTime, baseTime.AddHours(1));

        // Assert
        Assert.Equal(100, stats.Count);
        Assert.Equal(10.0, stats.First);
        Assert.Equal(109.0, stats.Last);
        Assert.Equal(10.0, stats.Min);
        Assert.Equal(109.0, stats.Max);

        _output.WriteLine($"✓ Stats: count={stats.Count}, first={stats.First}, last={stats.Last}");
    }

    [Fact]
    public void TimeSeriesTable_GroupBy_AggregatesCorrectly()
    {
        // Arrange
        using var table = new TimeSeriesTable("metrics", BucketGranularity.Hour, writeBufferSize: 200);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Insert 60 points (1 per minute for 1 hour)
        for (int i = 0; i < 60; i++)
        {
            table.Insert(baseTime.AddMinutes(i), i);
        }

        table.Flush();

        // Act: Group by 15-minute intervals
        var groups = table.GroupBy(baseTime, baseTime.AddHours(1), TimeSpan.FromMinutes(15)).ToList();

        // Assert
        Assert.Equal(4, groups.Count); // 4 x 15-minute intervals
        Assert.Equal(15, groups[0].Count); // First 15 minutes: 0-14
        Assert.Equal(105.0, groups[0].Sum); // 0+1+2+...+14 = 105

        _output.WriteLine($"✓ GroupBy returned {groups.Count} intervals");
        foreach (var g in groups)
        {
            _output.WriteLine($"  {g.StartTime:HH:mm}-{g.EndTime:HH:mm}: count={g.Count}, avg={g.Average:F1}");
        }
    }

    [Fact]
    public void TimeSeriesTable_CrossBucketQuery_WorksCorrectly()
    {
        // Arrange
        using var table = new TimeSeriesTable("metrics", BucketGranularity.Hour, writeBufferSize: 200);
        var baseTime = new DateTime(2026, 2, 15, 13, 30, 0, DateTimeKind.Utc);

        // Insert 120 points across 2 hours (60 points per hour)
        for (int i = 0; i < 120; i++)
        {
            table.Insert(baseTime.AddMinutes(i), i);
        }

        table.Flush();

        // Act: Query across bucket boundary
        var queryStart = new DateTime(2026, 2, 15, 13, 45, 0, DateTimeKind.Utc);
        var queryEnd = new DateTime(2026, 2, 15, 14, 15, 0, DateTimeKind.Utc);
        var results = table.Query(queryStart, queryEnd).ToList();

        // Assert: Should get 30 points (15 from first hour, 15 from second)
        Assert.Equal(30, results.Count);
        Assert.All(results, p => Assert.True(p.Timestamp >= queryStart && p.Timestamp < queryEnd));

        _output.WriteLine($"✓ Cross-bucket query returned {results.Count} points");
    }
}
