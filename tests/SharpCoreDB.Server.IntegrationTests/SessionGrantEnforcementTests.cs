// <copyright file="SessionGrantEnforcementTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using SharpCoreDB.Server.Protocol;
using CoreDatabaseService = SharpCoreDB.Server.Core.DatabaseService;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Integration tests for database grant enforcement during connect and session creation.
/// </summary>
public sealed class SessionGrantEnforcementTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private ServerConfiguration _configuration = null!;
    private DatabaseRegistry _databaseRegistry = null!;
    private SessionManager _sessionManager = null!;
    private DatabaseGrantsRepository _grantsRepository = null!;
    private UserAuthenticationService _authService = null!;
    private TenantQuotaEnforcementService _quotaService = null!;
    private TenantSecurityAuditService _securityAuditService = null!;
    private MetricsCollector _metricsCollector = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "sharpcoredb-session-grants", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _configuration = CreateConfiguration(_tempRoot);
        var options = Options.Create(_configuration);

        _metricsCollector = new MetricsCollector("session-grants-tests");
        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        _quotaService = new TenantQuotaEnforcementService(options, _metricsCollector, NullLogger<TenantQuotaEnforcementService>.Instance);
        _securityAuditService = new TenantSecurityAuditService(new TenantSecurityAuditStore(), NullLogger<TenantSecurityAuditService>.Instance);
        _authService = new UserAuthenticationService(
            options,
            new JwtTokenService(_configuration.Security.JwtSecretKey, 1),
            _securityAuditService,
            NullLogger<UserAuthenticationService>.Instance);

        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        var masterDatabase = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database is required for grant enforcement tests.");

        _grantsRepository = new DatabaseGrantsRepository(masterDatabase, _securityAuditService, NullLogger<DatabaseGrantsRepository>.Instance);
        await _grantsRepository.InitializeGrantsSchemaAsync(CancellationToken.None);

        var databaseAuthorizationService = new DatabaseAuthorizationService(_grantsRepository, NullLogger<DatabaseAuthorizationService>.Instance);
        _sessionManager = new SessionManager(
            _databaseRegistry,
            _quotaService,
            NullLogger<SessionManager>.Instance,
            _grantsRepository,
            databaseAuthorizationService,
            _metricsCollector);
    }

    public async ValueTask DisposeAsync()
    {
        await _grantsRepository.DisposeAsync();
        await _databaseRegistry.ShutdownAsync(CancellationToken.None);

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
    public async Task CreateSessionAsync_WithScopedGrant_AllowsGrantedDatabaseAndDeniesOtherDatabase()
    {
        _ = await _grantsRepository.GrantPermissionAsync(
            tenantId: "default",
            databaseName: "db-a",
            principal: "reader",
            permission: DatabasePermission.Connect,
            cancellationToken: CancellationToken.None);

        var allowedSession = await _sessionManager.CreateSessionAsync(
            databaseName: "db-a",
            userName: "reader",
            clientAddress: "127.0.0.1",
            role: DatabaseRole.Reader,
            tenantId: "default",
            cancellationToken: CancellationToken.None);

        Assert.Equal("db-a", allowedSession.DatabaseInstance.Configuration.Name);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sessionManager.CreateSessionAsync(
                databaseName: "db-b",
                userName: "reader",
                clientAddress: "127.0.0.1",
                role: DatabaseRole.Reader,
                tenantId: "default",
                cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task Connect_WhenGrantDenied_ReturnsInvalidCredentials()
    {
        _ = await _grantsRepository.GrantPermissionAsync(
            tenantId: "default",
            databaseName: "db-a",
            principal: "reader",
            permission: DatabasePermission.Connect,
            cancellationToken: CancellationToken.None);

        var service = new CoreDatabaseService(
            _databaseRegistry,
            _sessionManager,
            _authService,
            _quotaService,
            _securityAuditService,
            NullLogger<CoreDatabaseService>.Instance,
            _metricsCollector);

        var response = await service.Connect(new ConnectRequest
        {
            DatabaseName = "db-b",
            UserName = "reader",
            Password = "reader123",
            ClientName = "grant-enforcement-test",
        }, TestServerCallContext.Create(CancellationToken.None));

        Assert.Equal(ConnectionStatus.InvalidCredentials, response.Status);
    }

    private static ServerConfiguration CreateConfiguration(string root)
    {
        return new ServerConfiguration
        {
            ServerName = "SessionGrantEnforcementTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "db-a",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "db-a",
                    DatabasePath = Path.Combine(root, "db-a.db"),
                    StorageMode = "SingleFile",
                },
                new DatabaseInstanceConfiguration
                {
                    Name = "db-b",
                    DatabasePath = Path.Combine(root, "db-b.db"),
                    StorageMode = "SingleFile",
                },
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(root, "master.db"),
                    StorageMode = "SingleFile",
                    IsSystemDatabase = true,
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
                Users =
                [
                    new UserConfiguration
                    {
                        Username = "reader",
                        PasswordHash = "128a1cb71e153e042708de7ea043d9a030fc1a83fa258788e7ef7aa23309eb72",
                        Role = "reader",
                    },
                ],
            },
        };
    }
}
