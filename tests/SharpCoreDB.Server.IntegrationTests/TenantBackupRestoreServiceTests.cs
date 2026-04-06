// <copyright file="TenantBackupRestoreServiceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Integration tests for tenant backup and restore workflows.
/// </summary>
public sealed class TenantBackupRestoreServiceTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private DatabaseRegistry _databaseRegistry = null!;
    private TenantCatalogRepository _catalogRepository = null!;
    private TestTenantEncryptionKeyProvider _keyProvider = null!;
    private TenantProvisioningService _provisioningService = null!;
    private TenantBackupRestoreService _backupRestoreService = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-tenant-backup-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_tempRoot);

        var options = Options.Create(CreateConfiguration(_tempRoot));
        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        var masterDatabase = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database must be initialized for tenant backup tests.");

        EnsureCatalogTablesForTests(masterDatabase);
        _catalogRepository = new TenantCatalogRepository(masterDatabase, NullLogger<TenantCatalogRepository>.Instance);

        _keyProvider = new TestTenantEncryptionKeyProvider();
        _keyProvider.KeyMaterialByReference["key-old"] = "tenant-old-password";

        var auditService = new TenantSecurityAuditService(
            new TenantSecurityAuditStore(),
            NullLogger<TenantSecurityAuditService>.Instance);

        _provisioningService = new TenantProvisioningService(
            _databaseRegistry,
            _catalogRepository,
            _keyProvider,
            auditService,
            NullLogger<TenantProvisioningService>.Instance);

        _backupRestoreService = new TenantBackupRestoreService(
            _databaseRegistry,
            _catalogRepository,
            _keyProvider,
            NullLogger<TenantBackupRestoreService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseRegistry is not null)
        {
            await _databaseRegistry.ShutdownAsync(CancellationToken.None);
        }

        if (_catalogRepository is not null)
        {
            await _catalogRepository.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task RestoreBackupAsync_WithValidBackup_RestoresTenantDatabase()
    {
        var tenantPath = Path.Combine(_tempRoot, "tenant-restore-success.db");
        var backupDirectory = Path.Combine(_tempRoot, "backups");

        var (tenant, _) = await _provisioningService.CreateTenantAsync(
            "tenant-restore-success",
            "Tenant Restore Success",
            tenantPath,
            "idem-create-success",
            cancellationToken: CancellationToken.None);

        var mapping = (await _catalogRepository.GetTenantDatabasesAsync(tenant.TenantId, CancellationToken.None)).Single();
        var backup = await _backupRestoreService.CreateBackupAsync(
            tenant.TenantId,
            mapping.DatabaseName,
            backupDirectory,
            "idem-backup-success",
            CancellationToken.None);

        Assert.Equal(TenantDataOperationStatus.Completed, backup.Status);
        Assert.True(PathExists(backup.BackupPath));

        await MutatePathAsync(mapping.DatabasePath, CancellationToken.None);

        var restore = await _backupRestoreService.RestoreBackupAsync(
            tenant.TenantId,
            mapping.DatabaseName,
            backup.BackupPath,
            targetDatabasePath: null,
            idempotencyKey: "idem-restore-success",
            CancellationToken.None);

        Assert.Equal(TenantDataOperationStatus.Completed, restore.Status);
        Assert.True(restore.ValidationPassed);
        Assert.True(_databaseRegistry.DatabaseExists(mapping.DatabaseName));
        Assert.Equal(
            await CapturePathSignatureAsync(backup.BackupPath, CancellationToken.None),
            await CapturePathSignatureAsync(mapping.DatabasePath, CancellationToken.None));
    }

    [Fact]
    public async Task RestoreBackupAsync_WhenValidationFails_RollsBackOriginalDatabase()
    {
        var tenantPath = Path.Combine(_tempRoot, "tenant-restore-rollback.db");
        var backupDirectory = Path.Combine(_tempRoot, "backups-rollback");

        var (tenant, _) = await _provisioningService.CreateTenantAsync(
            "tenant-restore-rollback",
            "Tenant Restore Rollback",
            tenantPath,
            "idem-create-rollback",
            planTier: "pro",
            encryptionKeyReference: "key-old",
            cancellationToken: CancellationToken.None);

        var mapping = (await _catalogRepository.GetTenantDatabasesAsync(tenant.TenantId, CancellationToken.None)).Single();
        var backup = await _backupRestoreService.CreateBackupAsync(
            tenant.TenantId,
            mapping.DatabaseName,
            backupDirectory,
            "idem-backup-rollback",
            CancellationToken.None);

        var originalSignature = await CapturePathSignatureAsync(mapping.DatabasePath, CancellationToken.None);
        _keyProvider.FailNextResolution = true;

        var restore = await _backupRestoreService.RestoreBackupAsync(
            tenant.TenantId,
            mapping.DatabaseName,
            backup.BackupPath,
            targetDatabasePath: null,
            idempotencyKey: "idem-restore-rollback",
            CancellationToken.None);

        Assert.Equal(TenantDataOperationStatus.Failed, restore.Status);
        Assert.True(restore.RollbackApplied);
        Assert.True(_databaseRegistry.DatabaseExists(mapping.DatabaseName));
        Assert.Equal(originalSignature, await CapturePathSignatureAsync(mapping.DatabasePath, CancellationToken.None));
    }

    private static void EnsureCatalogTablesForTests(DatabaseInstance masterDatabase)
    {
        ArgumentNullException.ThrowIfNull(masterDatabase);

        if (!masterDatabase.Database.TryGetTable("tenants", out _))
        {
            masterDatabase.Database.ExecuteSQL(
                "CREATE TABLE tenants (tenant_id TEXT PRIMARY KEY, tenant_key TEXT, display_name TEXT, status TEXT, plan_tier TEXT, created_at TEXT, updated_at TEXT, created_by TEXT, metadata TEXT)");
        }

        if (!masterDatabase.Database.TryGetTable("tenant_databases", out _))
        {
            masterDatabase.Database.ExecuteSQL(
                "CREATE TABLE tenant_databases (mapping_id TEXT PRIMARY KEY, tenant_id TEXT, database_name TEXT, database_path TEXT, is_primary INTEGER, storage_mode TEXT, encryption_enabled INTEGER, encryption_key_reference TEXT, created_at TEXT)");
        }

        if (!masterDatabase.Database.TryGetTable("tenant_lifecycle_events", out _))
        {
            masterDatabase.Database.ExecuteSQL(
                "CREATE TABLE tenant_lifecycle_events (event_id TEXT PRIMARY KEY, tenant_id TEXT, event_type TEXT, event_status TEXT, event_details TEXT, created_at TEXT)");
        }
    }

    private static ServerConfiguration CreateConfiguration(string root)
    {
        return new ServerConfiguration
        {
            ServerName = "TenantBackupRestoreTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "master",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(root, "master.db"),
                    StorageMode = "SingleFile",
                    IsSystemDatabase = true,
                    ConnectionPoolSize = 5,
                },
            ],
            SystemDatabases = new SystemDatabasesConfiguration
            {
                Enabled = false,
            },
            Security = new SecurityConfiguration
            {
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
            },
        };
    }

    private static async Task MutatePathAsync(string path, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            var mutationFile = Path.Combine(path, "mutation.txt");
            await File.WriteAllTextAsync(mutationFile, "mutated", cancellationToken);
            return;
        }

        if (File.Exists(path))
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], cancellationToken);
            return;
        }

        throw new FileNotFoundException("Path not found for mutation.", path);
    }

    private static async Task<string> CapturePathSignatureAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return Convert.ToHexString(await File.ReadAllBytesAsync(path, cancellationToken));
        }

        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .OrderBy(static file => file, StringComparer.Ordinal)
                .ToArray();

            var lines = new List<string>(files.Length);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(path, file);
                var bytes = await File.ReadAllBytesAsync(file, cancellationToken);
                lines.Add($"{relative}:{Convert.ToHexString(bytes)}");
            }

            return string.Join("|", lines);
        }

        throw new FileNotFoundException("Path not found for signature capture.", path);
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private sealed class TestTenantEncryptionKeyProvider : ITenantEncryptionKeyProvider
    {
        public Dictionary<string, string> KeyMaterialByReference { get; } = [];

        public bool FailNextResolution { get; set; }

        public Task<TenantDatabaseEncryptionMaterial> ResolveDatabaseKeyAsync(
            string tenantId,
            string databaseName,
            string? encryptionKeyReference,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

            if (FailNextResolution)
            {
                FailNextResolution = false;
                throw new InvalidOperationException("Synthetic key resolution failure for restore validation.");
            }

            if (string.IsNullOrWhiteSpace(encryptionKeyReference))
            {
                return Task.FromResult(TenantDatabaseEncryptionMaterial.Disabled());
            }

            if (!KeyMaterialByReference.TryGetValue(encryptionKeyReference, out var keyMaterial))
            {
                throw new KeyNotFoundException($"Key reference '{encryptionKeyReference}' not found.");
            }

            return Task.FromResult(new TenantDatabaseEncryptionMaterial(
                EncryptionEnabled: true,
                KeyReference: encryptionKeyReference,
                KeyMaterial: keyMaterial,
                ProviderName: "test"));
        }
    }
}
