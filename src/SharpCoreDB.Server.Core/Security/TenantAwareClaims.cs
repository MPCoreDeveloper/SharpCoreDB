// <copyright file="TenantAwareClaims.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Security.Claims;
using System.Text.Json;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Extension claims for tenant and database scoping in JWT tokens.
/// Enables fine-grained access control based on token claims.
/// </summary>
public static class TenantAwareClaims
{
    /// <summary>Custom claim type for tenant ID.</summary>
    public const string TenantIdClaim = "tenant_id";

    /// <summary>Custom claim type for tenant key.</summary>
    public const string TenantKeyClaim = "tenant_key";

    /// <summary>Legacy custom claim type for database scope.</summary>
    public const string DatabaseScopeClaim = "database_scope";

    /// <summary>Custom claim type for allowed databases.</summary>
    public const string AllowedDatabasesClaim = "allowed_databases";

    /// <summary>Custom claim type for JWT scope version.</summary>
    public const string ScopeVersionClaim = "scope_version";

    /// <summary>Default scope version for tenant-aware JWT claims.</summary>
    public const string CurrentScopeVersion = "1";

    /// <summary>Custom claim type for database permissions as bitmask.</summary>
    public const string DatabasePermissionsClaim = "db_permissions";

    /// <summary>Custom claim type for tenant plan tier.</summary>
    public const string PlanTierClaim = "plan_tier";

    /// <summary>Custom claim type for database roles (JSON array).</summary>
    public const string DatabaseRolesClaim = "db_roles";

    /// <summary>
    /// Extracts tenant ID from JWT claims.
    /// </summary>
    public static string? GetTenantId(this ClaimsPrincipal user)
    {
        return user?.FindFirst(TenantIdClaim)?.Value;
    }

    /// <summary>
    /// Extracts tenant key from JWT claims.
    /// </summary>
    public static string? GetTenantKey(this ClaimsPrincipal user)
    {
        return user?.FindFirst(TenantKeyClaim)?.Value;
    }

    /// <summary>
    /// Extracts database scope from JWT claims.
    /// </summary>
    public static string? GetDatabaseScope(this ClaimsPrincipal user)
    {
        return user?.FindFirst(DatabaseScopeClaim)?.Value;
    }

    /// <summary>
    /// Extracts database permissions bitmask from JWT claims.
    /// </summary>
    public static DatabasePermission GetDatabasePermissions(this ClaimsPrincipal user)
    {
        var permClaim = user?.FindFirst(DatabasePermissionsClaim)?.Value;
        if (long.TryParse(permClaim, out var permissions))
        {
            return (DatabasePermission)permissions;
        }

        return DatabasePermission.None;
    }

    /// <summary>
    /// Extracts plan tier from JWT claims.
    /// </summary>
    public static string? GetPlanTier(this ClaimsPrincipal user)
    {
        return user?.FindFirst(PlanTierClaim)?.Value;
    }

    /// <summary>
    /// Extracts database roles from JWT claims.
    /// </summary>
    public static IReadOnlyList<string> GetDatabaseRoles(this ClaimsPrincipal user)
    {
        var rolesClaim = user?.FindFirst(DatabaseRolesClaim)?.Value;
        if (string.IsNullOrEmpty(rolesClaim))
        {
            return [];
        }

        try
        {
            var doc = JsonDocument.Parse(rolesClaim);
            var roles = new List<string>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.String)
                    {
                        roles.Add(elem.GetString() ?? string.Empty);
                    }
                }
            }

            return roles;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Extracts allowed databases claim from JWT claims.
    /// </summary>
    public static IReadOnlyList<string> GetAllowedDatabases(this ClaimsPrincipal user)
    {
        var allowedDatabasesValue = user?.FindFirst(AllowedDatabasesClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(allowedDatabasesValue))
        {
            return ParseDatabaseScopeList(allowedDatabasesValue);
        }

        var legacyScope = user?.GetDatabaseScope();
        return string.IsNullOrWhiteSpace(legacyScope)
            ? []
            : ParseDatabaseScopeList(legacyScope);
    }

    /// <summary>
    /// Extracts scope version from JWT claims.
    /// </summary>
    public static string? GetScopeVersion(this ClaimsPrincipal user)
    {
        return user?.FindFirst(ScopeVersionClaim)?.Value;
    }

    /// <summary>
    /// Verifies that a ClaimsPrincipal has access to a specific tenant.
    /// </summary>
    public static bool HasTenantAccess(this ClaimsPrincipal user, string requiredTenantId)
    {
        var tenantId = user.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
        {
            return false;
        }

        return tenantId == requiredTenantId;
    }

    /// <summary>
    /// Verifies that a ClaimsPrincipal has access to a specific database.
    /// </summary>
    public static bool HasDatabaseAccess(this ClaimsPrincipal user, string requiredDatabaseName)
    {
        var allowedDatabases = user.GetAllowedDatabases();
        if (allowedDatabases.Count == 0)
        {
            return false;
        }

        if (allowedDatabases.Any(static db => db == "*"))
        {
            return true;
        }

        return allowedDatabases.Any(db => string.Equals(db, requiredDatabaseName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseDatabaseScopeList(string scope)
    {
        var values = scope
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values;
    }

    /// <summary>
    /// Verifies that a ClaimsPrincipal has a specific database permission.
    /// </summary>
    public static bool HasDatabasePermission(this ClaimsPrincipal user, DatabasePermission requiredPermission)
    {
        var permissions = user.GetDatabasePermissions();
        return (permissions & requiredPermission) == requiredPermission;
    }
}

/// <summary>
/// Builder for tenant-aware JWT claims.
/// </summary>
public sealed class TenantAwareClaimsBuilder
{
    private readonly List<Claim> _claims = [];

    /// <summary>
    /// Adds tenant ID to claims.
    /// </summary>
    public TenantAwareClaimsBuilder WithTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        _claims.Add(new Claim(TenantAwareClaims.TenantIdClaim, tenantId));
        return this;
    }

    /// <summary>
    /// Adds tenant key to claims.
    /// </summary>
    public TenantAwareClaimsBuilder WithTenantKey(string tenantKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        _claims.Add(new Claim(TenantAwareClaims.TenantKeyClaim, tenantKey));
        return this;
    }

    /// <summary>
    /// Adds database scope to claims.
    /// </summary>
    /// <param name="scope">Scope can be "*" (all), "db1,db2" (specific), or single database name.</param>
    public TenantAwareClaimsBuilder WithDatabaseScope(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _claims.Add(new Claim(TenantAwareClaims.DatabaseScopeClaim, scope));
        return this;
    }

    /// <summary>
    /// Adds database permissions to claims as bitmask.
    /// </summary>
    public TenantAwareClaimsBuilder WithDatabasePermissions(DatabasePermission permissions)
    {
        _claims.Add(new Claim(TenantAwareClaims.DatabasePermissionsClaim, ((long)permissions).ToString()));
        return this;
    }

    /// <summary>
    /// Adds plan tier to claims.
    /// </summary>
    public TenantAwareClaimsBuilder WithPlanTier(string planTier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planTier);
        _claims.Add(new Claim(TenantAwareClaims.PlanTierClaim, planTier));
        return this;
    }

    /// <summary>
    /// Adds database roles to claims (as JSON array).
    /// </summary>
    public TenantAwareClaimsBuilder WithDatabaseRoles(params string[] roles)
    {
        if (roles.Length > 0)
        {
            var rolesJson = JsonSerializer.Serialize(roles);
            _claims.Add(new Claim(TenantAwareClaims.DatabaseRolesClaim, rolesJson));
        }

        return this;
    }

    /// <summary>
    /// Adds allowed databases to claims as CSV list.
    /// </summary>
    public TenantAwareClaimsBuilder WithAllowedDatabases(params string[] databases)
    {
        if (databases.Length == 0)
        {
            return this;
        }

        var normalized = databases
            .Where(static db => !string.IsNullOrWhiteSpace(db))
            .Select(static db => db.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return this;
        }

        _claims.Add(new Claim(TenantAwareClaims.AllowedDatabasesClaim, string.Join(",", normalized)));

        // Keep legacy claim for backward compatibility with older components.
        _claims.Add(new Claim(TenantAwareClaims.DatabaseScopeClaim, string.Join(",", normalized)));

        return this;
    }

    /// <summary>
    /// Adds JWT tenant scope version to claims.
    /// </summary>
    public TenantAwareClaimsBuilder WithScopeVersion(string scopeVersion = TenantAwareClaims.CurrentScopeVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeVersion);
        _claims.Add(new Claim(TenantAwareClaims.ScopeVersionClaim, scopeVersion));
        return this;
    }

    /// <summary>
    /// Gets all accumulated claims.
    /// </summary>
    public IReadOnlyList<Claim> Build()
    {
        return _claims.AsReadOnly();
    }
}

/// <summary>
/// Service for issuing and validating tenant-aware JWT tokens.
/// </summary>
public sealed class TenantAwareTokenService
{
    /// <summary>
    /// Creates a claims builder for constructing tenant-aware tokens.
    /// </summary>
    public static TenantAwareClaimsBuilder CreateClaimsBuilder()
    {
        return new TenantAwareClaimsBuilder();
    }

    /// <summary>
    /// Validates that a token has all required tenant and database claims.
    /// </summary>
    public static bool ValidateTenantClaims(ClaimsPrincipal user)
    {
        // Check for minimum required claims
        var tenantId = user.GetTenantId();
        var allowedDatabases = user.GetAllowedDatabases();

        return !string.IsNullOrEmpty(tenantId) && allowedDatabases.Count > 0;
    }
}
