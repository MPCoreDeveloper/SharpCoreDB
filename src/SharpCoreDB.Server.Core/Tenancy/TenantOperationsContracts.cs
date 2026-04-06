// <copyright file="TenantOperationsContracts.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Backup operation result for a tenant database.
/// </summary>
public sealed class TenantBackupOperation
{
    /// <summary>Operation identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Database name.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>Backup file path.</summary>
    public required string BackupPath { get; init; }

    /// <summary>Operation start timestamp.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>Operation completion timestamp.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Backup file size in bytes.</summary>
    public long BackupSizeBytes { get; set; }

    /// <summary>Operation status.</summary>
    public required TenantDataOperationStatus Status { get; set; }

    /// <summary>Error details when failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Restore operation result for a tenant database.
/// </summary>
public sealed class TenantRestoreOperation
{
    /// <summary>Operation identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Database name.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>Restore source path.</summary>
    public required string SourceBackupPath { get; init; }

    /// <summary>Restored database path.</summary>
    public required string RestoredDatabasePath { get; init; }

    /// <summary>Operation start timestamp.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>Operation completion timestamp.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Whether validation succeeded before activation.</summary>
    public bool ValidationPassed { get; set; }

    /// <summary>Whether rollback was applied after failure.</summary>
    public bool RollbackApplied { get; set; }

    /// <summary>Operation status.</summary>
    public required TenantDataOperationStatus Status { get; set; }

    /// <summary>Error details when failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Migration planning result for a tenant database.
/// </summary>
public sealed class TenantMigrationPlan
{
    /// <summary>Plan identifier.</summary>
    public required string PlanId { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Database name.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>Source database path.</summary>
    public required string SourceDatabasePath { get; init; }

    /// <summary>Target server identifier or address.</summary>
    public required string TargetServer { get; init; }

    /// <summary>Suggested export artifact path.</summary>
    public required string ExportArtifactPath { get; init; }

    /// <summary>Database size in bytes.</summary>
    public required long DatabaseSizeBytes { get; init; }

    /// <summary>Ordered migration steps.</summary>
    public required string[] Steps { get; init; }

    /// <summary>Generated execution hooks.</summary>
    public required TenantMigrationExecutionHooks ExecutionHooks { get; init; }

    /// <summary>Plan creation timestamp.</summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// Migration execution hooks for operators and automation.
/// </summary>
public sealed class TenantMigrationExecutionHooks
{
    /// <summary>Suggested source export command description.</summary>
    public required string ExportHook { get; init; }

    /// <summary>Suggested target restore command description.</summary>
    public required string RestoreHook { get; init; }

    /// <summary>Suggested validation hook description.</summary>
    public required string ValidationHook { get; init; }
}

/// <summary>
/// Common status for tenant data operations.
/// </summary>
public enum TenantDataOperationStatus
{
    /// <summary>Operation is running.</summary>
    InProgress,

    /// <summary>Operation completed successfully.</summary>
    Completed,

    /// <summary>Operation failed.</summary>
    Failed,
}
