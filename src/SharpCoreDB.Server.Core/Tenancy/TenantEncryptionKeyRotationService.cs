// <copyright file="TenantEncryptionKeyRotationService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Orchestrates tenant database encryption key rotation with audit-friendly lifecycle tracking.
/// </summary>
public sealed class TenantEncryptionKeyRotationService(
    DatabaseRegistry databaseRegistry,
    TenantCatalogRepository catalogRepository,
    ITenantEncryptionKeyProvider tenantEncryptionKeyProvider,
    ILogger<TenantEncryptionKeyRotationService> logger)
{
    /// <summary>
    /// Rotates a tenant database encryption key reference.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="databaseName">Database name.</param>
    /// <param name="newKeyReference">New key reference for rotation.</param>
    /// <param name="idempotencyKey">Caller-provided idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rotation operation result.</returns>
    public async Task<TenantEncryptionKeyRotationOperation> RotateTenantDatabaseKeyAsync(
        string tenantId,
        string databaseName,
        string newKeyReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newKeyReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var operation = new TenantEncryptionKeyRotationOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DatabaseName = databaseName,
            PreviousKeyReference = null,
            NewKeyReference = newKeyReference,
            IdempotencyKey = idempotencyKey,
            StartedAt = DateTime.UtcNow,
            Status = TenantEncryptionKeyRotationStatus.InProgress,
        };

        await catalogRepository.RecordLifecycleEventAsync(
            tenantId,
            "EncryptionKeyRotationStarted",
            TenantEventStatus.InProgress,
            $"Starting encryption key rotation for database '{databaseName}'.",
            cancellationToken);

        try
        {
            var mapping = await catalogRepository.GetTenantDatabaseAsync(tenantId, databaseName, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Tenant database mapping not found for tenant '{tenantId}' and database '{databaseName}'.");

            operation.PreviousKeyReference = mapping.EncryptionKeyReference;

            // Resolve key material before touching live registration for rotation safety.
            var newMaterial = await tenantEncryptionKeyProvider.ResolveDatabaseKeyAsync(
                tenantId,
                databaseName,
                newKeyReference,
                cancellationToken);

            var wasRegistered = databaseRegistry.DatabaseExists(databaseName);

            if (wasRegistered)
            {
                await databaseRegistry.UnregisterDatabaseRuntimeAsync(databaseName, cancellationToken: cancellationToken);
            }

            try
            {
                await databaseRegistry.RegisterDatabaseRuntimeAsync(
                    databaseName,
                    mapping.DatabasePath,
                    storageMode: mapping.StorageMode,
                    connectionPoolSize: 50,
                    encryptionEnabled: newMaterial.EncryptionEnabled,
                    encryptionMasterPassword: newMaterial.KeyMaterial,
                    encryptionKeyReference: newMaterial.KeyReference,
                    cancellationToken: cancellationToken);
            }
            catch
            {
                if (wasRegistered)
                {
                    await RestorePreviousDatabaseRegistrationAsync(mapping, cancellationToken);
                    operation.RollbackApplied = true;
                }

                throw;
            }

            await catalogRepository.UpdateTenantDatabaseEncryptionAsync(
                tenantId,
                databaseName,
                newMaterial.EncryptionEnabled,
                newMaterial.KeyReference,
                cancellationToken);

            operation.Status = TenantEncryptionKeyRotationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "EncryptionKeyRotationCompleted",
                TenantEventStatus.Completed,
                $"Encryption key rotation completed for database '{databaseName}'.",
                cancellationToken);

            return operation;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to rotate encryption key for tenant '{TenantId}' database '{DatabaseName}'",
                tenantId,
                databaseName);

            operation.Status = TenantEncryptionKeyRotationStatus.Failed;
            operation.ErrorMessage = ex.Message;
            operation.CompletedAt = DateTime.UtcNow;

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "EncryptionKeyRotationFailed",
                TenantEventStatus.Failed,
                ex.Message,
                cancellationToken);

            return operation;
        }
    }

    private async Task RestorePreviousDatabaseRegistrationAsync(
        TenantDatabaseMapping mapping,
        CancellationToken cancellationToken)
    {
        var oldMaterial = await tenantEncryptionKeyProvider.ResolveDatabaseKeyAsync(
            mapping.TenantId,
            mapping.DatabaseName,
            mapping.EncryptionKeyReference,
            cancellationToken);

        await databaseRegistry.RegisterDatabaseRuntimeAsync(
            mapping.DatabaseName,
            mapping.DatabasePath,
            storageMode: mapping.StorageMode,
            connectionPoolSize: 50,
            encryptionEnabled: oldMaterial.EncryptionEnabled,
            encryptionMasterPassword: oldMaterial.KeyMaterial,
            encryptionKeyReference: oldMaterial.KeyReference,
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Tenant encryption key rotation operation result.
/// </summary>
public sealed class TenantEncryptionKeyRotationOperation
{
    /// <summary>Operation identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>Tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>Database name.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>Previous key reference.</summary>
    public required string? PreviousKeyReference { get; set; }

    /// <summary>Requested new key reference.</summary>
    public required string NewKeyReference { get; init; }

    /// <summary>Idempotency key.</summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>Operation start timestamp.</summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>Operation completion timestamp.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Rotation operation status.</summary>
    public required TenantEncryptionKeyRotationStatus Status { get; set; }

    /// <summary>Indicates rollback was applied after failure.</summary>
    public bool RollbackApplied { get; set; }

    /// <summary>Error details when failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Status for tenant encryption key rotation operation.
/// </summary>
public enum TenantEncryptionKeyRotationStatus
{
    /// <summary>Rotation is running.</summary>
    InProgress,

    /// <summary>Rotation completed successfully.</summary>
    Completed,

    /// <summary>Rotation failed.</summary>
    Failed,
}
