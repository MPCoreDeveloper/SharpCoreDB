// <copyright file="TenantSecurityAudit.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Server.Core.Observability;

/// <summary>
/// Defines security event types for tenant-aware auditing.
/// </summary>
public enum TenantSecurityEventType
{
    /// <summary>Login succeeded.</summary>
    LoginSucceeded,

    /// <summary>Login failed.</summary>
    LoginFailed,

    /// <summary>Database connect succeeded.</summary>
    ConnectSucceeded,

    /// <summary>Database connect denied.</summary>
    ConnectDenied,

    /// <summary>Database grant changed.</summary>
    GrantChanged,

    /// <summary>Tenant provisioning event.</summary>
    Provisioning,

    /// <summary>Access denied by authorization policy.</summary>
    AccessDenied,
}

/// <summary>
/// Tenant-aware security audit event.
/// </summary>
public sealed record TenantSecurityAuditEvent(
    DateTime TimestampUtc,
    TenantSecurityEventType EventType,
    string TenantId,
    string DatabaseName,
    string Principal,
    string Protocol,
    bool IsAllowed,
    string DecisionCode,
    string Reason);

/// <summary>
/// Sink abstraction for exporting tenant security audit events.
/// </summary>
public interface ITenantSecurityAuditSink
{
    /// <summary>
    /// Writes a tenant security audit event to the sink.
    /// </summary>
    /// <param name="auditEvent">Event instance.</param>
    void Write(TenantSecurityAuditEvent auditEvent);
}

/// <summary>
/// In-memory store for tenant security audit events.
/// </summary>
public sealed class TenantSecurityAuditStore
{
    private readonly Lock _eventsLock = new();
    private readonly Queue<TenantSecurityAuditEvent> _events = [];
    private readonly int _maxEvents;

    /// <summary>
    /// Initializes a new <see cref="TenantSecurityAuditStore"/>.
    /// </summary>
    public TenantSecurityAuditStore(int maxEvents = 10_000)
    {
        _maxEvents = maxEvents > 0
            ? maxEvents
            : throw new ArgumentOutOfRangeException(nameof(maxEvents), "Max events must be greater than zero.");
    }

    /// <summary>
    /// Records an audit event.
    /// </summary>
    public void Record(TenantSecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        lock (_eventsLock)
        {
            _events.Enqueue(auditEvent);
            while (_events.Count > _maxEvents)
            {
                _events.Dequeue();
            }
        }
    }

    /// <summary>
    /// Gets recent audit events newest first.
    /// </summary>
    public IReadOnlyList<TenantSecurityAuditEvent> GetRecent(int maxCount = 100)
    {
        var count = Math.Max(maxCount, 1);

        lock (_eventsLock)
        {
            return _events
                .AsEnumerable()
                .Reverse()
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Gets retained event count.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_eventsLock)
            {
                return _events.Count;
            }
        }
    }
}

/// <summary>
/// Emits security audit events to store and optional sinks.
/// </summary>
public sealed class TenantSecurityAuditService(
    TenantSecurityAuditStore store,
    ILogger<TenantSecurityAuditService> logger,
    IEnumerable<ITenantSecurityAuditSink>? sinks = null)
{
    private readonly TenantSecurityAuditStore _store = store;
    private readonly ILogger<TenantSecurityAuditService> _logger = logger;
    private readonly ITenantSecurityAuditSink[] _sinks = sinks?.ToArray() ?? [];

    /// <summary>
    /// Emits a tenant security audit event.
    /// </summary>
    /// <param name="auditEvent">Audit event to emit.</param>
    public void Emit(TenantSecurityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        _store.Record(auditEvent);

        foreach (var sink in _sinks)
        {
            sink.Write(auditEvent);
        }

        _logger.LogInformation(
            "Tenant security event {EventType} tenant={TenantId} db={Database} principal={Principal} protocol={Protocol} allowed={Allowed} code={Code}",
            auditEvent.EventType,
            auditEvent.TenantId,
            auditEvent.DatabaseName,
            auditEvent.Principal,
            auditEvent.Protocol,
            auditEvent.IsAllowed,
            auditEvent.DecisionCode);
    }
}
