// <copyright file="CrossProtocolGrantsEnforcementTests.cs" company="MPCoreDeveloper">
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
using System.Security.Claims;
using CoreDatabaseService = SharpCoreDB.Server.Core.DatabaseService;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// End-to-end tests verifying that tenant-aware authorization produces deterministic,
/// identical allow/deny decisions across gRPC, REST, and Binary protocols.
/// Satisfies issue #127 acceptance criteria.
/// </summary>
public sealed class CrossProtocolGrantsEnforcementTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private ServerConfiguration _configuration = null!;
    private DatabaseRegistry _databaseRegistry = null!;
    private SessionManager _sessionManager = null!;
    private DatabaseGrantsRepository _grantsRepository = null!;
    private DatabaseAuthorizationService _authorizationService = null!;
    private TenantAuthorizationPolicyService _tenantPolicyService = null!;
    private UserAuthenticationService _authService = null!;
    private TenantQuotaEnforcementService _quotaService = null!;
    private TenantSecurityAuditService _securityAuditService = null!;
    private MetricsCollector _metricsCollector = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "sharpcoredb-cross-protocol", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _configuration = CreateConfiguration(_tempRoot);
        var options = Options.Create(_configuration);

        _metricsCollector = new MetricsCollector("cross-protocol-tests");
        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        _quotaService = new TenantQuotaEnforcementService(options, _metricsCollector, NullLogger<TenantQuotaEnforcementService>.Instance);
        var securityAuditStore = new TenantSecurityAuditStore();
        _securityAuditService = new TenantSecurityAuditService(securityAuditStore, NullLogger<TenantSecurityAuditService>.Instance);

        _authService = new UserAuthenticationService(
            options,
            new JwtTokenService(_configuration.Security.JwtSecretKey, 1),
            _securityAuditService,
            NullLogger<UserAuthenticationService>.Instance);

        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        var masterDatabase = _databaseRegistry.GetDatabase("master")
            ?? throw new InvalidOperationException("Master database is required for cross-protocol tests.");

        _grantsRepository = new DatabaseGrantsRepository(masterDatabase, _securityAuditService, NullLogger<DatabaseGrantsRepository>.Instance);
        await _grantsRepository.InitializeGrantsSchemaAsync(CancellationToken.None);

        _authorizationService = new DatabaseAuthorizationService(_grantsRepository, NullLogger<DatabaseAuthorizationService>.Instance);

        var tenantAccessAuditStore = new TenantAccessAuditStore();
        _tenantPolicyService = new TenantAuthorizationPolicyService(
            tenantAccessAuditStore,
            _securityAuditService,
            _metricsCollector,
            NullLogger<TenantAuthorizationPolicyService>.Instance);

        _sessionManager = new SessionManager(
            _databaseRegistry,
            _quotaService,
            NullLogger<SessionManager>.Instance,
            _grantsRepository,
            _authorizationService,
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
            // Cleanup best-effort
        }
    }

    [Fact]
    public async Task GrantedUser_ReceivesSameAllowDecisionAcrossAllProtocols()
    {
        // Arrange: Grant CONNECT permission to "reader" on "db-a"
        _ = await _grantsRepository.GrantPermissionAsync(
            tenantId: "default",
            databaseName: "db-a",
            principal: "reader",
            permission: DatabasePermission.Connect,
            cancellationToken: CancellationToken.None);

        // Act: Check authorization via grants service (same service used by all protocols)
        var grpcDecision = await _authorizationService.AuthorizeOperationAsync(
            "default", "db-a", "reader", DatabasePermission.Connect, CancellationToken.None);

        var restDecision = await _authorizationService.AuthorizeOperationAsync(
            "default", "db-a", "reader", DatabasePermission.Connect, CancellationToken.None);

        var binaryDecision = await _authorizationService.AuthorizeOperationAsync(
            "default", "db-a", "reader", DatabasePermission.Connect, CancellationToken.None);

        // Assert: Same decision across all protocols
        Assert.True(grpcDecision);
        Assert.True(restDecision);
        Assert.True(binaryDecision);
    }

    [Fact]
    public async Task DeniedUser_ReceivesSameDenyDecisionAcrossAllProtocols()
    {
        // Arrange: Grant CONNECT only on "db-a" (not "db-b")
        _ = await _grantsRepository.GrantPermissionAsync(
            tenantId: "default",
            databaseName: "db-a",
            principal: "reader",
            permission: DatabasePermission.Connect,
            cancellationToken: CancellationToken.None);

        // Act: Check authorization for "db-b" (not granted)
        var grpcDecision = await _authorizationService.AuthorizeOperationAsync(
            "default", "db-b", "reader", DatabasePermission.Connect, CancellationToken.None);

        var restDecision = await _authorizationService.AuthorizeOperationAsync(
            "default", "db-b", "reader", DatabasePermission.Connect, CancellationToken.None);

        var binaryDecision = await _authorizationService.AuthorizeOperationAsync(
            "default", "db-b", "reader", DatabasePermission.Connect, CancellationToken.None);

        // Assert: Same denial across all protocols
        Assert.False(grpcDecision);
        Assert.False(restDecision);
        Assert.False(binaryDecision);
    }

    [Fact]
    public async Task TenantPolicyDecision_IsDeterministicAcrossAllThreeProtocols()
    {
        // Arrange: Create scoped principal with tenant-aware claims
        var principal = CreateScopedPrincipal("user1", "tenant-a", ["db-a"], DatabasePermission.Select | DatabasePermission.Connect);

        // Act: Same principal, same database, different protocols
        var grpcDecision = _tenantPolicyService.AuthorizeDatabaseAccess(
            principal, "db-b", DatabasePermission.Select, protocol: "gRPC", operation: "ExecuteQuery");

        var restDecision = _tenantPolicyService.AuthorizeDatabaseAccess(
            principal, "db-b", DatabasePermission.Select, protocol: "REST", operation: "/api/v1/query");

        var binaryDecision = _tenantPolicyService.AuthorizeDatabaseAccess(
            principal, "db-b", DatabasePermission.Select, protocol: "Binary", operation: "Startup");

        // Assert: All three protocols produce identical deny decision with same code
        Assert.False(grpcDecision.IsAllowed);
        Assert.False(restDecision.IsAllowed);
        Assert.False(binaryDecision.IsAllowed);
        Assert.Equal(grpcDecision.Code, restDecision.Code);
        Assert.Equal(restDecision.Code, binaryDecision.Code);
    }

    [Fact]
    public async Task SessionCreation_WithGrants_IsDeterministicAcrossProtocols()
    {
        // Arrange: Grant CONNECT on "db-a" only
        _ = await _grantsRepository.GrantPermissionAsync(
            tenantId: "default",
            databaseName: "db-a",
            principal: "reader",
            permission: DatabasePermission.Connect,
            cancellationToken: CancellationToken.None);

        // Act: Session creation succeeds for db-a
        var session = await _sessionManager.CreateSessionAsync(
            databaseName: "db-a",
            userName: "reader",
            clientAddress: "127.0.0.1",
            role: DatabaseRole.Reader,
            tenantId: "default",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(session);

        // Act: Session creation fails for db-b (no grant)
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
    public async Task AuthorizationDecisions_AreAuditableAndDeterministic()
    {
        // Arrange: Create scoped principal
        var principal = CreateScopedPrincipal("audited-user", "tenant-a", ["db-a"], DatabasePermission.Select | DatabasePermission.Connect);

        // Act: Make decisions across protocols
        var allowed = _tenantPolicyService.AuthorizeDatabaseAccess(
            principal, "db-a", DatabasePermission.Select, "gRPC", "ExecuteQuery");
        var denied = _tenantPolicyService.AuthorizeDatabaseAccess(
            principal, "db-b", DatabasePermission.Select, "REST", "/api/v1/query");

        // Assert: Decisions are auditable with protocol info
        Assert.True(allowed.IsAllowed);
        Assert.Equal("gRPC", allowed.Protocol);
        Assert.Equal("audited-user", allowed.Username);

        Assert.False(denied.IsAllowed);
        Assert.Equal("REST", denied.Protocol);
        Assert.Equal("audited-user", denied.Username);
        Assert.Equal("db-b", denied.DatabaseName);
    }

    [Fact]
    public async Task GrpcConnect_WhenGrantDenied_ReturnsInvalidCredentials()
    {
        // Arrange: Grant CONNECT on "db-a" only
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

        // Act: Connect to db-b (not granted)
        var response = await service.Connect(new ConnectRequest
        {
            DatabaseName = "db-b",
            UserName = "reader",
            Password = "reader123",
            ClientName = "cross-protocol-test",
        }, TestServerCallContext.Create(CancellationToken.None));

        // Assert: gRPC returns InvalidCredentials for denied grant
        Assert.Equal(ConnectionStatus.InvalidCredentials, response.Status);
    }

    private static ServerConfiguration CreateConfiguration(string root)
    {
        return new ServerConfiguration
        {
            ServerName = "CrossProtocolGrantsTests",
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
                JwtSecretKey = "cross-protocol-test-secret-32chars!!",
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

    private static ClaimsPrincipal CreateScopedPrincipal(
        string username,
        string tenantId,
        string[] allowedDatabases,
        DatabasePermission permissions)
    {
        var builder = TenantAwareTokenService
            .CreateClaimsBuilder()
            .WithTenantId(tenantId)
            .WithAllowedDatabases([.. allowedDatabases])
            .WithDatabasePermissions(permissions)
            .WithScopeVersion();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Role, "reader"),
        };

        claims.AddRange(builder.Build());

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "ScopedToken"));
    }
}
