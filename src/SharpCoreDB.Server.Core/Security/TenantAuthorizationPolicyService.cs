// <copyright file="TenantAuthorizationPolicyService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Observability;
using System.Security.Claims;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Provides deterministic tenant-aware authorization decisions for database-scoped operations.
/// Ensures protocol handlers (gRPC, REST, binary) evaluate the same tenant scope rules.
/// </summary>
public sealed class TenantAuthorizationPolicyService(
    TenantAccessAuditStore auditStore,
    TenantSecurityAuditService securityAuditService,
    MetricsCollector metricsCollector,
    ILogger<TenantAuthorizationPolicyService> logger)
{
    private readonly TenantAccessAuditStore _auditStore = auditStore;
    private readonly TenantSecurityAuditService _securityAuditService = securityAuditService;
    private readonly MetricsCollector _metricsCollector = metricsCollector;
    private readonly ILogger<TenantAuthorizationPolicyService> _logger = logger;

    /// <summary>
    /// Evaluates whether a principal may access a database for a required permission.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <param name="databaseName">Target database name.</param>
    /// <param name="requiredPermission">Required database permission.</param>
    /// <param name="protocol">Protocol name used for auditing.</param>
    /// <param name="operation">Operation name used for auditing.</param>
    /// <returns>Deterministic authorization decision.</returns>
    public TenantAuthorizationDecision AuthorizeDatabaseAccess(
        ClaimsPrincipal principal,
        string databaseName,
        DatabasePermission requiredPermission,
        string protocol,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var username = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        var tenantId = principal.GetTenantId() ?? "legacy";

        if (principal.Identity?.IsAuthenticated != true)
        {
            return FinalizeDecision(TenantAuthorizationDecision.Deny(
                "UNAUTHENTICATED",
                "Principal is not authenticated",
                username,
                tenantId,
                databaseName,
                protocol,
                operation));
        }

        var hasScopeVersion = !string.IsNullOrWhiteSpace(principal.GetScopeVersion());
        var hasAnyDatabaseScopeClaim =
            principal.HasClaim(static c => c.Type == TenantAwareClaims.AllowedDatabasesClaim)
            || principal.HasClaim(static c => c.Type == TenantAwareClaims.DatabaseScopeClaim);

        if (hasScopeVersion)
        {
            if (!TenantAwareTokenService.ValidateTenantClaims(principal))
            {
                return FinalizeDecision(TenantAuthorizationDecision.Deny(
                    "TENANT_SCOPE_INVALID",
                    "Scope version is present but tenant claims are invalid",
                    username,
                    tenantId,
                    databaseName,
                    protocol,
                    operation));
            }

            if (!principal.HasDatabaseAccess(databaseName))
            {
                return FinalizeDecision(TenantAuthorizationDecision.Deny(
                    "DATABASE_SCOPE_DENIED",
                    "Database is outside allowed tenant scope",
                    username,
                    tenantId,
                    databaseName,
                    protocol,
                    operation));
            }

            var hasPermissionClaim = principal.HasClaim(static c => c.Type == TenantAwareClaims.DatabasePermissionsClaim);
            if (hasPermissionClaim && !principal.HasDatabasePermission(requiredPermission))
            {
                return FinalizeDecision(TenantAuthorizationDecision.Deny(
                    "TENANT_PERMISSION_DENIED",
                    "Database permission claim does not include required permission",
                    username,
                    tenantId,
                    databaseName,
                    protocol,
                    operation));
            }

            return FinalizeDecision(TenantAuthorizationDecision.Allow(username, tenantId, databaseName, protocol, operation));
        }

        if (hasAnyDatabaseScopeClaim && !principal.HasDatabaseAccess(databaseName))
        {
            return FinalizeDecision(TenantAuthorizationDecision.Deny(
                "DATABASE_SCOPE_DENIED",
                "Legacy database scope claim denies requested database",
                username,
                tenantId,
                databaseName,
                protocol,
                operation));
        }

        return FinalizeDecision(TenantAuthorizationDecision.Allow(username, tenantId, databaseName, protocol, operation));
    }

    private TenantAuthorizationDecision FinalizeDecision(TenantAuthorizationDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var auditEvent = new TenantAccessAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            IsAllowed: decision.IsAllowed,
            Code: decision.Code,
            Reason: decision.Reason,
            Username: decision.Username,
            TenantId: decision.TenantId,
            DatabaseName: decision.DatabaseName,
            Protocol: decision.Protocol,
            Operation: decision.Operation);

        _auditStore.Record(auditEvent);

        _metricsCollector.RecordTenantAuthorizationDecision(
            decision.Protocol,
            decision.Operation,
            decision.DatabaseName,
            decision.IsAllowed,
            decision.Code);

        if (decision.IsAllowed)
        {
            _logger.LogInformation(
                "Tenant authorization allowed [{Code}] user={User} tenant={TenantId} database={Database} protocol={Protocol} operation={Operation}",
                decision.Code,
                decision.Username,
                decision.TenantId,
                decision.DatabaseName,
                decision.Protocol,
                decision.Operation);

            return decision;
        }

        _securityAuditService.Emit(new TenantSecurityAuditEvent(
            TimestampUtc: DateTime.UtcNow,
            EventType: TenantSecurityEventType.AccessDenied,
            TenantId: decision.TenantId,
            DatabaseName: decision.DatabaseName,
            Principal: decision.Username,
            Protocol: decision.Protocol,
            IsAllowed: false,
            DecisionCode: decision.Code,
            Reason: decision.Reason));

        _logger.LogWarning(
            "Tenant authorization denied [{Code}] user={User} tenant={TenantId} database={Database} protocol={Protocol} operation={Operation}: {Reason}",
            decision.Code,
            decision.Username,
            decision.TenantId,
            decision.DatabaseName,
            decision.Protocol,
            decision.Operation,
            decision.Reason);

        return decision;
    }
}

/// <summary>
/// Result object for a tenant authorization policy decision.
/// </summary>
public sealed record TenantAuthorizationDecision(
    bool IsAllowed,
    string Code,
    string Reason,
    string Username,
    string TenantId,
    string DatabaseName,
    string Protocol,
    string Operation)
{
    /// <summary>
    /// Creates an allow decision.
    /// </summary>
    public static TenantAuthorizationDecision Allow(
        string username,
        string tenantId,
        string databaseName,
        string protocol,
        string operation)
    {
        return new TenantAuthorizationDecision(true, "ALLOWED", "Allowed", username, tenantId, databaseName, protocol, operation);
    }

    /// <summary>
    /// Creates a deny decision.
    /// </summary>
    public static TenantAuthorizationDecision Deny(
        string code,
        string reason,
        string username,
        string tenantId,
        string databaseName,
        string protocol,
        string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new TenantAuthorizationDecision(false, code, reason, username, tenantId, databaseName, protocol, operation);
    }
}
