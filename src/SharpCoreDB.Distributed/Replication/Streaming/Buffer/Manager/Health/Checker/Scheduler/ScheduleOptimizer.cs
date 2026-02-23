// <copyright file="ScheduleOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Intelligent optimizer for health check scheduling based on usage patterns and system conditions.
/// Provides ML-style optimization algorithms for optimal check timing and resource utilization.
/// C# 14: Primary constructors, pattern matching, advanced analytics.
/// </summary>
public sealed class ScheduleOptimizer
{
    private readonly Dictionary<string, BufferScheduleProfile> _bufferProfiles = [];
    private readonly Lock _optimizerLock = new();
    private readonly CostCalculator _costCalculator;
    private readonly OptimizationEngine _optimizationEngine;

    private readonly ILogger<ScheduleOptimizer>? _logger;

    /// <summary>Gets the number of optimized buffer profiles.</summary>
    public int OptimizedBufferCount => _bufferProfiles.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduleOptimizer"/> class.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public ScheduleOptimizer(ILogger<ScheduleOptimizer>? logger = null)
    {
        _logger = logger;
        _costCalculator = new CostCalculator(logger);
        _optimizationEngine = new OptimizationEngine(
            new PatternAnalyzer(logger),
            _costCalculator,
            logger);
    }

    /// <summary>
    /// Optimizes the check schedule for a buffer based on its usage patterns.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="currentInterval">The current check interval.</param>
    /// <param name="usageHistory">The buffer usage history.</param>
    /// <param name="healthHistory">The buffer health history.</param>
    /// <returns>The optimized check interval.</returns>
    public TimeSpan OptimizeSchedule(
        string bufferId,
        TimeSpan currentInterval,
        IReadOnlyList<BufferUsageRecord> usageHistory,
        IReadOnlyList<HealthCheckRecord> healthHistory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        var result = _optimizationEngine.OptimizeSchedule(bufferId, usageHistory, healthHistory, currentInterval);

        // Update local profile cache
        lock (_optimizerLock)
        {
            var profile = GetOrCreateProfile(bufferId);
            profile.UpdatePatterns(usageHistory, healthHistory);
            profile.OptimizedInterval = result.OptimalInterval;
            profile.LastOptimization = result.OptimizationTimestamp;
        }

        _logger?.LogInformation("Optimized schedule for buffer {BufferId}: {CurrentInterval} -> {OptimizedInterval}",
            bufferId, currentInterval, result.OptimalInterval);

        return result.OptimalInterval;
    }

    /// <summary>
    /// Predicts the optimal check time for a buffer based on usage patterns.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="currentTime">The current time.</param>
    /// <returns>The predicted optimal check time.</returns>
    public DateTimeOffset PredictOptimalCheckTime(string bufferId, DateTimeOffset currentTime)
    {
        var profile = GetProfile(bufferId);
        if (profile is null)
        {
            // Default prediction: next regular interval
            return currentTime + TimeSpan.FromMinutes(2);
        }

        return _costCalculator.PredictNextCheckTime(profile, currentTime);
    }

    /// <summary>
    /// Gets optimization recommendations for all buffers.
    /// </summary>
    /// <returns>Collection of optimization recommendations.</returns>
    public IReadOnlyCollection<ScheduleOptimizationRecommendation> GetOptimizationRecommendations()
    {
        lock (_optimizerLock)
        {
            return _bufferProfiles.Values
                .Select(CreateRecommendation)
                .Where(r => r is not null)
                .Cast<ScheduleOptimizationRecommendation>()
                .ToList();
        }
    }

    /// <summary>
    /// Gets optimization statistics.
    /// </summary>
    /// <returns>Optimization statistics.</returns>
    public ScheduleOptimizationStats GetOptimizationStats()
    {
        lock (_optimizerLock)
        {
            var profiles = _bufferProfiles.Values.ToList();

            return new ScheduleOptimizationStats
            {
                OptimizedBuffers = profiles.Count,
                AverageOptimizationImprovement = profiles.Any()
                    ? profiles.Average(p => p.OptimizationImprovementPercent)
                    : 0,
                TotalScheduleAdjustments = profiles.Sum(p => p.ScheduleAdjustmentCount),
                OptimizationDistribution = CalculateOptimizationDistribution(profiles),
                PatternConfidenceLevels = profiles.ToDictionary(
                    p => p.BufferId,
                    p => p.PatternConfidence)
            };
        }
    }

    /// <summary>
    /// Resets optimization data for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    public void ResetOptimization(string bufferId)
    {
        lock (_optimizerLock)
        {
            if (_bufferProfiles.TryGetValue(bufferId, out var profile))
            {
                profile.Reset();
                _logger?.LogInformation("Reset optimization data for buffer {BufferId}", bufferId);
            }
        }
    }

    /// <summary>
    /// Gets or creates a buffer schedule profile.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The buffer schedule profile.</returns>
    private BufferScheduleProfile GetOrCreateProfile(string bufferId)
    {
        lock (_optimizerLock)
        {
            if (!_bufferProfiles.TryGetValue(bufferId, out var profile))
            {
                profile = new BufferScheduleProfile(bufferId);
                _bufferProfiles[bufferId] = profile;
            }
            return profile;
        }
    }

    /// <summary>
    /// Gets a buffer schedule profile.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The buffer schedule profile, or null if not found.</returns>
    private BufferScheduleProfile? GetProfile(string bufferId)
    {
        lock (_optimizerLock)
        {
            return _bufferProfiles.TryGetValue(bufferId, out var profile) ? profile : null;
        }
    }

    /// <summary>
    /// Predicts the next optimal check time.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <param name="currentTime">The current time.</param>
    /// <returns>The predicted check time.</returns>
    private static DateTimeOffset PredictNextCheckTime(BufferScheduleProfile profile, DateTimeOffset currentTime)
    {
        // Simple prediction based on usage patterns
        // In a real implementation, this would use more sophisticated ML algorithms

        if (profile.UsagePatterns.Any())
        {
            // Predict based on typical usage intervals
            var avgIntervalSeconds = profile.UsagePatterns.Average(p => p.UsageInterval.TotalSeconds);
            return currentTime + TimeSpan.FromSeconds(avgIntervalSeconds);
        }

        // Default prediction
        return currentTime + profile.OptimizedInterval;
    }

    /// <summary>
    /// Creates an optimization recommendation for a profile.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <returns>The recommendation, or null if no recommendation needed.</returns>
    private static ScheduleOptimizationRecommendation? CreateRecommendation(BufferScheduleProfile profile)
    {
        if (profile.OptimizationImprovementPercent < 10)
        {
            return null; // Not significant enough
        }

        var recommendationType = profile.OptimizationImprovementPercent > 50
            ? OptimizationRecommendationType.SignificantImprovement
            : OptimizationRecommendationType.ModerateImprovement;

        return new ScheduleOptimizationRecommendation
        {
            BufferId = profile.BufferId,
            RecommendationType = recommendationType,
            CurrentInterval = profile.CurrentInterval,
            RecommendedInterval = profile.OptimizedInterval,
            ImprovementPercent = profile.OptimizationImprovementPercent,
            ConfidenceLevel = profile.PatternConfidence,
            Rationale = GenerateRecommendationRationale(profile)
        };
    }

    /// <summary>
    /// Generates rationale for a recommendation.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <returns>The recommendation rationale.</returns>
    private static string GenerateRecommendationRationale(BufferScheduleProfile profile)
    {
        var reasons = new List<string>();

        if (profile.FailureRate > 0.05)
        {
            reasons.Add($"high failure rate ({profile.FailureRate:P1})");
        }

        if (profile.HealthPatterns.Any(h => h.Status == BufferHealthStatus.Corrupted))
        {
            reasons.Add("history of corruption");
        }

        if (profile.UsagePatterns.Any(p => p.UsageInterval < TimeSpan.FromMinutes(1)))
        {
            reasons.Add("frequent usage patterns");
        }

        return reasons.Any()
            ? $"Based on: {string.Join(", ", reasons)}"
            : "Based on usage and health pattern analysis";
    }

    /// <summary>
    /// Calculates the distribution of optimization improvements.
    /// </summary>
    /// <param name="profiles">The buffer profiles.</param>
    /// <returns>The optimization distribution.</returns>
    private static IReadOnlyDictionary<string, int> CalculateOptimizationDistribution(List<BufferScheduleProfile> profiles)
    {
        var distribution = new Dictionary<string, int>
        {
            ["< 10%"] = 0,
            ["10-25%"] = 0,
            ["25-50%"] = 0,
            ["> 50%"] = 0
        };

        foreach (var profile in profiles)
        {
            var improvement = profile.OptimizationImprovementPercent;
            var bucket = improvement switch
            {
                < 10 => "< 10%",
                < 25 => "10-25%",
                < 50 => "25-50%",
                _ => "> 50%"
            };
            distribution[bucket]++;
        }

        return distribution;
    }
}

/// <summary>
/// Buffer schedule profile for optimization.
/// </summary>
internal class BufferScheduleProfile
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; }

    /// <summary>Gets or sets the current check interval.</summary>
    public TimeSpan CurrentInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets the optimized check interval.</summary>
    public TimeSpan OptimizedInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets or sets the last optimization time.</summary>
    public DateTimeOffset? LastOptimization { get; set; }

    /// <summary>Gets or sets the optimization improvement percentage.</summary>
    public double OptimizationImprovementPercent { get; set; }

    /// <summary>Gets or sets the schedule adjustment count.</summary>
    public int ScheduleAdjustmentCount { get; set; }

    /// <summary>Gets or sets the pattern confidence level (0-1).</summary>
    public double PatternConfidence { get; set; }

    /// <summary>Gets the usage patterns.</summary>
    public List<UsagePattern> UsagePatterns { get; } = [];

    /// <summary>Gets the health patterns.</summary>
    public List<HealthPattern> HealthPatterns { get; } = [];

    /// <summary>Gets or sets the failure rate.</summary>
    public double FailureRate { get; set; }

    /// <summary>Gets or sets the average buffer size.</summary>
    public long AverageSize { get; set; }

    /// <summary>Gets or sets the last corruption time.</summary>
    public DateTimeOffset? LastCorruptionTime { get; set; }

    public BufferScheduleProfile(string bufferId)
    {
        BufferId = bufferId;
    }

    /// <summary>
    /// Updates patterns from usage and health history.
    /// </summary>
    /// <param name="usageHistory">The usage history.</param>
    /// <param name="healthHistory">The health history.</param>
    public void UpdatePatterns(IReadOnlyList<BufferUsageRecord> usageHistory, IReadOnlyList<HealthCheckRecord> healthHistory)
    {
        UpdateUsagePatterns(usageHistory);
        UpdateHealthPatterns(healthHistory);
        CalculateMetrics();
    }

    /// <summary>
    /// Resets the profile.
    /// </summary>
    public void Reset()
    {
        UsagePatterns.Clear();
        HealthPatterns.Clear();
        OptimizationImprovementPercent = 0;
        ScheduleAdjustmentCount = 0;
        PatternConfidence = 0;
        FailureRate = 0;
        AverageSize = 0;
        LastCorruptionTime = null;
    }

    private void UpdateUsagePatterns(IReadOnlyList<BufferUsageRecord> usageHistory)
    {
        UsagePatterns.Clear();

        if (usageHistory.Count < 2)
        {
            PatternConfidence = 0;
            return;
        }

        // Analyze usage intervals
        var rentals = usageHistory.Where(r => r.EventType == BufferEventType.Rented).ToList();
        for (var i = 1; i < rentals.Count; i++)
        {
            var interval = rentals[i].Timestamp - rentals[i - 1].Timestamp;
            UsagePatterns.Add(new UsagePattern
            {
                Timestamp = rentals[i].Timestamp,
                UsageInterval = interval
            });
        }

        // Calculate confidence based on pattern consistency
        if (UsagePatterns.Count > 1)
        {
            var avgInterval = UsagePatterns.Average(p => p.UsageInterval.TotalSeconds);
            var variance = UsagePatterns.Sum(p => Math.Pow(p.UsageInterval.TotalSeconds - avgInterval, 2)) / UsagePatterns.Count;
            var stdDev = Math.Sqrt(variance);
            PatternConfidence = Math.Max(0, 1.0 - (stdDev / avgInterval)); // Lower variance = higher confidence
        }
    }

    private void UpdateHealthPatterns(IReadOnlyList<HealthCheckRecord> healthHistory)
    {
        HealthPatterns.Clear();

        foreach (var record in healthHistory)
        {
            HealthPatterns.Add(new HealthPattern
            {
                Timestamp = record.Timestamp,
                Status = record.Status,
                IssueCount = record.IssueCount
            });

            if (record.Status == BufferHealthStatus.Corrupted)
            {
                LastCorruptionTime = record.Timestamp;
            }
        }
    }

    private void CalculateMetrics()
    {
        // Calculate failure rate
        if (HealthPatterns.Count > 0)
        {
            var failures = HealthPatterns.Count(h => h.Status != BufferHealthStatus.Healthy);
            FailureRate = (double)failures / HealthPatterns.Count;
        }

        // Calculate average size (would need size data from usage records)
        // This is a placeholder - in real implementation, extract from usage data
        AverageSize = 64 * 1024; // 64KB default
    }
}

/// <summary>
/// Usage pattern data.
/// </summary>
internal class UsagePattern
{
    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the usage interval.</summary>
    public TimeSpan UsageInterval { get; init; }
}

/// <summary>
/// Health pattern data.
/// </summary>
internal class HealthPattern
{
    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the health status.</summary>
    public BufferHealthStatus Status { get; init; }

    /// <summary>Gets the issue count.</summary>
    public int IssueCount { get; init; }
}

/// <summary>
/// Schedule optimization recommendation.
/// </summary>
public class ScheduleOptimizationRecommendation
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the recommendation type.</summary>
    public OptimizationRecommendationType RecommendationType { get; init; }

    /// <summary>Gets the current interval.</summary>
    public TimeSpan CurrentInterval { get; init; }

    /// <summary>Gets the recommended interval.</summary>
    public TimeSpan RecommendedInterval { get; init; }

    /// <summary>Gets the improvement percentage.</summary>
    public double ImprovementPercent { get; init; }

    /// <summary>Gets the confidence level (0-1).</summary>
    public double ConfidenceLevel { get; init; }

    /// <summary>Gets the recommendation rationale.</summary>
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>
/// Optimization recommendation types.
/// </summary>
public enum OptimizationRecommendationType
{
    /// <summary>Moderate improvement in scheduling.</summary>
    ModerateImprovement,

    /// <summary>Significant improvement in scheduling.</summary>
    SignificantImprovement,

    /// <summary>Critical scheduling optimization needed.</summary>
    CriticalOptimization
}

/// <summary>
/// Schedule optimization statistics.
/// </summary>
public class ScheduleOptimizationStats
{
    /// <summary>Gets the number of optimized buffers.</summary>
    public int OptimizedBuffers { get; init; }

    /// <summary>Gets the average optimization improvement percentage.</summary>
    public double AverageOptimizationImprovement { get; init; }

    /// <summary>Gets the total number of schedule adjustments.</summary>
    public int TotalScheduleAdjustments { get; init; }

    /// <summary>Gets the optimization improvement distribution.</summary>
    public IReadOnlyDictionary<string, int> OptimizationDistribution { get; init; } = new Dictionary<string, int>();

    /// <summary>Gets the pattern confidence levels by buffer.</summary>
    public IReadOnlyDictionary<string, double> PatternConfidenceLevels { get; init; } = new Dictionary<string, double>();
}
