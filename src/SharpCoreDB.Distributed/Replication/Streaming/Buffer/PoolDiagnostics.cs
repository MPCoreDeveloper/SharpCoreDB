// <copyright file="PoolDiagnostics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Text;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Advanced diagnostics for buffer pool memory usage and performance.
/// Provides detailed analysis and troubleshooting capabilities.
/// C# 14: Collection expressions, pattern matching for diagnostics.
/// </summary>
public class PoolDiagnostics
{
    private readonly BufferPool _pool;
    private readonly PoolMetricsCollector _metricsCollector;
    private readonly ILogger<PoolDiagnostics>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PoolDiagnostics"/> class.
    /// </summary>
    /// <param name="pool">The buffer pool to diagnose.</param>
    /// <param name="metricsCollector">The metrics collector.</param>
    /// <param name="logger">Optional logger.</param>
    public PoolDiagnostics(BufferPool pool, PoolMetricsCollector metricsCollector, ILogger<PoolDiagnostics>? logger = null)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _logger = logger;
    }

    /// <summary>
    /// Performs a comprehensive diagnostic analysis.
    /// </summary>
    /// <returns>Detailed diagnostic report.</returns>
    public PoolDiagnosticReport PerformFullAnalysis()
    {
        var metrics = _pool.GetMetrics();
        var snapshot = metrics.CreateSnapshot();
        var aggregatedStats = _metricsCollector.GetAggregatedStats();

        var issues = DetectIssues(metrics, snapshot, aggregatedStats);
        var recommendations = GenerateRecommendations(metrics, snapshot, aggregatedStats, issues);
        var performanceAnalysis = AnalyzePerformance(metrics, aggregatedStats);

        return new PoolDiagnosticReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            CurrentMetrics = metrics,
            MetricsSnapshot = snapshot,
            AggregatedStats = aggregatedStats,
            DetectedIssues = issues,
            Recommendations = recommendations,
            PerformanceAnalysis = performanceAnalysis,
            OverallHealth = DetermineOverallHealth(issues, performanceAnalysis)
        };
    }

    /// <summary>
    /// Detects potential issues with the buffer pool.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <param name="snapshot">Metrics snapshot.</param>
    /// <param name="aggregated">Aggregated statistics.</param>
    /// <returns>List of detected issues.</returns>
    private IReadOnlyList<PoolIssue> DetectIssues(
        PoolMetrics metrics,
        PoolMetricsSnapshot snapshot,
        PoolAggregatedStats aggregated)
    {
        var issues = new List<PoolIssue>();

        // High utilization issue
        if (metrics.PoolUtilizationPercent > 95)
        {
            issues.Add(new PoolIssue
            {
                Severity = DiagnosticSeverity.Critical,
                Category = IssueCategory.Capacity,
                Title = "Pool Critically Full",
                Description = $"Pool utilization is {metrics.PoolUtilizationPercent:F1}%, risking allocation failures",
                Evidence = $"Available: {metrics.AvailableBuffers}, Total: {metrics.CurrentBufferCount}"
            });
        }
        else if (metrics.PoolUtilizationPercent > 85)
        {
            issues.Add(new PoolIssue
            {
                Severity = DiagnosticSeverity.Warning,
                Category = IssueCategory.Capacity,
                Title = "Pool Nearly Full",
                Description = $"Pool utilization is {metrics.PoolUtilizationPercent:F1}%, consider increasing capacity",
                Evidence = $"Available: {metrics.AvailableBuffers}, Total: {metrics.CurrentBufferCount}"
            });
        }

        // Low efficiency issue
        if (metrics.EfficiencyRating < 0.3)
        {
            issues.Add(new PoolIssue
            {
                Severity = DiagnosticSeverity.Warning,
                Category = IssueCategory.Performance,
                Title = "Low Buffer Reuse",
                Description = $"Buffer reuse efficiency is {metrics.EfficiencyRating:P1}, indicating poor cache locality",
                Evidence = $"Efficiency: {metrics.EfficiencyRating:P1}, Created: {metrics.TotalBuffersCreated}"
            });
        }

        // Memory waste issue
        if (metrics.PoolUtilizationPercent < 20 && metrics.CurrentBufferCount > 50)
        {
            issues.Add(new PoolIssue
            {
                Severity = DiagnosticSeverity.Info,
                Category = IssueCategory.Memory,
                Title = "Excessive Memory Allocation",
                Description = $"Pool has low utilization ({metrics.PoolUtilizationPercent:F1}%) with {metrics.CurrentBufferCount} buffers",
                Evidence = $"Utilization: {metrics.PoolUtilizationPercent:F1}%, Memory: {metrics.TotalMemoryUsage / 1024}KB"
            });
        }

        // High allocation rate issue
        if (aggregated.AllocationRatePerMinute > 100)
        {
            issues.Add(new PoolIssue
            {
                Severity = DiagnosticSeverity.Warning,
                Category = IssueCategory.Performance,
                Title = "High Allocation Rate",
                Description = $"Buffer allocation rate is {aggregated.AllocationRatePerMinute:F1} buffers/minute",
                Evidence = $"Rate: {aggregated.AllocationRatePerMinute:F1}/min, Total: {aggregated.TotalAllocations}"
            });
        }

        return issues;
    }

    /// <summary>
    /// Generates recommendations based on analysis.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <param name="snapshot">Metrics snapshot.</param>
    /// <param name="aggregated">Aggregated statistics.</param>
    /// <param name="issues">Detected issues.</param>
    /// <returns>List of recommendations.</returns>
    private IReadOnlyList<PoolRecommendation> GenerateRecommendations(
        PoolMetrics metrics,
        PoolMetricsSnapshot snapshot,
        PoolAggregatedStats aggregated,
        IReadOnlyList<PoolIssue> issues)
    {
        var recommendations = new List<PoolRecommendation>();

        // Capacity recommendations
        if (issues.Any(i => i.Category == IssueCategory.Capacity && i.Severity >= DiagnosticSeverity.Warning))
        {
            recommendations.Add(new PoolRecommendation
            {
                Priority = RecommendationPriority.High,
                Category = RecommendationCategory.Configuration,
                Title = "Increase Pool Capacity",
                Description = "Increase MaxBuffers to prevent allocation failures under load",
                Action = $"Set MaxBuffers to {metrics.CurrentBufferCount * 2}",
                Rationale = "Current utilization is too high, risking performance degradation"
            });
        }

        // Memory optimization recommendations
        if (issues.Any(i => i.Category == IssueCategory.Memory))
        {
            recommendations.Add(new PoolRecommendation
            {
                Priority = RecommendationPriority.Medium,
                Category = RecommendationCategory.Configuration,
                Title = "Reduce Pool Size",
                Description = "Decrease MaxBuffers to reduce memory footprint",
                Action = $"Set MaxBuffers to {Math.Max(10, metrics.CurrentBufferCount / 2)}",
                Rationale = "Pool has excess capacity that is not being utilized"
            });
        }

        // Performance recommendations
        if (issues.Any(i => i.Category == IssueCategory.Performance))
        {
            recommendations.Add(new PoolRecommendation
            {
                Priority = RecommendationPriority.Medium,
                Category = RecommendationCategory.Optimization,
                Title = "Optimize Buffer Sizes",
                Description = "Adjust DefaultBufferSize based on actual usage patterns",
                Action = $"Analyze buffer size distribution and adjust DefaultBufferSize accordingly",
                Rationale = "Buffer sizes don't match application access patterns"
            });
        }

        // Monitoring recommendations
        if (aggregated.SampleCount < 10)
        {
            recommendations.Add(new PoolRecommendation
            {
                Priority = RecommendationPriority.Low,
                Category = RecommendationCategory.Monitoring,
                Title = "Increase Metrics Collection",
                Description = "Collect more metrics samples for better analysis",
                Action = "Ensure metrics are collected regularly over a longer period",
                Rationale = "Limited historical data makes optimization difficult"
            });
        }

        return recommendations;
    }

    /// <summary>
    /// Analyzes performance characteristics.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <param name="aggregated">Aggregated statistics.</param>
    /// <returns>Performance analysis.</returns>
    private PoolPerformanceAnalysis AnalyzePerformance(PoolMetrics metrics, PoolAggregatedStats aggregated)
    {
        var throughput = CalculateThroughput(metrics, aggregated);
        var latency = EstimateLatency(metrics);
        var bottlenecks = IdentifyBottlenecks(metrics, aggregated);

        return new PoolPerformanceAnalysis
        {
            EstimatedThroughput = throughput,
            EstimatedLatency = latency,
            IdentifiedBottlenecks = bottlenecks,
            PerformanceScore = CalculatePerformanceScore(metrics, aggregated, throughput, latency)
        };
    }

    /// <summary>
    /// Calculates estimated throughput.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <param name="aggregated">Aggregated statistics.</param>
    /// <returns>Estimated throughput in operations per second.</returns>
    private double CalculateThroughput(PoolMetrics metrics, PoolAggregatedStats aggregated)
    {
        // Simplified throughput calculation based on allocation patterns
        if (aggregated.TimeSpan.TotalSeconds == 0)
        {
            return 0;
        }

        var operationsPerSecond = aggregated.TotalAllocations / aggregated.TimeSpan.TotalSeconds;
        return operationsPerSecond;
    }

    /// <summary>
    /// Estimates average latency.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <returns>Estimated latency.</returns>
    private TimeSpan EstimateLatency(PoolMetrics metrics)
    {
        // Simplified latency estimation based on pool utilization
        var baseLatencyMs = 0.1; // Base allocation time
        var contentionFactor = metrics.PoolUtilizationPercent / 100.0;
        var estimatedLatencyMs = baseLatencyMs * (1 + contentionFactor);

        return TimeSpan.FromMilliseconds(estimatedLatencyMs);
    }

    /// <summary>
    /// Identifies performance bottlenecks.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <param name="aggregated">Aggregated statistics.</param>
    /// <returns>List of identified bottlenecks.</returns>
    private IReadOnlyList<string> IdentifyBottlenecks(PoolMetrics metrics, PoolAggregatedStats aggregated)
    {
        var bottlenecks = new List<string>();

        if (metrics.PoolUtilizationPercent > 90)
        {
            bottlenecks.Add("Buffer pool capacity exhausted");
        }

        if (aggregated.AllocationRatePerMinute > 500)
        {
            bottlenecks.Add("High allocation rate causing GC pressure");
        }

        if (metrics.EfficiencyRating < 0.2)
        {
            bottlenecks.Add("Poor buffer reuse efficiency");
        }

        return bottlenecks;
    }

    /// <summary>
    /// Calculates an overall performance score.
    /// </summary>
    /// <param name="metrics">Current metrics.</param>
    /// <param name="aggregated">Aggregated statistics.</param>
    /// <param name="throughput">Calculated throughput.</param>
    /// <param name="latency">Estimated latency.</param>
    /// <returns>Performance score (0-100).</returns>
    private double CalculatePerformanceScore(
        PoolMetrics metrics,
        PoolAggregatedStats aggregated,
        double throughput,
        TimeSpan latency)
    {
        var utilizationScore = Math.Max(0, 100 - metrics.PoolUtilizationPercent);
        var efficiencyScore = metrics.EfficiencyRating * 100;
        var latencyScore = Math.Max(0, 100 - (latency.TotalMilliseconds * 10));

        return (utilizationScore + efficiencyScore + latencyScore) / 3.0;
    }

    /// <summary>
    /// Determines overall health based on issues and performance.
    /// </summary>
    /// <param name="issues">Detected issues.</param>
    /// <param name="performance">Performance analysis.</param>
    /// <returns>Overall health status.</returns>
    private PoolHealthStatus DetermineOverallHealth(
        IReadOnlyList<PoolIssue> issues,
        PoolPerformanceAnalysis performance)
    {
        var criticalIssues = issues.Count(i => i.Severity == DiagnosticSeverity.Critical);
        var warningIssues = issues.Count(i => i.Severity == DiagnosticSeverity.Warning);

        if (criticalIssues > 0)
        {
            return PoolHealthStatus.Critical;
        }

        if (warningIssues > 0 || performance.PerformanceScore < 50)
        {
            return PoolHealthStatus.Warning;
        }

        if (issues.Count > 0 || performance.PerformanceScore < 80)
        {
            return PoolHealthStatus.NeedsAttention;
        }

        return PoolHealthStatus.Healthy;
    }
}

/// <summary>
/// Comprehensive diagnostic report for buffer pool analysis.
/// </summary>
public class PoolDiagnosticReport
{
    /// <summary>Gets the timestamp when the report was generated.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the current pool metrics.</summary>
    public PoolMetrics CurrentMetrics { get; init; } = new();

    /// <summary>Gets the metrics snapshot with analysis.</summary>
    public PoolMetricsSnapshot MetricsSnapshot { get; init; } = new();

    /// <summary>Gets the aggregated statistics.</summary>
    public PoolAggregatedStats AggregatedStats { get; init; } = new();

    /// <summary>Gets the list of detected issues.</summary>
    public IReadOnlyList<PoolIssue> DetectedIssues { get; init; } = [];

    /// <summary>Gets the list of recommendations.</summary>
    public IReadOnlyList<PoolRecommendation> Recommendations { get; init; } = [];

    /// <summary>Gets the performance analysis.</summary>
    public PoolPerformanceAnalysis PerformanceAnalysis { get; init; } = new();

    /// <summary>Gets the overall health status.</summary>
    public PoolHealthStatus OverallHealth { get; init; }

    /// <summary>Gets a summary of the report.</summary>
    public string Summary => $"{OverallHealth} - {DetectedIssues.Count} issues, {Recommendations.Count} recommendations";

    /// <summary>
    /// Generates a detailed text report.
    /// </summary>
    /// <returns>Formatted report text.</returns>
    public string GenerateReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Buffer Pool Diagnostic Report - {Timestamp}");
        sb.AppendLine($"Overall Health: {OverallHealth}");
        sb.AppendLine();

        sb.AppendLine("Current Metrics:");
        sb.AppendLine($"- Utilization: {CurrentMetrics.PoolUtilizationPercent:F1}%");
        sb.AppendLine($"- Efficiency: {CurrentMetrics.EfficiencyRating:P1}");
        sb.AppendLine($"- Memory Usage: {CurrentMetrics.TotalMemoryUsage / 1024}KB");
        sb.AppendLine();

        if (DetectedIssues.Count > 0)
        {
            sb.AppendLine("Detected Issues:");
            foreach (var issue in DetectedIssues)
            {
                sb.AppendLine($"- [{issue.Severity}] {issue.Title}: {issue.Description}");
            }
            sb.AppendLine();
        }

        if (Recommendations.Count > 0)
        {
            sb.AppendLine("Recommendations:");
            foreach (var rec in Recommendations)
            {
                sb.AppendLine($"- [{rec.Priority}] {rec.Title}: {rec.Description}");
                sb.AppendLine($"  Action: {rec.Action}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Performance Analysis:");
        sb.AppendLine($"- Throughput: {PerformanceAnalysis.EstimatedThroughput:F0} ops/sec");
        sb.AppendLine($"- Latency: {PerformanceAnalysis.EstimatedLatency.TotalMilliseconds:F2}ms");
        sb.AppendLine($"- Score: {PerformanceAnalysis.PerformanceScore:F1}/100");

        return sb.ToString();
    }
}

/// <summary>
/// Represents a detected issue with the buffer pool.
/// </summary>
public class PoolIssue
{
    /// <summary>Gets the severity of the issue.</summary>
    public DiagnosticSeverity Severity { get; init; }

    /// <summary>Gets the category of the issue.</summary>
    public IssueCategory Category { get; init; }

    /// <summary>Gets the issue title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the detailed description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the evidence supporting the issue.</summary>
    public string Evidence { get; init; } = string.Empty;
}

/// <summary>
/// Represents a recommendation for pool optimization.
/// </summary>
public class PoolRecommendation
{
    /// <summary>Gets the priority of the recommendation.</summary>
    public RecommendationPriority Priority { get; init; }

    /// <summary>Gets the category of the recommendation.</summary>
    public RecommendationCategory Category { get; init; }

    /// <summary>Gets the recommendation title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the detailed description.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the recommended action.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Gets the rationale for the recommendation.</summary>
    public string Rationale { get; init; } = string.Empty;
}

/// <summary>
/// Performance analysis results.
/// </summary>
public class PoolPerformanceAnalysis
{
    /// <summary>Gets the estimated throughput in operations per second.</summary>
    public double EstimatedThroughput { get; init; }

    /// <summary>Gets the estimated latency.</summary>
    public TimeSpan EstimatedLatency { get; init; }

    /// <summary>Gets the list of identified bottlenecks.</summary>
    public IReadOnlyList<string> IdentifiedBottlenecks { get; init; } = [];

    /// <summary>Gets the overall performance score (0-100).</summary>
    public double PerformanceScore { get; init; }
}

/// <summary>
/// Diagnostic severity levels.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational issue, no immediate action required.</summary>
    Info,

    /// <summary>Warning, should be addressed but not critical.</summary>
    Warning,

    /// <summary>Critical issue requiring immediate attention.</summary>
    Critical
}

/// <summary>
/// Issue categories.
/// </summary>
public enum IssueCategory
{
    /// <summary>Capacity-related issues.</summary>
    Capacity,

    /// <summary>Performance-related issues.</summary>
    Performance,

    /// <summary>Memory-related issues.</summary>
    Memory,

    /// <summary>Configuration-related issues.</summary>
    Configuration
}

/// <summary>
/// Recommendation priority levels.
/// </summary>
public enum RecommendationPriority
{
    /// <summary>Low priority, can be addressed later.</summary>
    Low,

    /// <summary>Medium priority, should be addressed soon.</summary>
    Medium,

    /// <summary>High priority, address immediately.</summary>
    High
}

/// <summary>
/// Recommendation categories.
/// </summary>
public enum RecommendationCategory
{
    /// <summary>Configuration changes.</summary>
    Configuration,

    /// <summary>Code optimization.</summary>
    Optimization,

    /// <summary>Monitoring improvements.</summary>
    Monitoring,

    /// <summary>Infrastructure changes.</summary>
    Infrastructure
}

/// <summary>
/// Overall pool health status.
/// </summary>
public enum PoolHealthStatus
{
    /// <summary>Pool is operating normally.</summary>
    Healthy,

    /// <summary>Pool needs some attention but is functional.</summary>
    NeedsAttention,

    /// <summary>Pool has warnings that should be addressed.</summary>
    Warning,

    /// <summary>Pool has critical issues requiring immediate action.</summary>
    Critical
}
