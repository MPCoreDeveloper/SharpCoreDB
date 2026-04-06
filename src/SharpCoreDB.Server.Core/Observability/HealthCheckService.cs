// <copyright file="HealthCheckService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Diagnostics;

namespace SharpCoreDB.Server.Core.Observability;

/// <summary>
/// Production-grade health check service with detailed diagnostics.
/// Provides server health monitoring for external systems.
/// C# 14: Primary constructor with collection expressions.
/// </summary>
public sealed class HealthCheckService(
    DatabaseRegistry databaseRegistry,
    SessionManager sessionManager,
    MetricsCollector metricsCollector)
{
    private readonly DatabaseRegistry _databaseRegistry = databaseRegistry;
    private readonly SessionManager _sessionManager = sessionManager;
    private readonly MetricsCollector _metricsCollector = metricsCollector;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    /// <summary>
    /// Gets detailed server health information for monitoring systems.
    /// </summary>
    public ServerHealthInfo GetDetailedHealth()
    {
        // Database status
        var totalDatabases = _databaseRegistry.DatabaseNames.Count;
        var onlineDatabases = 0;
        var dbErrors = new List<string>();

        foreach (var dbName in _databaseRegistry.DatabaseNames)
        {
            try
            {
                var db = _databaseRegistry.GetDatabase(dbName);
                if (db is not null)
                {
                    onlineDatabases++;
                }
                else
                {
                    dbErrors.Add($"Database '{dbName}' not found");
                }
            }
            catch (Exception ex)
            {
                dbErrors.Add($"Database '{dbName}' error: {ex.Message}");
            }
        }

        // Memory metrics
        var memoryMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
        var gcInfo = GC.GetGCMemoryInfo();

        var snapshot = _metricsCollector.GetSnapshot();

        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["databases"] = dbErrors.Count == 0 ? "healthy" : "degraded",
            ["request_errors"] = snapshot.ErrorRatePercent >= 20 ? "degraded" : "healthy",
            ["request_activity"] = snapshot.LastRequestAgeSeconds > 300 ? "stale" : "healthy",
            ["memory"] = memoryMb > 4096 ? "degraded" : "healthy",
        };

        var overallStatus = checks.Values.Any(static s => string.Equals(s, "degraded", StringComparison.OrdinalIgnoreCase))
            ? "degraded"
            : "healthy";

        return new ServerHealthInfo
        {
            Status = overallStatus,
            Timestamp = DateTimeOffset.UtcNow,
            Version = "1.7.0",
            UptimeSeconds = (long)_uptime.Elapsed.TotalSeconds,
            ActiveSessions = _sessionManager.ActiveSessionCount,
            ActiveConnections = snapshot.ActiveConnections,
            MemoryUsageMb = memoryMb,
            HostedDatabases = totalDatabases,
            DatabasesOnline = onlineDatabases,
            DatabaseErrors = dbErrors,
            ErrorRatePercent = snapshot.ErrorRatePercent,
            TotalRequests = snapshot.TotalRequests,
            FailedRequests = snapshot.FailedRequests,
            AverageRequestLatencyMs = snapshot.AverageLatencyMs,
            LastFailureCode = snapshot.LastFailureCode,
            LastFailureTimestamp = snapshot.LastFailureTimestamp,
            Checks = checks,
            Protocols = snapshot.Protocols,
            GarbageCollections = new GarbageCollectionMetrics
            {
                HeapSizeMb = (int)(gcInfo.HeapSizeBytes / (1024 * 1024)),
                TotalMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
            },
        };
    }
}

/// <summary>
/// Detailed server health information for external monitoring.
/// C# 14: Required properties with init accessors.
/// </summary>
public sealed class ServerHealthInfo
{
    public required string Status { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Version { get; init; }
    public required long UptimeSeconds { get; init; }
    public required int ActiveSessions { get; init; }
    public required int ActiveConnections { get; init; }
    public required long MemoryUsageMb { get; init; }
    public required int HostedDatabases { get; init; }
    public required int DatabasesOnline { get; init; }
    public required List<string> DatabaseErrors { get; init; }
    public required double ErrorRatePercent { get; init; }
    public required long TotalRequests { get; init; }
    public required long FailedRequests { get; init; }
    public required double AverageRequestLatencyMs { get; init; }
    public required string LastFailureCode { get; init; }
    public required DateTimeOffset? LastFailureTimestamp { get; init; }
    public required Dictionary<string, string> Checks { get; init; }
    public required Dictionary<string, ProtocolMetricsSnapshot> Protocols { get; init; }
    public required GarbageCollectionMetrics GarbageCollections { get; init; }
}

/// <summary>
/// Garbage collection metrics.
/// C# 14: Required properties.
/// </summary>
public sealed class GarbageCollectionMetrics
{
    public required int HeapSizeMb { get; init; }
    public required long TotalMemoryBytes { get; init; }
}
