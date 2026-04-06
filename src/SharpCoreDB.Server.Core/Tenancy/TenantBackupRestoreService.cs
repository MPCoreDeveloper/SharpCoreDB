// <copyright file="TenantBackupRestoreService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Security;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Handles tenant-scoped backup export and restore operations with validation and rollback.
/// </summary>
public sealed class TenantBackupRestoreService(
    DatabaseRegistry databaseRegistry,
    TenantCatalogRepository catalogRepository,
    ITenantEncryptionKeyProvider tenantEncryptionKeyProvider,
    ILogger<TenantBackupRestoreService> logger)
{
    /// <summary>
    /// Creates a backup export for a tenant database.
    /// </summary>
    public async Task<TenantBackupOperation> CreateBackupAsync(
        string tenantId,
        string databaseName,
        string backupDirectory,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var mapping = await catalogRepository.GetTenantDatabaseAsync(tenantId, databaseName, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Tenant database mapping not found for tenant '{tenantId}' and database '{databaseName}'.");

        var operation = new TenantBackupOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DatabaseName = databaseName,
            BackupPath = BuildBackupPath(backupDirectory, tenantId, databaseName),
            StartedAt = DateTime.UtcNow,
            Status = TenantDataOperationStatus.InProgress,
        };

        await catalogRepository.RecordLifecycleEventAsync(
            tenantId,
            "TenantBackupStarted",
            TenantEventStatus.InProgress,
            $"Backup started for database '{databaseName}'.",
            cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(backupDirectory);
            await CopyPathAsync(mapping.DatabasePath, operation.BackupPath, cancellationToken).ConfigureAwait(false);

            operation.BackupSizeBytes = GetPathSizeBytes(operation.BackupPath);
            operation.Status = TenantDataOperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "TenantBackupCompleted",
                TenantEventStatus.Completed,
                $"Backup created at '{operation.BackupPath}'.",
                cancellationToken).ConfigureAwait(false);

            return operation;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to back up tenant '{TenantId}' database '{DatabaseName}'",
                tenantId,
                databaseName);

            operation.Status = TenantDataOperationStatus.Failed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = ex.Message;

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "TenantBackupFailed",
                TenantEventStatus.Failed,
                ex.Message,
                cancellationToken).ConfigureAwait(false);

            return operation;
        }
    }

    /// <summary>
    /// Restores a tenant database from a backup export with validation and rollback.
    /// </summary>
    public async Task<TenantRestoreOperation> RestoreBackupAsync(
        string tenantId,
        string databaseName,
        string backupPath,
        string? targetDatabasePath,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        if (!PathExists(backupPath))
        {
            throw new FileNotFoundException("Backup artifact not found.", backupPath);
        }

        var mapping = await catalogRepository.GetTenantDatabaseAsync(tenantId, databaseName, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Tenant database mapping not found for tenant '{tenantId}' and database '{databaseName}'.");

        var restorePath = string.IsNullOrWhiteSpace(targetDatabasePath)
            ? mapping.DatabasePath
            : targetDatabasePath;

        var operation = new TenantRestoreOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            DatabaseName = databaseName,
            SourceBackupPath = backupPath,
            RestoredDatabasePath = restorePath,
            StartedAt = DateTime.UtcNow,
            Status = TenantDataOperationStatus.InProgress,
        };

        await catalogRepository.RecordLifecycleEventAsync(
            tenantId,
            "TenantRestoreStarted",
            TenantEventStatus.InProgress,
            $"Restore started for database '{databaseName}' from '{backupPath}'.",
            cancellationToken).ConfigureAwait(false);

        var rollbackCopyPath = PathExists(restorePath)
            ? $"{restorePath}.rollback-{Guid.NewGuid():N}"
            : null;
        var wasRegistered = databaseRegistry.DatabaseExists(databaseName);

        try
        {
            if (rollbackCopyPath is not null)
            {
                await CopyPathAsync(restorePath, rollbackCopyPath, cancellationToken).ConfigureAwait(false);
            }

            if (wasRegistered)
            {
                await databaseRegistry.UnregisterDatabaseRuntimeAsync(databaseName, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            await ReplacePathAsync(backupPath, restorePath, cancellationToken).ConfigureAwait(false);

            await ValidateDatabaseFileAsync(mapping, restorePath, cancellationToken).ConfigureAwait(false);
            operation.ValidationPassed = true;

            if (!string.Equals(mapping.DatabasePath, restorePath, StringComparison.Ordinal))
            {
                await catalogRepository.UpdateTenantDatabaseLocationAsync(
                    tenantId,
                    databaseName,
                    restorePath,
                    mapping.StorageMode,
                    cancellationToken).ConfigureAwait(false);
            }

            if (wasRegistered)
            {
                await RegisterMappingAsync(mapping with { DatabasePath = restorePath }, cancellationToken).ConfigureAwait(false);
            }

            operation.Status = TenantDataOperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "TenantRestoreCompleted",
                TenantEventStatus.Completed,
                $"Restore completed for database '{databaseName}'.",
                cancellationToken).ConfigureAwait(false);

            return operation;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to restore tenant '{TenantId}' database '{DatabaseName}'",
                tenantId,
                databaseName);

            operation.Status = TenantDataOperationStatus.Failed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = ex.Message;

            if (rollbackCopyPath is not null && PathExists(rollbackCopyPath))
            {
                await ReplacePathAsync(rollbackCopyPath, restorePath, cancellationToken).ConfigureAwait(false);
                operation.RollbackApplied = true;
            }

            if (wasRegistered)
            {
                try
                {
                    await RegisterMappingAsync(mapping, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception registerEx)
                {
                    logger.LogError(registerEx,
                        "Failed to re-register original database '{DatabaseName}' after restore rollback",
                        databaseName);
                }
            }

            await catalogRepository.RecordLifecycleEventAsync(
                tenantId,
                "TenantRestoreFailed",
                TenantEventStatus.Failed,
                ex.Message,
                cancellationToken).ConfigureAwait(false);

            return operation;
        }
        finally
        {
            if (rollbackCopyPath is not null && PathExists(rollbackCopyPath))
            {
                try
                {
                    DeletePath(rollbackCopyPath);
                }
                catch (IOException ioEx)
                {
                    logger.LogWarning(ioEx, "Failed to delete temporary rollback copy '{RollbackCopyPath}'", rollbackCopyPath);
                }
            }
        }
    }

    private async Task RegisterMappingAsync(TenantDatabaseMapping mapping, CancellationToken cancellationToken)
    {
        var encryptionMaterial = mapping.EncryptionEnabled
            ? await tenantEncryptionKeyProvider.ResolveDatabaseKeyAsync(
                mapping.TenantId,
                mapping.DatabaseName,
                mapping.EncryptionKeyReference,
                cancellationToken).ConfigureAwait(false)
            : TenantDatabaseEncryptionMaterial.Disabled();

        await databaseRegistry.RegisterDatabaseRuntimeAsync(
            mapping.DatabaseName,
            mapping.DatabasePath,
            storageMode: mapping.StorageMode,
            connectionPoolSize: 50,
            encryptionEnabled: encryptionMaterial.EncryptionEnabled,
            encryptionMasterPassword: encryptionMaterial.KeyMaterial,
            encryptionKeyReference: encryptionMaterial.KeyReference,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateDatabaseFileAsync(
        TenantDatabaseMapping mapping,
        string databasePath,
        CancellationToken cancellationToken)
    {
        var validationConfig = new DatabaseInstanceConfiguration
        {
            Name = $"validate-{mapping.DatabaseName}-{Guid.NewGuid():N}",
            DatabasePath = databasePath,
            StorageMode = mapping.StorageMode,
            ConnectionPoolSize = 1,
            EncryptionEnabled = mapping.EncryptionEnabled,
            EncryptionKeyFile = mapping.EncryptionKeyReference,
            EncryptionMasterPassword = mapping.EncryptionEnabled
                ? (await tenantEncryptionKeyProvider.ResolveDatabaseKeyAsync(
                    mapping.TenantId,
                    mapping.DatabaseName,
                    mapping.EncryptionKeyReference,
                    cancellationToken).ConfigureAwait(false)).KeyMaterial
                : null,
            IsReadOnly = true,
        };

        await using var validationInstance = new DatabaseInstance(validationConfig, logger);
        await validationInstance.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildBackupPath(string backupDirectory, string tenantId, string databaseName)
    {
        return Path.Combine(
            backupDirectory,
            $"tenant-{tenantId}-{databaseName}-{DateTime.UtcNow:yyyyMMddHHmmss}.backup");
    }

    private static async Task ReplacePathAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        if (PathExists(targetPath))
        {
            DeletePath(targetPath);
        }

        await CopyPathAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyPathAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (Directory.Exists(sourcePath))
        {
            Directory.CreateDirectory(targetPath);

            foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourcePath, directory);
                Directory.CreateDirectory(Path.Combine(targetPath, relative));
            }

            foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourcePath, file);
                var destinationFile = Path.Combine(targetPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? targetPath);
                await CopyFileAsync(file, destinationFile, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? Directory.GetCurrentDirectory());
            await CopyFileAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new FileNotFoundException("Source path not found.", sourcePath);
    }

    private static long GetPathSizeBytes(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(static file => new FileInfo(file).Length);
        }

        return 0;
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void DeletePath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static async Task CopyFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var target = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
