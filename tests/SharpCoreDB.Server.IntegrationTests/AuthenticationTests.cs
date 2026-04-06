// <copyright file="AuthenticationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Protocol;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Tests for user authentication, JWT token generation, and gRPC Connect flow.
/// </summary>
public sealed class AuthenticationTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public AuthenticationTests(TestServerFixture fixture) => _fixture = fixture;

    // ── UserAuthenticationService ──

    [Fact]
    public void Authenticate_ValidAdminCredentials_ReturnsSuccess()
    {
        // Arrange
        var authService = CreateAuthService();

        // Act
        var result = authService.Authenticate("admin", "admin123", "session-1");

        // Assert
        Assert.True(result.IsAuthenticated);
        Assert.NotNull(result.Token);
        Assert.Equal(DatabaseRole.Admin, result.Role);
    }

    [Fact]
    public void Authenticate_ValidWriterCredentials_ReturnsWriterRole()
    {
        var authService = CreateAuthService();

        var result = authService.Authenticate("writer", "writer123", "session-2");

        Assert.True(result.IsAuthenticated);
        Assert.Equal(DatabaseRole.Writer, result.Role);
    }

    [Fact]
    public void Authenticate_ValidReaderCredentials_ReturnsReaderRole()
    {
        var authService = CreateAuthService();

        var result = authService.Authenticate("reader", "reader123", "session-3");

        Assert.True(result.IsAuthenticated);
        Assert.Equal(DatabaseRole.Reader, result.Role);
    }

    [Fact]
    public void Authenticate_InvalidPassword_ReturnsFailed()
    {
        var authService = CreateAuthService();

        var result = authService.Authenticate("admin", "wrong-password", "session-4");

        Assert.False(result.IsAuthenticated);
        Assert.Null(result.Token);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Authenticate_UnknownUser_ReturnsFailed()
    {
        var authService = CreateAuthService();

        var result = authService.Authenticate("nonexistent", "password", "session-5");

        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public void Authenticate_CaseInsensitiveUsername_ReturnsSuccess()
    {
        var authService = CreateAuthService();

        var result = authService.Authenticate("Admin", "admin123", "session-6");

        Assert.True(result.IsAuthenticated);
        Assert.Equal(DatabaseRole.Admin, result.Role);
    }

    [Fact]
    public void Authenticate_GeneratedTokenContainsRoleClaim()
    {
        // Arrange
        var authService = CreateAuthService();
        var tokenService = CreateTokenService();

        // Act
        var result = authService.Authenticate("writer", "writer123", "session-7");

        // Assert
        Assert.True(result.IsAuthenticated);
        var principal = tokenService.ValidateToken(result.Token!);
        var role = RbacService.GetRoleFromPrincipal(principal);
        Assert.Equal(DatabaseRole.Writer, role);
    }

    [Fact]
    public void Authenticate_GeneratedTokenContainsTenantScopeClaims()
    {
        // Arrange
        var authService = CreateAuthService();
        var tokenService = CreateTokenService();

        // Act
        var result = authService.Authenticate("writer", "writer123", "session-scope-1");

        // Assert
        Assert.True(result.IsAuthenticated);

        var principal = tokenService.ValidateToken(result.Token!);

        Assert.Equal("default", principal.GetTenantId());
        Assert.Equal(TenantAwareClaims.CurrentScopeVersion, principal.GetScopeVersion());
        Assert.Contains("*", principal.GetAllowedDatabases());
        Assert.True(principal.HasDatabasePermission(DatabasePermission.Connect));
    }

    [Fact]
    public void ValidateToken_LegacyTokenWithoutScopeVersion_AllowedWhenConfigured()
    {
        // Arrange
        var legacyIssuer = new JwtTokenService("integration-test-secret-key-32chars!!", 1, allowLegacyTokens: true);
        var strictValidator = new JwtTokenService("integration-test-secret-key-32chars!!", 1, allowLegacyTokens: true);
        var legacyToken = legacyIssuer.GenerateToken("legacy-user", "legacy-session", "reader");

        // Act
        var principal = strictValidator.ValidateToken(legacyToken);

        // Assert
        Assert.Equal("legacy-user", strictValidator.GetUsernameFromToken(principal));
        Assert.Null(principal.GetScopeVersion());
    }

    [Fact]
    public void ValidateToken_LegacyTokenWithoutScopeVersion_RejectedWhenDisabled()
    {
        // Arrange
        var legacyIssuer = new JwtTokenService("integration-test-secret-key-32chars!!", 1, allowLegacyTokens: true);
        var strictValidator = new JwtTokenService("integration-test-secret-key-32chars!!", 1, allowLegacyTokens: false);
        var legacyToken = legacyIssuer.GenerateToken("legacy-user", "legacy-session", "reader");

        // Act / Assert
        Assert.Throws<SecurityTokenValidationException>(() => strictValidator.ValidateToken(legacyToken));
    }

    [Fact]
    public void GetUserRole_ExistingUser_ReturnsRole()
    {
        var authService = CreateAuthService();

        Assert.Equal(DatabaseRole.Admin, authService.GetUserRole("admin"));
        Assert.Equal(DatabaseRole.Writer, authService.GetUserRole("writer"));
        Assert.Equal(DatabaseRole.Reader, authService.GetUserRole("reader"));
    }

    [Fact]
    public void GetUserRole_UnknownUser_ReturnsNull()
    {
        var authService = CreateAuthService();

        Assert.Null(authService.GetUserRole("nonexistent"));
    }

    // ── gRPC Connect with Authentication ──

    [Fact]
    public async Task Connect_ValidCredentials_ReturnsSuccessWithSession()
    {
        // Arrange
        var service = _fixture.CreateDatabaseService();
        var context = TestServerCallContext.Create();
        var request = new ConnectRequest
        {
            DatabaseName = "testdb",
            UserName = "test-user",
            Password = "test",
            ClientName = "auth-test",
        };

        // Act
        var response = await service.Connect(request, context);

        // Assert
        Assert.Equal(ConnectionStatus.Success, response.Status);
        Assert.False(string.IsNullOrEmpty(response.SessionId));
        Assert.Contains("RBAC", response.SupportedFeatures);
    }

    [Fact]
    public async Task Connect_InvalidCredentials_ReturnsInvalidCredentials()
    {
        var service = _fixture.CreateDatabaseService();
        var context = TestServerCallContext.Create();
        var request = new ConnectRequest
        {
            DatabaseName = "testdb",
            UserName = "admin",
            Password = "wrong-password",
            ClientName = "auth-test",
        };

        var response = await service.Connect(request, context);

        Assert.Equal(ConnectionStatus.InvalidCredentials, response.Status);
        Assert.True(string.IsNullOrEmpty(response.SessionId));
    }

    [Fact]
    public async Task Connect_NonexistentDatabase_ReturnsDatabaseNotFound()
    {
        var service = _fixture.CreateDatabaseService();
        var context = TestServerCallContext.Create();
        var request = new ConnectRequest
        {
            DatabaseName = "nonexistent",
            UserName = "admin",
            Password = "admin123",
            ClientName = "auth-test",
        };

        var response = await service.Connect(request, context);

        Assert.Equal(ConnectionStatus.DatabaseNotFound, response.Status);
    }

    // ── Session Role Enforcement ──

    [Fact]
    public async Task Session_CreatedWithRole_HasCorrectRole()
    {
        var session = await _fixture.SessionManager!.CreateSessionAsync(
            "testdb", "reader", "127.0.0.1", DatabaseRole.Reader, cancellationToken: CancellationToken.None);

        Assert.Equal(DatabaseRole.Reader, session.Role);
    }

    [Fact]
    public async Task ValidateSessionAsync_ReaderWithReadPermission_ReturnsTrue()
    {
        var session = await _fixture.SessionManager!.CreateSessionAsync(
            "testdb", "reader", "127.0.0.1", DatabaseRole.Reader, cancellationToken: CancellationToken.None);

        var valid = await _fixture.SessionManager.ValidateSessionAsync(
            session.SessionId, Permission.Read);

        Assert.True(valid);
    }

    [Fact]
    public async Task ValidateSessionAsync_ReaderWithWritePermission_ReturnsFalse()
    {
        var session = await _fixture.SessionManager!.CreateSessionAsync(
            "testdb", "reader", "127.0.0.1", DatabaseRole.Reader, cancellationToken: CancellationToken.None);

        var valid = await _fixture.SessionManager.ValidateSessionAsync(
            session.SessionId, Permission.Write);

        Assert.False(valid);
    }

    [Fact]
    public async Task ValidateSessionAsync_AdminWithAllPermissions_ReturnsTrue()
    {
        var session = await _fixture.SessionManager!.CreateSessionAsync(
            "testdb", "admin", "127.0.0.1", DatabaseRole.Admin, cancellationToken: CancellationToken.None);

        Assert.True(await _fixture.SessionManager.ValidateSessionAsync(session.SessionId, Permission.Read));
        Assert.True(await _fixture.SessionManager.ValidateSessionAsync(session.SessionId, Permission.Write));
        Assert.True(await _fixture.SessionManager.ValidateSessionAsync(session.SessionId, Permission.SchemaModify));
        Assert.True(await _fixture.SessionManager.ValidateSessionAsync(session.SessionId, Permission.ManageUsers));
    }

    // ── Helpers ──

    private static UserAuthenticationService CreateAuthService()
    {
        var config = CreateTestConfig();
        var tokenService = CreateTokenService();
        var auditService = new TenantSecurityAuditService(
            new TenantSecurityAuditStore(),
            NullLogger<TenantSecurityAuditService>.Instance);

        return new UserAuthenticationService(
            Options.Create(config),
            tokenService,
            auditService,
            NullLogger<UserAuthenticationService>.Instance);
    }

    private static JwtTokenService CreateTokenService() =>
        new("integration-test-secret-key-32chars!!", 1);

    private static ServerConfiguration CreateTestConfig() => new()
    {
        ServerName = "Test",
        BindAddress = "127.0.0.1",
        GrpcPort = 0,
        DefaultDatabase = "testdb",
        Databases = [new DatabaseInstanceConfiguration { Name = "testdb", DatabasePath = "test.db" }],
        Security = new SecurityConfiguration
        {
            TlsCertificatePath = "dummy.pem",
            TlsPrivateKeyPath = "dummy.key",
            JwtSecretKey = "integration-test-secret-key-32chars!!",
            Users =
            [
                // SHA-256 of "admin123"
                new UserConfiguration { Username = "admin", PasswordHash = "240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9", Role = "admin" },
                // SHA-256 of "writer123"
                new UserConfiguration { Username = "writer", PasswordHash = "0a42b8f38c2c1b24ac890f9f72abb8a186160e9a7e909237a84734660154487b", Role = "writer" },
                // SHA-256 of "reader123"
                new UserConfiguration { Username = "reader", PasswordHash = "128a1cb71e153e042708de7ea043d9a030fc1a83fa258788e7ef7aa23309eb72", Role = "reader" },
            ],
        },
    };
}
