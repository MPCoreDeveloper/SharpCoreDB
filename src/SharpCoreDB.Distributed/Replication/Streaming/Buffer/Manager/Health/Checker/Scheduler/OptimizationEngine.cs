// <copyright file="OptimizationEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Core optimization engine that orchestrates pattern analysis and cost calculation
/// for intelligent health check scheduling. Combines ML-style analysis with resource optimization.
/// C# 14: Primary constructors, async patterns for optimization workflows.
/// </summary>
internal sealed class OptimizationEngine
{
    private readonly PatternAnalyzer _patternAnalyzer;
    private readonly CostCalculator _costCalculator;
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizationEngine"/> class.
    /// </summary>
    /// <param name="patternAnalyzer">The pattern analyzer for usage recognition.</param>
    /// <param name="costCalculator">The cost calculator for resource optimization.</param>
    /// <param name="logger">Optional logger for optimization events.</param>
    public OptimizationEngine(
        PatternAnalyzer patternAnalyzer,
        CostCalculator costCalculator,
        ILogger? logger = null)
    {
        _patternAnalyzer = patternAnalyzer ?? throw new ArgumentNullException(nameof(patternAnalyzer));
        _costCalculator = costCalculator ?? throw new ArgumentNullException(nameof(costCalculator));
        _logger = logger;
    }

    /// <summary>
    /// Performs comprehensive optimization for a buffer's health check schedule.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="usageHistory">The buffer usage history.</param>
    /// <param name="healthHistory">The buffer health history.</param>
    /// <param name="currentInterval">The current check interval.</param>
    /// <returns>The optimization result with recommended schedule and analysis.</returns>
    public OptimizationResult OptimizeSchedule(
        string bufferId,
        IReadOnlyList<BufferUsageRecord> usageHistory,
        IReadOnlyList<HealthCheckRecord> healthHistory,
        TimeSpan currentInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentNullException.ThrowIfNull(usageHistory);
        ArgumentNullException.ThrowIfNull(healthHistory);

        _logger?.LogInformation("Starting optimization for buffer {BufferId}", bufferId);

        // Step 1: Analyze usage patterns
        var patternAnalysis = _patternAnalyzer.AnalyzePatterns(bufferId, usageHistory, TimeSpan.FromDays(7));

        // Step 2: Create or update buffer profile
        var profile = CreateBufferProfile(bufferId, usageHistory, healthHistory, patternAnalysis);

        // Step 3: Calculate optimal interval
        var optimalInterval = _costCalculator.CalculateOptimalInterval(profile, currentInterval);

        // Step 4: Predict next check time
        var nextCheckTime = _costCalculator.PredictNextCheckTime(profile, DateTimeOffset.UtcNow);

        // Step 5: Generate recommendations
        var recommendations = GenerateOptimizationRecommendations(profile, optimalInterval, currentInterval);

        var result = new OptimizationResult
        {
            BufferId = bufferId,
            OptimalInterval = optimalInterval,
            NextCheckTime = nextCheckTime,
            PatternAnalysis = patternAnalysis,
            CostAnalysis = CreateCostAnalysis(profile, optimalInterval),
            Recommendations = recommendations,
            ConfidenceLevel = patternAnalysis.Confidence,
            OptimizationTimestamp = DateTimeOffset.UtcNow
        };

        _logger?.LogInformation("Optimization completed for buffer {BufferId}: {CurrentInterval} -> {OptimalInterval}",
            bufferId, currentInterval, optimalInterval);

        return result;
    }

    /// <summary>
    /// Evaluates multiple scheduling options and returns the most cost-effective.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="profile">The buffer schedule profile.</param>
    /// <param name="candidateIntervals">The candidate intervals to evaluate.</param>
    /// <returns>The best interval based on cost-benefit analysis.</returns>
    public TimeSpan EvaluateSchedulingOptions(
        string bufferId,
        BufferScheduleProfile profile,
        IReadOnlyList<TimeSpan> candidateIntervals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(candidateIntervals);

        if (candidateIntervals.Count == 0)
        {
            throw new ArgumentException("At least one candidate interval must be provided", nameof(candidateIntervals));
        }

        _logger?.LogDebug("Evaluating {Count} scheduling options for buffer {BufferId}",
            candidateIntervals.Count, bufferId);

        var bestInterval = candidateIntervals[0];
        var lowestCost = double.MaxValue;

        foreach (var interval in candidateIntervals)
        {
            var cost = _costCalculator.CalculateIntervalCost(interval, profile);

            if (cost < lowestCost)
            {
                lowestCost = cost;
                bestInterval = interval;
            }

            _logger?.LogTrace("Evaluated interval {Interval}: cost = {Cost:F2}", interval, cost);
        }

        _logger?.LogDebug("Selected best interval {BestInterval} with cost {LowestCost:F2} for buffer {BufferId}",
            bestInterval, lowestCost, bufferId);

        return bestInterval;
    }

    /// <summary>
    /// Performs predictive optimization based on trend analysis.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="usageHistory">The usage history for trend analysis.</param>
    /// <param name="predictionHorizon">The time horizon for predictions.</param>
    /// <returns>The predictive optimization result.</returns>
    public PredictiveOptimizationResult OptimizePredictively(
        string bufferId,
        IReadOnlyList<BufferUsageRecord> usageHistory,
        TimeSpan predictionHorizon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);
        ArgumentNullException.ThrowIfNull(usageHistory);

        _logger?.LogInformation("Starting predictive optimization for buffer {BufferId}", bufferId);

        // Analyze patterns for prediction
        var patternAnalysis = _patternAnalyzer.AnalyzePatterns(bufferId, usageHistory, predictionHorizon);

        // Generate future usage predictions
        var predictions = GenerateUsagePredictions(patternAnalysis, predictionHorizon);

        // Calculate optimal schedule based on predictions
        var recommendedInterval = CalculatePredictiveInterval(predictions, patternAnalysis);

        var result = new PredictiveOptimizationResult
        {
            BufferId = bufferId,
            RecommendedInterval = recommendedInterval,
            UsagePredictions = predictions,
            PatternAnalysis = patternAnalysis,
            PredictionHorizon = predictionHorizon,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        _logger?.LogInformation("Predictive optimization completed for buffer {BufferId}: recommended interval {RecommendedInterval}",
            bufferId, recommendedInterval);

        return result;
    }

    /// <summary>
    /// Creates a buffer schedule profile from usage and health data.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="usageHistory">The usage history.</param>
    /// <param name="healthHistory">The health history.</param>
    /// <param name="patternAnalysis">The pattern analysis result.</param>
    /// <returns>The created buffer schedule profile.</returns>
    private static BufferScheduleProfile CreateBufferProfile(
        string bufferId,
        IReadOnlyList<BufferUsageRecord> usageHistory,
        IReadOnlyList<HealthCheckRecord> healthHistory,
        BufferPatternAnalysis patternAnalysis)
    {
        var profile = new BufferScheduleProfile(bufferId);

        // Populate usage patterns
        profile.UsagePatterns.AddRange(patternAnalysis.TrendAnalysis.Direction == TrendDirection.Increasing
            ? GenerateIncreasingUsagePatterns(usageHistory)
            : GenerateStandardUsagePatterns(usageHistory));

        // Populate health patterns
        profile.HealthPatterns.AddRange(healthHistory.Select(h => new HealthPattern
        {
            Timestamp = h.Timestamp,
            Status = h.Status
        }));

        // Calculate derived metrics
        profile.FailureRate = healthHistory.Any()
            ? healthHistory.Count(h => h.Status != BufferHealthStatus.Healthy) / (double)healthHistory.Count
            : 0.0;

        profile.AverageSize = usageHistory.Any()
            ? (long)usageHistory.Where(u => u.Size.HasValue).Average(u => u.Size!.Value)
            : 0L;

        profile.LastCorruptionTime = healthHistory
            .Where(h => h.Status == BufferHealthStatus.Corrupted)
            .MaxBy(h => h.Timestamp)?.Timestamp;

        profile.PatternConfidence = patternAnalysis.Confidence;

        return profile;
    }

    /// <summary>
    /// Creates a cost analysis summary.
    /// </summary>
    /// <param name="profile">The buffer profile.</param>
    /// <param name="optimalInterval">The optimal interval.</param>
    /// <returns>The cost analysis.</returns>
    private CostAnalysis CreateCostAnalysis(BufferScheduleProfile profile, TimeSpan optimalInterval)
    {
        return new CostAnalysis
        {
            OptimalInterval = optimalInterval,
            CpuCost = _costCalculator.CalculateIntervalCost(optimalInterval, profile) * 0.1, // Estimated CPU portion
            MemoryCost = 50.0, // Base memory cost
            IoCost = (profile.AverageSize / (1024.0 * 1024.0)) * 0.05, // I/O cost based on size
            RiskCost = profile.FailureRate * 100.0, // Risk cost based on failure rate
            TotalCost = _costCalculator.CalculateIntervalCost(optimalInterval, profile)
        };
    }

    /// <summary>
    /// Generates optimization recommendations.
    /// </summary>
    /// <param name="profile">The buffer profile.</param>
    /// <param name="optimalInterval">The optimal interval.</param>
    /// <param name="currentInterval">The current interval.</param>
    /// <returns>The list of recommendations.</returns>
    private static IReadOnlyList<string> GenerateOptimizationRecommendations(
        BufferScheduleProfile profile,
        TimeSpan optimalInterval,
        TimeSpan currentInterval)
    {
        var recommendations = new List<string>();

        var improvement = currentInterval.TotalSeconds > 0
            ? ((currentInterval - optimalInterval).TotalSeconds / currentInterval.TotalSeconds) * 100.0
            : 0.0;

        if (improvement > 50)
        {
            recommendations.Add($"Significant optimization opportunity: {improvement:F1}% improvement possible");
        }
        else if (improvement > 20)
        {
            recommendations.Add($"Moderate optimization: {improvement:F1}% improvement available");
        }

        if (profile.FailureRate > 0.1)
        {
            recommendations.Add("High failure rate detected - consider more frequent checks");
        }

        if (profile.AverageSize > 10 * 1024 * 1024)
        {
            recommendations.Add("Large buffer size - increased monitoring recommended");
        }

        if (profile.LastCorruptionTime.HasValue &&
            profile.LastCorruptionTime.Value > DateTimeOffset.UtcNow.AddHours(-24))
        {
            recommendations.Add("Recent corruption detected - immediate attention required");
        }

        return recommendations;
    }

    /// <summary>
    /// Generates usage predictions based on pattern analysis.
    /// </summary>
    /// <param name="patternAnalysis">The pattern analysis.</param>
    /// <param name="horizon">The prediction horizon.</param>
    /// <returns>The usage predictions.</returns>
    private static IReadOnlyList<UsagePrediction> GenerateUsagePredictions(
        BufferPatternAnalysis patternAnalysis,
        TimeSpan horizon)
    {
        // Simplified prediction logic - in practice, this would use more sophisticated ML
        var predictions = new List<UsagePrediction>();
        var currentTime = DateTimeOffset.UtcNow;
        var endTime = currentTime + horizon;

        // Generate predictions at regular intervals
        for (var time = currentTime; time <= endTime; time = time.AddMinutes(5))
        {
            var predictedUsage = patternAnalysis.TrendAnalysis.Magnitude *
                (patternAnalysis.TrendAnalysis.Direction == TrendDirection.Increasing ? 1.1 : 0.9);

            predictions.Add(new UsagePrediction
            {
                Timestamp = time,
                PredictedUsage = predictedUsage,
                Confidence = patternAnalysis.Confidence
            });
        }

        return predictions;
    }

    /// <summary>
    /// Calculates the optimal interval based on predictive analysis.
    /// </summary>
    /// <param name="predictions">The usage predictions.</param>
    /// <param name="patternAnalysis">The pattern analysis.</param>
    /// <returns>The recommended interval.</returns>
    private static TimeSpan CalculatePredictiveInterval(
        IReadOnlyList<UsagePrediction> predictions,
        BufferPatternAnalysis patternAnalysis)
    {
        if (!predictions.Any())
        {
            return TimeSpan.FromMinutes(5); // Default
        }

        var avgPredictedUsage = predictions.Average(p => p.PredictedUsage);

        // Higher predicted usage = shorter intervals
        return avgPredictedUsage switch
        {
            > 0.8 => TimeSpan.FromMinutes(1),
            > 0.5 => TimeSpan.FromMinutes(3),
            > 0.2 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(10)
        };
    }

    /// <summary>
    /// Generates standard usage patterns from history.
    /// </summary>
    /// <param name="usageHistory">The usage history.</param>
    /// <returns>The usage patterns.</returns>
    private static IEnumerable<UsagePattern> GenerateStandardUsagePatterns(IReadOnlyList<BufferUsageRecord> usageHistory)
    {
        if (usageHistory.Count < 2)
        {
            yield break;
        }

        for (var i = 1; i < usageHistory.Count; i++)
        {
            var interval = usageHistory[i].Timestamp - usageHistory[i - 1].Timestamp;
            yield return new UsagePattern
            {
                UsageInterval = interval,
                Timestamp = usageHistory[i].Timestamp
            };
        }
    }

    /// <summary>
    /// Generates increasing usage patterns (for trending up scenarios).
    /// </summary>
    /// <param name="usageHistory">The usage history.</param>
    /// <returns>The usage patterns with increasing trend.</returns>
    private static IEnumerable<UsagePattern> GenerateIncreasingUsagePatterns(IReadOnlyList<BufferUsageRecord> usageHistory)
    {
        foreach (var pattern in GenerateStandardUsagePatterns(usageHistory))
        {
            // Adjust interval for increasing trend (shorter intervals)
            yield return new UsagePattern
            {
                UsageInterval = TimeSpan.FromTicks((long)(pattern.UsageInterval.Ticks * 0.8)),
                Timestamp = pattern.Timestamp
            };
        }
    }
}

/// <summary>
/// Result of an optimization operation.
/// </summary>
internal class OptimizationResult
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the optimal check interval.</summary>
    public TimeSpan OptimalInterval { get; init; }

    /// <summary>Gets the predicted next check time.</summary>
    public DateTimeOffset NextCheckTime { get; init; }

    /// <summary>Gets the pattern analysis result.</summary>
    public BufferPatternAnalysis PatternAnalysis { get; init; } = new();

    /// <summary>Gets the cost analysis.</summary>
    public CostAnalysis CostAnalysis { get; init; } = new();

    /// <summary>Gets the optimization recommendations.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>Gets the confidence level of the optimization.</summary>
    public double ConfidenceLevel { get; init; }

    /// <summary>Gets the timestamp when optimization was performed.</summary>
    public DateTimeOffset OptimizationTimestamp { get; init; }
}

/// <summary>
/// Cost analysis breakdown.
/// </summary>
internal class CostAnalysis
{
    /// <summary>Gets the optimal interval.</summary>
    public TimeSpan OptimalInterval { get; init; }

    /// <summary>Gets the CPU cost.</summary>
    public double CpuCost { get; init; }

    /// <summary>Gets the memory cost.</summary>
    public double MemoryCost { get; init; }

    /// <summary>Gets the I/O cost.</summary>
    public double IoCost { get; init; }

    /// <summary>Gets the risk cost.</summary>
    public double RiskCost { get; init; }

    /// <summary>Gets the total cost.</summary>
    public double TotalCost { get; init; }
}

/// <summary>
/// Result of predictive optimization.
/// </summary>
internal class PredictiveOptimizationResult
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the recommended check interval.</summary>
    public TimeSpan RecommendedInterval { get; init; }

    /// <summary>Gets the usage predictions.</summary>
    public IReadOnlyList<UsagePrediction> UsagePredictions { get; init; } = [];

    /// <summary>Gets the pattern analysis.</summary>
    public BufferPatternAnalysis PatternAnalysis { get; init; } = new();

    /// <summary>Gets the prediction horizon.</summary>
    public TimeSpan PredictionHorizon { get; init; }

    /// <summary>Gets the timestamp when prediction was generated.</summary>
    public DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// Usage prediction.
/// </summary>
internal class UsagePrediction
{
    /// <summary>Gets the prediction timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the predicted usage level.</summary>
    public double PredictedUsage { get; init; }

    /// <summary>Gets the prediction confidence.</summary>
    public double Confidence { get; init; }
}
