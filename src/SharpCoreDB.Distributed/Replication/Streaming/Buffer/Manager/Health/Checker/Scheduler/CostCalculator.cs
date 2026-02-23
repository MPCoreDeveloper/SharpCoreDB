// <copyright file="CostCalculator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Calculates scheduling costs and optimization factors for health check intervals.
/// Provides quantitative analysis of resource usage, risk factors, and performance trade-offs.
/// C# 14: Primary constructors, pattern matching for cost evaluation.
/// </summary>
public sealed class CostCalculator
{
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CostCalculator"/> class.
    /// </summary>
    /// <param name="logger">Optional logger for cost calculation events.</param>
    public CostCalculator(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Calculates the optimal check interval based on usage patterns and system conditions.
    /// </summary>
    /// <param name="profile">The buffer schedule profile containing usage and health data.</param>
    /// <param name="currentInterval">The current check interval.</param>
    /// <returns>The calculated optimal interval.</returns>
    internal TimeSpan CalculateOptimalInterval(BufferScheduleProfile profile, TimeSpan currentInterval)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Base interval from usage patterns
        var baseInterval = CalculateBaseInterval(profile);

        // Adjustments for health and risk factors
        var healthAdjustment = CalculateHealthAdjustment(profile);
        var riskAdjustment = CalculateRiskAdjustment(profile);

        // Combine all factors
        var optimalInterval = baseInterval * healthAdjustment * riskAdjustment;

        // Apply bounds and calculate improvement
        optimalInterval = ApplyIntervalBounds(optimalInterval);
        var improvementPercent = CalculateImprovementPercent(currentInterval, optimalInterval);

        // Update profile metrics
        profile.OptimizationImprovementPercent = improvementPercent;
        profile.ScheduleAdjustmentCount++;

        _logger?.LogDebug("Calculated optimal interval for buffer {BufferId}: {BaseInterval} * {HealthAdjustment:F2} * {RiskAdjustment:F2} = {OptimalInterval}",
            profile.BufferId, baseInterval, healthAdjustment, riskAdjustment, optimalInterval);

        return optimalInterval;
    }

    /// <summary>
    /// Calculates the base check interval based on usage patterns.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <returns>The base interval derived from usage frequency.</returns>
    internal TimeSpan CalculateBaseInterval(BufferScheduleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.UsagePatterns.Any())
        {
            return TimeSpan.FromMinutes(2); // Default interval
        }

        // Calculate average usage interval
        var avgUsageInterval = profile.UsagePatterns.Average(p => p.UsageInterval.TotalSeconds);
        var usageFrequency = 1.0 / Math.Max(avgUsageInterval, 60); // Minimum 1 minute

        // Determine interval based on usage frequency thresholds
        return usageFrequency switch
        {
            > 0.1 => TimeSpan.FromSeconds(30),    // High frequency (> every 10s)
            > 0.0167 => TimeSpan.FromMinutes(1),   // Medium frequency (> every minute)
            > 0.000278 => TimeSpan.FromMinutes(5), // Low frequency (> every hour)
            _ => TimeSpan.FromMinutes(15)          // Very low frequency
        };
    }

    /// <summary>
    /// Calculates health-based adjustment factor for check intervals.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <returns>Adjustment factor (1.0 = no change, <1.0 = shorter intervals, >1.0 = longer intervals).</returns>
    internal double CalculateHealthAdjustment(BufferScheduleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!profile.HealthPatterns.Any())
        {
            return 1.0; // No health data, no adjustment
        }

        // Count recent health issues (last hour)
        var recentHealthIssues = profile.HealthPatterns
            .Where(h => h.Timestamp > DateTimeOffset.UtcNow.AddHours(-1))
            .Count(h => h.Status != BufferHealthStatus.Healthy);

        // Adjust interval based on health issue count
        return recentHealthIssues switch
        {
            0 => 1.5,  // No issues: can check less frequently
            1 => 1.0,  // One issue: maintain current frequency
            2 => 0.8,  // Two issues: check more frequently
            _ => 0.5   // Multiple issues: check much more frequently
        };
    }

    /// <summary>
    /// Calculates risk-based adjustment factor for check intervals.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <returns>Adjustment factor (1.0 = no change, <1.0 = shorter intervals due to higher risk).</returns>
    internal double CalculateRiskAdjustment(BufferScheduleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var riskScore = 0.0;

        // Failure rate risk
        if (profile.FailureRate > 0.1) riskScore += 0.3;
        if (profile.FailureRate > 0.05) riskScore += 0.2;

        // Size risk (larger buffers need more frequent checks)
        if (profile.AverageSize > 1024 * 1024) riskScore += 0.2; // > 1MB
        if (profile.AverageSize > 10 * 1024 * 1024) riskScore += 0.3; // > 10MB

        // Recent corruption risk
        if (profile.LastCorruptionTime.HasValue &&
            profile.LastCorruptionTime.Value > DateTimeOffset.UtcNow.AddHours(-24))
        {
            riskScore += 0.4;
        }

        // Convert risk score to adjustment factor (higher risk = shorter intervals)
        return Math.Max(0.3, 1.0 - riskScore);
    }

    /// <summary>
    /// Calculates the cost of a specific check interval in terms of resource usage.
    /// </summary>
    /// <param name="interval">The check interval.</param>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <returns>The calculated cost score (higher = more expensive).</returns>
    internal double CalculateIntervalCost(TimeSpan interval, BufferScheduleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Cost factors:
        // - CPU cost: more frequent checks = higher CPU usage
        // - Memory cost: check state maintenance
        // - I/O cost: potential disk access during checks
        // - Risk cost: missed issues due to infrequent checks

        var checksPerHour = TimeSpan.FromHours(1).TotalSeconds / interval.TotalSeconds;

        // CPU cost (linear with frequency)
        var cpuCost = checksPerHour * 0.1; // Base CPU cost per check

        // Memory cost (constant overhead)
        var memoryCost = 50.0; // KB of memory per buffer check

        // I/O cost (depends on buffer size and check complexity)
        var ioCost = (profile.AverageSize / (1024.0 * 1024.0)) * checksPerHour * 0.05;

        // Risk cost (exponential penalty for very infrequent checks)
        var riskCost = profile.FailureRate * Math.Exp(-checksPerHour / 10.0) * 100.0;

        var totalCost = cpuCost + memoryCost + ioCost + riskCost;

        _logger?.LogTrace("Calculated cost for {Interval}: CPU={CpuCost:F2}, Memory={MemoryCost:F2}, IO={IoCost:F2}, Risk={RiskCost:F2}, Total={TotalCost:F2}",
            interval, cpuCost, memoryCost, ioCost, riskCost, totalCost);

        return totalCost;
    }

    /// <summary>
    /// Predicts the next optimal check time based on usage patterns.
    /// </summary>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <param name="currentTime">The current time.</param>
    /// <returns>The predicted optimal check time.</returns>
    internal DateTimeOffset PredictNextCheckTime(BufferScheduleProfile profile, DateTimeOffset currentTime)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.UsagePatterns.Any())
        {
            // Predict based on average usage intervals
            var avgIntervalSeconds = profile.UsagePatterns.Average(p => p.UsageInterval.TotalSeconds);
            return currentTime + TimeSpan.FromSeconds(avgIntervalSeconds);
        }

        // Fallback to optimized interval
        return currentTime + profile.OptimizedInterval;
    }

    /// <summary>
    /// Applies reasonable bounds to the calculated interval.
    /// </summary>
    /// <param name="interval">The raw calculated interval.</param>
    /// <returns>The bounded interval.</returns>
    private static TimeSpan ApplyIntervalBounds(TimeSpan interval)
    {
        const long minTicks = 10 * TimeSpan.TicksPerSecond; // 10 seconds minimum
        const long maxTicks = 60 * TimeSpan.TicksPerMinute; // 1 hour maximum

        return TimeSpan.FromTicks(Math.Clamp(interval.Ticks, minTicks, maxTicks));
    }

    /// <summary>
    /// Calculates the percentage improvement from current to optimal interval.
    /// </summary>
    /// <param name="currentInterval">The current interval.</param>
    /// <param name="optimalInterval">The optimal interval.</param>
    /// <returns>The improvement percentage.</returns>
    private static double CalculateImprovementPercent(TimeSpan currentInterval, TimeSpan optimalInterval)
    {
        if (currentInterval.TotalSeconds <= 0)
        {
            return 0;
        }

        var difference = currentInterval - optimalInterval;
        return (difference.TotalSeconds / currentInterval.TotalSeconds) * 100.0;
    }
}
