// <copyright file="HealthMetrics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Comprehensive health metrics collection for buffer monitoring.
/// Tracks health check performance, failure rates, and trends.
/// C# 14: Collection expressions, primary constructors.
/// </summary>
public class HealthMetrics
{
    private readonly List<HealthCheckRecord> _checkHistory = [];
    private readonly Lock _metricsLock = new();

    private int _totalChecks;
    private int _failedChecks;
    private TimeSpan _totalCheckTime;
    private int _integrityChecks;
    private int _integrityFailures;
    private int _memoryChecks;
    private int _memoryFailures;
    private int _usageChecks;
    private int _usageFailures;
    private int _performanceChecks;
    private int _performanceFailures;

    /// <summary>Gets the total number of health checks performed.</summary>
    public int TotalChecks => _totalChecks;

    /// <summary>Gets the number of failed health checks.</summary>
    public int FailedChecks => _failedChecks;

    /// <summary>Gets the health check failure rate (0-1).</summary>
    public double FailedChecksRate => _totalChecks > 0 ? (double)_failedChecks / _totalChecks : 0;

    /// <summary>Gets the average time per health check.</summary>
    public TimeSpan AverageHealthCheckTime => _totalChecks > 0
        ? TimeSpan.FromTicks(_totalCheckTime.Ticks / _totalChecks)
        : TimeSpan.Zero;

    /// <summary>Gets the integrity check success rate (0-1).</summary>
    public double IntegritySuccessRate => _integrityChecks > 0
        ? 1.0 - ((double)_integrityFailures / _integrityChecks)
        : 1.0;

    /// <summary>Gets the memory check success rate (0-1).</summary>
    public double MemorySuccessRate => _memoryChecks > 0
        ? 1.0 - ((double)_memoryFailures / _memoryChecks)
        : 1.0;

    /// <summary>Gets the usage check success rate (0-1).</summary>
    public double UsageSuccessRate => _usageChecks > 0
        ? 1.0 - ((double)_usageFailures / _usageChecks)
        : 1.0;

    /// <summary>Gets the performance check success rate (0-1).</summary>
    public double PerformanceSuccessRate => _performanceChecks > 0
        ? 1.0 - ((double)_performanceFailures / _performanceChecks)
        : 1.0;

    /// <summary>Gets the overall health score (0-100).</summary>
    public double OverallHealthScore => CalculateOverallHealthScore();

    /// <summary>Gets the recent health check history.</summary>
    public IReadOnlyList<HealthCheckRecord> RecentHistory
    {
        get
        {
            lock (_metricsLock)
            {
                return [.. _checkHistory.TakeLast(10)]; // Last 10 checks
            }
        }
    }

    /// <summary>
    /// Records a health check result.
    /// </summary>
    /// <param name="status">The health status.</param>
    /// <param name="issueCount">The number of issues found.</param>
    /// <param name="checkTime">The time taken for the check.</param>
    public void RecordHealthCheck(BufferHealthStatus status, int issueCount, TimeSpan? checkTime = null)
    {
        var record = new HealthCheckRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            Status = status,
            IssueCount = issueCount,
            CheckTime = checkTime ?? TimeSpan.Zero
        };

        lock (_metricsLock)
        {
            _checkHistory.Add(record);
            _totalChecks++;
            _totalCheckTime += record.CheckTime;

            if (status == BufferHealthStatus.Corrupted || issueCount > 0)
            {
                _failedChecks++;
            }

            // Keep only recent history
            if (_checkHistory.Count > 100)
            {
                _checkHistory.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Records an integrity check result.
    /// </summary>
    /// <param name="success">Whether the check succeeded.</param>
    public void RecordIntegrityCheck(bool success)
    {
        lock (_metricsLock)
        {
            _integrityChecks++;
            if (!success)
            {
                _integrityFailures++;
            }
        }
    }

    /// <summary>
    /// Records a memory check result.
    /// </summary>
    /// <param name="success">Whether the check succeeded.</param>
    public void RecordMemoryCheck(bool success)
    {
        lock (_metricsLock)
        {
            _memoryChecks++;
            if (!success)
            {
                _memoryFailures++;
            }
        }
    }

    /// <summary>
    /// Records a usage check result.
    /// </summary>
    /// <param name="success">Whether the check succeeded.</param>
    public void RecordUsageCheck(bool success)
    {
        lock (_metricsLock)
        {
            _usageChecks++;
            if (!success)
            {
                _usageFailures++;
            }
        }
    }

    /// <summary>
    /// Records a performance check result.
    /// </summary>
    /// <param name="success">Whether the check succeeded.</param>
    public void RecordPerformanceCheck(bool success)
    {
        lock (_metricsLock)
        {
            _performanceChecks++;
            if (!success)
            {
                _performanceFailures++;
            }
        }
    }

    /// <summary>
    /// Gets health trend analysis.
    /// </summary>
    /// <returns>Health trend analysis.</returns>
    public HealthTrendAnalysis GetTrendAnalysis()
    {
        lock (_metricsLock)
        {
            if (_checkHistory.Count < 2)
            {
                return new HealthTrendAnalysis
                {
                    Direction = HealthTrend.Stable,
                    Magnitude = 0,
                    Description = "Insufficient data for trend analysis"
                };
            }

            // Analyze last 20 checks or all if less
            var recentChecks = _checkHistory.TakeLast(Math.Min(20, _checkHistory.Count)).ToList();

            // Calculate health score trend
            var healthScores = recentChecks.Select(c => CalculateHealthScore(c.Status, c.IssueCount)).ToList();
            var firstHalf = healthScores.Take(healthScores.Count / 2).Average();
            var secondHalf = healthScores.Skip(healthScores.Count / 2).Average();

            var change = secondHalf - firstHalf;
            var direction = Math.Abs(change) < 5 ? HealthTrend.Stable :
                           change > 0 ? HealthTrend.Improving : HealthTrend.Degrading;

            return new HealthTrendAnalysis
            {
                Direction = direction,
                Magnitude = Math.Abs(change),
                Description = $"{direction} health trend ({change:F1} point change)"
            };
        }
    }

    /// <summary>
    /// Gets detailed health statistics.
    /// </summary>
    /// <returns>Detailed health statistics.</returns>
    public DetailedHealthStats GetDetailedStats()
    {
        lock (_metricsLock)
        {
            var statusDistribution = _checkHistory
                .GroupBy(r => r.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            var issueDistribution = _checkHistory
                .GroupBy(r => r.IssueCount)
                .ToDictionary(g => g.Key, g => g.Count());

            return new DetailedHealthStats
            {
                TotalChecks = _totalChecks,
                FailedChecks = _failedChecks,
                AverageCheckTime = AverageHealthCheckTime,
                StatusDistribution = statusDistribution,
                IssueCountDistribution = issueDistribution,
                IntegritySuccessRate = IntegritySuccessRate,
                MemorySuccessRate = MemorySuccessRate,
                UsageSuccessRate = UsageSuccessRate,
                PerformanceSuccessRate = PerformanceSuccessRate,
                OverallHealthScore = OverallHealthScore,
                TrendAnalysis = GetTrendAnalysis()
            };
        }
    }

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    public void Reset()
    {
        lock (_metricsLock)
        {
            _checkHistory.Clear();
            _totalChecks = 0;
            _failedChecks = 0;
            _totalCheckTime = TimeSpan.Zero;
            _integrityChecks = 0;
            _integrityFailures = 0;
            _memoryChecks = 0;
            _memoryFailures = 0;
            _usageChecks = 0;
            _usageFailures = 0;
            _performanceChecks = 0;
            _performanceFailures = 0;
        }
    }

    /// <summary>
    /// Calculates the overall health score.
    /// </summary>
    /// <returns>Health score (0-100).</returns>
    private double CalculateOverallHealthScore()
    {
        var failureRate = FailedChecksRate;
        var integrityScore = IntegritySuccessRate * 20; // 20 points for integrity
        var memoryScore = MemorySuccessRate * 20;      // 20 points for memory
        var usageScore = UsageSuccessRate * 20;        // 20 points for usage
        var performanceScore = PerformanceSuccessRate * 20; // 20 points for performance
        var failurePenalty = failureRate * 20;         // Penalty for failures

        var baseScore = integrityScore + memoryScore + usageScore + performanceScore;
        return Math.Max(0, Math.Min(100, baseScore - failurePenalty));
    }

    /// <summary>
    /// Calculates a health score for a single check.
    /// </summary>
    /// <param name="status">The health status.</param>
    /// <param name="issueCount">The number of issues.</param>
    /// <returns>Health score (0-100).</returns>
    private static double CalculateHealthScore(BufferHealthStatus status, int issueCount)
    {
        return status switch
        {
            BufferHealthStatus.Healthy => 100,
            BufferHealthStatus.Warning => Math.Max(50, 100 - (issueCount * 10)),
            BufferHealthStatus.Degraded => Math.Max(25, 75 - (issueCount * 15)),
            BufferHealthStatus.Corrupted => 0,
            _ => 50
        };
    }
}

/// <summary>
/// Record of a single health check.
/// </summary>
public class HealthCheckRecord
{
    /// <summary>Gets the timestamp of the check.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the health status result.</summary>
    public BufferHealthStatus Status { get; init; }

    /// <summary>Gets the number of issues found.</summary>
    public int IssueCount { get; init; }

    /// <summary>Gets the time taken for the check.</summary>
    public TimeSpan CheckTime { get; init; }
}

/// <summary>
/// Health trend analysis.
/// </summary>
public class HealthTrendAnalysis
{
    /// <summary>Gets the trend direction.</summary>
    public HealthTrend Direction { get; init; }

    /// <summary>Gets the magnitude of the trend.</summary>
    public double Magnitude { get; init; }

    /// <summary>Gets the trend description.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Detailed health statistics.
/// </summary>
public class DetailedHealthStats
{
    /// <summary>Gets the total number of checks.</summary>
    public int TotalChecks { get; init; }

    /// <summary>Gets the number of failed checks.</summary>
    public int FailedChecks { get; init; }

    /// <summary>Gets the average check time.</summary>
    public TimeSpan AverageCheckTime { get; init; }

    /// <summary>Gets the distribution of health statuses.</summary>
    public IReadOnlyDictionary<BufferHealthStatus, int> StatusDistribution { get; init; } = new Dictionary<BufferHealthStatus, int>();

    /// <summary>Gets the distribution of issue counts.</summary>
    public IReadOnlyDictionary<int, int> IssueCountDistribution { get; init; } = new Dictionary<int, int>();

    /// <summary>Gets the integrity success rate.</summary>
    public double IntegritySuccessRate { get; init; }

    /// <summary>Gets the memory success rate.</summary>
    public double MemorySuccessRate { get; init; }

    /// <summary>Gets the usage success rate.</summary>
    public double UsageSuccessRate { get; init; }

    /// <summary>Gets the performance success rate.</summary>
    public double PerformanceSuccessRate { get; init; }

    /// <summary>Gets the overall health score.</summary>
    public double OverallHealthScore { get; init; }

    /// <summary>Gets the trend analysis.</summary>
    public HealthTrendAnalysis TrendAnalysis { get; init; } = new();
}

/// <summary>
/// Health trend directions.
/// </summary>
public enum HealthTrend
{
    /// <summary>Health is improving.</summary>
    Improving,

    /// <summary>Health is stable.</summary>
    Stable,

    /// <summary>Health is degrading.</summary>
    Degrading
}
