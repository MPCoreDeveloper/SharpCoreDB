// <copyright file="TestServerFixture.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Catalog;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using CoreDatabaseService = SharpCoreDB.Server.Core.DatabaseService;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Provides an in-process server environment for integration testing.
/// Bootstraps DatabaseRegistry, SessionManager, and DatabaseService
/// without requiring network ports or TLS certificates.
/// C# 14: Primary constructor, collection expressions.
/// </summary>
public sealed class TestServerFixture : IAsyncLifetime
{
    private readonly string _testDataDir;
    private ServiceProvider? _serviceProvider;

    /// <summary>Gets the gRPC DatabaseService under test.</summary>
    public SharpCoreDB.Server.Protocol.DatabaseService.DatabaseServiceClient? Client { get; private set; }

    /// <summary>Gets the session manager for test assertions.</summary>
    public SessionManager? SessionManager { get; private set; }

    /// <summary>Gets the database registry for test assertions.</summary>
    public DatabaseRegistry? DatabaseRegistry { get; private set; }

    /// <summary>Gets a valid session ID created during initialization.</summary>
    public string? TestSessionId { get; private set; }

    /// <summary>Gets the JWT token service for creating test tokens.</summary>
    public JwtTokenService? TokenService { get; private set; }

    /// <summary>Gets tenant security audit store for test assertions.</summary>
    public TenantSecurityAuditStore? TenantSecurityAuditStore { get; private set; }

    /// <summary>
    /// Gets the MetricsCollector instance for observability assertions.
    /// </summary>
    public MetricsCollector GetMetricsCollector()
        => _serviceProvider!.GetRequiredService<MetricsCollector>();

    /// <summary>
    /// Gets the HealthCheckService instance for observability assertions.
    /// </summary>
    public HealthCheckService GetHealthCheckService()
        => _serviceProvider!.GetRequiredService<HealthCheckService>();

    public TestServerFixture()
    {
        _testDataDir = Path.Combine(
            Path.GetTempPath(),
            "sharpcoredb-integration-tests",
            Guid.NewGuid().ToString("N")[..8]);
    }

    /// <summary>
    /// Initializes the in-process test server.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_testDataDir);

        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Add SharpCoreDB core services
        services.AddSharpCoreDB();

        // Configure server with test settings
        var testConfig = new ServerConfiguration
        {
            ServerName = "IntegrationTest",
            BindAddress = "127.0.0.1",
            GrpcPort = 0, // not used in-process
            DefaultDatabase = "testdb",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "testdb",
                    DatabasePath = Path.Combine(_testDataDir, "testdb.db"),
                    StorageMode = "SingleFile",
                    ConnectionPoolSize = 10,
                },
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(_testDataDir, "master.db"),
                    StorageMode = "SingleFile",
                    ConnectionPoolSize = 5,
                    IsSystemDatabase = true,
                },
            ],
            SystemDatabases = new SystemDatabasesConfiguration { Enabled = false },
            Security = new SecurityConfiguration
            {
                TlsEnabled = true,
                TlsCertificatePath = "dummy.pem",
                TlsPrivateKeyPath = "dummy.key",
                JwtSecretKey = "integration-test-secret-key-32chars!!",
                Users =
                [
                    new UserConfiguration { Username = "admin", PasswordHash = "240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9", Role = "admin" },
                    new UserConfiguration { Username = "writer", PasswordHash = "0a42b8f38c2c1b24ac890f9f72abb8a186160e9a7e909237a84734660154487b", Role = "writer" },
                    new UserConfiguration { Username = "reader", PasswordHash = "128a1cb71e153e042708de7ea043d9a030fc1a83fa258788e7ef7aa23309eb72", Role = "reader" },
                    new UserConfiguration { Username = "test-user", PasswordHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", Role = "admin" },
                ],
            },
        };

        services.AddSingleton(Options.Create(testConfig));
        services.AddSingleton<DatabaseRegistry>();
        services.AddSingleton<TenantQuotaEnforcementService>();
        services.AddSingleton<TenantBackupRestoreService>();
        services.AddSingleton<TenantMigrationPlanningService>();
        services.AddSingleton<PgCatalogService>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<RbacService>();
        services.AddSingleton<UserAuthenticationService>();
        services.AddSingleton<TenantAccessAuditStore>();
        services.AddSingleton<TenantSecurityAuditStore>();
        services.AddSingleton<TenantSecurityAuditService>();
        services.AddSingleton<ITenantEncryptionKeyProvider, ConfigurationTenantEncryptionKeyProvider>();
        services.AddSingleton<TenantEncryptionKeyRotationService>();
        services.AddSingleton<TenantAuthorizationPolicyService>();
        services.AddSingleton(new MetricsCollector("test-server"));
        services.AddSingleton<HealthCheckService>();
        services.AddSingleton(new JwtTokenService(
            testConfig.Security.JwtSecretKey, 1));

        _serviceProvider = services.BuildServiceProvider();

        DatabaseRegistry = _serviceProvider.GetRequiredService<DatabaseRegistry>();
        SessionManager = _serviceProvider.GetRequiredService<SessionManager>();
        TokenService = _serviceProvider.GetRequiredService<JwtTokenService>();
        TenantSecurityAuditStore = _serviceProvider.GetRequiredService<TenantSecurityAuditStore>();

        // Initialize databases
        await DatabaseRegistry.InitializeAsync(CancellationToken.None);

        // Create a test session for convenience
        var session = await SessionManager.CreateSessionAsync(
            "testdb",
            "test-user",
            "127.0.0.1:0",
            DatabaseRole.Admin,
            cancellationToken: CancellationToken.None);
        TestSessionId = session.SessionId;
    }

    /// <summary>
    /// Gets the DatabaseService instance for direct testing (bypasses gRPC transport).
    /// </summary>
    public CoreDatabaseService CreateDatabaseService()
    {
        return new CoreDatabaseService(
            DatabaseRegistry!,
            SessionManager!,
            _serviceProvider!.GetRequiredService<UserAuthenticationService>(),
            _serviceProvider!.GetRequiredService<TenantQuotaEnforcementService>(),
            _serviceProvider!.GetRequiredService<TenantSecurityAuditService>(),
            _serviceProvider!.GetRequiredService<ILogger<Core.DatabaseService>>(),
            _serviceProvider!.GetRequiredService<MetricsCollector>());
    }

    /// <summary>
    /// Gets the PgCatalogService instance for direct testing.
    /// </summary>
    public PgCatalogService GetPgCatalogService()
        => _serviceProvider!.GetRequiredService<PgCatalogService>();

    /// <summary>
    /// Creates a <see cref="BinaryProtocolHandler"/> wired to the test DI container.
    /// </summary>
    public BinaryProtocolHandler CreateBinaryProtocolHandler()
    {
        return new BinaryProtocolHandler(
            DatabaseRegistry!,
            SessionManager!,
            _serviceProvider!.GetRequiredService<UserAuthenticationService>(),
            _serviceProvider!.GetRequiredService<TenantAuthorizationPolicyService>(),
            _serviceProvider!.GetRequiredService<PgCatalogService>(),
            _serviceProvider!.GetRequiredService<MetricsCollector>(),
            _serviceProvider!.GetRequiredService<ILogger<BinaryProtocolHandler>>());
    }

    /// <summary>
    /// Creates a new test session for the specified database.
    /// </summary>
    public async Task<string> CreateSessionAsync(string databaseName = "testdb")
    {
        var session = await SessionManager!.CreateSessionAsync(
            databaseName,
            "test-user",
            "127.0.0.1:0",
            DatabaseRole.Admin,
            cancellationToken: CancellationToken.None);
        return session.SessionId;
    }

    /// <summary>
    /// Executes SQL directly on the test database (setup helper).
    /// </summary>
    public async Task ExecuteSetupSqlAsync(string sql)
    {
        var db = DatabaseRegistry!.GetDatabase("testdb")
            ?? throw new InvalidOperationException("testdb not found");
        await using var conn = await db.GetConnectionAsync(CancellationToken.None);
        conn.Database.ExecuteSQL(sql);
    }

    /// <summary>
    /// Cleans up all test resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (DatabaseRegistry is not null)
        {
            await DatabaseRegistry.ShutdownAsync(CancellationToken.None);
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
            // Best-effort cleanup
        }
    }
}
