// <copyright file="TenantCatalogRepositoryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Integration tests for tenant catalog bootstrap and persistence behavior.
/// </summary>
public sealed class TenantCatalogRepositoryTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private string _masterDatabasePath = string.Empty;
    private DatabaseRegistry _databaseRegistry = null!;
    private TenantCatalogRepository _catalogRepository = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-tenant-catalog-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_tempRoot);
        _masterDatabasePath = Path.Combine(_tempRoot, "master.db");

        (_databaseRegistry, _catalogRepository) = await CreateRegistryAndRepositoryAsync(_masterDatabasePath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_databaseRegistry is not null)
        {
            await _databaseRegistry.ShutdownAsync(CancellationToken.None);
        }

        await _catalogRepository.DisposeAsync();

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
    public async Task InitializeCatalogAsync_WhenCalled_CreatesCatalogTables()
    {
        await _catalogRepository.InitializeCatalogAsync(CancellationToken.None);

        var master = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database is required for catalog tests.");

        Assert.True(master.Database.TryGetTable("tenants", out _));
        Assert.True(master.Database.TryGetTable("tenant_databases", out _));
        Assert.True(master.Database.TryGetTable("tenant_lifecycle_events", out _));
    }

    [Fact]
    public async Task CatalogEntries_WhenServerReloads_PersistAndResolveDeterministically()
    {
        await _catalogRepository.InitializeCatalogAsync(CancellationToken.None);

        var tenant = await _catalogRepository.CreateTenantAsync(
            "tenant-reload",
            "Tenant Reload",
            planTier: "pro",
            createdBy: "integration-test",
            cancellationToken: CancellationToken.None);

        await _catalogRepository.RegisterTenantDatabaseAsync(
            tenant.TenantId,
            "tenant_reload_primary",
            Path.Combine(_tempRoot, "tenant-reload.db"),
            isPrimary: true,
            storageMode: "SingleFile",
            cancellationToken: CancellationToken.None);

        await _catalogRepository.UpdateTenantAsync(
            tenant.TenantId,
            TenantStatus.Suspended,
            cancellationToken: CancellationToken.None);

        await _databaseRegistry.ShutdownAsync(CancellationToken.None);
        await _catalogRepository.DisposeAsync();

        (_databaseRegistry, _catalogRepository) = await CreateRegistryAndRepositoryAsync(_masterDatabasePath);

        var reloadedTenant = await _catalogRepository.GetTenantByKeyAsync("tenant-reload", CancellationToken.None);
        Assert.NotNull(reloadedTenant);
        Assert.Equal(TenantStatus.Suspended, reloadedTenant!.Status);

        var mappings = await _catalogRepository.GetTenantDatabasesAsync(reloadedTenant.TenantId, CancellationToken.None);
        Assert.Single(mappings);
        Assert.Equal("tenant_reload_primary", mappings[0].DatabaseName);
    }

    private static ServerConfiguration CreateConfiguration(string masterDatabasePath)
    {
        return new ServerConfiguration
        {
            ServerName = "TenantCatalogTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "master",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = masterDatabasePath,
                    StorageMode = "SingleFile",
                    IsSystemDatabase = true,
                    ConnectionPoolSize = 5,
                },
            ],
            SystemDatabases = new SystemDatabasesConfiguration
            {
                Enabled = false,
                MasterDatabaseName = "master",
            },
            Security = new SecurityConfiguration
            {
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
            },
        };
    }

    private static async Task<(DatabaseRegistry Registry, TenantCatalogRepository Repository)> CreateRegistryAndRepositoryAsync(string masterDatabasePath)
    {
        var options = Options.Create(CreateConfiguration(masterDatabasePath));
        var registry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        await registry.InitializeAsync(CancellationToken.None);

        var masterDatabase = registry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database is required for catalog tests.");

        var repository = new TenantCatalogRepository(
            masterDatabase,
            NullLogger<TenantCatalogRepository>.Instance);

        return (registry, repository);
    }
}
