// <copyright file="JwtClaimsScopeEnforcementTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;
using System.Security.Claims;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Integration tests for JWT tenant/database scope claims enforcement during session creation.
/// Verifies that SessionManager validates allowed_databases and db_permissions claims against requested database.
/// </summary>
public sealed class JwtClaimsScopeEnforcementTests : IAsyncLifetime
{
    private string _tempRoot = string.Empty;
    private ServerConfiguration _configuration = null!;
    private DatabaseRegistry _databaseRegistry = null!;
    private SessionManager _sessionManager = null!;
    private TenantQuotaEnforcementService _quotaService = null!;

    public async ValueTask InitializeAsync()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "sharpcoredb-jwt-claims", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        _configuration = CreateConfiguration(_tempRoot);
        var options = Options.Create(_configuration);

        var metricsCollector = new MetricsCollector("jwt-claims-tests");
        _databaseRegistry = new DatabaseRegistry(options, NullLogger<DatabaseRegistry>.Instance);
        _quotaService = new TenantQuotaEnforcementService(options, metricsCollector, NullLogger<TenantQuotaEnforcementService>.Instance);

        await _databaseRegistry.InitializeAsync(CancellationToken.None);

        // SessionManager without grants repository for JWT-only testing
        _sessionManager = new SessionManager(
            _databaseRegistry,
            _quotaService,
            NullLogger<SessionManager>.Instance,
            databaseGrantsRepository: null,
            databaseAuthorizationService: null,
            metricsCollector: null);
    }

    public async ValueTask DisposeAsync()
    {
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
    public async Task CreateSessionAsync_WithScopedJwtAllowingDatabase_AllowsSession()
    {
        // Arrange
        var principal = CreateJwtPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["master"],
            permissions: DatabasePermission.Connect);

        // Act
        var session = await _sessionManager.CreateSessionAsync(
            "master",
            "user1",
            "127.0.0.1",
            DatabaseRole.Reader,
            "tenant-a",
            principal,
            CancellationToken.None);

        // Assert
        Assert.NotNull(session);
        Assert.Equal("master", session.DatabaseInstance.Configuration.Name);
        Assert.Equal("user1", session.UserName);
    }

    [Fact]
    public async Task CreateSessionAsync_WithScopedJwtDenyingDatabase_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var principal = CreateJwtPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["other-db"],  // Token allows other-db, not master
            permissions: DatabasePermission.Connect);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sessionManager.CreateSessionAsync(
                "master",  // Requesting master (not in allowed_databases)
                "user1",
                "127.0.0.1",
                DatabaseRole.Reader,
                "tenant-a",
                principal,
                CancellationToken.None));

        Assert.Contains("JWT_SCOPE_DENIED", ex.Message);
    }

    [Fact]
    public async Task CreateSessionAsync_WithScopedJwtWithoutConnectPermission_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var principal = CreateJwtPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["master"],
            permissions: DatabasePermission.Select);  // Missing CONNECT permission

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sessionManager.CreateSessionAsync(
                "master",
                "user1",
                "127.0.0.1",
                DatabaseRole.Reader,
                "tenant-a",
                principal,
                CancellationToken.None));

        Assert.Contains("JWT_PERMISSION_DENIED", ex.Message);
    }

    [Fact]
    public async Task CreateSessionAsync_WithWildcardDatabaseScope_AllowsAllDatabases()
    {
        // Arrange
        var principal = CreateJwtPrincipal(
            username: "admin",
            tenantId: "tenant-a",
            allowedDatabases: ["*"],  // Wildcard allows all
            permissions: DatabasePermission.All);

        // Act - should allow master database
        var session = await _sessionManager.CreateSessionAsync(
            "master",
            "admin",
            "127.0.0.1",
            DatabaseRole.Reader,
            "tenant-a",
            principal,
            CancellationToken.None);

        // Assert
        Assert.NotNull(session);
    }

    [Fact]
    public async Task CreateSessionAsync_WithAdminRoleIgnoresJwtScope_AllowsSession()
    {
        // Arrange
        var principal = CreateJwtPrincipal(
            username: "admin",
            tenantId: "tenant-a",
            allowedDatabases: ["other-db"],  // Limited scope
            permissions: DatabasePermission.Select);  // No CONNECT

        // Act - Admin should bypass JWT scope validation
        var session = await _sessionManager.CreateSessionAsync(
            "master",
            "admin",
            "127.0.0.1",
            DatabaseRole.Admin,  // Admin role
            "tenant-a",
            principal,
            CancellationToken.None);

        // Assert
        Assert.NotNull(session);
        Assert.Equal(DatabaseRole.Admin, session.Role);
    }

    [Fact]
    public async Task CreateSessionAsync_WithoutPrincipal_BypassesJwtScopeValidation()
    {
        // Act - No principal provided, should not throw
        var session = await _sessionManager.CreateSessionAsync(
            "master",
            "user1",
            "127.0.0.1",
            DatabaseRole.Reader,
            "tenant-a",
            principal: null,  // No JWT principal
            CancellationToken.None);

        // Assert
        Assert.NotNull(session);
    }

    [Fact]
    public async Task CreateSessionAsync_WithLegacyTokenWithoutScopeVersion_BypassesJwtScopeValidation()
    {
        // Arrange - Create principal without scope_version (legacy token)
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user1"),
            new(ClaimTypes.Role, "reader"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        // Act - Legacy tokens without scope_version should not be validated
        var session = await _sessionManager.CreateSessionAsync(
            "master",
            "user1",
            "127.0.0.1",
            DatabaseRole.Reader,
            "tenant-a",
            principal,
            CancellationToken.None);

        // Assert
        Assert.NotNull(session);
    }

    private static ServerConfiguration CreateConfiguration(string tempRoot)
    {
        return new ServerConfiguration
        {
            ServerName = "JwtClaimsScopeTests",
            BindAddress = "127.0.0.1",
            GrpcPort = 0,
            DefaultDatabase = "master",
            Databases =
            [
                new DatabaseInstanceConfiguration
                {
                    Name = "master",
                    DatabasePath = Path.Combine(tempRoot, "master.db"),
                    StorageMode = "SingleFile",
                    IsSystemDatabase = true,
                },
                new DatabaseInstanceConfiguration
                {
                    Name = "test-db",
                    DatabasePath = Path.Combine(tempRoot, "test-db.db"),
                    StorageMode = "SingleFile",
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
                JwtSecretKey = "jwt-claims-test-secret-key-32chars!",
                Users =
                [
                    new UserConfiguration
                    {
                        Username = "admin",
                        PasswordHash = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918",
                        Role = "admin",
                        TenantId = "default",
                    },
                    new UserConfiguration
                    {
                        Username = "user1",
                        PasswordHash = "6512bd43d9caa6e02c990b0a82652dca3a35e40bab1a6e5b5a19cf0a8c98ac",
                        Role = "reader",
                        TenantId = "tenant-a",
                    },
                ],
            },
        };
    }

    private static ClaimsPrincipal CreateJwtPrincipal(
        string username,
        string tenantId,
        IReadOnlyList<string> allowedDatabases,
        DatabasePermission permissions)
    {
        var builder = TenantAwareTokenService
            .CreateClaimsBuilder()
            .WithTenantId(tenantId)
            .WithAllowedDatabases([.. allowedDatabases])
            .WithDatabasePermissions(permissions)
            .WithScopeVersion(TenantAwareClaims.CurrentScopeVersion);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.Role, "reader"),
        };
        claims.AddRange(builder.Build());

        var identity = new ClaimsIdentity(claims, "TestJWT");
        return new ClaimsPrincipal(identity);
    }
}
