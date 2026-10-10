// <copyright file="NetworkServerBootstrapTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Catalog;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Verifies that <see cref="NetworkServer"/> bootstraps the master-database schemas through the
/// tenant-catalog and database-grants repositories the container serves, instead of building private
/// copies of them. The server host registers both repositories as singletons (the API, gRPC and binary
/// endpoints resolve those same instances), so a second copy inside the server would initialize a
/// schema nothing serves and split the tenant-security audit store in two.
/// C# 14: Uses collection expressions.
/// </summary>
public sealed class NetworkServerBootstrapTests : IAsyncLifetime
{
    private const string ServedDatabaseName = "testdb";
    private const string LocatorDatabaseName = "master";

    private string _testDataDir = string.Empty;
    private DatabaseRegistry _registry = null!;
    private DatabaseInstance _servedDatabase = null!;
    private ServiceProvider _serviceProvider = null!;
    private NetworkServer _server = null!;

    /// <summary>
    /// Builds the container and starts the network server against a temporary database directory.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-server-bootstrap-tests",
            Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(_testDataDir);

        var config = CreateConfiguration(_testDataDir);

        // One registry for the whole test: the container hands this very instance to NetworkServer, and
        // the assertions below read the hosted databases back out of it.
        _registry = new DatabaseRegistry(Options.Create(config), NullLogger<DatabaseRegistry>.Instance);
        await _registry.InitializeAsync(CancellationToken.None);

        _servedDatabase = _registry.GetDatabase(ServedDatabaseName)
            ?? throw new InvalidOperationException(
                $"The '{ServedDatabaseName}' database must exist for the bootstrap test.");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_registry);
        services.AddSingleton(Options.Create(config));
        services.AddSingleton<PgCatalogService>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<TenantQuotaEnforcementService>();
        services.AddSingleton<TenantBackupRestoreService>();
        services.AddSingleton<TenantMigrationPlanningService>();
        services.AddSingleton<RbacService>();
        services.AddSingleton<UserAuthenticationService>();
        services.AddSingleton<TenantAccessAuditStore>();
        services.AddSingleton<TenantSecurityAuditStore>();
        services.AddSingleton<TenantSecurityAuditService>();
        services.AddSingleton<ITenantEncryptionKeyProvider, ConfigurationTenantEncryptionKeyProvider>();
        services.AddSingleton<TenantEncryptionKeyRotationService>();
        services.AddSingleton<TenantAuthorizationPolicyService>();
        services.AddSingleton(new MetricsCollector("bootstrap-test"));
        services.AddSingleton<HealthCheckService>();
        services.AddSingleton(new JwtTokenService(
            config.Security.JwtSecretKey,
            config.Security.JwtExpirationHours));

        // The master-database repositories as the container serves them. The served database is
        // deliberately not the database MasterDatabaseLocator resolves for this configuration
        // (see CreateConfiguration), so the test can tell which database the bootstrap wrote to.
        services.AddSingleton(sp => new TenantCatalogRepository(
            _servedDatabase,
            sp.GetRequiredService<ILogger<TenantCatalogRepository>>()));
        services.AddSingleton(sp => new DatabaseGrantsRepository(
            _servedDatabase,
            sp.GetRequiredService<TenantSecurityAuditService>(),
            sp.GetRequiredService<ILogger<DatabaseGrantsRepository>>()));

        services.AddSingleton<NetworkServer>();

        _serviceProvider = services.BuildServiceProvider();
        _server = _serviceProvider.GetRequiredService<NetworkServer>();

        await _server.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Stops the server and removes the temporary database directory.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.StopAsync();
        }

        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(_testDataDir))
            {
                Directory.Delete(_testDataDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task StartAsync_BootstrapsTheSchemaOnTheContainersRepositories_SoTheServedInstancesAreUsable()
    {
        Assert.True(
            _servedDatabase.Database.TryGetTable("tenants", out _),
            "NetworkServer must initialize the tenant catalog schema on the TenantCatalogRepository the "
            + "container serves, not on a private copy bound to MasterDatabaseLocator's pick.");

        Assert.True(
            _servedDatabase.Database.TryGetTable("database_grants", out _),
            "NetworkServer must initialize the database-grants schema on the DatabaseGrantsRepository the "
            + "container serves, not on a private copy bound to MasterDatabaseLocator's pick.");

        // A real round trip through the served repository: with the schema bootstrapped in another
        // database this throws "Tenants table not found in master database".
        var catalogRepository = _serviceProvider.GetRequiredService<TenantCatalogRepository>();
        await catalogRepository.CreateTenantAsync("bootstrap-tenant", "Bootstrap Tenant");

        var tenants = await catalogRepository.ListTenantsAsync();

        Assert.Contains(tenants, tenant => tenant.TenantKey == "bootstrap-tenant");
    }

    [Fact]
    public async Task StartAsync_WhenTheContainerServesTheRepositories_LeavesNoSecondBootstrapInTheLocatorDatabase()
    {
        var locatorDatabase = MasterDatabaseLocator.TryResolve(
            _registry,
            LocatorDatabaseName,
            ServedDatabaseName);

        Assert.NotNull(locatorDatabase);

        Assert.False(
            locatorDatabase!.Database.TryGetTable("tenants", out _),
            "The tenant catalog schema was bootstrapped twice: once on the repository the container "
            + "serves and once on a private copy built from MasterDatabaseLocator's pick.");

        Assert.False(
            locatorDatabase.Database.TryGetTable("database_grants", out _),
            "The database-grants schema was bootstrapped twice: once on the repository the container "
            + "serves and once on a private copy built from MasterDatabaseLocator's pick.");
    }

    private static ServerConfiguration CreateConfiguration(string dataDirectory)
    {
        return new ServerConfiguration
        {
            ServerName = "NetworkServerBootstrapTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            EnableBinaryProtocol = false,
            DefaultDatabase = ServedDatabaseName,
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = ServedDatabaseName,
                    DatabasePath = Path.Combine(dataDirectory, "testdb.db"),
                    StorageMode = "SingleFile",
                    ConnectionPoolSize = 5,
                },
                new DatabaseInstanceConfiguration
                {
                    Name = LocatorDatabaseName,
                    DatabasePath = Path.Combine(dataDirectory, "master.db"),
                    StorageMode = "SingleFile",
                    ConnectionPoolSize = 5,
                    IsSystemDatabase = true,
                },
            ],
            SystemDatabases = new SystemDatabasesConfiguration
            {
                Enabled = false,
                MasterDatabaseName = LocatorDatabaseName,
            },
            Security = new SecurityConfiguration
            {
                TlsEnabled = true,
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
            },
        };
    }
}
