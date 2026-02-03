// <copyright file="RetentionPolicy.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.TimeSeries;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Data retention policy for time-series data.
/// C# 14: Record types, required members, modern patterns.
/// 
/// ✅ SCDB Phase 8.4: Downsampling & Retention
/// 
/// Purpose:
/// - Define data lifecycle rules
/// - Configure retention periods per tier
/// - Automatic expiration detection
/// - Support for downsampling policies
/// </summary>
public sealed record RetentionPolicy
{
    /// <summary>Unique policy name.</summary>
    public required string Name { get; init; }

    /// <summary>How long to keep raw data in hot tier.</summary>
    public TimeSpan HotRetention { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long to keep compressed data in warm tier.</summary>
    public TimeSpan WarmRetention { get; init; } = TimeSpan.FromDays(7);

    /// <summary>How long to keep archived data in cold tier.</summary>
    public TimeSpan ColdRetention { get; init; } = TimeSpan.FromDays(90);

    /// <summary>Total retention period (sum of all tiers).</summary>
    public TimeSpan TotalRetention => HotRetention + WarmRetention + ColdRetention;

    /// <summary>Downsampling rules to apply.</summary>
    public IReadOnlyList<DownsamplingRule> DownsamplingRules { get; init; } = [];

    /// <summary>Whether to automatically delete expired data.</summary>
    public bool AutoPurge { get; init; } = true;

    /// <summary>Whether to automatically archive cold data.</summary>
    public bool AutoArchive { get; init; } = true;

    /// <summary>
    /// Gets the tier a bucket should be in based on its age.
    /// </summary>
    public BucketTier GetTierForAge(TimeSpan age)
    {
        if (age <= HotRetention)
            return BucketTier.Hot;

        if (age <= HotRetention + WarmRetention)
            return BucketTier.Warm;

        if (age <= TotalRetention)
            return BucketTier.Cold;

        // Beyond retention - should be purged
        return BucketTier.Cold;
    }

    /// <summary>
    /// Checks if data of the given age should be purged.
    /// </summary>
    public bool ShouldPurge(TimeSpan age)
    {
        return AutoPurge && age > TotalRetention;
    }

    /// <summary>
    /// Checks if a bucket should transition to a different tier.
    /// </summary>
    public bool ShouldTransition(TimeSeriesBucket bucket, DateTime now)
    {
        var age = now - bucket.EndTime;
        var expectedTier = GetTierForAge(age);
        return bucket.Tier != expectedTier;
    }

    /// <summary>
    /// Gets the downsampling rule that applies to data of the given age.
    /// </summary>
    public DownsamplingRule? GetDownsamplingRuleForAge(TimeSpan age)
    {
        return DownsamplingRules
            .Where(r => age >= r.AgeThreshold)
            .OrderByDescending(r => r.AgeThreshold)
            .FirstOrDefault();
    }

    /// <summary>
    /// Validates the policy configuration.
    /// </summary>
    public PolicyValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (HotRetention <= TimeSpan.Zero)
            errors.Add("HotRetention must be positive");

        if (WarmRetention < TimeSpan.Zero)
            errors.Add("WarmRetention cannot be negative");

        if (ColdRetention < TimeSpan.Zero)
            errors.Add("ColdRetention cannot be negative");

        if (TotalRetention > TimeSpan.FromDays(365 * 10))
            warnings.Add("Total retention exceeds 10 years");

        // Validate downsampling rules
        var previousThreshold = TimeSpan.Zero;
        foreach (var rule in DownsamplingRules.OrderBy(r => r.AgeThreshold))
        {
            if (rule.AgeThreshold < previousThreshold)
            {
                errors.Add($"Downsampling rules must have increasing age thresholds");
                break;
            }

            if (rule.TargetInterval <= TimeSpan.Zero)
            {
                errors.Add($"Downsampling rule target interval must be positive");
            }

            previousThreshold = rule.AgeThreshold;
        }

        return new PolicyValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Creates a default retention policy.
    /// </summary>
    public static RetentionPolicy Default => new()
    {
        Name = "default",
        HotRetention = TimeSpan.FromHours(1),
        WarmRetention = TimeSpan.FromDays(7),
        ColdRetention = TimeSpan.FromDays(90)
    };

    /// <summary>
    /// Creates a high-frequency metrics policy (short retention).
    /// </summary>
    public static RetentionPolicy HighFrequency => new()
    {
        Name = "high-frequency",
        HotRetention = TimeSpan.FromMinutes(15),
        WarmRetention = TimeSpan.FromHours(24),
        ColdRetention = TimeSpan.FromDays(7),
        DownsamplingRules =
        [
            new DownsamplingRule
            {
                AgeThreshold = TimeSpan.FromMinutes(15),
                TargetInterval = TimeSpan.FromMinutes(1),
                Strategy = AggregationType.Average
            },
            new DownsamplingRule
            {
                AgeThreshold = TimeSpan.FromHours(1),
                TargetInterval = TimeSpan.FromMinutes(5),
                Strategy = AggregationType.Average
            },
            new DownsamplingRule
            {
                AgeThreshold = TimeSpan.FromHours(24),
                TargetInterval = TimeSpan.FromHours(1),
                Strategy = AggregationType.Average
            }
        ]
    };

    /// <summary>
    /// Creates a long-term storage policy.
    /// </summary>
    public static RetentionPolicy LongTerm => new()
    {
        Name = "long-term",
        HotRetention = TimeSpan.FromHours(24),
        WarmRetention = TimeSpan.FromDays(30),
        ColdRetention = TimeSpan.FromDays(365),
        DownsamplingRules =
        [
            new DownsamplingRule
            {
                AgeThreshold = TimeSpan.FromDays(1),
                TargetInterval = TimeSpan.FromMinutes(5),
                Strategy = AggregationType.Average
            },
            new DownsamplingRule
            {
                AgeThreshold = TimeSpan.FromDays(7),
                TargetInterval = TimeSpan.FromHours(1),
                Strategy = AggregationType.Average
            },
            new DownsamplingRule
            {
                AgeThreshold = TimeSpan.FromDays(30),
                TargetInterval = TimeSpan.FromDays(1),
                Strategy = AggregationType.Average
            }
        ]
    };
}

/// <summary>
/// Rule for downsampling data at a certain age.
/// </summary>
public sealed record DownsamplingRule
{
    /// <summary>Age at which this rule applies.</summary>
    public required TimeSpan AgeThreshold { get; init; }

    /// <summary>Target interval after downsampling.</summary>
    public required TimeSpan TargetInterval { get; init; }

    /// <summary>Aggregation strategy to use.</summary>
    public required AggregationType Strategy { get; init; }

    /// <summary>Whether to keep the original data after downsampling.</summary>
    public bool KeepOriginal { get; init; }
}

/// <summary>
/// Result of policy validation.
/// </summary>
public sealed record PolicyValidationResult
{
    /// <summary>Whether the policy is valid.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Validation errors.</summary>
    public required IReadOnlyList<string> Errors { get; init; }

    /// <summary>Validation warnings.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Manages retention policies for multiple tables.
/// </summary>
public sealed class RetentionPolicyManager
{
    private readonly Dictionary<string, RetentionPolicy> _policies = [];
    private readonly Lock _lock = new();

    /// <summary>Default policy for tables without a specific policy.</summary>
    public RetentionPolicy DefaultPolicy { get; set; } = RetentionPolicy.Default;

    /// <summary>
    /// Sets the retention policy for a table.
    /// </summary>
    public void SetPolicy(string tableName, RetentionPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(policy);

        var validation = policy.Validate();
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                $"Invalid policy: {string.Join(", ", validation.Errors)}",
                nameof(policy));
        }

        lock (_lock)
        {
            _policies[tableName] = policy;
        }
    }

    /// <summary>
    /// Gets the retention policy for a table.
    /// </summary>
    public RetentionPolicy GetPolicy(string tableName)
    {
        lock (_lock)
        {
            return _policies.TryGetValue(tableName, out var policy)
                ? policy
                : DefaultPolicy;
        }
    }

    /// <summary>
    /// Removes the policy for a table (falls back to default).
    /// </summary>
    public bool RemovePolicy(string tableName)
    {
        lock (_lock)
        {
            return _policies.Remove(tableName);
        }
    }

    /// <summary>
    /// Gets all table names with custom policies.
    /// </summary>
    public IEnumerable<string> GetTablesWithPolicies()
    {
        lock (_lock)
        {
            return _policies.Keys.ToList();
        }
    }
}
