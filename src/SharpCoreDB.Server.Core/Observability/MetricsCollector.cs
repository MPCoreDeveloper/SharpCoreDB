// <copyright file="MetricsCollector.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Diagnostics.Metrics;
using System.Diagnostics;

namespace SharpCoreDB.Server.Core.Observability;

/// <summary>
/// Centralized metrics collection for SharpCoreDB server.
/// Tracks gRPC performance, database operations, and resource utilization.
/// C# 14: Primary constructor with immutable dependencies.
/// </summary>
public sealed class MetricsCollector : IDisposable
{
    private readonly Meter _meter;
    private readonly UpDownCounter<int> _activeConnections;
    private readonly Counter<long> _totalRequests;
    private readonly Histogram<double> _requestLatencyMs;
    private readonly UpDownCounter<long> _totalBytesReceived;
    private readonly UpDownCounter<long> _totalBytesSent;
    private readonly Counter<long> _failedRequests;
    private readonly UpDownCounter<int> _activeSessions;
    private readonly Histogram<int> _rowsReturned;
    private readonly Counter<long> _tenantAuthorizationAllowed;
    private readonly Counter<long> _tenantAuthorizationDenied;
    private readonly Counter<long> _tenantQuotaThrottles;

    public MetricsCollector(string serviceName = "sharpcoredb-server")
    {
        _meter = new Meter(serviceName, "1.5.0");

        _activeConnections = _meter.CreateUpDownCounter<int>(
            "sharpcoredb.connections.active",
            unit: "{connection}",
            description: "Current number of active gRPC connections");

        _totalRequests = _meter.CreateCounter<long>(
            "sharpcoredb.requests.total",
            unit: "{request}",
            description: "Total number of gRPC requests processed");

        _requestLatencyMs = _meter.CreateHistogram<double>(
            "sharpcoredb.request.latency_ms",
            unit: "ms",
            description: "gRPC request latency in milliseconds");

        _totalBytesReceived = _meter.CreateUpDownCounter<long>(
            "sharpcoredb.network.bytes_received",
            unit: "By",
            description: "Total bytes received from clients");

        _totalBytesSent = _meter.CreateUpDownCounter<long>(
            "sharpcoredb.network.bytes_sent",
            unit: "By",
            description: "Total bytes sent to clients");

        _failedRequests = _meter.CreateCounter<long>(
            "sharpcoredb.requests.failed",
            unit: "{request}",
            description: "Total number of failed gRPC requests");

        _activeSessions = _meter.CreateUpDownCounter<int>(
            "sharpcoredb.sessions.active",
            unit: "{session}",
            description: "Current number of active database sessions");

        _rowsReturned = _meter.CreateHistogram<int>(
            "sharpcoredb.query.rows_returned",
            unit: "{row}",
            description: "Number of rows returned per query");

        _tenantAuthorizationAllowed = _meter.CreateCounter<long>(
            "sharpcoredb.tenant.auth.allowed",
            unit: "{decision}",
            description: "Total number of allowed tenant database authorization decisions");

        _tenantAuthorizationDenied = _meter.CreateCounter<long>(
            "sharpcoredb.tenant.auth.denied",
            unit: "{decision}",
            description: "Total number of denied tenant database authorization decisions");

        _tenantQuotaThrottles = _meter.CreateCounter<long>(
            "sharpcoredb.tenant.quota.throttles",
            unit: "{event}",
            description: "Total tenant quota throttle events");
    }

    /// <summary>
    /// Records a successful gRPC request with latency and payload metrics.
    /// </summary>
    public void RecordSuccessfulRequest(
        string method,
        double latencyMs,
        long bytesReceived,
        long bytesSent,
        int rowsReturned = 0)
    {
        var tags = new TagList { { "method", method } };

        _totalRequests.Add(1, tags);
        _requestLatencyMs.Record(latencyMs, tags);
        _totalBytesReceived.Add(bytesReceived, tags);
        _totalBytesSent.Add(bytesSent, tags);

        if (rowsReturned > 0)
        {
            _rowsReturned.Record(rowsReturned, tags);
        }
    }

    /// <summary>
    /// Records a failed gRPC request.
    /// </summary>
    public void RecordFailedRequest(string method, string? errorCode = null)
    {
        var tags = new TagList
        {
            { "method", method },
            { "error_code", errorCode ?? "unknown" }
        };

        _failedRequests.Add(1, tags);
    }

    /// <summary>
    /// Increments active connection count.
    /// </summary>
    public void IncrementActiveConnections()
    {
        _activeConnections.Add(1);
    }

    /// <summary>
    /// Decrements active connection count.
    /// </summary>
    public void DecrementActiveConnections()
    {
        _activeConnections.Add(-1);
    }

    /// <summary>
    /// Increments active session count.
    /// </summary>
    public void IncrementActiveSessions()
    {
        _activeSessions.Add(1);
    }

    /// <summary>
    /// Decrements active session count.
    /// </summary>
    public void DecrementActiveSessions()
    {
        _activeSessions.Add(-1);
    }

    /// <summary>
    /// Records a tenant authorization decision for observability.
    /// </summary>
    /// <param name="protocol">Protocol name (e.g. gRPC/REST/Binary).</param>
    /// <param name="operation">Operation name.</param>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="isAllowed">Authorization result.</param>
    /// <param name="code">Decision code.</param>
    public void RecordTenantAuthorizationDecision(
        string protocol,
        string operation,
        string databaseName,
        bool isAllowed,
        string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var tags = new TagList
        {
            { "protocol", protocol },
            { "operation", operation },
            { "database", databaseName },
            { "code", code },
        };

        if (isAllowed)
        {
            _tenantAuthorizationAllowed.Add(1, tags);
            return;
        }

        _tenantAuthorizationDenied.Add(1, tags);
    }

    /// <summary>
    /// Records a tenant quota throttle event.
    /// </summary>
    public void RecordTenantQuotaThrottle(
        string tenantId,
        string quotaType,
        string operation,
        string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quotaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var tags = new TagList
        {
            { "tenant_id", tenantId },
            { "quota_type", quotaType },
            { "operation", operation },
            { "code", code },
        };

        _tenantQuotaThrottles.Add(1, tags);
    }

    /// <summary>
    /// Gets the underlying OpenTelemetry Meter for custom instrument creation.
    /// </summary>
    public Meter Meter => _meter;

    /// <inheritdoc />
    public void Dispose()
    {
        _meter?.Dispose();
    }
}
