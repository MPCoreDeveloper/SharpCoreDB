// <copyright file="TenantAuthorizationPolicyServiceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging.Abstractions;
using SharpCoreDB.Server.Core.Observability;
using SharpCoreDB.Server.Core.Security;
using System.Security.Claims;

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Tests for tenant-aware authorization policy decisions across protocol paths.
/// </summary>
public sealed class TenantAuthorizationPolicyServiceTests
{
    private readonly TenantAccessAuditStore _auditStore = new();
    private readonly TenantSecurityAuditStore _securityAuditStore = new();
    private readonly MetricsCollector _metricsCollector = new("tenant-policy-tests");
    private readonly TenantAuthorizationPolicyService _policy;

    public TenantAuthorizationPolicyServiceTests()
    {
        var securityAuditService = new TenantSecurityAuditService(
            _securityAuditStore,
            NullLogger<TenantSecurityAuditService>.Instance);

        _policy = new TenantAuthorizationPolicyService(
            _auditStore,
            securityAuditService,
            _metricsCollector,
            NullLogger<TenantAuthorizationPolicyService>.Instance);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_ScopedPrincipalWithAllowedDatabaseAndPermission_Allows()
    {
        // Arrange
        var principal = CreateScopedPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["db-a"],
            permissions: DatabasePermission.Select | DatabasePermission.Connect);

        // Act
        var decision = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-a",
            DatabasePermission.Select,
            protocol: "gRPC",
            operation: "ExecuteQuery");

        // Assert
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_ScopedPrincipalOutsideDatabaseScope_Denies()
    {
        // Arrange
        var principal = CreateScopedPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["db-a"],
            permissions: DatabasePermission.All);

        // Act
        var decision = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-b",
            DatabasePermission.Select,
            protocol: "REST",
            operation: "/api/v1/query");

        // Assert
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_ScopedPrincipalMissingPermission_Denies()
    {
        // Arrange
        var principal = CreateScopedPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["db-a"],
            permissions: DatabasePermission.Connect | DatabasePermission.Select);

        // Act
        var decision = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-a",
            DatabasePermission.Insert,
            protocol: "Binary",
            operation: "Startup");

        // Assert
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_LegacyPrincipalWithoutScopeClaims_Allows()
    {
        // Arrange
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "legacy-user"),
            new Claim(ClaimTypes.Role, "reader"),
        ], authenticationType: "LegacyToken"));

        // Act
        var decision = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-any",
            DatabasePermission.Connect,
            protocol: "gRPC",
            operation: "ExecuteQuery");

        // Assert
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_DeniedDecisionIsDeterministicAcrossProtocols()
    {
        // Arrange
        var principal = CreateScopedPrincipal(
            username: "user1",
            tenantId: "tenant-a",
            allowedDatabases: ["db-a"],
            permissions: DatabasePermission.Select);

        // Act
        var grpcDecision = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-b",
            DatabasePermission.Select,
            protocol: "gRPC",
            operation: "ExecuteQuery");

        var restDecision = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-b",
            DatabasePermission.Select,
            protocol: "REST",
            operation: "/api/v1/query");

        // Assert
        Assert.False(grpcDecision.IsAllowed);
        Assert.False(restDecision.IsAllowed);
        Assert.Equal(grpcDecision.Code, restDecision.Code);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_StoresAuditEventForAllowedDecision()
    {
        // Arrange
        var principal = CreateScopedPrincipal(
            username: "audited-user",
            tenantId: "tenant-a",
            allowedDatabases: ["db-a"],
            permissions: DatabasePermission.Select | DatabasePermission.Connect);

        // Act
        _ = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-a",
            DatabasePermission.Select,
            protocol: "gRPC",
            operation: "ExecuteQuery");

        var events = _auditStore.GetRecent(10);

        // Assert
        Assert.True(events.Count > 0);
        Assert.True(events[0].IsAllowed);
        Assert.Equal("audited-user", events[0].Username);
        Assert.Equal("db-a", events[0].DatabaseName);
    }

    [Fact]
    public void AuthorizeDatabaseAccess_StoresDeniedAuditEventAndDeniedFilterReturnsOnlyDenied()
    {
        // Arrange
        var principal = CreateScopedPrincipal(
            username: "audited-user",
            tenantId: "tenant-a",
            allowedDatabases: ["db-a"],
            permissions: DatabasePermission.Select);

        // Act
        _ = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-a",
            DatabasePermission.Select,
            protocol: "gRPC",
            operation: "ExecuteQuery");

        _ = _policy.AuthorizeDatabaseAccess(
            principal,
            "db-b",
            DatabasePermission.Select,
            protocol: "REST",
            operation: "/api/v1/query");

        var deniedEvents = _auditStore.GetRecent(50, deniedOnly: true);

        // Assert
        Assert.True(deniedEvents.Count > 0);
        Assert.All(deniedEvents, static e => Assert.False(e.IsAllowed));
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
