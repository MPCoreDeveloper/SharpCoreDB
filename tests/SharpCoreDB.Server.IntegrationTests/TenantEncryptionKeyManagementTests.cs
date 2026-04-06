// <copyright file="TenantEncryptionKeyManagementTests.cs" company="MPCoreDeveloper">
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
/// Integration tests for tenant encryption key resolution and rotation safety.
/// </summary>
public sealed class TenantEncryptionKeyManagementTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private DatabaseRegistry _databaseRegistry = null!;
    private TenantCatalogRepository _catalogRepository = null!;
    private ITenantEncryptionKeyProvider _keyProvider = null!;
    private TenantProvisioningService _provisioningService = null!;
    private TenantEncryptionKeyRotationService _rotationService = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-tenant-keys-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_tempRoot);

        var config = CreateConfiguration(_tempRoot);
        var options = Options.Create(config);

        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        var masterDatabase = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database must be initialized for tenant catalog tests.");

        EnsureCatalogTablesForTests(masterDatabase);

        _catalogRepository = new TenantCatalogRepository(masterDatabase, NullLogger<TenantCatalogRepository>.Instance);

        _keyProvider = new ConfigurationTenantEncryptionKeyProvider(options);
        var auditService = new TenantSecurityAuditService(
            new TenantSecurityAuditStore(),
            NullLogger<TenantSecurityAuditService>.Instance);

        _provisioningService = new TenantProvisioningService(
            _databaseRegistry,
            _catalogRepository,
            _keyProvider,
            auditService,
            NullLogger<TenantProvisioningService>.Instance);

        _rotationService = new TenantEncryptionKeyRotationService(
            _databaseRegistry,
            _catalogRepository,
            _keyProvider,
            NullLogger<TenantEncryptionKeyRotationService>.Instance);
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
    public async Task CreateTenantAsync_WithMissingEncryptionKeyReference_Throws()
    {
        var tenantPath = Path.Combine(_tempRoot, "tenant-missing-key.db");

        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await _provisioningService.CreateTenantAsync(
                "tenant-missing",
                "Tenant Missing Key",
                tenantPath,
                "idem-missing-key",
                planTier: "pro",
                encryptionKeyReference: "missing-ref",
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task RotateTenantDatabaseKeyAsync_WhenNewKeyResolutionFails_KeepsPreviousMapping()
    {
        var tenantPath = Path.Combine(_tempRoot, "tenant-rotate.db");

        var (tenant, _) = await _provisioningService.CreateTenantAsync(
            "tenant-rotate",
            "Tenant Rotate",
            tenantPath,
            "idem-create-rotate",
            planTier: "pro",
            encryptionKeyReference: "key-old",
            cancellationToken: CancellationToken.None);

        var mapping = (await _catalogRepository.GetTenantDatabasesAsync(tenant.TenantId, CancellationToken.None)).Single();

        var rotation = await _rotationService.RotateTenantDatabaseKeyAsync(
            tenant.TenantId,
            mapping.DatabaseName,
            "missing-rotation-key",
            "idem-rotate-fail",
            CancellationToken.None);

        var after = await _catalogRepository.GetTenantDatabaseAsync(
            tenant.TenantId,
            mapping.DatabaseName,
            CancellationToken.None);

        Assert.Equal(TenantEncryptionKeyRotationStatus.Failed, rotation.Status);
        Assert.NotNull(after);
        Assert.Equal("key-old", after!.EncryptionKeyReference);
        Assert.True(_databaseRegistry.DatabaseExists(mapping.DatabaseName));
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
            ServerName = "TenantEncryptionKeyTests",
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
                TenantEncryption = new TenantEncryptionConfiguration
                {
                    Enabled = true,
                    RequireDedicatedTenantKey = true,
                    DefaultProvider = "configuration",
                    KeyMaterialByReference = new Dictionary<string, string>
                    {
                        ["key-old"] = "tenant-old-password",
                        ["key-new"] = "tenant-new-password",
                    },
                },
            },
        };
    }
}
