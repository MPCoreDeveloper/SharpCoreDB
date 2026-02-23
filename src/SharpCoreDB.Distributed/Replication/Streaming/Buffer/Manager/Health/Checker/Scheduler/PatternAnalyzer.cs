// <copyright file="PatternAnalyzer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Advanced pattern analyzer for buffer usage recognition and prediction.
/// Uses statistical analysis and machine learning techniques to identify usage patterns.
/// C# 14: Primary constructors, LINQ for data analysis, pattern matching.
/// </summary>
public sealed class PatternAnalyzer
{
    private readonly Dictionary<string, BufferPatternModel> _patternModels = [];
    private readonly Lock _analyzerLock = new();

    private readonly ILogger? _logger;

    /// <summary>Gets the number of analyzed buffer patterns.</summary>
    public int AnalyzedBufferCount => _patternModels.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatternAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public PatternAnalyzer(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes usage patterns for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="usageHistory">The usage history.</param>
    /// <param name="timeWindow">The analysis time window.</param>
    /// <returns>The pattern analysis result.</returns>
    public BufferPatternAnalysis AnalyzePatterns(
        string bufferId,
        IReadOnlyList<BufferUsageRecord> usageHistory,
        TimeSpan timeWindow = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferId);

        if (timeWindow == default)
        {
            timeWindow = TimeSpan.FromDays(7); // Default to 7 days
        }

        var cutoffTime = DateTimeOffset.UtcNow - timeWindow;
        var relevantHistory = usageHistory
            .Where(r => r.Timestamp >= cutoffTime)
            .OrderBy(r => r.Timestamp)
            .ToList();

        var model = GetOrCreateModel(bufferId);
        model.UpdateFromHistory(relevantHistory);

        var analysis = PerformPatternAnalysis(model, relevantHistory);

        lock (_analyzerLock)
        {
            _patternModels[bufferId] = model;
        }

        _logger?.LogDebug("Analyzed patterns for buffer {BufferId}: {PatternType} with {Confidence:P1} confidence",
            bufferId, analysis.DetectedPattern, analysis.Confidence);

        return analysis;
    }

    /// <summary>
    /// Predicts future usage for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <param name="predictionHorizon">The prediction time horizon.</param>
    /// <returns>The usage prediction.</returns>
    public BufferUsagePrediction PredictUsage(string bufferId, TimeSpan predictionHorizon)
    {
        var model = GetModel(bufferId);
        if (model is null)
        {
            return new BufferUsagePrediction
            {
                BufferId = bufferId,
                PredictionType = PredictionType.Uncertain,
                Confidence = 0,
                PredictedUsageCount = 0,
                Rationale = "No pattern data available"
            };
        }

        return GeneratePrediction(model, predictionHorizon);
    }

    /// <summary>
    /// Gets pattern analysis for all buffers.
    /// </summary>
    /// <returns>Collection of pattern analyses.</returns>
    public IReadOnlyCollection<BufferPatternAnalysis> GetAllPatternAnalyses()
    {
        lock (_analyzerLock)
        {
            return _patternModels.Values
                .Select(model => PerformPatternAnalysis(model, []))
                .ToList();
        }
    }

    /// <summary>
    /// Gets pattern analyzer statistics.
    /// </summary>
    /// <returns>Pattern analyzer statistics.</returns>
    public PatternAnalyzerStats GetStats()
    {
        lock (_analyzerLock)
        {
            var models = _patternModels.Values.ToList();

            return new PatternAnalyzerStats
            {
                AnalyzedBuffers = models.Count,
                AverageConfidence = models.Any() ? models.Average(m => m.Confidence) : 0,
                PatternDistribution = CalculatePatternDistribution(models),
                PredictionAccuracy = CalculatePredictionAccuracy(models),
                MostCommonPatterns = GetMostCommonPatterns(models)
            };
        }
    }

    /// <summary>
    /// Resets pattern analysis for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    public void ResetAnalysis(string bufferId)
    {
        lock (_analyzerLock)
        {
            if (_patternModels.TryGetValue(bufferId, out var model))
            {
                model.Reset();
                _logger?.LogInformation("Reset pattern analysis for buffer {BufferId}", bufferId);
            }
        }
    }

    /// <summary>
    /// Gets or creates a pattern model for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The pattern model.</returns>
    private BufferPatternModel GetOrCreateModel(string bufferId)
    {
        lock (_analyzerLock)
        {
            if (!_patternModels.TryGetValue(bufferId, out var model))
            {
                model = new BufferPatternModel(bufferId);
                _patternModels[bufferId] = model;
            }
            return model;
        }
    }

    /// <summary>
    /// Gets a pattern model for a buffer.
    /// </summary>
    /// <param name="bufferId">The buffer identifier.</param>
    /// <returns>The pattern model, or null if not found.</returns>
    private BufferPatternModel? GetModel(string bufferId)
    {
        lock (_analyzerLock)
        {
            return _patternModels.TryGetValue(bufferId, out var model) ? model : null;
        }
    }

    /// <summary>
    /// Performs pattern analysis on a model and history.
    /// </summary>
    /// <param name="model">The pattern model.</param>
    /// <param name="history">The usage history.</param>
    /// <returns>The pattern analysis.</returns>
    private static BufferPatternAnalysis PerformPatternAnalysis(
        BufferPatternModel model,
        IReadOnlyList<BufferUsageRecord> history)
    {
        var detectedPattern = DetectUsagePattern(history);
        var confidence = CalculatePatternConfidence(history, detectedPattern);
        var seasonality = DetectSeasonality(history);
        var trends = AnalyzeTrends(history);
        var anomalies = DetectAnomalies(history, detectedPattern);

        model.Confidence = confidence;
        model.DetectedPattern = detectedPattern;

        return new BufferPatternAnalysis
        {
            BufferId = model.BufferId,
            DetectedPattern = detectedPattern,
            Confidence = confidence,
            SeasonalityInfo = seasonality,
            TrendAnalysis = trends,
            Anomalies = anomalies,
            Recommendations = GeneratePatternRecommendations(detectedPattern, confidence, anomalies)
        };
    }

    /// <summary>
    /// Detects the usage pattern from history.
    /// </summary>
    /// <param name="history">The usage history.</param>
    /// <returns>The detected usage pattern.</returns>
    private static UsagePatternType DetectUsagePattern(IReadOnlyList<BufferUsageRecord> history)
    {
        if (history.Count < 3)
        {
            return UsagePatternType.Unknown;
        }

        var rentals = history.Where(r => r.EventType == BufferEventType.Rented).ToList();
        if (rentals.Count < 3)
        {
            return UsagePatternType.Sporadic;
        }

        // Calculate intervals between rentals
        var intervals = new List<double>();
        for (var i = 1; i < rentals.Count; i++)
        {
            var interval = (rentals[i].Timestamp - rentals[i - 1].Timestamp).TotalSeconds;
            intervals.Add(interval);
        }

        if (!intervals.Any())
        {
            return UsagePatternType.Sporadic;
        }

        var avgInterval = intervals.Average();
        var variance = intervals.Sum(i => Math.Pow(i - avgInterval, 2)) / intervals.Count;
        var stdDev = Math.Sqrt(variance);
        var cv = stdDev / avgInterval; // Coefficient of variation

        // Classify based on regularity
        if (cv < 0.2)
        {
            return UsagePatternType.Regular;
        }
        else if (cv < 0.5)
        {
            return UsagePatternType.Periodic;
        }
        else if (avgInterval < 300) // Less than 5 minutes
        {
            return UsagePatternType.Frequent;
        }
        else
        {
            return UsagePatternType.Sporadic;
        }
    }

    /// <summary>
    /// Calculates pattern confidence.
    /// </summary>
    /// <param name="history">The usage history.</param>
    /// <param name="pattern">The detected pattern.</param>
    /// <returns>The confidence level (0-1).</returns>
    private static double CalculatePatternConfidence(IReadOnlyList<BufferUsageRecord> history, UsagePatternType pattern)
    {
        if (history.Count < 3)
        {
            return 0;
        }

        // Base confidence on sample size and pattern strength
        var sampleConfidence = Math.Min(history.Count / 10.0, 1.0); // More samples = higher confidence

        var patternStrength = pattern switch
        {
            UsagePatternType.Regular => 1.0,
            UsagePatternType.Periodic => 0.8,
            UsagePatternType.Frequent => 0.6,
            UsagePatternType.Sporadic => 0.3,
            _ => 0.1
        };

        return (sampleConfidence + patternStrength) / 2.0;
    }

    /// <summary>
    /// Detects seasonality in usage patterns.
    /// </summary>
    /// <param name="history">The usage history.</param>
    /// <returns>Seasonality information.</returns>
    private static SeasonalityInfo DetectSeasonality(IReadOnlyList<BufferUsageRecord> history)
    {
        if (history.Count < 24) // Need at least a day's worth of data
        {
            return new SeasonalityInfo
            {
                HasSeasonality = false,
                DetectedCycles = [],
                Strength = 0
            };
        }

        // Simple hourly seasonality detection
        var hourlyUsage = new int[24];
        foreach (var record in history)
        {
            var hour = record.Timestamp.Hour;
            if (record.EventType == BufferEventType.Rented)
            {
                hourlyUsage[hour]++;
            }
        }

        var avgUsage = hourlyUsage.Average();
        var peakHours = hourlyUsage
            .Select((count, hour) => (count, hour))
            .Where(x => x.count > avgUsage * 1.5)
            .Select(x => x.hour)
            .ToList();

        var hasSeasonality = peakHours.Count > 0;
        var strength = hasSeasonality ? (double)peakHours.Count / 24.0 : 0;

        return new SeasonalityInfo
        {
            HasSeasonality = hasSeasonality,
            DetectedCycles = [new CycleInfo { Type = CycleType.Hourly, PeakPeriods = peakHours }],
            Strength = strength
        };
    }

    /// <summary>
    /// Analyzes trends in usage.
    /// </summary>
    /// <param name="history">The usage history.</param>
    /// <returns>Trend analysis.</returns>
    private static TrendAnalysis AnalyzeTrends(IReadOnlyList<BufferUsageRecord> history)
    {
        if (history.Count < 10)
        {
            return new TrendAnalysis
            {
                Direction = TrendDirection.Stable,
                Magnitude = 0,
                Description = "Insufficient data for trend analysis"
            };
        }

        // Simple linear regression for trend detection
        var rentals = history.Where(r => r.EventType == BufferEventType.Rented)
            .OrderBy(r => r.Timestamp)
            .ToList();

        if (rentals.Count < 2)
        {
            return new TrendAnalysis
            {
                Direction = TrendDirection.Stable,
                Magnitude = 0,
                Description = "No rental data available"
            };
        }

        // Calculate trend using simple linear regression
        var n = rentals.Count;
        var sumX = rentals.Select((r, i) => i).Sum();
        var sumY = rentals.Sum(r => r.Timestamp.ToUnixTimeSeconds());
        var sumXY = rentals.Select((r, i) => i * r.Timestamp.ToUnixTimeSeconds()).Sum();
        var sumXX = rentals.Select((r, i) => i * i).Sum();

        var slope = (n * sumXY - sumX * sumY) / (n * sumXX - sumX * sumX);

        var direction = slope switch
        {
            > 100 => TrendDirection.Increasing,    // Increasing timestamps = later usage
            < -100 => TrendDirection.Decreasing,   // Decreasing timestamps = earlier usage
            _ => TrendDirection.Stable
        };

        var magnitude = Math.Abs(slope) / 1000; // Scale down for readability

        return new TrendAnalysis
        {
            Direction = direction,
            Magnitude = magnitude,
            Description = $"{direction} usage trend detected"
        };
    }

    /// <summary>
    /// Detects anomalies in usage patterns.
    /// </summary>
    /// <param name="history">The usage history.</param>
    /// <param name="expectedPattern">The expected pattern.</param>
    /// <returns>List of detected anomalies.</returns>
    private static IReadOnlyList<UsageAnomaly> DetectAnomalies(
        IReadOnlyList<BufferUsageRecord> history,
        UsagePatternType expectedPattern)
    {
        var anomalies = new List<UsageAnomaly>();

        if (history.Count < 5)
        {
            return anomalies;
        }

        // Detect unusual intervals
        var rentals = history.Where(r => r.EventType == BufferEventType.Rented).ToList();
        if (rentals.Count < 3)
        {
            return anomalies;
        }

        var intervals = new List<double>();
        for (var i = 1; i < rentals.Count; i++)
        {
            intervals.Add((rentals[i].Timestamp - rentals[i - 1].Timestamp).TotalSeconds);
        }

        var avgInterval = intervals.Average();
        var stdDev = Math.Sqrt(intervals.Sum(i => Math.Pow(i - avgInterval, 2)) / intervals.Count);

        for (var i = 0; i < intervals.Count; i++)
        {
            var deviation = Math.Abs(intervals[i] - avgInterval);
            if (deviation > 3 * stdDev) // 3-sigma rule
            {
                anomalies.Add(new UsageAnomaly
                {
                    Timestamp = rentals[i + 1].Timestamp,
                    Type = AnomalyType.UnusualInterval,
                    Severity = deviation > 5 * stdDev ? AnomalySeverity.High : AnomalySeverity.Medium,
                    Description = $"Unusual interval: {TimeSpan.FromSeconds(intervals[i]):g} (expected ~{TimeSpan.FromSeconds(avgInterval):g})"
                });
            }
        }

        return anomalies;
    }

    /// <summary>
    /// Generates pattern-based recommendations.
    /// </summary>
    /// <param name="pattern">The detected pattern.</param>
    /// <param name="confidence">The confidence level.</param>
    /// <param name="anomalies">The detected anomalies.</param>
    /// <returns>List of recommendations.</returns>
    private static IReadOnlyList<string> GeneratePatternRecommendations(
        UsagePatternType pattern,
        double confidence,
        IReadOnlyList<UsageAnomaly> anomalies)
    {
        var recommendations = new List<string>();

        if (confidence < 0.5)
        {
            recommendations.Add("Collect more usage data to improve pattern recognition confidence");
        }

        if (pattern == UsagePatternType.Frequent)
        {
            recommendations.Add("Consider increasing buffer pool size due to frequent usage");
        }

        if (pattern == UsagePatternType.Sporadic && anomalies.Count > 0)
        {
            recommendations.Add("Monitor for unusual usage spikes that may indicate issues");
        }

        if (anomalies.Any(a => a.Severity == AnomalySeverity.High))
        {
            recommendations.Add("Investigate high-severity usage anomalies for potential issues");
        }

        return recommendations;
    }

    /// <summary>
    /// Generates usage prediction.
    /// </summary>
    /// <param name="model">The pattern model.</param>
    /// <param name="horizon">The prediction horizon.</param>
    /// <returns>The usage prediction.</returns>
    private static BufferUsagePrediction GeneratePrediction(BufferPatternModel model, TimeSpan horizon)
    {
        var predictionType = model.Confidence > 0.7 ? PredictionType.Confident :
                           model.Confidence > 0.4 ? PredictionType.Moderate : PredictionType.Uncertain;

        // Simple prediction based on pattern
        var predictedCount = model.DetectedPattern switch
        {
            UsagePatternType.Frequent => (int)(horizon.TotalMinutes * 2), // ~2 rentals per minute
            UsagePatternType.Regular => (int)(horizon.TotalHours * 6),    // ~6 rentals per hour
            UsagePatternType.Periodic => (int)(horizon.TotalHours * 3),   // ~3 rentals per hour
            _ => (int)(horizon.TotalHours * 1) // ~1 rental per hour
        };

        return new BufferUsagePrediction
        {
            BufferId = model.BufferId,
            PredictionType = predictionType,
            Confidence = model.Confidence,
            PredictedUsageCount = predictedCount,
            Rationale = $"Based on {model.DetectedPattern} usage pattern with {model.Confidence:P1} confidence"
        };
    }

    /// <summary>
    /// Calculates pattern distribution.
    /// </summary>
    /// <param name="models">The pattern models.</param>
    /// <returns>Pattern distribution.</returns>
    private static IReadOnlyDictionary<UsagePatternType, int> CalculatePatternDistribution(List<BufferPatternModel> models)
    {
        return models.GroupBy(m => m.DetectedPattern)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Calculates prediction accuracy.
    /// </summary>
    /// <param name="models">The pattern models.</param>
    /// <returns>Prediction accuracy (0-1).</returns>
    private static double CalculatePredictionAccuracy(List<BufferPatternModel> models)
    {
        // Placeholder - would need actual vs predicted comparison
        return models.Any() ? models.Average(m => m.Confidence) : 0;
    }

    /// <summary>
    /// Gets the most common patterns.
    /// </summary>
    /// <param name="models">The pattern models.</param>
    /// <returns>Most common patterns.</returns>
    private static IReadOnlyList<UsagePatternType> GetMostCommonPatterns(List<BufferPatternModel> models)
    {
        return models.GroupBy(m => m.DetectedPattern)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();
    }
}

/// <summary>
/// Buffer pattern model.
/// </summary>
internal class BufferPatternModel
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; }

    /// <summary>Gets or sets the detected pattern.</summary>
    public UsagePatternType DetectedPattern { get; set; }

    /// <summary>Gets or sets the confidence level.</summary>
    public double Confidence { get; set; }

    /// <summary>Gets the usage statistics.</summary>
    public UsageStatistics Stats { get; } = new();

    public BufferPatternModel(string bufferId)
    {
        BufferId = bufferId;
    }

    /// <summary>
    /// Updates the model from usage history.
    /// </summary>
    /// <param name="history">The usage history.</param>
    public void UpdateFromHistory(IReadOnlyList<BufferUsageRecord> history)
    {
        Stats.TotalRentals = history.Count(r => r.EventType == BufferEventType.Rented);
        Stats.TotalReturns = history.Count(r => r.EventType == BufferEventType.Returned);
        Stats.AverageSize = history.Where(r => r.Size.HasValue).Any()
            ? history.Where(r => r.Size.HasValue).Average(r => r.Size!.Value)
            : 0;

        if (Stats.TotalRentals > 1)
        {
            var rentals = history.Where(r => r.EventType == BufferEventType.Rented)
                .OrderBy(r => r.Timestamp)
                .ToList();

            var intervals = new List<double>();
            for (var i = 1; i < rentals.Count; i++)
            {
                intervals.Add((rentals[i].Timestamp - rentals[i - 1].Timestamp).TotalSeconds);
            }

            Stats.AverageInterval = intervals.Any() ? TimeSpan.FromSeconds(intervals.Average()) : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Resets the model.
    /// </summary>
    public void Reset()
    {
        DetectedPattern = UsagePatternType.Unknown;
        Confidence = 0;
        Stats.Reset();
    }
}

/// <summary>
/// Usage statistics.
/// </summary>
internal class UsageStatistics
{
    /// <summary>Gets or sets the total rentals.</summary>
    public int TotalRentals { get; set; }

    /// <summary>Gets or sets the total returns.</summary>
    public int TotalReturns { get; set; }

    /// <summary>Gets or sets the average buffer size.</summary>
    public double AverageSize { get; set; }

    /// <summary>Gets or sets the average usage interval.</summary>
    public TimeSpan AverageInterval { get; set; }

    /// <summary>
    /// Resets the statistics.
    /// </summary>
    public void Reset()
    {
        TotalRentals = 0;
        TotalReturns = 0;
        AverageSize = 0;
        AverageInterval = TimeSpan.Zero;
    }
}

/// <summary>
/// Buffer pattern analysis.
/// </summary>
public class BufferPatternAnalysis
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the detected usage pattern.</summary>
    public UsagePatternType DetectedPattern { get; init; }

    /// <summary>Gets the confidence level (0-1).</summary>
    public double Confidence { get; init; }

    /// <summary>Gets the seasonality information.</summary>
    public SeasonalityInfo SeasonalityInfo { get; init; } = new();

    /// <summary>Gets the trend analysis.</summary>
    public TrendAnalysis TrendAnalysis { get; init; } = new();

    /// <summary>Gets the detected anomalies.</summary>
    public IReadOnlyList<UsageAnomaly> Anomalies { get; init; } = [];

    /// <summary>Gets the recommendations.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];
}

/// <summary>
/// Buffer usage prediction.
/// </summary>
public class BufferUsagePrediction
{
    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the prediction type.</summary>
    public PredictionType PredictionType { get; init; }

    /// <summary>Gets the confidence level (0-1).</summary>
    public double Confidence { get; init; }

    /// <summary>Gets the predicted usage count.</summary>
    public int PredictedUsageCount { get; init; }

    /// <summary>Gets the prediction rationale.</summary>
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>
/// Seasonality information.
/// </summary>
public class SeasonalityInfo
{
    /// <summary>Gets whether seasonality was detected.</summary>
    public bool HasSeasonality { get; init; }

    /// <summary>Gets the detected cycles.</summary>
    public IReadOnlyList<CycleInfo> DetectedCycles { get; init; } = [];

    /// <summary>Gets the seasonality strength (0-1).</summary>
    public double Strength { get; init; }
}

/// <summary>
/// Cycle information.
/// </summary>
public class CycleInfo
{
    /// <summary>Gets the cycle type.</summary>
    public CycleType Type { get; init; }

    /// <summary>Gets the peak periods.</summary>
    public IReadOnlyList<int> PeakPeriods { get; init; } = [];
}

/// <summary>
/// Trend analysis.
/// </summary>
public class TrendAnalysis
{
    /// <summary>Gets the trend direction.</summary>
    public TrendDirection Direction { get; init; }

    /// <summary>Gets the trend magnitude.</summary>
    public double Magnitude { get; init; }

    /// <summary>Gets the trend description.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Usage anomaly.
/// </summary>
public class UsageAnomaly
{
    /// <summary>Gets the anomaly timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the anomaly type.</summary>
    public AnomalyType Type { get; init; }

    /// <summary>Gets the anomaly severity.</summary>
    public AnomalySeverity Severity { get; init; }

    /// <summary>Gets the anomaly description.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Pattern analyzer statistics.
/// </summary>
public class PatternAnalyzerStats
{
    /// <summary>Gets the number of analyzed buffers.</summary>
    public int AnalyzedBuffers { get; init; }

    /// <summary>Gets the average confidence level.</summary>
    public double AverageConfidence { get; init; }

    /// <summary>Gets the pattern distribution.</summary>
    public IReadOnlyDictionary<UsagePatternType, int> PatternDistribution { get; init; } = new Dictionary<UsagePatternType, int>();

    /// <summary>Gets the prediction accuracy.</summary>
    public double PredictionAccuracy { get; init; }

    /// <summary>Gets the most common patterns.</summary>
    public IReadOnlyList<UsagePatternType> MostCommonPatterns { get; init; } = [];
}

/// <summary>
/// Usage pattern types.
/// </summary>
public enum UsagePatternType
{
    /// <summary>Unknown pattern.</summary>
    Unknown,

    /// <summary>Regular, predictable usage.</summary>
    Regular,

    /// <summary>Periodic usage with some variation.</summary>
    Periodic,

    /// <summary>Frequent, irregular usage.</summary>
    Frequent,

    /// <summary>Sporadic, infrequent usage.</summary>
    Sporadic
}

/// <summary>
/// Prediction types.
/// </summary>
public enum PredictionType
{
    /// <summary>High confidence prediction.</summary>
    Confident,

    /// <summary>Moderate confidence prediction.</summary>
    Moderate,

    /// <summary>Uncertain prediction.</summary>
    Uncertain
}

/// <summary>
/// Cycle types.
/// </summary>
public enum CycleType
{
    /// <summary>Hourly cycle.</summary>
    Hourly,

    /// <summary>Daily cycle.</summary>
    Daily,

    /// <summary>Weekly cycle.</summary>
    Weekly
}

/// <summary>
/// Trend directions.
/// </summary>
public enum TrendDirection
{
    /// <summary>Increasing trend.</summary>
    Increasing,

    /// <summary>Stable trend.</summary>
    Stable,

    /// <summary>Decreasing trend.</summary>
    Decreasing
}

/// <summary>
/// Anomaly types.
/// </summary>
public enum AnomalyType
{
    /// <summary>Unusual time interval.</summary>
    UnusualInterval,

    /// <summary>Unusual usage volume.</summary>
    UnusualVolume,

    /// <summary>Unusual usage pattern.</summary>
    UnusualPattern
}

/// <summary>
/// Anomaly severity levels.
/// </summary>
public enum AnomalySeverity
{
    /// <summary>Low severity anomaly.</summary>
    Low,

    /// <summary>Medium severity anomaly.</summary>
    Medium,

    /// <summary>High severity anomaly.</summary>
    High
}
