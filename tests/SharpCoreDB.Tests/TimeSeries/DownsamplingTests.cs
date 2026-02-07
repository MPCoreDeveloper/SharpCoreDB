// <copyright file="DownsamplingTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.TimeSeries;

using System;
using System.Linq;
using SharpCoreDB.TimeSeries;
using Xunit;

/// <summary>
/// Tests for Phase 8.4: Downsampling & Retention.
/// ✅ SCDB Phase 8.4: Verifies retention policies, aggregation, downsampling, and archival.
/// </summary>
public sealed class DownsamplingTests
{
    private readonly ITestOutputHelper _output;

    public DownsamplingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // RetentionPolicy Tests
    // ========================================

    [Fact]
    public void RetentionPolicy_Default_HasReasonableDefaults()
    {
        // Act
        var policy = RetentionPolicy.Default;

        // Assert
        Assert.Equal("default", policy.Name);
        Assert.Equal(TimeSpan.FromHours(1), policy.HotRetention);
        Assert.Equal(TimeSpan.FromDays(7), policy.WarmRetention);
        Assert.Equal(TimeSpan.FromDays(90), policy.ColdRetention);
        _output.WriteLine($"✓ Default policy: Hot={policy.HotRetention}, Warm={policy.WarmRetention}, Cold={policy.ColdRetention}");
    }

    [Fact]
    public void RetentionPolicy_GetTierForAge_ReturnsCorrectTier()
    {
        // Arrange
        var policy = new RetentionPolicy
        {
            Name = "test",
            HotRetention = TimeSpan.FromHours(1),
            WarmRetention = TimeSpan.FromDays(1),
            ColdRetention = TimeSpan.FromDays(7)
        };

        // Act & Assert
        Assert.Equal(BucketTier.Hot, policy.GetTierForAge(TimeSpan.FromMinutes(30)));
        Assert.Equal(BucketTier.Warm, policy.GetTierForAge(TimeSpan.FromHours(12)));
        Assert.Equal(BucketTier.Cold, policy.GetTierForAge(TimeSpan.FromDays(3)));

        _output.WriteLine("✓ Tier assignment by age works correctly");
    }

    [Fact]
    public void RetentionPolicy_ShouldPurge_DetectsExpiredData()
    {
        // Arrange
        var policy = new RetentionPolicy
        {
            Name = "test",
            HotRetention = TimeSpan.FromHours(1),
            WarmRetention = TimeSpan.FromDays(1),
            ColdRetention = TimeSpan.FromDays(7),
            AutoPurge = true
        };

        // Act & Assert
        Assert.False(policy.ShouldPurge(TimeSpan.FromDays(5))); // Within retention
        Assert.True(policy.ShouldPurge(TimeSpan.FromDays(10))); // Beyond retention

        _output.WriteLine($"✓ Purge detection works (total retention: {policy.TotalRetention.TotalDays} days)");
    }

    [Fact]
    public void RetentionPolicy_Validate_CatchesInvalidConfiguration()
    {
        // Arrange
        var invalidPolicy = new RetentionPolicy
        {
            Name = "invalid",
            HotRetention = TimeSpan.Zero, // Invalid!
            WarmRetention = TimeSpan.FromDays(1),
            ColdRetention = TimeSpan.FromDays(7)
        };

        // Act
        var result = invalidPolicy.Validate();

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        _output.WriteLine($"✓ Validation caught error: {result.Errors[0]}");
    }

    [Fact]
    public void RetentionPolicy_HighFrequency_HasDownsamplingRules()
    {
        // Act
        var policy = RetentionPolicy.HighFrequency;

        // Assert
        Assert.Equal("high-frequency", policy.Name);
        Assert.NotEmpty(policy.DownsamplingRules);
        Assert.Equal(3, policy.DownsamplingRules.Count);

        _output.WriteLine($"✓ High-frequency policy has {policy.DownsamplingRules.Count} downsampling rules");
    }

    [Fact]
    public void RetentionPolicyManager_SetAndGetPolicy_Works()
    {
        // Arrange
        var manager = new RetentionPolicyManager();
        var customPolicy = new RetentionPolicy
        {
            Name = "custom",
            HotRetention = TimeSpan.FromMinutes(30),
            WarmRetention = TimeSpan.FromHours(12),
            ColdRetention = TimeSpan.FromDays(30)
        };

        // Act
        manager.SetPolicy("metrics", customPolicy);
        var retrieved = manager.GetPolicy("metrics");

        // Assert
        Assert.Same(customPolicy, retrieved);
        _output.WriteLine("✓ Policy manager set/get works");
    }

    // ========================================
    // TimeSeriesAggregator Tests
    // ========================================

    [Fact]
    public void Aggregator_Sum_CalculatesCorrectly()
    {
        // Arrange
        double[] values = [1, 2, 3, 4, 5];

        // Act
        var sum = TimeSeriesAggregator.Sum(values);

        // Assert
        Assert.Equal(15.0, sum);
        _output.WriteLine("✓ Sum aggregation: 1+2+3+4+5 = 15");
    }

    [Fact]
    public void Aggregator_Average_CalculatesCorrectly()
    {
        // Arrange
        double[] values = [2, 4, 6, 8, 10];

        // Act
        var avg = TimeSeriesAggregator.Average(values);

        // Assert
        Assert.Equal(6.0, avg);
        _output.WriteLine("✓ Average aggregation: (2+4+6+8+10)/5 = 6");
    }

    [Fact]
    public void Aggregator_MinMax_FindsExtremes()
    {
        // Arrange
        double[] values = [5, 2, 8, 1, 9, 3];

        // Act
        var min = TimeSeriesAggregator.Min(values);
        var max = TimeSeriesAggregator.Max(values);

        // Assert
        Assert.Equal(1.0, min);
        Assert.Equal(9.0, max);
        _output.WriteLine($"✓ Min/Max: min={min}, max={max}");
    }

    [Fact]
    public void Aggregator_StandardDeviation_CalculatesCorrectly()
    {
        // Arrange
        double[] values = [2, 4, 4, 4, 5, 5, 7, 9];

        // Act
        var stddev = TimeSeriesAggregator.StandardDeviation(values);

        // Assert: Known stddev for this dataset is ~2.138
        Assert.True(stddev > 2.0 && stddev < 2.3, $"Expected ~2.14, got {stddev}");
        _output.WriteLine($"✓ Standard deviation: {stddev:F3}");
    }

    [Fact]
    public void Aggregator_Percentile_CalculatesCorrectly()
    {
        // Arrange
        double[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        // Act
        var p50 = TimeSeriesAggregator.Percentile(values, 50); // Median
        var p90 = TimeSeriesAggregator.Percentile(values, 90);

        // Assert
        Assert.Equal(5.5, p50); // Median of 1-10
        Assert.True(p90 > 9.0 && p90 <= 10.0);
        _output.WriteLine($"✓ Percentiles: P50={p50}, P90={p90}");
    }

    [Fact]
    public void Aggregator_WeightedAverage_CalculatesCorrectly()
    {
        // Arrange
        double[] values = [10, 20, 30];
        double[] weights = [1, 2, 3]; // Weights: 1, 2, 3

        // Act
        var weightedAvg = TimeSeriesAggregator.WeightedAverage(values, weights);

        // Assert: (10*1 + 20*2 + 30*3) / (1+2+3) = (10 + 40 + 90) / 6 = 23.33...
        Assert.True(Math.Abs(weightedAvg - 23.333) < 0.01);
        _output.WriteLine($"✓ Weighted average: {weightedAvg:F3}");
    }

    [Fact]
    public void Aggregator_AggregateToIntervals_GroupsCorrectly()
    {
        // Arrange
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);
        var points = Enumerable.Range(0, 60)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = 1 // All values = 1
            })
            .ToList();

        // Act: Aggregate to 15-minute intervals
        var aggregated = TimeSeriesAggregator.AggregateToIntervals(
            points,
            TimeSpan.FromMinutes(15),
            AggregationType.Sum).ToList();

        // Assert: 4 intervals, each with sum = 15
        Assert.Equal(4, aggregated.Count);
        Assert.All(aggregated, a => Assert.Equal(15.0, a.Sum));
        _output.WriteLine($"✓ Aggregated 60 points to {aggregated.Count} intervals");
    }

    [Fact]
    public void Aggregator_CalculateSummary_ReturnsAllStats()
    {
        // Arrange
        double[] values = [1, 2, 3, 4, 5];

        // Act
        var summary = TimeSeriesAggregator.CalculateSummary(values);

        // Assert
        Assert.Equal(5, summary.Count);
        Assert.Equal(15.0, summary.Sum);
        Assert.Equal(3.0, summary.Average);
        Assert.Equal(1.0, summary.Min);
        Assert.Equal(5.0, summary.Max);
        Assert.True(summary.Stddev > 0);
        _output.WriteLine($"✓ Summary: count={summary.Count}, avg={summary.Average}, stddev={summary.Stddev:F2}");
    }

    // ========================================
    // DownsamplingEngine Tests
    // ========================================

    [Fact]
    public void DownsamplingEngine_CreateDownsampledView_AggregatesCorrectly()
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

        using var engine = new DownsamplingEngine(manager);

        // Act: Create 15-minute downsampled view
        var downsampled = engine.CreateDownsampledView(
            "metrics",
            baseTime,
            baseTime.AddHours(1),
            TimeSpan.FromMinutes(15),
            AggregationType.Average).ToList();

        // Assert
        Assert.Equal(4, downsampled.Count);
        _output.WriteLine($"✓ Created downsampled view with {downsampled.Count} intervals");
        foreach (var d in downsampled)
        {
            _output.WriteLine($"  {d.Timestamp:HH:mm}: avg={d.Value:F1}, count={d.Count}");
        }
    }

    [Fact]
    public void DownsamplingEngine_CalculateRollupStats_ReportsCompression()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 1000)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddSeconds(i * 3.6), // 1000 points in 1 hour
                Value = Math.Sin(i * 0.01) * 100
            })
            .ToList();

        manager.Insert("metrics", points);

        using var engine = new DownsamplingEngine(manager);

        // Act
        var stats = engine.CalculateRollupStats(
            "metrics",
            baseTime,
            baseTime.AddHours(1),
            TimeSpan.FromMinutes(5));

        // Assert
        Assert.Equal(1000, stats.OriginalPoints);
        Assert.True(stats.RolledUpPoints < stats.OriginalPoints);
        Assert.True(stats.CompressionRatio > 1);
        _output.WriteLine($"✓ Rollup stats: {stats.OriginalPoints} → {stats.RolledUpPoints} ({stats.CompressionRatio:F1}x compression)");
    }

    [Fact]
    public void DownsamplingEngine_GetDownsampledTable_CreatesTable()
    {
        // Arrange
        using var manager = new BucketManager(BucketGranularity.Hour);
        using var engine = new DownsamplingEngine(manager);

        // Act
        var table = engine.GetDownsampledTable("metrics", TimeSpan.FromMinutes(5));

        // Assert
        Assert.NotNull(table);
        Assert.Equal("metrics", table.SourceTableName);
        Assert.Empty(table.Points);
        _output.WriteLine("✓ Downsampled table created");
    }

    // ========================================
    // ArchivalManager Tests
    // ========================================

    [Fact]
    public void ArchivalManager_ArchiveBucket_StoresData()
    {
        // Arrange
        using var bucketManager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 100)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i * 0.5),
                Value = i
            })
            .ToList();

        bucketManager.Insert("metrics", points);
        var bucket = bucketManager.GetBuckets("metrics").First();

        using var archival = new ArchivalManager(bucketManager);

        // Act
        var result = archival.ArchiveBucket(bucket);

        // Assert
        Assert.True(result);
        Assert.Equal(1, archival.ArchivedBucketCount);
        Assert.Equal(BucketTier.Cold, bucket.Tier);
        _output.WriteLine($"✓ Archived bucket with {bucket.RowCount} rows");
    }

    [Fact]
    public void ArchivalManager_QueryArchived_ReturnsData()
    {
        // Arrange
        using var bucketManager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 50)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i
            })
            .ToList();

        bucketManager.Insert("metrics", points);
        var bucket = bucketManager.GetBuckets("metrics").First();

        using var archival = new ArchivalManager(bucketManager);
        archival.ArchiveBucket(bucket);

        // Act
        var archived = archival.QueryArchived("metrics", baseTime, baseTime.AddHours(1)).ToList();

        // Assert
        Assert.Equal(50, archived.Count);
        _output.WriteLine($"✓ Queried {archived.Count} archived points");
    }

    [Fact]
    public void ArchivalManager_VerifyArchive_ValidatesIntegrity()
    {
        // Arrange
        using var bucketManager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 10)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i
            })
            .ToList();

        bucketManager.Insert("metrics", points);
        var bucket = bucketManager.GetBuckets("metrics").First();

        using var archival = new ArchivalManager(bucketManager);
        archival.ArchiveBucket(bucket);

        // Act
        var verification = archival.VerifyArchive(bucket.BucketId);

        // Assert
        Assert.True(verification.IsValid);
        Assert.Equal(10, verification.RowCount);
        _output.WriteLine($"✓ Archive verified: {verification.RowCount} rows");
    }

    [Fact]
    public void ArchivalManager_RestoreBucket_BringsBackData()
    {
        // Arrange
        using var bucketManager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        var points = Enumerable.Range(0, 20)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i * 10
            })
            .ToList();

        bucketManager.Insert("metrics", points);
        var bucket = bucketManager.GetBuckets("metrics").First();
        var bucketId = bucket.BucketId;

        using var archival = new ArchivalManager(bucketManager);
        archival.ArchiveBucket(bucket);

        // Act
        var result = archival.RestoreBucket(bucketId);

        // Assert
        Assert.True(result);
        Assert.Equal(0, archival.ArchivedBucketCount);
        _output.WriteLine("✓ Bucket restored from archive");
    }

    [Fact]
    public void ArchivalManager_GetStats_ReportsCorrectly()
    {
        // Arrange
        using var bucketManager = new BucketManager(BucketGranularity.Hour);
        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);

        // Insert data across 3 hours
        for (int hour = 0; hour < 3; hour++)
        {
            var points = Enumerable.Range(0, 100)
                .Select(i => new TimeSeriesDataPoint
                {
                    Timestamp = baseTime.AddHours(hour).AddMinutes(i * 0.5),
                    Value = i
                })
                .ToList();

            bucketManager.Insert("metrics", points);
        }

        using var archival = new ArchivalManager(bucketManager);

        foreach (var bucket in bucketManager.GetBuckets("metrics"))
        {
            archival.ArchiveBucket(bucket);
        }

        // Act
        var stats = archival.GetStats();

        // Assert
        Assert.Equal(3, stats.TotalBuckets);
        Assert.Equal(300, stats.TotalRows);
        Assert.True(stats.TotalBytes > 0);
        _output.WriteLine($"✓ Archive stats: {stats.TotalBuckets} buckets, {stats.TotalRows} rows, {stats.TotalBytes} bytes");
    }

    [Fact]
    public void ArchivalManager_RunMaintenance_ProcessesPolicies()
    {
        // Arrange
        using var bucketManager = new BucketManager(BucketGranularity.Hour);
        var policyManager = new RetentionPolicyManager();

        policyManager.SetPolicy("metrics", new RetentionPolicy
        {
            Name = "short",
            HotRetention = TimeSpan.FromMinutes(1),
            WarmRetention = TimeSpan.FromMinutes(5),
            ColdRetention = TimeSpan.FromMinutes(10),
            AutoPurge = true
        });

        var baseTime = new DateTime(2026, 2, 15, 14, 0, 0, DateTimeKind.Utc);
        var points = Enumerable.Range(0, 50)
            .Select(i => new TimeSeriesDataPoint
            {
                Timestamp = baseTime.AddMinutes(i),
                Value = i
            })
            .ToList();

        bucketManager.Insert("metrics", points);

        using var archival = new ArchivalManager(bucketManager, policyManager);

        // Act: Run maintenance as if time has passed
        var futureTime = baseTime.AddHours(2); // 2 hours later
        var result = archival.RunMaintenance(futureTime);

        // Assert
        Assert.True(result.TablesProcessed >= 1);
        _output.WriteLine($"✓ Maintenance: {result.TablesProcessed} tables, {result.BucketsTransitioned} transitions, {result.BucketsPurged} purged");
    }
}
