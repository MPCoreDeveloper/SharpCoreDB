// <copyright file="BufferHealth.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Comprehensive health assessment for buffer segments.
/// Provides corruption detection, integrity validation, and health monitoring.
/// C# 14: Primary constructors, pattern matching, Span<T> for validation.
/// </summary>
public sealed class BufferHealth
{
    private readonly BufferSegment _buffer;
    private readonly HealthMetrics _metrics;
    private readonly Lock _healthLock = new();

    private BufferHealthStatus _currentStatus = BufferHealthStatus.Unknown;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private string? _lastErrorMessage;

    /// <summary>Gets the buffer segment being monitored.</summary>
    public BufferSegment Buffer => _buffer;

    /// <summary>Gets the current health status.</summary>
    public BufferHealthStatus CurrentStatus => _currentStatus;

    /// <summary>Gets the timestamp of the last health check.</summary>
    public DateTimeOffset LastCheckTime => _lastCheckTime;

    /// <summary>Gets the last error message, if any.</summary>
    public string? LastErrorMessage => _lastErrorMessage;

    /// <summary>Gets the health metrics.</summary>
    public HealthMetrics Metrics => _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferHealth"/> class.
    /// </summary>
    /// <param name="buffer">The buffer segment to monitor.</param>
    public BufferHealth(BufferSegment buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _metrics = new HealthMetrics();
    }

    /// <summary>
    /// Performs a comprehensive health assessment.
    /// </summary>
    /// <returns>The health assessment result.</returns>
    public BufferHealthAssessment AssessHealth()
    {
        lock (_healthLock)
        {
            _lastCheckTime = DateTimeOffset.UtcNow;

            try
            {
                var issues = new List<BufferHealthIssue>();

                // Perform all health checks
                CheckBufferIntegrity(issues);
                CheckMemoryConsistency(issues);
                CheckUsagePatterns(issues);
                CheckPerformanceMetrics(issues);

                // Determine overall status
                _currentStatus = DetermineOverallStatus(issues);

                // Update metrics
                _metrics.RecordHealthCheck(_currentStatus, issues.Count);

                if (issues.Count > 0)
                {
                    _lastErrorMessage = string.Join("; ", issues.Select(i => i.Description));
                }
                else
                {
                    _lastErrorMessage = null;
                }

                return new BufferHealthAssessment
                {
                    Status = _currentStatus,
                    Issues = issues,
                    AssessmentTime = _lastCheckTime,
                    Recommendations = GenerateRecommendations(issues)
                };
            }
            catch (Exception ex)
            {
                _currentStatus = BufferHealthStatus.Corrupted;
                _lastErrorMessage = $"Health assessment failed: {ex.Message}";
                _metrics.RecordHealthCheck(_currentStatus, 1);

                return new BufferHealthAssessment
                {
                    Status = _currentStatus,
                    Issues = [new BufferHealthIssue
                    {
                        Severity = HealthIssueSeverity.Critical,
                        Category = HealthIssueCategory.System,
                        Title = "Health Assessment Failed",
                        Description = $"Exception during health check: {ex.Message}"
                    }],
                    AssessmentTime = _lastCheckTime,
                    Recommendations = ["Investigate system health and restart if necessary"]
                };
            }
        }
    }

    /// <summary>
    /// Checks buffer data integrity.
    /// </summary>
    /// <param name="issues">The list to add issues to.</param>
    private void CheckBufferIntegrity(List<BufferHealthIssue> issues)
    {
        try
        {
            // Check for null or empty buffer
            if (_buffer.Data is null)
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Critical,
                    Category = HealthIssueCategory.Data,
                    Title = "Null Buffer Data",
                    Description = "Buffer data array is null"
                });
                return;
            }

            if (_buffer.Data.Length == 0)
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Warning,
                    Category = HealthIssueCategory.Data,
                    Title = "Empty Buffer",
                    Description = "Buffer data array is empty"
                });
                return;
            }

            // Check buffer bounds
            if (_buffer.Position < 0 || _buffer.Position > _buffer.Data.Length)
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Critical,
                    Category = HealthIssueCategory.Data,
                    Title = "Invalid Position",
                    Description = $"Buffer position {_buffer.Position} is out of bounds [0, {_buffer.Data.Length}]"
                });
            }

            if (_buffer.Length < 0 || _buffer.Length > _buffer.Data.Length)
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Critical,
                    Category = HealthIssueCategory.Data,
                    Title = "Invalid Length",
                    Description = $"Buffer length {_buffer.Length} is out of bounds [0, {_buffer.Data.Length}]"
                });
            }

            // Check data consistency
            var writtenSpan = _buffer.GetWrittenSpan();
            if (writtenSpan.Length != _buffer.Length)
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Error,
                    Category = HealthIssueCategory.Data,
                    Title = "Data Length Mismatch",
                    Description = $"Written span length {writtenSpan.Length} doesn't match buffer length {_buffer.Length}"
                });
            }

            // Check for invalid data patterns (all zeros might indicate corruption)
            if (IsAllZeros(writtenSpan))
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Warning,
                    Category = HealthIssueCategory.Data,
                    Title = "Suspicious Data Pattern",
                    Description = "Buffer contains all zeros, which may indicate corruption or uninitialized data"
                });
            }

            _metrics.RecordIntegrityCheck(true);
        }
        catch (Exception ex)
        {
            issues.Add(new BufferHealthIssue
            {
                Severity = HealthIssueSeverity.Critical,
                Category = HealthIssueCategory.System,
                Title = "Integrity Check Failed",
                Description = $"Exception during integrity check: {ex.Message}"
            });
            _metrics.RecordIntegrityCheck(false);
        }
    }

    /// <summary>
    /// Checks memory consistency.
    /// </summary>
    /// <param name="issues">The list to add issues to.</param>
    private void CheckMemoryConsistency(List<BufferHealthIssue> issues)
    {
        try
        {
            // Check for memory corruption patterns
            var data = _buffer.Data.AsSpan();

            // Look for known corruption patterns
            if (HasCorruptionPatterns(data))
            {
                issues.Add(new BufferHealthIssue
                {
                    Severity = HealthIssueSeverity.Critical,
                    Category = HealthIssueCategory.Memory,
                    Title = "Memory Corruption Detected",
                    Description = "Buffer contains known memory corruption patterns"
                });
            }

            // Check memory alignment (if applicable)
            // This is platform-specific and may not be relevant for all scenarios

            _metrics.RecordMemoryCheck(true);
        }
        catch (Exception ex)
        {
            issues.Add(new BufferHealthIssue
            {
                Severity = HealthIssueSeverity.Error,
                Category = HealthIssueCategory.System,
                Title = "Memory Check Failed",
                Description = $"Exception during memory check: {ex.Message}"
            });
            _metrics.RecordMemoryCheck(false);
        }
    }

    /// <summary>
    /// Checks usage patterns for anomalies.
    /// </summary>
    /// <param name="issues">The list to add issues to.</param>
    private void CheckUsagePatterns(List<BufferHealthIssue> issues)
    {
        // This would integrate with BufferLifecycle to check usage patterns
        // For now, this is a placeholder for usage pattern analysis

        _metrics.RecordUsageCheck(true);
    }

    /// <summary>
    /// Checks performance metrics.
    /// </summary>
    /// <param name="issues">The list to add issues to.</param>
    private void CheckPerformanceMetrics(List<BufferHealthIssue> issues)
    {
        // Check for performance degradation
        if (_metrics.AverageHealthCheckTime > TimeSpan.FromMilliseconds(100))
        {
            issues.Add(new BufferHealthIssue
            {
                Severity = HealthIssueSeverity.Warning,
                Category = HealthIssueCategory.Performance,
                Title = "Slow Health Checks",
                Description = $"Average health check time {_metrics.AverageHealthCheckTime.TotalMilliseconds:F2}ms exceeds threshold"
            });
        }

        if (_metrics.FailedChecksRate > 0.1) // 10% failure rate
        {
            issues.Add(new BufferHealthIssue
            {
                Severity = HealthIssueSeverity.Warning,
                Category = HealthIssueCategory.Performance,
                Title = "High Check Failure Rate",
                Description = $"Health check failure rate {_metrics.FailedChecksRate:P1} exceeds threshold"
            });
        }

        _metrics.RecordPerformanceCheck(true);
    }

    /// <summary>
    /// Determines the overall health status from issues.
    /// </summary>
    /// <param name="issues">The list of issues.</param>
    /// <returns>The overall health status.</returns>
    private static BufferHealthStatus DetermineOverallStatus(IReadOnlyList<BufferHealthIssue> issues)
    {
        if (issues.Count == 0)
        {
            return BufferHealthStatus.Healthy;
        }

        var hasCritical = issues.Any(i => i.Severity == HealthIssueSeverity.Critical);
        if (hasCritical)
        {
            return BufferHealthStatus.Corrupted;
        }

        var hasError = issues.Any(i => i.Severity == HealthIssueSeverity.Error);
        if (hasError)
        {
            return BufferHealthStatus.Degraded;
        }

        return BufferHealthStatus.Warning;
    }

    /// <summary>
    /// Generates recommendations based on issues.
    /// </summary>
    /// <param name="issues">The list of issues.</param>
    /// <returns>List of recommendations.</returns>
    private static IReadOnlyList<string> GenerateRecommendations(IReadOnlyList<BufferHealthIssue> issues)
    {
        var recommendations = new List<string>();

        foreach (var issue in issues)
        {
            var recommendation = issue.Category switch
            {
                HealthIssueCategory.Data => "Consider revalidating buffer data or recreating the buffer",
                HealthIssueCategory.Memory => "Check for memory corruption in the system, consider restarting the application",
                HealthIssueCategory.Performance => "Monitor system performance and consider optimizing health check frequency",
                HealthIssueCategory.System => "Investigate system-level issues and ensure proper error handling",
                _ => "Review buffer usage patterns and system configuration"
            };

            if (!recommendations.Contains(recommendation))
            {
                recommendations.Add(recommendation);
            }
        }

        return recommendations;
    }

    /// <summary>
    /// Checks if a span contains all zeros.
    /// </summary>
    /// <param name="span">The span to check.</param>
    /// <returns>True if all bytes are zero.</returns>
    private static bool IsAllZeros(ReadOnlySpan<byte> span)
    {
        foreach (var b in span)
        {
            if (b != 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Checks for known memory corruption patterns.
    /// </summary>
    /// <param name="data">The data to check.</param>
    /// <returns>True if corruption patterns are detected.</returns>
    private static bool HasCorruptionPatterns(ReadOnlySpan<byte> data)
    {
        // Check for repeating patterns that might indicate corruption
        if (data.Length < 8)
        {
            return false;
        }

        // Check for repeating byte patterns
        var firstByte = data[0];
        var repeatingCount = 1;
        for (var i = 1; i < Math.Min(data.Length, 64); i++) // Check first 64 bytes
        {
            if (data[i] == firstByte)
            {
                repeatingCount++;
            }
            else
            {
                break;
            }
        }

        // If more than 16 bytes repeat, it might be corruption
        return repeatingCount > 16;
    }
}

/// <summary>
/// Health assessment result.
/// </summary>
public class BufferHealthAssessment
{
    /// <summary>Gets the overall health status.</summary>
    public BufferHealthStatus Status { get; init; }

    /// <summary>Gets the list of health issues.</summary>
    public IReadOnlyList<BufferHealthIssue> Issues { get; init; } = [];

    /// <summary>Gets the assessment timestamp.</summary>
    public DateTimeOffset AssessmentTime { get; init; }

    /// <summary>Gets the list of recommendations.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = [];

    /// <summary>Gets a human-readable summary.</summary>
    public string Summary => $"{Status} ({Issues.Count} issues)";

    /// <summary>
    /// Gets whether the buffer requires immediate attention.
    /// </summary>
    public bool RequiresAttention => Status is BufferHealthStatus.Corrupted or BufferHealthStatus.Degraded;
}

/// <summary>
/// Buffer health issue.
/// </summary>
public class BufferHealthIssue
{
    /// <summary>Gets the issue severity.</summary>
    public HealthIssueSeverity Severity { get; init; }

    /// <summary>Gets the issue category.</summary>
    public HealthIssueCategory Category { get; init; }

    /// <summary>Gets the issue title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the detailed description.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Buffer health status enumeration.
/// </summary>
public enum BufferHealthStatus
{
    /// <summary>Health status unknown.</summary>
    Unknown,

    /// <summary>Buffer is healthy.</summary>
    Healthy,

    /// <summary>Buffer has warnings but is functional.</summary>
    Warning,

    /// <summary>Buffer is degraded but operational.</summary>
    Degraded,

    /// <summary>Buffer is corrupted and unusable.</summary>
    Corrupted
}

/// <summary>
/// Health issue severity levels.
/// </summary>
public enum HealthIssueSeverity
{
    /// <summary>Informational issue.</summary>
    Info,

    /// <summary>Warning that should be noted.</summary>
    Warning,

    /// <summary>Error that affects functionality.</summary>
    Error,

    /// <summary>Critical issue requiring immediate action.</summary>
    Critical
}

/// <summary>
/// Health issue categories.
/// </summary>
public enum HealthIssueCategory
{
    /// <summary>Data-related issues.</summary>
    Data,

    /// <summary>Memory-related issues.</summary>
    Memory,

    /// <summary>Performance-related issues.</summary>
    Performance,

    /// <summary>System-level issues.</summary>
    System
}
