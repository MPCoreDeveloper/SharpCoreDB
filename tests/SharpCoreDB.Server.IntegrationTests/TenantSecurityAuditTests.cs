// <copyright file="TenantSecurityAuditTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Protocol;
using System.Security.Claims;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Tests for tenant-aware security audit event emission.
/// </summary>
public sealed class TenantSecurityAuditTests : IClassFixture<TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public TenantSecurityAuditTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Authenticate_InvalidPassword_EmitsLoginFailedSecurityEvent()
    {
        var securityStore = new TenantSecurityAuditStore();
        var auditService = new TenantSecurityAuditService(
            securityStore,
            NullLogger<TenantSecurityAuditService>.Instance);

        var authService = new UserAuthenticationService(
            Options.Create(CreateConfig()),
            new JwtTokenService("integration-test-secret-key-32chars!!", 1),
            auditService,
            NullLogger<UserAuthenticationService>.Instance);

        _ = authService.Authenticate("admin", "wrong-password", "session-audit-1");

        var latest = securityStore.GetRecent(1).Single();
        Assert.Equal(TenantSecurityEventType.LoginFailed, latest.EventType);
        Assert.False(latest.IsAllowed);
    }

    [Fact]
    public async Task Connect_InvalidCredentials_EmitsConnectDeniedSecurityEvent()
    {
        var service = _fixture.CreateDatabaseService();
        var context = TestServerCallContext.Create();

        var response = await service.Connect(new ConnectRequest
        {
            DatabaseName = "testdb",
            UserName = "admin",
            Password = "wrong",
            ClientName = "audit-test",
        }, context);

        Assert.Equal(ConnectionStatus.InvalidCredentials, response.Status);

        var connectDenied = _fixture.TenantSecurityAuditStore!
            .GetRecent(50)
            .FirstOrDefault(e => e.EventType == TenantSecurityEventType.ConnectDenied);

        Assert.NotNull(connectDenied);
        Assert.Equal("gRPC", connectDenied!.Protocol);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_Denied_EmitsAccessDeniedSecurityEvent()
    {
        var accessStore = new TenantAccessAuditStore();
        var securityStore = new TenantSecurityAuditStore();
        var securityService = new TenantSecurityAuditService(
            securityStore,
            NullLogger<TenantSecurityAuditService>.Instance);

        var policy = new TenantAuthorizationPolicyService(
            accessStore,
            securityService,
            new MetricsCollector("tenant-security-audit-tests"),
            NullLogger<TenantAuthorizationPolicyService>.Instance);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "reader-user"),
            new Claim(ClaimTypes.Role, "reader"),
            new Claim(TenantAwareClaims.TenantIdClaim, "tenant-a"),
            new Claim(TenantAwareClaims.AllowedDatabasesClaim, "db-a"),
            new Claim(TenantAwareClaims.ScopeVersionClaim, TenantAwareClaims.CurrentScopeVersion),
        ], "Test"));

        var decision = policy.AuthorizeDatabaseAccess(
            principal,
            "db-b",
            DatabasePermission.Select,
            protocol: "REST",
            operation: "/api/v1/query");

        Assert.False(decision.IsAllowed);

        var denied = securityStore.GetRecent(1).Single();
        Assert.Equal(TenantSecurityEventType.AccessDenied, denied.EventType);
        Assert.False(denied.IsAllowed);
        Assert.Equal("tenant-a", denied.TenantId);
    }

    [Fact]
    public async Task GrantPermission_EmitsGrantChangedSecurityEvent()
    {
        var securityStore = new TenantSecurityAuditStore();
        var auditService = new TenantSecurityAuditService(
            securityStore,
            NullLogger<TenantSecurityAuditService>.Instance);

        var masterDb = _fixture.DatabaseRegistry!.GetDatabase("master")
            ?? throw new InvalidOperationException("master database required");

        if (!masterDb.Database.TryGetTable("database_grants", out _))
        {
            masterDb.Database.ExecuteSQL(
                "CREATE TABLE database_grants (grant_id TEXT PRIMARY KEY, tenant_id TEXT, database_name TEXT, principal TEXT, permission INTEGER, is_grantable INTEGER, created_at TEXT, expires_at TEXT)");
        }

        var grantsRepo = new DatabaseGrantsRepository(
            masterDb,
            auditService,
            NullLogger<DatabaseGrantsRepository>.Instance);

        await grantsRepo.GrantPermissionAsync(
            "tenant-audit", "testdb", "user-a", DatabasePermission.Select, cancellationToken: CancellationToken.None);

        var grantEvent = securityStore.GetRecent(10)
            .FirstOrDefault(e => e.EventType == TenantSecurityEventType.GrantChanged);

        Assert.NotNull(grantEvent);
        Assert.Equal("tenant-audit", grantEvent!.TenantId);
        Assert.Equal("GRANT_CREATED", grantEvent.DecisionCode);
        Assert.True(grantEvent.IsAllowed);
    }

    [Fact]
    public void AuditService_Emit_ForwardsToSinksAndStore()
    {
        var store = new TenantSecurityAuditStore();
        var sinkEvents = new List<TenantSecurityAuditEvent>();
        var sink = new TestAuditSink(sinkEvents);

        var auditService = new TenantSecurityAuditService(
            store,
            NullLogger<TenantSecurityAuditService>.Instance,
            [sink]);

        var auditEvent = new TenantSecurityAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            EventType: TenantSecurityEventType.Provisioning,
            TenantId: "tenant-prov",
            DatabaseName: "pending",
            Principal: "admin",
            Protocol: "Provisioning",
            IsAllowed: true,
            DecisionCode: "PROVISIONING_STARTED",
            Reason: "Create tenant flow started");

        auditService.Emit(auditEvent);

        Assert.Equal(1, store.Count);
        Assert.Single(sinkEvents);
        Assert.Equal(TenantSecurityEventType.Provisioning, sinkEvents[0].EventType);
        Assert.Equal("tenant-prov", sinkEvents[0].TenantId);
    }

    private sealed class TestAuditSink(List<TenantSecurityAuditEvent> events) : ITenantSecurityAuditSink
    {
        public void Write(TenantSecurityAuditEvent auditEvent) => events.Add(auditEvent);
    }

    private static ServerConfiguration CreateConfig() => new()
    {
        ServerName = "AuditTest",
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
                new UserConfiguration { Username = "admin", PasswordHash = "240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9", Role = "admin" },
            ],
        },
    };
}
