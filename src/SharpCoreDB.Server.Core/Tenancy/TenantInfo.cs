// <copyright file="TenantInfo.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Text.Json;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Represents a tenant in the SaaS platform.
/// Contains core tenant metadata and lifecycle state.
/// </summary>
public sealed record TenantInfo(
    string TenantId,
    string TenantKey,
    string DisplayName,
    TenantStatus Status = TenantStatus.Active,
    string? PlanTier = null,
    DateTime? CreatedAt = null,
    DateTime? UpdatedAt = null,
    string? CreatedBy = null,
    JsonDocument? Metadata = null)
{
    /// <summary>
    /// Creates a new tenant with generated ID and current timestamps.
    /// </summary>
    public static TenantInfo Create(
        string tenantKey,
        string displayName,
        string? planTier = null,
        string? createdBy = null,
        JsonDocument? metadata = null)
    {
        var tenantId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        
        return new TenantInfo(
            TenantId: tenantId,
            TenantKey: tenantKey,
            DisplayName: displayName,
            Status: TenantStatus.Active,
            PlanTier: planTier,
            CreatedAt: now,
            UpdatedAt: now,
            CreatedBy: createdBy,
            Metadata: metadata);
    }
}

/// <summary>
/// Represents the lifecycle status of a tenant.
/// </summary>
public enum TenantStatus
{
    /// <summary>Tenant is active and operational.</summary>
    Active,

    /// <summary>Tenant is suspended (no operations allowed).</summary>
    Suspended,

    /// <summary>Tenant is marked for deletion but data retained.</summary>
    Deleted,

    /// <summary>Tenant is in provisioning state.</summary>
    Provisioning,

    /// <summary>Tenant is being deprovisioned.</summary>
    Deprovisioning
}

/// <summary>
/// Represents a database associated with a tenant.
/// </summary>
public sealed record TenantDatabaseMapping(
    Guid MappingId,
    string TenantId,
    string DatabaseName,
    string DatabasePath,
    bool IsPrimary = true,
    string StorageMode = "SingleFile",
    bool EncryptionEnabled = false,
    string? EncryptionKeyReference = null,
    DateTime? CreatedAt = null)
{
    /// <summary>
    /// Creates a new tenant database mapping.
    /// </summary>
    public static TenantDatabaseMapping Create(
        string tenantId,
        string databaseName,
        string databasePath,
        bool isPrimary = true,
        string storageMode = "SingleFile",
        bool encryptionEnabled = false,
        string? encryptionKeyReference = null)
    {
        return new TenantDatabaseMapping(
            MappingId: Guid.NewGuid(),
            TenantId: tenantId,
            DatabaseName: databaseName,
            DatabasePath: databasePath,
            IsPrimary: isPrimary,
            StorageMode: storageMode,
            EncryptionEnabled: encryptionEnabled,
            EncryptionKeyReference: encryptionKeyReference,
            CreatedAt: DateTime.UtcNow);
    }
}

/// <summary>
/// Represents a lifecycle event for tenant auditing and debugging.
/// </summary>
public sealed record TenantLifecycleEvent(
    Guid EventId,
    string TenantId,
    string EventType,
    TenantEventStatus EventStatus = TenantEventStatus.Completed,
    string? EventDetails = null,
    DateTime? CreatedAt = null)
{
    /// <summary>
    /// Creates a new lifecycle event.
    /// </summary>
    public static TenantLifecycleEvent Create(
        string tenantId,
        string eventType,
        TenantEventStatus status = TenantEventStatus.Completed,
        string? details = null)
    {
        return new TenantLifecycleEvent(
            EventId: Guid.NewGuid(),
            TenantId: tenantId,
            EventType: eventType,
            EventStatus: status,
            EventDetails: details,
            CreatedAt: DateTime.UtcNow);
    }
}

/// <summary>
/// Represents the status of a tenant lifecycle event.
/// </summary>
public enum TenantEventStatus
{
    /// <summary>Event is in progress.</summary>
    InProgress,

    /// <summary>Event completed successfully.</summary>
    Completed,

    /// <summary>Event failed.</summary>
    Failed,

    /// <summary>Event was rolled back.</summary>
    RolledBack
}

/// <summary>
/// Represents tenant quota policy values.
/// </summary>
public sealed record TenantQuotaPolicy(
    string TenantId,
    int MaxActiveSessions,
    int MaxRequestsPerSecond,
    long MaxStorageMb,
    int MaxBatchSize,
    DateTime? UpdatedAt = null)
{
    /// <summary>
    /// Creates a tenant quota policy instance.
    /// </summary>
    public static TenantQuotaPolicy Create(
        string tenantId,
        int maxActiveSessions,
        int maxRequestsPerSecond,
        long maxStorageMb,
        int maxBatchSize)
    {
        return new TenantQuotaPolicy(
            TenantId: tenantId,
            MaxActiveSessions: maxActiveSessions,
            MaxRequestsPerSecond: maxRequestsPerSecond,
            MaxStorageMb: maxStorageMb,
            MaxBatchSize: maxBatchSize,
            UpdatedAt: DateTime.UtcNow);
    }
}
