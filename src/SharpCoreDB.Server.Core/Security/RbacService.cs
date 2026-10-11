// <copyright file="RbacService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Role-Based Access Control service.
/// Maps roles to permissions and enforces authorization for gRPC methods and REST endpoints.
/// C# 15: Primary constructor, collection expressions, pattern matching.
/// </summary>
public sealed class RbacService(ILogger<RbacService> logger)
{
    private readonly ILogger<RbacService> _logger = logger;

    /// <summary>
    /// Maps gRPC method names to the minimum permission required.
    /// Public methods (HealthCheck, Connect) are not listed — they bypass RBAC.
    /// </summary>
    private static readonly Dictionary<string, Permission> GrpcMethodPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sharpcoredb.DatabaseService/Ping"] = Permission.Read,
        ["sharpcoredb.DatabaseService/ExecuteQuery"] = Permission.Read,
        ["sharpcoredb.DatabaseService/ExecuteNonQuery"] = Permission.Write,
        ["sharpcoredb.DatabaseService/BeginTransaction"] = Permission.Transaction,
        ["sharpcoredb.DatabaseService/CommitTransaction"] = Permission.Transaction,
        ["sharpcoredb.DatabaseService/RollbackTransaction"] = Permission.Transaction,
        ["sharpcoredb.DatabaseService/VectorSearch"] = Permission.VectorSearch,
        ["sharpcoredb.DatabaseService/Disconnect"] = Permission.Read,
    };

    /// <summary>
    /// Maps REST endpoints (METHOD path) to the minimum permission required.
    /// </summary>
    private static readonly Dictionary<string, Permission> RestEndpointPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POST /api/v1/query"] = Permission.Read,
        ["POST /api/v1/execute"] = Permission.Write,
        ["POST /api/v1/batch"] = Permission.Batch,
        ["GET /api/v1/schema"] = Permission.SchemaRead,
        ["GET /api/v1/databases"] = Permission.SchemaRead,
        ["GET /api/v1/metrics"] = Permission.ViewMetrics,
    };

    /// <summary>
    /// Returns the combined permissions granted by a <see cref="DatabaseRole"/>.
    /// </summary>
    /// <param name="role">The role to resolve.</param>
    /// <returns>Composite permission flags.</returns>
    public static Permission GetPermissionsForRole(DatabaseRole role) => role switch
    {
        DatabaseRole.Reader => Permission.AllReader,
        DatabaseRole.Writer => Permission.AllWriter,
        DatabaseRole.Admin => Permission.AllAdmin,
        _ => Permission.None,
    };

    /// <summary>
    /// Extracts the highest <see cref="DatabaseRole"/> from a <see cref="ClaimsPrincipal"/>.
    /// Falls back to <see cref="DatabaseRole.Reader"/> when no role claim is present.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The effective role.</returns>
    public static DatabaseRole GetRoleFromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var roleClaims = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        if (roleClaims.Count == 0)
        {
            return DatabaseRole.Reader;
        }

        // Return the highest role found
        if (roleClaims.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase)))
        {
            return DatabaseRole.Admin;
        }

        if (roleClaims.Any(r => r.Equals("writer", StringComparison.OrdinalIgnoreCase)))
        {
            return DatabaseRole.Writer;
        }

        return DatabaseRole.Reader;
    }

    /// <summary>
    /// Checks whether a role has the required permission.
    /// </summary>
    /// <param name="role">The user's role.</param>
    /// <param name="required">The permission to check.</param>
    /// <returns>True when permission is granted.</returns>
    public static bool HasPermission(DatabaseRole role, Permission required)
    {
        var granted = GetPermissionsForRole(role);
        return (granted & required) == required;
    }

    /// <summary>
    /// Resolves the required <see cref="Permission"/> for a gRPC method.
    /// Returns <c>null</c> when the method is public (no permission needed).
    /// </summary>
    /// <param name="grpcMethod">Full gRPC method name (e.g. "sharpcoredb.DatabaseService/ExecuteQuery").</param>
    /// <returns>Required permission or null for public methods.</returns>
    public static Permission? GetRequiredPermissionForGrpcMethod(string grpcMethod)
    {
        ArgumentNullException.ThrowIfNull(grpcMethod);
        return GrpcMethodPermissions.GetValueOrDefault(grpcMethod);
    }

    /// <summary>
    /// Resolves the required <see cref="Permission"/> for a REST endpoint.
    /// </summary>
    /// <param name="method">HTTP method (GET, POST, etc.).</param>
    /// <param name="path">Request path (e.g. "/api/v1/query").</param>
    /// <returns>Required permission or null when endpoint is public.</returns>
    public static Permission? GetRequiredPermissionForRestEndpoint(string method, string path)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);

        var key = $"{method.ToUpperInvariant()} {path}";
        return RestEndpointPermissions.GetValueOrDefault(key);
    }

    /// <summary>
    /// Authorizes a gRPC call. Logs and returns false when denied.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    /// <param name="grpcMethod">Full gRPC method name.</param>
    /// <returns>True when authorized.</returns>
    public bool AuthorizeGrpcCall(ClaimsPrincipal principal, string grpcMethod)
    {
        var required = GetRequiredPermissionForGrpcMethod(grpcMethod);
        if (required is null)
        {
            // Public method — always allowed
            return true;
        }

        var role = GetRoleFromPrincipal(principal);
        var authorized = HasPermission(role, required.Value);

        if (!authorized)
        {
            var username = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            _logger.LogWarning(
                "RBAC denied: user '{Username}' (role={Role}) lacks permission {Permission} for {Method}",
                username, role, required.Value, grpcMethod);
        }

        return authorized;
    }

    /// <summary>
    /// Authorizes a REST request. Logs and returns false when denied.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    /// <param name="httpMethod">HTTP method.</param>
    /// <param name="path">Request path.</param>
    /// <returns>True when authorized.</returns>
    public bool AuthorizeRestRequest(ClaimsPrincipal principal, string httpMethod, string path)
    {
        var required = GetRequiredPermissionForRestEndpoint(httpMethod, path);
        if (required is null)
        {
            return true;
        }

        var role = GetRoleFromPrincipal(principal);
        var authorized = HasPermission(role, required.Value);

        if (!authorized)
        {
            var username = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            _logger.LogWarning(
                "RBAC denied: user '{Username}' (role={Role}) lacks permission {Permission} for {HttpMethod} {Path}",
                username, role, required.Value, httpMethod, path);
        }

        return authorized;
    }
}
