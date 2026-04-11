// <copyright file="TenantProvisioningServiceTests.cs" company="MPCoreDeveloper">
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
/// Integration tests for tenant provisioning runtime APIs and idempotency behavior.
/// </summary>
public sealed class TenantProvisioningServiceTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private DatabaseRegistry _databaseRegistry = null!;
    private TenantCatalogRepository _catalogRepository = null!;
    private TenantProvisioningService _provisioningService = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-tenant-provisioning-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_tempRoot);

        var configuration = CreateConfiguration(_tempRoot);
        var options = Options.Create(configuration);

        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        var masterDatabase = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database must be initialized for tenant provisioning tests.");

        EnsureCatalogTablesForTests(masterDatabase);
        _catalogRepository = new TenantCatalogRepository(masterDatabase, NullLogger<TenantCatalogRepository>.Instance);

        var auditService = new TenantSecurityAuditService(
            new TenantSecurityAuditStore(),
            NullLogger<TenantSecurityAuditService>.Instance);

        var keyProvider = new ConfigurationTenantEncryptionKeyProvider(options);

        _provisioningService = new TenantProvisioningService(
            _databaseRegistry,
            _catalogRepository,
            keyProvider,
            auditService,
            NullLogger<TenantProvisioningService>.Instance);
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
    public async Task CreateTenantAsync_WhenSameIdempotencyKey_ReusesOperationAndTenant()
    {
        var firstDatabasePath = Path.Combine(_tempRoot, "tenant-idempotent-first.db");
        var secondDatabasePath = Path.Combine(_tempRoot, "tenant-idempotent-second.db");

        var (firstTenant, firstOperation) = await _provisioningService.CreateTenantAsync(
            "tenant-idempotent",
            "Tenant Idempotent",
            firstDatabasePath,
            "idem-create-1",
            cancellationToken: CancellationToken.None);

        var (secondTenant, secondOperation) = await _provisioningService.CreateTenantAsync(
            "tenant-idempotent",
            "Tenant Idempotent",
            secondDatabasePath,
            "idem-create-1",
            cancellationToken: CancellationToken.None);

        Assert.Equal(firstTenant.TenantId, secondTenant.TenantId);
        Assert.Equal(firstOperation.OperationId, secondOperation.OperationId);
    }

    [Fact]
    public async Task GetOperationStatus_WhenCreateCompleted_ReturnsCompletedOperation()
    {
        var (_, operation) = await _provisioningService.CreateTenantAsync(
            "tenant-status",
            "Tenant Status",
            Path.Combine(_tempRoot, "tenant-status.db"),
            "idem-status-1",
            cancellationToken: CancellationToken.None);

        var status = _provisioningService.GetOperationStatus(operation.OperationId);

        Assert.NotNull(status);
        Assert.Equal(TenantProvisioningService.OperationStatus.Completed, status!.Status);
    }

    [Fact]
    public async Task DeleteTenantAsync_WhenSameIdempotencyKey_ReusesOperation()
    {
        var (tenant, _) = await _provisioningService.CreateTenantAsync(
            "tenant-delete-idempotent",
            "Tenant Delete Idempotent",
            Path.Combine(_tempRoot, "tenant-delete-idempotent.db"),
            "idem-create-delete-1",
            cancellationToken: CancellationToken.None);

        var firstDelete = await _provisioningService.DeleteTenantAsync(
            tenant.TenantId,
            "idem-delete-1",
            CancellationToken.None);

        var secondDelete = await _provisioningService.DeleteTenantAsync(
            tenant.TenantId,
            "idem-delete-1",
            CancellationToken.None);

        Assert.Equal(firstDelete.OperationId, secondDelete.OperationId);
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
            ServerName = "TenantProvisioningTests",
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
                    Enabled = false,
                },
            },
        };
    }
}
