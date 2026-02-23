// <copyright file="ReplicationState.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Tracks the replication state for a master-slave relationship.
/// C# 14: Primary constructors, collection expressions, modern patterns.
/// </summary>
public sealed class ReplicationState(string masterNodeId, string replicaNodeId)
{
    private readonly Lock _lock = new();

    /// <summary>Gets the master node identifier.</summary>
    public string MasterNodeId { get; } = masterNodeId ?? throw new ArgumentNullException(nameof(masterNodeId));

    /// <summary>Gets the replica node identifier.</summary>
    public string ReplicaNodeId { get; } = replicaNodeId ?? throw new ArgumentNullException(nameof(replicaNodeId));

    /// <summary>Gets or sets the current replication state.</summary>
    public ReplicationProtocol.ReplicationState State { get; set; } = ReplicationProtocol.ReplicationState.Starting;

    /// <summary>Gets or sets the last WAL position sent to the replica.</summary>
    public long LastSentWalPosition { get; set; }

    /// <summary>Gets or sets the last WAL position acknowledged by the replica.</summary>
    public long LastAcknowledgedWalPosition { get; set; }

    /// <summary>Gets or sets the last WAL position applied by the replica.</summary>
    public long LastAppliedWalPosition { get; set; }

    /// <summary>Gets or sets the timestamp of the last successful communication.</summary>
    public DateTimeOffset LastCommunication { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the timestamp when replication started.</summary>
    public DateTimeOffset ReplicationStartTime { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the current replication lag in bytes.</summary>
    public long ReplicationLagBytes { get; set; }

    /// <summary>Gets or sets the current replication lag in time.</summary>
    public TimeSpan ReplicationLagTime { get; set; }

    /// <summary>Gets or sets the number of WAL entries pending replication.</summary>
    public int PendingWalEntries { get; set; }

    /// <summary>Gets or sets the total number of WAL entries sent.</summary>
    public long TotalWalEntriesSent { get; set; }

    /// <summary>Gets or sets the total number of WAL entries acknowledged.</summary>
    public long TotalWalEntriesAcknowledged { get; set; }

    /// <summary>Gets or sets the total number of bytes sent.</summary>
    public long TotalBytesSent { get; set; }

    /// <summary>Gets or sets the total number of bytes acknowledged.</summary>
    public long TotalBytesAcknowledged { get; set; }

    /// <summary>Gets or sets the number of consecutive communication failures.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Gets or sets the last error message, if any.</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the timestamp of the last error.</summary>
    public DateTimeOffset? LastErrorTime { get; set; }

    /// <summary>Gets whether the replica is currently in sync.</summary>
    public bool IsInSync => State == ReplicationProtocol.ReplicationState.Streaming &&
                           ReplicationLagTime < TimeSpan.FromSeconds(5) &&
                           ConsecutiveFailures == 0;

    /// <summary>Gets whether the replica is catching up.</summary>
    public bool IsCatchingUp => State == ReplicationProtocol.ReplicationState.CatchingUp;

    /// <summary>Gets whether replication is active.</summary>
    public bool IsActive => State is ReplicationProtocol.ReplicationState.CatchingUp or
                                   ReplicationProtocol.ReplicationState.Streaming;

    /// <summary>Gets whether replication has failed.</summary>
    public bool HasFailed => State == ReplicationProtocol.ReplicationState.Error ||
                            ConsecutiveFailures > 5;

    /// <summary>
    /// Updates the replication state with new position information.
    /// </summary>
    /// <param name="sentPosition">The last sent WAL position.</param>
    /// <param name="acknowledgedPosition">The last acknowledged WAL position.</param>
    /// <param name="appliedPosition">The last applied WAL position.</param>
    public void UpdatePositions(long sentPosition, long acknowledgedPosition, long appliedPosition)
    {
        lock (_lock)
        {
            LastSentWalPosition = sentPosition;
            LastAcknowledgedWalPosition = acknowledgedPosition;
            LastAppliedWalPosition = appliedPosition;
            LastCommunication = DateTimeOffset.UtcNow;

            // Reset consecutive failures on successful communication
            ConsecutiveFailures = 0;
            LastError = null;
            LastErrorTime = null;

            // Update lag calculations
            ReplicationLagBytes = Math.Max(0, sentPosition - appliedPosition);
            ReplicationLagTime = TimeSpan.FromTicks((long)(ReplicationLagBytes * 0.001)); // Estimate 1KB per millisecond
        }
    }

    /// <summary>
    /// Records a successful WAL entry transmission.
    /// </summary>
    /// <param name="walPosition">The WAL position of the entry.</param>
    /// <param name="entrySize">The size of the WAL entry in bytes.</param>
    public void RecordWalEntrySent(long walPosition, int entrySize)
    {
        lock (_lock)
        {
            LastSentWalPosition = walPosition;
            TotalWalEntriesSent++;
            TotalBytesSent += entrySize;
            PendingWalEntries++;
        }
    }

    /// <summary>
    /// Records acknowledgment of WAL entries.
    /// </summary>
    /// <param name="acknowledgedPosition">The acknowledged WAL position.</param>
    /// <param name="entriesAcknowledged">The number of entries acknowledged.</param>
    public void RecordAcknowledgment(long acknowledgedPosition, int entriesAcknowledged)
    {
        lock (_lock)
        {
            LastAcknowledgedWalPosition = acknowledgedPosition;
            TotalWalEntriesAcknowledged += entriesAcknowledged;
            TotalBytesAcknowledged += (acknowledgedPosition - LastAcknowledgedWalPosition); // Estimate
            PendingWalEntries = Math.Max(0, PendingWalEntries - entriesAcknowledged);
            LastCommunication = DateTimeOffset.UtcNow;

            ConsecutiveFailures = 0;
            LastError = null;
            LastErrorTime = null;
        }
    }

    /// <summary>
    /// Records a communication failure.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public void RecordFailure(string errorMessage)
    {
        lock (_lock)
        {
            ConsecutiveFailures++;
            LastError = errorMessage;
            LastErrorTime = DateTimeOffset.UtcNow;

            // If too many failures, mark as error state
            if (ConsecutiveFailures > 5)
            {
                State = ReplicationProtocol.ReplicationState.Error;
            }
        }
    }

    /// <summary>
    /// Changes the replication state.
    /// </summary>
    /// <param name="newState">The new replication state.</param>
    /// <param name="reason">Optional reason for the state change.</param>
    public void ChangeState(ReplicationProtocol.ReplicationState newState, string? reason = null)
    {
        lock (_lock)
        {
            var oldState = State;
            State = newState;

            if (newState == ReplicationProtocol.ReplicationState.Starting)
            {
                ReplicationStartTime = DateTimeOffset.UtcNow;
                ConsecutiveFailures = 0;
                LastError = null;
                LastErrorTime = null;
            }

            // Log state change if logger is available
            // _logger?.LogInformation("Replication state changed: {OldState} -> {NewState} for replica {ReplicaNodeId}. Reason: {Reason}",
            //     oldState, newState, ReplicaNodeId, reason ?? "None");
        }
    }

    /// <summary>
    /// Gets a snapshot of the current replication statistics.
    /// </summary>
    /// <returns>A snapshot of replication statistics.</returns>
    public ReplicationStatistics GetStatistics()
    {
        lock (_lock)
        {
            var uptime = DateTimeOffset.UtcNow - ReplicationStartTime;

            return new ReplicationStatistics
            {
                MasterNodeId = MasterNodeId,
                ReplicaNodeId = ReplicaNodeId,
                State = State,
                Uptime = uptime,
                LastSentWalPosition = LastSentWalPosition,
                LastAcknowledgedWalPosition = LastAcknowledgedWalPosition,
                LastAppliedWalPosition = LastAppliedWalPosition,
                ReplicationLagBytes = ReplicationLagBytes,
                ReplicationLagTime = ReplicationLagTime,
                PendingWalEntries = PendingWalEntries,
                TotalWalEntriesSent = TotalWalEntriesSent,
                TotalWalEntriesAcknowledged = TotalWalEntriesAcknowledged,
                TotalBytesSent = TotalBytesSent,
                TotalBytesAcknowledged = TotalBytesAcknowledged,
                ConsecutiveFailures = ConsecutiveFailures,
                LastError = LastError,
                LastErrorTime = LastErrorTime,
                LastCommunication = LastCommunication,
                IsInSync = IsInSync,
                IsCatchingUp = IsCatchingUp,
                IsActive = IsActive,
                HasFailed = HasFailed
            };
        }
    }

    /// <summary>
    /// Resets the replication state for a fresh start.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            State = ReplicationProtocol.ReplicationState.Starting;
            LastSentWalPosition = 0;
            LastAcknowledgedWalPosition = 0;
            LastAppliedWalPosition = 0;
            ReplicationLagBytes = 0;
            ReplicationLagTime = TimeSpan.Zero;
            PendingWalEntries = 0;
            TotalWalEntriesSent = 0;
            TotalWalEntriesAcknowledged = 0;
            TotalBytesSent = 0;
            TotalBytesAcknowledged = 0;
            ConsecutiveFailures = 0;
            LastError = null;
            LastErrorTime = null;
            ReplicationStartTime = DateTimeOffset.UtcNow;
            LastCommunication = DateTimeOffset.UtcNow;
        }
    }
}

/// <summary>
/// Snapshot of replication statistics.
/// </summary>
public class ReplicationStatistics
{
    /// <summary>Gets the master node identifier.</summary>
    public string MasterNodeId { get; init; } = string.Empty;

    /// <summary>Gets the replica node identifier.</summary>
    public string ReplicaNodeId { get; init; } = string.Empty;

    /// <summary>Gets the current replication state.</summary>
    public ReplicationProtocol.ReplicationState State { get; init; }

    /// <summary>Gets the replication uptime.</summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>Gets the last sent WAL position.</summary>
    public long LastSentWalPosition { get; init; }

    /// <summary>Gets the last acknowledged WAL position.</summary>
    public long LastAcknowledgedWalPosition { get; init; }

    /// <summary>Gets the last applied WAL position.</summary>
    public long LastAppliedWalPosition { get; init; }

    /// <summary>Gets the replication lag in bytes.</summary>
    public long ReplicationLagBytes { get; init; }

    /// <summary>Gets the replication lag in time.</summary>
    public TimeSpan ReplicationLagTime { get; init; }

    /// <summary>Gets the number of pending WAL entries.</summary>
    public int PendingWalEntries { get; init; }

    /// <summary>Gets the total number of WAL entries sent.</summary>
    public long TotalWalEntriesSent { get; init; }

    /// <summary>Gets the total number of WAL entries acknowledged.</summary>
    public long TotalWalEntriesAcknowledged { get; init; }

    /// <summary>Gets the total number of bytes sent.</summary>
    public long TotalBytesSent { get; init; }

    /// <summary>Gets the total number of bytes acknowledged.</summary>
    public long TotalBytesAcknowledged { get; init; }

    /// <summary>Gets the number of consecutive failures.</summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>Gets the last error message.</summary>
    public string? LastError { get; init; }

    /// <summary>Gets the timestamp of the last error.</summary>
    public DateTimeOffset? LastErrorTime { get; init; }

    /// <summary>Gets the timestamp of the last communication.</summary>
    public DateTimeOffset LastCommunication { get; init; }

    /// <summary>Gets whether the replica is in sync.</summary>
    public bool IsInSync { get; init; }

    /// <summary>Gets whether the replica is catching up.</summary>
    public bool IsCatchingUp { get; init; }

    /// <summary>Gets whether replication is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets whether replication has failed.</summary>
    public bool HasFailed { get; init; }

    /// <summary>
    /// Gets the replication health status.
    /// </summary>
    public ReplicationHealthStatus HealthStatus => this switch
    {
        { HasFailed: true } => ReplicationHealthStatus.Failed,
        { IsInSync: true } => ReplicationHealthStatus.Healthy,
        { IsCatchingUp: true } => ReplicationHealthStatus.CatchingUp,
        { IsActive: true } => ReplicationHealthStatus.Lagging,
        _ => ReplicationHealthStatus.Unknown
    };

    /// <summary>
    /// Gets a human-readable status description.
    /// </summary>
    public string StatusDescription => HealthStatus switch
    {
        ReplicationHealthStatus.Healthy => "Replication is healthy and in sync",
        ReplicationHealthStatus.CatchingUp => "Replica is catching up with master",
        ReplicationHealthStatus.Lagging => $"Replica is lagging by {ReplicationLagTime.TotalSeconds:F1}s",
        ReplicationHealthStatus.Failed => $"Replication failed: {LastError ?? "Unknown error"}",
        _ => "Replication status unknown"
    };
}

/// <summary>
/// Replication health status enumeration.
/// </summary>
public enum ReplicationHealthStatus
{
    /// <summary>Replication is healthy and in sync.</summary>
    Healthy,

    /// <summary>Replica is catching up with master.</summary>
    CatchingUp,

    /// <summary>Replica is lagging behind master.</summary>
    Lagging,

    /// <summary>Replication has failed.</summary>
    Failed,

    /// <summary>Replication status is unknown.</summary>
    Unknown
}
