// <copyright file="MetricsCollector.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

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

    private readonly ConcurrentDictionary<string, ProtocolMetricsSnapshot> _protocolMetrics = new(StringComparer.OrdinalIgnoreCase);

    private long _activeConnectionsCurrent;
    private long _activeSessionsCurrent;
    private long _totalRequestsCount;
    private long _failedRequestsCount;
    private long _totalBytesReceivedCount;
    private long _totalBytesSentCount;
    private long _totalRowsReturnedCount;
    private long _queryRequestsCount;
    private long _nonQueryRequestsCount;
    private long _requestLatencyTotalMicros;
    private long _lastRequestUnixMs;
    private long _lastFailureUnixMs;
    private string _lastFailureCode = "none";

    public MetricsCollector(string serviceName = "sharpcoredb-server")
    {
        _meter = new Meter(serviceName, "1.7.0");

        _activeConnections = _meter.CreateUpDownCounter<int>(
            "sharpcoredb.connections.active",
            unit: "{connection}",
            description: "Current number of active server connections");

        _totalRequests = _meter.CreateCounter<long>(
            "sharpcoredb.requests.total",
            unit: "{request}",
            description: "Total number of processed requests");

        _requestLatencyMs = _meter.CreateHistogram<double>(
            "sharpcoredb.request.latency_ms",
            unit: "ms",
            description: "Request latency in milliseconds");

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
            description: "Total number of failed requests");

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
    /// Records a successful request with latency and payload metrics.
    /// </summary>
    public void RecordSuccessfulRequest(
        string method,
        double latencyMs,
        long bytesReceived,
        long bytesSent,
        int rowsReturned = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var tags = new TagList { { "method", method } };

        _totalRequests.Add(1, tags);
        _requestLatencyMs.Record(latencyMs, tags);
        _totalBytesReceived.Add(bytesReceived, tags);
        _totalBytesSent.Add(bytesSent, tags);

        if (rowsReturned > 0)
        {
            _rowsReturned.Record(rowsReturned, tags);
        }

        Interlocked.Increment(ref _totalRequestsCount);
        Interlocked.Add(ref _totalBytesReceivedCount, bytesReceived);
        Interlocked.Add(ref _totalBytesSentCount, bytesSent);
        Interlocked.Add(ref _totalRowsReturnedCount, rowsReturned);
        Interlocked.Add(ref _requestLatencyTotalMicros, (long)Math.Round(latencyMs * 1000, MidpointRounding.AwayFromZero));
        Interlocked.Exchange(ref _lastRequestUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (IsQueryMethod(method))
        {
            Interlocked.Increment(ref _queryRequestsCount);
        }
        else
        {
            Interlocked.Increment(ref _nonQueryRequestsCount);
        }

        var protocol = ResolveProtocol(method);
        IncrementProtocolCounter(protocol, static snapshot => snapshot with { TotalRequests = snapshot.TotalRequests + 1 });
    }

    /// <summary>
    /// Records a failed request.
    /// </summary>
    public void RecordFailedRequest(string method, string? errorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var normalizedCode = string.IsNullOrWhiteSpace(errorCode) ? "unknown" : errorCode;
        var tags = new TagList
        {
            { "method", method },
            { "error_code", normalizedCode }
        };

        _failedRequests.Add(1, tags);

        Interlocked.Increment(ref _totalRequestsCount);
        Interlocked.Increment(ref _failedRequestsCount);
        Interlocked.Exchange(ref _lastFailureUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref _lastRequestUnixMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Interlocked.Exchange(ref _lastFailureCode, normalizedCode);

        if (IsQueryMethod(method))
        {
            Interlocked.Increment(ref _queryRequestsCount);
        }
        else
        {
            Interlocked.Increment(ref _nonQueryRequestsCount);
        }

        var protocol = ResolveProtocol(method);
        IncrementProtocolCounter(protocol, static snapshot => snapshot with
        {
            TotalRequests = snapshot.TotalRequests + 1,
            FailedRequests = snapshot.FailedRequests + 1,
        });
    }

    /// <summary>
    /// Increments active connection count.
    /// </summary>
    public void IncrementActiveConnections()
    {
        _activeConnections.Add(1);
        Interlocked.Increment(ref _activeConnectionsCurrent);
    }

    /// <summary>
    /// Decrements active connection count.
    /// </summary>
    public void DecrementActiveConnections()
    {
        _activeConnections.Add(-1);
        var decremented = Interlocked.Decrement(ref _activeConnectionsCurrent);
        if (decremented < 0)
        {
            Interlocked.Exchange(ref _activeConnectionsCurrent, 0);
        }
    }

    /// <summary>
    /// Increments active session count.
    /// </summary>
    public void IncrementActiveSessions()
    {
        _activeSessions.Add(1);
        Interlocked.Increment(ref _activeSessionsCurrent);
    }

    /// <summary>
    /// Decrements active session count.
    /// </summary>
    public void DecrementActiveSessions()
    {
        _activeSessions.Add(-1);
        var decremented = Interlocked.Decrement(ref _activeSessionsCurrent);
        if (decremented < 0)
        {
            Interlocked.Exchange(ref _activeSessionsCurrent, 0);
        }
    }

    /// <summary>
    /// Records a protocol-level connection open event.
    /// </summary>
    public void RecordConnectionOpened(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        IncrementActiveConnections();
        IncrementProtocolCounter(protocol, static snapshot => snapshot with
        {
            ActiveConnections = snapshot.ActiveConnections + 1,
            TotalConnections = snapshot.TotalConnections + 1,
        });
    }

    /// <summary>
    /// Records a protocol-level connection close event.
    /// </summary>
    public void RecordConnectionClosed(string protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        DecrementActiveConnections();
        IncrementProtocolCounter(protocol, static snapshot => snapshot with
        {
            ActiveConnections = Math.Max(0, snapshot.ActiveConnections - 1),
        });
    }

    /// <summary>
    /// Records a protocol-level message event.
    /// </summary>
    public void RecordProtocolMessage(string protocol, string messageType, bool isError = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        var errorIncrement = isError ? 1 : 0;
        IncrementProtocolCounter(protocol, snapshot => snapshot with
        {
            TotalMessages = snapshot.TotalMessages + 1,
            ErrorMessages = snapshot.ErrorMessages + errorIncrement,
        });
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
    /// Gets a point-in-time metrics snapshot for diagnostics endpoints.
    /// </summary>
    public MetricsSnapshot GetSnapshot()
    {
        var totalRequests = Interlocked.Read(ref _totalRequestsCount);
        var failedRequests = Interlocked.Read(ref _failedRequestsCount);
        var latencyMicros = Interlocked.Read(ref _requestLatencyTotalMicros);

        Dictionary<string, ProtocolMetricsSnapshot> protocols = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _protocolMetrics)
        {
            protocols[entry.Key] = entry.Value;
        }

        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lastRequestUnixMs = Interlocked.Read(ref _lastRequestUnixMs);
        var requestAgeSeconds = lastRequestUnixMs == 0 ? 0d : Math.Max(0d, (nowUnixMs - lastRequestUnixMs) / 1000d);

        return new MetricsSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            ActiveConnections = (int)Interlocked.Read(ref _activeConnectionsCurrent),
            ActiveSessions = (int)Interlocked.Read(ref _activeSessionsCurrent),
            TotalRequests = totalRequests,
            FailedRequests = failedRequests,
            ErrorRatePercent = totalRequests == 0 ? 0d : Math.Round((double)failedRequests / totalRequests * 100d, 2),
            AverageLatencyMs = totalRequests == 0 ? 0d : Math.Round(latencyMicros / 1000d / totalRequests, 2),
            TotalBytesReceived = Interlocked.Read(ref _totalBytesReceivedCount),
            TotalBytesSent = Interlocked.Read(ref _totalBytesSentCount),
            TotalRowsReturned = Interlocked.Read(ref _totalRowsReturnedCount),
            QueryRequests = Interlocked.Read(ref _queryRequestsCount),
            NonQueryRequests = Interlocked.Read(ref _nonQueryRequestsCount),
            LastFailureCode = Interlocked.CompareExchange(ref _lastFailureCode, "none", "none"),
            LastFailureTimestamp = UnixMillisecondsToTimestamp(Interlocked.Read(ref _lastFailureUnixMs)),
            LastRequestAgeSeconds = requestAgeSeconds,
            Protocols = protocols,
        };
    }

    /// <summary>
    /// Gets the underlying OpenTelemetry Meter for custom instrument creation.
    /// </summary>
    public Meter Meter => _meter;

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
    }

    private static string ResolveProtocol(string method)
    {
        if (method.StartsWith("REST/", StringComparison.OrdinalIgnoreCase))
        {
            return "rest";
        }

        if (method.StartsWith("binary/", StringComparison.OrdinalIgnoreCase))
        {
            return "binary";
        }

        if (method.Contains("DatabaseService", StringComparison.OrdinalIgnoreCase)
            || method.StartsWith("/", StringComparison.OrdinalIgnoreCase))
        {
            return "grpc";
        }

        return "other";
    }

    private static bool IsQueryMethod(string method)
    {
        return method.Contains("query", StringComparison.OrdinalIgnoreCase)
            || method.Contains("search", StringComparison.OrdinalIgnoreCase)
            || method.Contains("describe", StringComparison.OrdinalIgnoreCase)
            || method.Contains("schema", StringComparison.OrdinalIgnoreCase)
            || method.Contains("health", StringComparison.OrdinalIgnoreCase)
            || method.Contains("ping", StringComparison.OrdinalIgnoreCase);
    }

    private void IncrementProtocolCounter(string protocol, Func<ProtocolMetricsSnapshot, ProtocolMetricsSnapshot> update)
    {
        _protocolMetrics.AddOrUpdate(
            protocol,
            _ => update(new ProtocolMetricsSnapshot()),
            (_, current) => update(current));
    }

    private static DateTimeOffset? UnixMillisecondsToTimestamp(long unixMs)
    {
        if (unixMs <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
    }
}

/// <summary>
/// Point-in-time diagnostics snapshot exposed by the server observability endpoints.
/// </summary>
public sealed class MetricsSnapshot
{
    public required DateTimeOffset Timestamp { get; init; }
    public required int ActiveConnections { get; init; }
    public required int ActiveSessions { get; init; }
    public required long TotalRequests { get; init; }
    public required long FailedRequests { get; init; }
    public required double ErrorRatePercent { get; init; }
    public required double AverageLatencyMs { get; init; }
    public required long TotalBytesReceived { get; init; }
    public required long TotalBytesSent { get; init; }
    public required long TotalRowsReturned { get; init; }
    public required long QueryRequests { get; init; }
    public required long NonQueryRequests { get; init; }
    public required string LastFailureCode { get; init; }
    public required DateTimeOffset? LastFailureTimestamp { get; init; }
    public required double LastRequestAgeSeconds { get; init; }
    public required Dictionary<string, ProtocolMetricsSnapshot> Protocols { get; init; }
}

/// <summary>
/// Per-protocol activity counters used by metrics and health payloads.
/// </summary>
public readonly record struct ProtocolMetricsSnapshot(
    int ActiveConnections = 0,
    long TotalConnections = 0,
    long TotalRequests = 0,
    long FailedRequests = 0,
    long TotalMessages = 0,
    long ErrorMessages = 0);
