// <copyright file="TenantProvisioningService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using System.Collections.Concurrent;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Orchestrates tenant provisioning and deprovisioning workflows.
/// Handles idempotency, rollback, and lifecycle tracking for multi-database tenants.
/// C# 14: Uses primary constructor and modern patterns.
/// </summary>
public sealed class TenantProvisioningService(
    DatabaseRegistry databaseRegistry,
    TenantCatalogRepository catalogRepository,
    ITenantEncryptionKeyProvider tenantEncryptionKeyProvider,
    TenantSecurityAuditService securityAuditService,
    ILogger<TenantProvisioningService> logger) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ProvisioningOperation> _activeOperations = new();
    private readonly ConcurrentDictionary<string, ProvisioningOperation> _operationHistory = new();
    private readonly ConcurrentDictionary<string, string> _idempotencyOperationIds = new();
    private readonly Lock _orchestrationLock = new();
    private const int MaxOperationHistoryEntries = 1_000;

    /// <summary>
    /// Represents the result of a provisioning operation with tracking and rollback capability.
    /// </summary>
    public sealed class ProvisioningOperation
    {
        public required string OperationId { get; init; }
        public string TenantId { get; set; } = string.Empty;
        public required string IdempotencyKey { get; init; }
        public required ProvisioningOperationType OperationType { get; init; }
        public required DateTime StartedAt { get; init; }
        public DateTime? CompletedAt { get; set; }
        public required OperationStatus Status { get; set; }
        public List<string> CreatedResources { get; init; } = [];
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Type of provisioning operation.
    /// </summary>
    public enum ProvisioningOperationType
    {
        /// <summary>Create new tenant and database.</summary>
        Create,

        /// <summary>Delete tenant and database.</summary>
        Delete,

        /// <summary>Add replica database to tenant.</summary>
        AddReplica
    }

    /// <summary>
    /// Status of a provisioning operation.
    /// </summary>
    public enum OperationStatus
    {
        /// <summary>Operation is in progress.</summary>
        InProgress,

        /// <summary>Operation completed successfully.</summary>
        Completed,

        /// <summary>Operation failed and was rolled back.</summary>
        Failed,

        /// <summary>Operation timed out.</summary>
        TimedOut
    }

    /// <summary>
    /// Creates a new tenant with a primary database.
    /// Supports idempotency - subsequent calls with same idempotency key return cached result.
    /// </summary>
    /// <param name="tenantKey">Unique tenant key.</param>
    /// <param name="displayName">Display name for tenant.</param>
    /// <param name="databasePath">Physical path for primary database file.</param>
    /// <param name="idempotencyKey">Idempotency key for retries (e.g., request ID).</param>
    /// <param name="planTier">Optional plan tier for the tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created tenant and its operation result.</returns>
    public async Task<(TenantInfo Tenant, ProvisioningOperation Operation)> CreateTenantAsync(
        string tenantKey,
        string displayName,
        string databasePath,
        string idempotencyKey,
        string? planTier = null,
        string? encryptionKeyReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        logger.LogInformation(
            "Provisioning new tenant '{TenantKey}' (idempotency: {IdempotencyKey})",
            tenantKey, idempotencyKey);

        securityAuditService.Emit(new TenantSecurityAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            EventType: TenantSecurityEventType.Provisioning,
            TenantId: "pending",
            DatabaseName: "pending",
            Principal: tenantKey,
            Protocol: "Provisioning",
            IsAllowed: true,
            DecisionCode: "PROVISIONING_STARTED",
            Reason: "Create tenant flow started"));

        if (TryGetIdempotentOperation(ProvisioningOperationType.Create, idempotencyKey, out var existingOperation))
        {
            var existingTenant = await ResolveTenantForOperationAsync(existingOperation, tenantKey, cancellationToken);
            if (existingTenant is not null)
            {
                logger.LogInformation(
                    "Returning cached create operation '{OperationId}' for tenant '{TenantKey}'",
                    existingOperation.OperationId,
                    tenantKey);

                return (existingTenant, existingOperation);
            }

            throw new InvalidOperationException(
                $"Idempotency key '{idempotencyKey}' was already used for operation '{existingOperation.OperationId}'.");
        }

        var operationId = Guid.NewGuid().ToString("N");
        var operation = new ProvisioningOperation
        {
            OperationId = operationId,
            TenantId = "", // Will be populated after tenant creation
            IdempotencyKey = idempotencyKey,
            OperationType = ProvisioningOperationType.Create,
            StartedAt = DateTime.UtcNow,
            Status = OperationStatus.InProgress
        };

        try
        {
            // Check for duplicate idempotent operation
            var existingTenant = await catalogRepository.GetTenantByKeyAsync(tenantKey, cancellationToken);
            if (existingTenant != null)
            {
                logger.LogInformation(
                    "Tenant '{TenantKey}' already exists (idempotent), returning existing tenant",
                    tenantKey);

                operation.TenantId = existingTenant.TenantId;
                operation.Status = OperationStatus.Completed;
                operation.CompletedAt = DateTime.UtcNow;
                return (existingTenant, operation);
            }

            lock (_orchestrationLock)
            {
                TrackActiveOperation(operation);
                _idempotencyOperationIds[BuildIdempotencyLookupKey(ProvisioningOperationType.Create, idempotencyKey)] = operationId;
            }

            // Create tenant in catalog
            var tenant = await catalogRepository.CreateTenantAsync(
                tenantKey,
                displayName,
                planTier,
                createdBy: "system",
                cancellationToken: cancellationToken);

            operation.TenantId = tenant.TenantId;

            // Generate database name from tenant key
            var databaseName = GenerateDatabaseName(tenantKey);

            var encryptionMaterial = await tenantEncryptionKeyProvider.ResolveDatabaseKeyAsync(
                tenant.TenantId,
                databaseName,
                encryptionKeyReference,
                cancellationToken);

            // Register database at runtime
            _ = await databaseRegistry.RegisterDatabaseRuntimeAsync(
                databaseName,
                databasePath,
                storageMode: "SingleFile",
                connectionPoolSize: 50,
                encryptionEnabled: encryptionMaterial.EncryptionEnabled,
                encryptionMasterPassword: encryptionMaterial.KeyMaterial,
                encryptionKeyReference: encryptionMaterial.KeyReference,
                cancellationToken: cancellationToken);

            operation.CreatedResources.Add(databaseName);

            // Register database mapping in catalog
            await catalogRepository.RegisterTenantDatabaseAsync(
                tenant.TenantId,
                databaseName,
                databasePath,
                isPrimary: true,
                storageMode: "SingleFile",
                encryptionEnabled: encryptionMaterial.EncryptionEnabled,
                encryptionKeyReference: encryptionMaterial.KeyReference,
                cancellationToken: cancellationToken);

            // Record lifecycle event
            await catalogRepository.RecordLifecycleEventAsync(
                tenant.TenantId,
                "ProvisioningCompleted",
                TenantEventStatus.Completed,
                $"Tenant and database '{databaseName}' provisioned successfully",
                cancellationToken: cancellationToken);

            operation.Status = OperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            logger.LogInformation(
                "Tenant '{TenantKey}' (ID: {TenantId}) provisioned successfully",
                tenantKey, tenant.TenantId);

            securityAuditService.Emit(new TenantSecurityAuditEvent(
                TimestampUtc: DateTime.UtcNow,
                EventType: TenantSecurityEventType.Provisioning,
                TenantId: tenant.TenantId,
                DatabaseName: databaseName,
                Principal: tenantKey,
                Protocol: "Provisioning",
                IsAllowed: true,
                DecisionCode: "PROVISIONING_COMPLETED",
                Reason: "Tenant provisioning completed"));

            return (tenant, operation);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to provision tenant '{TenantKey}'", tenantKey);

            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = ex.Message;
            operation.CompletedAt = DateTime.UtcNow;

            securityAuditService.Emit(new TenantSecurityAuditEvent(
                TimestampUtc: DateTime.UtcNow,
                EventType: TenantSecurityEventType.Provisioning,
                TenantId: string.IsNullOrWhiteSpace(operation.TenantId) ? "pending" : operation.TenantId,
                DatabaseName: operation.CreatedResources.FirstOrDefault() ?? "pending",
                Principal: tenantKey,
                Protocol: "Provisioning",
                IsAllowed: false,
                DecisionCode: "PROVISIONING_FAILED",
                Reason: ex.Message));

            // Rollback created resources
            await RollbackProvisioningAsync(operation, cancellationToken);

            throw;
        }
        finally
        {
            lock (_orchestrationLock)
            {
                _activeOperations.TryRemove(operationId, out _);
                TrackOperationHistory(operation);
            }
        }
    }

    /// <summary>
    /// Deletes a tenant and its associated databases.
    /// Supports idempotency - subsequent calls with same idempotency key return cached result.
    /// </summary>
    /// <param name="tenantId">Tenant ID to delete.</param>
    /// <param name="idempotencyKey">Idempotency key for retries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deprovisioning operation result.</returns>
    public async Task<ProvisioningOperation> DeleteTenantAsync(
        string tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        logger.LogInformation(
            "Deprovisioning tenant '{TenantId}' (idempotency: {IdempotencyKey})",
            tenantId, idempotencyKey);

        securityAuditService.Emit(new TenantSecurityAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            EventType: TenantSecurityEventType.Provisioning,
            TenantId: tenantId,
            DatabaseName: "*",
            Principal: tenantId,
            Protocol: "Provisioning",
            IsAllowed: true,
            DecisionCode: "DEPROVISIONING_STARTED",
            Reason: "Delete tenant flow started"));

        if (TryGetIdempotentOperation(ProvisioningOperationType.Delete, idempotencyKey, out var existingOperation))
        {
            logger.LogInformation(
                "Returning cached delete operation '{OperationId}' for tenant '{TenantId}'",
                existingOperation.OperationId,
                tenantId);
            return existingOperation;
        }

        var operationId = Guid.NewGuid().ToString("N");
        var operation = new ProvisioningOperation
        {
            OperationId = operationId,
            TenantId = tenantId,
            IdempotencyKey = idempotencyKey,
            OperationType = ProvisioningOperationType.Delete,
            StartedAt = DateTime.UtcNow,
            Status = OperationStatus.InProgress
        };

        try
        {
            // Check tenant exists
            var tenant = await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);
            if (tenant == null)
            {
                logger.LogInformation("Tenant '{TenantId}' not found, skipping delete", tenantId);
                operation.Status = OperationStatus.Completed;
                operation.CompletedAt = DateTime.UtcNow;
                return operation;
            }

            lock (_orchestrationLock)
            {
                TrackActiveOperation(operation);
                _idempotencyOperationIds[BuildIdempotencyLookupKey(ProvisioningOperationType.Delete, idempotencyKey)] = operationId;
            }

            // Get all databases for tenant
            var databases = await catalogRepository.GetTenantDatabasesAsync(tenantId, cancellationToken);

            // Unregister all databases
            foreach (var db in databases)
            {
                try
                {
                    logger.LogInformation("Unregistering database '{DatabaseName}' for tenant", db.DatabaseName);
                    await databaseRegistry.UnregisterDatabaseRuntimeAsync(
                        db.DatabaseName,
                        gracefulTimeoutSeconds: 30,
                        cancellationToken: cancellationToken);

                    operation.CreatedResources.Add($"Unregistered: {db.DatabaseName}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to unregister database '{DatabaseName}'", db.DatabaseName);
                    throw;
                }
            }

            // Update tenant status to deleted
            await catalogRepository.UpdateTenantAsync(
                tenantId,
                TenantStatus.Deleted,
                cancellationToken: cancellationToken);

            // Record lifecycle event
            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "DeprovisioningCompleted",
                TenantEventStatus.Completed,
                "Tenant and all databases deprovisioned successfully",
                cancellationToken: cancellationToken);

            operation.Status = OperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            logger.LogInformation("Tenant '{TenantId}' deprovisioned successfully", tenantId);

            securityAuditService.Emit(new TenantSecurityAuditEvent(
                TimestampUtc: DateTime.UtcNow,
                EventType: TenantSecurityEventType.Provisioning,
                TenantId: tenantId,
                DatabaseName: "*",
                Principal: tenantId,
                Protocol: "Provisioning",
                IsAllowed: true,
                DecisionCode: "DEPROVISIONING_COMPLETED",
                Reason: "Tenant deprovisioning completed"));

            return operation;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deprovision tenant '{TenantId}'", tenantId);

            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = ex.Message;
            operation.CompletedAt = DateTime.UtcNow;

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "DeprovisioningFailed",
                TenantEventStatus.Failed,
                ex.Message,
                cancellationToken: cancellationToken);

            securityAuditService.Emit(new TenantSecurityAuditEvent(
                TimestampUtc: DateTime.UtcNow,
                EventType: TenantSecurityEventType.Provisioning,
                TenantId: tenantId,
                DatabaseName: "*",
                Principal: tenantId,
                Protocol: "Provisioning",
                IsAllowed: false,
                DecisionCode: "DEPROVISIONING_FAILED",
                Reason: ex.Message));

            throw;
        }
        finally
        {
            lock (_orchestrationLock)
            {
                _activeOperations.TryRemove(operationId, out _);
                TrackOperationHistory(operation);
            }
        }
    }

    /// <summary>
    /// Gets the status of a provisioning operation.
    /// </summary>
    /// <param name="operationId">Operation ID.</param>
    /// <returns>Operation details or null if not found.</returns>
    public ProvisioningOperation? GetOperationStatus(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        lock (_orchestrationLock)
        {
            if (_activeOperations.TryGetValue(operationId, out var activeOperation))
            {
                return activeOperation;
            }

            _operationHistory.TryGetValue(operationId, out var completedOperation);
            return completedOperation;
        }
    }

    /// <summary>
    /// Gets the status of a provisioning operation via tenant idempotency key.
    /// </summary>
    /// <param name="operationType">Provisioning operation type.</param>
    /// <param name="idempotencyKey">Idempotency key used by the request.</param>
    /// <returns>Operation details or null when no operation matches the key.</returns>
    public ProvisioningOperation? GetOperationStatusByIdempotencyKey(
        ProvisioningOperationType operationType,
        string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        lock (_orchestrationLock)
        {
            if (!_idempotencyOperationIds.TryGetValue(BuildIdempotencyLookupKey(operationType, idempotencyKey), out var operationId))
            {
                return null;
            }

            if (_activeOperations.TryGetValue(operationId, out var activeOperation))
            {
                return activeOperation;
            }

            _operationHistory.TryGetValue(operationId, out var completedOperation);
            return completedOperation;
        }
    }

    /// <summary>
    /// Generates a database name from tenant key.
    /// </summary>
    private static string GenerateDatabaseName(string tenantKey)
    {
        // Replace invalid characters and ensure SQL-safe name
        var sanitized = System.Text.RegularExpressions.Regex.Replace(tenantKey, @"[^a-zA-Z0-9_-]", "_");
        return $"tenant_{sanitized}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
    }

    /// <summary>
    /// Rolls back provisioning operation by removing created resources.
    /// </summary>
    private async Task RollbackProvisioningAsync(ProvisioningOperation operation, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Rolling back provisioning operation '{OperationId}' (created {ResourceCount} resources)",
            operation.OperationId, operation.CreatedResources.Count);

        foreach (var resource in operation.CreatedResources)
        {
            try
            {
                // Attempt to unregister database
                if (resource.StartsWith("Unregistered:", StringComparison.Ordinal))
                {
                    continue; // Already unregistered
                }

                logger.LogInformation("Rolling back resource '{Resource}'", resource);
                await databaseRegistry.UnregisterDatabaseRuntimeAsync(resource, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to rollback resource '{Resource}'", resource);
                // Continue rollback despite errors
            }
        }

        await catalogRepository.RecordLifecycleEventAsync(
            operation.TenantId,
            "ProvisioningRolledBack",
            TenantEventStatus.RolledBack,
            $"Operation {operation.OperationId} rolled back due to: {operation.ErrorMessage}",
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Wait for all active operations to complete
        var timeout = TimeSpan.FromSeconds(30);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        lock (_orchestrationLock)
        {
            while (_activeOperations.Count > 0 && sw.Elapsed < timeout)
            {
                System.Threading.Thread.Sleep(100);
            }

            if (_activeOperations.Count > 0)
            {
                logger.LogWarning("{OperationCount} active operations at disposal", _activeOperations.Count);
            }
        }

        await Task.CompletedTask;
    }

    private bool TryGetIdempotentOperation(
        ProvisioningOperationType operationType,
        string idempotencyKey,
        out ProvisioningOperation operation)
    {
        lock (_orchestrationLock)
        {
            var lookupKey = BuildIdempotencyLookupKey(operationType, idempotencyKey);
            if (!_idempotencyOperationIds.TryGetValue(lookupKey, out var operationId))
            {
                operation = null!;
                return false;
            }

            if (_activeOperations.TryGetValue(operationId, out operation))
            {
                return true;
            }

            if (_operationHistory.TryGetValue(operationId, out operation))
            {
                return true;
            }

            _idempotencyOperationIds.TryRemove(lookupKey, out _);
            operation = null!;
            return false;
        }
    }

    private static string BuildIdempotencyLookupKey(ProvisioningOperationType operationType, string idempotencyKey)
    {
        return $"{operationType}:{idempotencyKey.Trim()}";
    }

    private static void TrimOperationHistory(ConcurrentDictionary<string, ProvisioningOperation> operationHistory)
    {
        while (operationHistory.Count > MaxOperationHistoryEntries)
        {
            var oldest = operationHistory
                .OrderBy(static kvp => kvp.Value.StartedAt)
                .FirstOrDefault();

            if (oldest.Equals(default(KeyValuePair<string, ProvisioningOperation>)))
            {
                break;
            }

            operationHistory.TryRemove(oldest.Key, out _);
        }
    }

    private static void TrackOperation(ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
    }

    private void TrackActiveOperation(ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _activeOperations[operation.OperationId] = operation;
        TrackOperationHistory(operation);
    }

    private void TrackOperationHistory(ProvisioningOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _operationHistory[operation.OperationId] = operation;
        TrimOperationHistory(_operationHistory);
    }

    private async Task<TenantInfo?> ResolveTenantForOperationAsync(
        ProvisioningOperation operation,
        string tenantKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(operation.TenantId))
        {
            var tenantById = await catalogRepository.GetTenantByIdAsync(operation.TenantId, cancellationToken);
            if (tenantById is not null)
            {
                return tenantById;
            }
        }

        return await catalogRepository.GetTenantByKeyAsync(tenantKey, cancellationToken);
    }
}
