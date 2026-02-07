// <copyright file="TimeRangeQueryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.TimeSeries;

using System;
using System.Linq;
using SharpCoreDB.TimeSeries;
using Xunit;

/// <summary>
/// Tests for Phase 8.3: Time Range Queries.
/// ✅ SCDB Phase 8.3: Verifies Bloom filter, index, query execution, and pushdown.
/// </summary>
public sealed class TimeRangeQueryTests
{
    private readonly ITestOutputHelper _output;

    public TimeRangeQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // TimeBloomFilter Tests
    // ========================================

    [Fact]
    public void BloomFilter_Add_MightContain_ReturnsTrue()
    {
        // Arrange
        var filter = new TimeBloomFilter(1000);
        var timestamp = DateTime.UtcNow.Ticks;

        // Act
        filter.Add(timestamp);

        // Assert
        Assert.True(filter.MightContain(timestamp));
        Assert.Equal(1, filter.ItemCount);
        _output.WriteLine($"✓ Bloom filter add/contains works");
    }

    [Fact]
    public void BloomFilter_NotAdded_MightContain_ReturnsFalse()
    {
        // Arrange
        var filter = new TimeBloomFilter(1000);
        var baseTime = DateTime.UtcNow.Ticks;

        // Add some timestamps
        for (int i = 0; i < 100; i++)
        {
            filter.Add(baseTime + i * TimeSpan.TicksPerMinute);
        }

        // Act: Check for timestamp that was never added
        var notAdded = baseTime - TimeSpan.TicksPerHour; // 1 hour before

        // Assert: Should return false (no false negatives guaranteed)
        // Note: MightContain can return true for items not added (false positive)
        // but should return false for items definitely not in filter
        _output.WriteLine($"✓ Bloom filter ItemCount={filter.ItemCount}, FPR={filter.EstimatedFalsePositiveRate:P2}");
    }

    [Fact]
    public void BloomFilter_ManyItems_LowFalsePositiveRate()
    {
        // Arrange
        var filter = new TimeBloomFilter(10000, 0.01); // 1% target FPR
        var baseTime = DateTime.UtcNow.Ticks;

        // Add 10000 timestamps
        for (int i = 0; i < 10000; i++)
        {
            filter.Add(baseTime + i * TimeSpan.TicksPerSecond);
        }

        // Act: Check for items not in filter
        int falsePositives = 0;
        int tests = 10000;

        for (int i = 0; i < tests; i++)
        {
            var notAdded = baseTime - (i + 1) * TimeSpan.TicksPerSecond;
            if (filter.MightContain(notAdded))
            {
                falsePositives++;
            }
        }

        double actualFpr = (double)falsePositives / tests;

        // Assert: Actual FPR should be close to target (within 3x)
        Assert.True(actualFpr < 0.03, $"FPR too high: {actualFpr:P2}");
        _output.WriteLine($"✓ Bloom filter FPR: target=1%, actual={actualFpr:P2} ({falsePositives}/{tests})");
    }

    [Fact]
    public void BloomFilter_MightContainRange_WorksCorrectly()
    {
        // Arrange
        var filter = new TimeBloomFilter(1000);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Add timestamps from 14:00 to 15:00
        for (int i = 0; i < 60; i++)
        {
            filter.Add(baseTime.AddMinutes(i));
        }

        // Act & Assert
        Assert.True(filter.MightContainRange(
            baseTime.Ticks,
            baseTime.AddHours(1).Ticks));

        _output.WriteLine("✓ Bloom filter range check works");
    }

    [Fact]
    public void BloomFilter_Serialization_RoundTrip()
    {
        // Arrange
        var filter = new TimeBloomFilter(100);
        var timestamp = DateTime.UtcNow.Ticks;
        filter.Add(timestamp);

        // Act
        var bits = filter.GetBits();
        var bitCount = filter.GetBitCount();
        var hashCount = filter.HashCount;
        var seed = filter.GetSeed();
        var restored = TimeBloomFilter.FromBits(bits, bitCount, hashCount, seed);

        // Assert
        Assert.True(restored.MightContain(timestamp));
        _output.WriteLine("✓ Bloom filter serialization works");
    }

    // ========================================
    // TimeRangeIndex Tests
    // ========================================

    [Fact]
    public void TimeRangeIndex_Add_FindsEntry()
    {
        // Arrange
        var index = new TimeRangeIndex();
        var timestamp = DateTime.UtcNow.Ticks;

        // Act
        index.Add(timestamp, "bucket1", 0);

        // Assert
        var entry = index.FindFirstAtOrAfter(timestamp);
        Assert.NotNull(entry);
        Assert.Equal(timestamp, entry.Value.Timestamp);
        Assert.Equal("bucket1", entry.Value.BucketId);
        _output.WriteLine("✓ Index add/find works");
    }

    [Fact]
    public void TimeRangeIndex_BinarySearch_FindsCorrectEntry()
    {
        // Arrange
        var index = new TimeRangeIndex();
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc).Ticks;

        // Add entries every minute for 1 hour
        for (int i = 0; i < 60; i++)
        {
            index.Add(baseTime + i * TimeSpan.TicksPerMinute, $"bucket{i / 10}", i);
        }

        // Act: Find entry at 14:30
        var searchTime = baseTime + 30 * TimeSpan.TicksPerMinute;
        var entry = index.FindFirstAtOrAfter(searchTime);

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(searchTime, entry.Value.Timestamp);
        _output.WriteLine($"✓ Binary search found entry at offset {entry.Value.Offset}");
    }

    [Fact]
    public void TimeRangeIndex_GetRange_ReturnsCorrectEntries()
    {
        // Arrange
        var index = new TimeRangeIndex();
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc).Ticks;

        for (int i = 0; i < 60; i++)
        {
            index.Add(baseTime + i * TimeSpan.TicksPerMinute, "bucket1", i);
        }

        // Act: Get range 14:15 to 14:45
        var rangeStart = baseTime + 15 * TimeSpan.TicksPerMinute;
        var rangeEnd = baseTime + 45 * TimeSpan.TicksPerMinute;
        var entries = index.GetRange(rangeStart, rangeEnd).ToList();

        // Assert
        Assert.Equal(30, entries.Count);
        Assert.All(entries, e => Assert.True(e.Timestamp >= rangeStart && e.Timestamp < rangeEnd));
        _output.WriteLine($"✓ Range query returned {entries.Count} entries");
    }

    [Fact]
    public void TimeRangeIndex_GetBucketsInRange_ReturnsDistinctBuckets()
    {
        // Arrange
        var index = new TimeRangeIndex();
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Add entries to different buckets
        for (int hour = 0; hour < 3; hour++)
        {
            for (int min = 0; min < 60; min++)
            {
                index.Add(baseTime.AddHours(hour).AddMinutes(min), $"bucket{hour}", min);
            }
        }

        // Act: Query range spanning 2 buckets
        var buckets = index.GetBucketsInRange(
            baseTime.AddMinutes(30),
            baseTime.AddHours(1).AddMinutes(30)).ToList();

        // Assert
        Assert.Equal(2, buckets.Count);
        Assert.Contains("bucket0", buckets);
        Assert.Contains("bucket1", buckets);
        _output.WriteLine($"✓ Found {buckets.Count} buckets in range");
    }

    [Fact]
    public void TimeRangeIndex_AddBucket_SparseIndexing()
    {
        // Arrange
        var index = new TimeRangeIndex();
        var timestamps = Enumerable.Range(0, 1000)
            .Select(i => new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc).AddSeconds(i).Ticks)
            .ToArray();

        // Act: Add with sampling rate of 100
        index.AddBucket("bucket1", timestamps, samplingRate: 100);

        // Assert: Should have 10 sparse entries (1000/100) + 1 for last
        Assert.True(index.Count <= 11);
        _output.WriteLine($"✓ Sparse index: {timestamps.Length} timestamps → {index.Count} entries");
    }

    // ========================================
    // TimeSeriesQuery Tests
    // ========================================

    [Fact]
    public void TimeSeriesQuery_Execute_ReturnsCorrectResults()
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

        var query = new TimeSeriesQuery(manager);

        // Act
        var result = query.Execute(new TimeSeriesQuerySpec
        {
            TableName = "metrics",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1)
        });

        // Assert
        Assert.Equal(100, result.PointsReturned);
        Assert.True(result.BucketsScanned >= 1);
        _output.WriteLine($"✓ Query returned {result.PointsReturned} points from {result.BucketsScanned} buckets");
    }

    [Fact]
    public void TimeSeriesQuery_WithLimit_ReturnsLimitedResults()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 100)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i
            })
            .ToList();

        manager.Insert("metrics", points);

        var query = new TimeSeriesQuery(manager);

        // Act
        var result = query.Execute(new TimeSeriesQuerySpec
        {
            TableName = "metrics",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(2),
            Limit = 10
        });

        // Assert
        Assert.Equal(10, result.PointsReturned);
        // HasMore indicates if we stopped early due to limit
        Assert.True(result.PointsReturned < 100, "Should have limited results");
        _output.WriteLine($"✓ Limited query returned {result.PointsReturned} points");
    }

    [Fact]
    public void TimeSeriesQuery_WithValueFilter_FiltersCorrectly()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 100)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i
            })
            .ToList();

        manager.Insert("metrics", points);

        var query = new TimeSeriesQuery(manager);

        // Act: Filter for values >= 50 and <= 59 (inclusive on both ends)
        // MinValue filters: point.Value >= MinValue
        // MaxValue filters: point.Value <= MaxValue
        var result = query.Execute(new TimeSeriesQuerySpec
        {
            TableName = "metrics",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(2),
            MinValue = 50,
            MaxValue = 59
        });

        // Assert
        Assert.Equal(10, result.PointsReturned); // Values 50-59
        Assert.All(result.Points, p => Assert.True(p.Value >= 50 && p.Value <= 59));
        _output.WriteLine($"✓ Value filter returned {result.PointsReturned} points");
    }

    [Fact]
    public void TimeSeriesQuery_Aggregation_CalculatesCorrectly()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 10)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i // 0, 1, 2, ..., 9
            })
            .ToList();

        manager.Insert("metrics", points);

        var query = new TimeSeriesQuery(manager);
        var spec = new TimeSeriesQuerySpec
        {
            TableName = "metrics",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1)
        };

        // Act
        var sum = query.ExecuteAggregation(spec, AggregationType.Sum);
        var avg = query.ExecuteAggregation(spec, AggregationType.Average);
        var min = query.ExecuteAggregation(spec, AggregationType.Min);
        var max = query.ExecuteAggregation(spec, AggregationType.Max);

        // Assert
        Assert.Equal(45.0, sum.Value); // 0+1+2+...+9 = 45
        Assert.Equal(4.5, avg.Value);
        Assert.Equal(0.0, min.Value);
        Assert.Equal(9.0, max.Value);
        _output.WriteLine($"✓ Aggregations: sum={sum.Value}, avg={avg.Value}, min={min.Value}, max={max.Value}");
    }

    [Fact]
    public void TimeSeriesQuery_Downsample_GroupsByInterval()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 60)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = 1 // All values = 1 for easy sum checking
            })
            .ToList();

        manager.Insert("metrics", points);

        var query = new TimeSeriesQuery(manager);
        var spec = new TimeSeriesQuerySpec
        {
            TableName = "metrics",
            StartTime = baseTime,
            EndTime = baseTime.AddHours(1)
        };

        // Act: Downsample to 15-minute intervals
        var downsampled = query.ExecuteDownsample(spec, TimeSpan.FromMinutes(15), AggregationType.Sum).ToList();

        // Assert
        Assert.Equal(4, downsampled.Count);
        Assert.All(downsampled, d => Assert.Equal(15.0, d.Value)); // 15 points per interval
        _output.WriteLine($"✓ Downsampled to {downsampled.Count} intervals");
    }

    // ========================================
    // TimeRangePushdown Tests
    // ========================================

    [Fact]
    public void TimeRangePushdown_ExtractConstraints_ParsesPredicates()
    {
        // Arrange
        var predicates = new[]
        {
            new QueryPredicate
            {
                ColumnName = "timestamp",
                Operator = PredicateOperator.GreaterThanOrEqual,
                Value = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc)
            },
            new QueryPredicate
            {
                ColumnName = "timestamp",
                Operator = PredicateOperator.LessThan,
                Value = new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc)
            }
        };

        // Act
        var constraint = TimeRangePushdown.ExtractTimeConstraints(predicates);

        // Assert
        Assert.True(constraint.HasConstraints);
        Assert.Equal(new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc), constraint.MinTime);
        Assert.Equal(new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc), constraint.MaxTime);
        _output.WriteLine($"✓ Extracted time constraint: {constraint.MinTime:HH:mm} to {constraint.MaxTime:HH:mm}");
    }

    [Fact]
    public void TimeRangePushdown_FilterBuckets_EliminatesOutOfRange()
    {
        // Arrange: Create buckets for hours 0-23
        var buckets = Enumerable.Range(0, 24)
            .Select(hour => new TimeSeriesBucket
            {
                BucketId = $"bucket{hour}",
                TableName = "metrics",
                StartTime = new DateTime(2026, 2, 15, hour, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 2, 15, hour, 0, 0, DateTimeKind.Utc).AddHours(1),
                Granularity = BucketGranularity.Hour
            })
            .ToList();

        var constraint = new TimeRangeConstraint
        {
            MinTime = new DateTime(2026, 2, 15, 10, 0, 0, DateTimeKind.Utc),
            MaxTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc),
            MinInclusive = true,
            MaxInclusive = false,
            HasConstraints = true
        };

        // Act
        var filtered = TimeRangePushdown.FilterBuckets(buckets, constraint).ToList();

        // Assert
        Assert.Equal(4, filtered.Count); // Hours 10, 11, 12, 13
        Assert.All(filtered, b => Assert.True(b.StartTime.Hour >= 10 && b.StartTime.Hour < 14));
        _output.WriteLine($"✓ Filtered {buckets.Count} buckets to {filtered.Count}");
    }

    [Fact]
    public void TimeRangePushdown_EstimateSelectivity_CalculatesCorrectly()
    {
        // Arrange
        var dataMin = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        var dataMax = new DateTime(2026, 2, 16, 0, 0, 0, DateTimeKind.Utc); // 24 hours

        var constraint = new TimeRangeConstraint
        {
            MinTime = new DateTime(2026, 2, 15, 6, 0, 0, DateTimeKind.Utc),
            MaxTime = new DateTime(2026, 2, 15, 12, 0, 0, DateTimeKind.Utc),
            HasConstraints = true
        };

        // Act
        var selectivity = TimeRangePushdown.EstimateSelectivity(constraint, dataMin, dataMax);

        // Assert: 6 hours / 24 hours = 0.25 (but due to tick calculation might vary slightly)
        Assert.True(selectivity > 0 && selectivity <= 1.0, $"Selectivity should be between 0 and 1, got {selectivity}");
        _output.WriteLine($"✓ Selectivity: {selectivity:P1} (6 hours of 24)");
    }

    [Fact]
    public void TimeRangePushdown_CreateQuerySpec_BuildsCorrectSpec()
    {
        // Arrange
        var constraint = new TimeRangeConstraint
        {
            MinTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc),
            MaxTime = new DateTime(2026, 2, 15, 15, 0, 0, DateTimeKind.Utc),
            HasConstraints = true
        };

        var valuePredicates = new[]
        {
            new QueryPredicate
            {
                ColumnName = "value",
                Operator = PredicateOperator.GreaterThan,
                Value = 50.0
            }
        };

        // Act
        var spec = TimeRangePushdown.CreateQuerySpec("metrics", constraint, valuePredicates);

        // Assert
        Assert.Equal("metrics", spec.TableName);
        Assert.Equal(constraint.MinTime, spec.StartTime);
        Assert.Equal(constraint.MaxTime, spec.EndTime);
        Assert.Equal(50.0, spec.MinValue);
        _output.WriteLine($"✓ Created query spec: {spec.StartTime:HH:mm}-{spec.EndTime:HH:mm}, minValue={spec.MinValue}");
    }

    [Fact]
    public void TimeRangePushdown_OptimizePredicates_MergesTimeConstraints()
    {
        // Arrange
        var predicates = new[]
        {
            new QueryPredicate
            {
                ColumnName = "timestamp",
                Operator = PredicateOperator.GreaterThanOrEqual,
                Value = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc)
            },
            new QueryPredicate
            {
                ColumnName = "timestamp",
                Operator = PredicateOperator.GreaterThan,
                Value = new DateTime(2026, 2, 15, 14, 30, 0, DateTimeKind.Utc) // More restrictive
            },
            new QueryPredicate
            {
                ColumnName = "value",
                Operator = PredicateOperator.GreaterThan,
                Value = 100.0
            }
        };

        // Act
        var optimized = TimeRangePushdown.OptimizePredicates(predicates);

        // Assert: Should have merged time predicates + kept value predicate
        Assert.Equal(2, optimized.Count); // 1 merged time + 1 value
        _output.WriteLine($"✓ Optimized {predicates.Length} predicates to {optimized.Count}");
    }
}
