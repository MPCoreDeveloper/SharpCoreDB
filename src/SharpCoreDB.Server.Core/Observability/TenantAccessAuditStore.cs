// <copyright file="TenantAccessAuditStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core.Observability;

/// <summary>
/// In-memory audit store for tenant/database authorization decisions.
/// Keeps the latest events for operational diagnostics without external dependencies.
/// </summary>
public sealed class TenantAccessAuditStore
{
    private readonly Lock _eventsLock = new();
    private readonly Queue<TenantAccessAuditEvent> _events = [];
    private readonly int _maxEvents;

    /// <summary>
    /// Creates a new audit store.
    /// </summary>
    /// <param name="maxEvents">Maximum number of retained events.</param>
    public TenantAccessAuditStore(int maxEvents = 5000)
    {
        _maxEvents = maxEvents > 0
            ? maxEvents
            : throw new ArgumentOutOfRangeException(nameof(maxEvents), "Max events must be greater than zero.");
    }

    /// <summary>
    /// Records an authorization decision as an audit event.
    /// </summary>
    /// <param name="auditEvent">The event to persist.</param>
    public void Record(TenantAccessAuditEvent auditEvent)
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
    /// Returns the most recent tenant access audit events.
    /// </summary>
    /// <param name="maxCount">Maximum number of events to return.</param>
    /// <param name="deniedOnly">When true, only denied decisions are returned.</param>
    /// <returns>Newest-first list of audit events.</returns>
    public IReadOnlyList<TenantAccessAuditEvent> GetRecent(int maxCount = 100, bool deniedOnly = false)
    {
        var count = Math.Max(maxCount, 1);

        lock (_eventsLock)
        {
            var query = _events.AsEnumerable().Reverse();
            if (deniedOnly)
            {
                query = query.Where(static e => !e.IsAllowed);
            }

            return query.Take(count).ToList();
        }
    }

    /// <summary>
    /// Gets total retained event count.
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
/// Tenant access authorization audit event.
/// </summary>
/// <param name="TimestampUtc">Event timestamp in UTC.</param>
/// <param name="IsAllowed">Whether the request was authorized.</param>
/// <param name="Code">Decision code.</param>
/// <param name="Reason">Decision reason.</param>
/// <param name="Username">User identifier.</param>
/// <param name="TenantId">Tenant identifier.</param>
/// <param name="DatabaseName">Requested database name.</param>
/// <param name="Protocol">Protocol name (gRPC, REST, Binary, ...).</param>
/// <param name="Operation">Operation identifier.</param>
public sealed record TenantAccessAuditEvent(
    DateTime TimestampUtc,
    bool IsAllowed,
    string Code,
    string Reason,
    string Username,
    string TenantId,
    string DatabaseName,
    string Protocol,
    string Operation);
