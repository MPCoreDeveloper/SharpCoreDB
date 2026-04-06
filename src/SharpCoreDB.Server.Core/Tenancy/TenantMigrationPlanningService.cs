// <copyright file="TenantMigrationPlanningService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Builds tenant migration plans and execution hooks for moving tenant databases between server instances.
/// </summary>
public sealed class TenantMigrationPlanningService(
    TenantCatalogRepository catalogRepository,
    ILogger<TenantMigrationPlanningService> logger)
{
    /// <summary>
    /// Creates a migration plan for a tenant database.
    /// </summary>
    public async Task<TenantMigrationPlan> CreateMigrationPlanAsync(
        string tenantId,
        string databaseName,
        string targetServer,
        string exportDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetServer);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);

        var mapping = await catalogRepository.GetTenantDatabaseAsync(tenantId, databaseName, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Tenant database mapping not found for tenant '{tenantId}' and database '{databaseName}'.");

        var sizeBytes = File.Exists(mapping.DatabasePath)
            ? new FileInfo(mapping.DatabasePath).Length
            : 0L;
        var exportArtifactPath = Path.Combine(
            exportDirectory,
            $"migration-{tenantId}-{databaseName}-{DateTime.UtcNow:yyyyMMddHHmmss}.bak");

        var plan = new TenantMigrationPlan
        {
            PlanId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DatabaseName = databaseName,
            SourceDatabasePath = mapping.DatabasePath,
            TargetServer = targetServer,
            ExportArtifactPath = exportArtifactPath,
            DatabaseSizeBytes = sizeBytes,
            Steps =
            [
                "Create a tenant-scoped backup artifact from the source server.",
                "Transfer the artifact to the target server using a secure channel.",
                "Restore the artifact on the target server to the tenant database path.",
                "Validate tenant-scoped authentication and isolation on the target server.",
                "Update routing or cutover metadata after validation succeeds."
            ],
            ExecutionHooks = new TenantMigrationExecutionHooks
            {
                ExportHook = $"POST /api/v1/tenants/{tenantId}/databases/{databaseName}/backup with backupDirectory='{exportDirectory}'",
                RestoreHook = $"POST /api/v1/tenants/{tenantId}/databases/{databaseName}/restore on target '{targetServer}' using backupPath='{exportArtifactPath}'",
                ValidationHook = $"Run tenant isolation validation against target '{targetServer}' before traffic cutover."
            },
            CreatedAt = DateTime.UtcNow,
        };

        await catalogRepository.RecordLifecycleEventAsync(
            tenantId,
            "TenantMigrationPlanCreated",
            TenantEventStatus.Completed,
            $"Migration plan created for database '{databaseName}' to target '{targetServer}'.",
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Created tenant migration plan {PlanId} for tenant '{TenantId}' database '{DatabaseName}' target '{TargetServer}'",
            plan.PlanId,
            tenantId,
            databaseName,
            targetServer);

        return plan;
    }
}
