// <copyright file="TenantAwareSessionContext.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Security;
using SharpCoreDB.Server.Core.Tenancy;

namespace SharpCoreDB.Server.Core;

/// <summary>
/// Extends session management with tenant-aware authorization.
/// Enforces database grants on top of RBAC for multi-tenant deployments.
/// C# 14: Uses primary constructor.
/// </summary>
public sealed class TenantAwareSessionContext(
    ClientSession session,
    string tenantId,
    DatabaseAuthorizationService authService,
    TenantCatalogRepository catalogRepository,
    ILogger<TenantAwareSessionContext> logger)
{
    public ClientSession Session => session;
    public string TenantId => tenantId;
    public string DatabaseName => session.DatabaseInstance.Configuration.Name;

    /// <summary>
    /// Validates that the session's user has required permissions for a database operation.
    /// Checks both RBAC (role-based) and database grants (tenant-based).
    /// </summary>
    /// <param name="requiredDatabasePermission">Required database permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user is authorized for the operation.</returns>
    public async Task<bool> AuthorizeOperationAsync(
        DatabasePermission requiredDatabasePermission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session.UserName);

        // First check: RBAC role-based access
        var rbacAllowed = RbacService.HasPermission(session.Role, MapDatabasePermissionToRbacPermission(requiredDatabasePermission));
        if (!rbacAllowed)
        {
            logger.LogWarning(
                "RBAC check failed for user '{User}' with role {Role} on database '{Database}'",
                session.UserName, session.Role, DatabaseName);
            return false;
        }

        // Second check: Database-level grants for tenant
        var grantsAllowed = await authService.AuthorizeOperationAsync(
            tenantId,
            DatabaseName,
            session.UserName,
            requiredDatabasePermission,
            cancellationToken);

        if (!grantsAllowed)
        {
            logger.LogWarning(
                "Database grant check failed for user '{User}' on tenant '{TenantId}' database '{Database}'",
                session.UserName, tenantId, DatabaseName);
        }

        return grantsAllowed;
    }

    /// <summary>
    /// Gets the combined permissions for this session's user on the database.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Database permissions and whether they're grantable.</returns>
    public async Task<(DatabasePermission Permissions, bool IsGrantable)> GetPermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session.UserName);

        var (dbPermissions, isGrantable) = await authService.GetPermissionsAsync(
            tenantId,
            DatabaseName,
            session.UserName,
            cancellationToken);

        logger.LogDebug(
            "Retrieved permissions for user '{User}' on tenant '{TenantId}': {Permissions}",
            session.UserName, tenantId, dbPermissions);

        return (dbPermissions, isGrantable);
    }

    /// <summary>
    /// Gets the tenant information for this session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tenant info or null if not found.</returns>
    public async Task<TenantInfo?> GetTenantInfoAsync(CancellationToken cancellationToken = default)
    {
        return await catalogRepository.GetTenantByIdAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// Maps database permission to RBAC permission for role-based access check.
    /// </summary>
    private static Permission MapDatabasePermissionToRbacPermission(DatabasePermission dbPermission)
    {
        // Map common database permissions to RBAC permissions
        return dbPermission switch
        {
            DatabasePermission.Select => Permission.Read,
            DatabasePermission.Insert or DatabasePermission.Update or DatabasePermission.Delete => Permission.Write,
            DatabasePermission.CreateTable or DatabasePermission.Alter or DatabasePermission.Drop => Permission.SchemaModify,
            DatabasePermission.ManageUsers => Permission.SchemaModify,
            DatabasePermission.Admin => Permission.SchemaModify,
            _ => Permission.Read
        };
    }
}

/// <summary>
/// Extension methods for SessionManager to support tenant-aware sessions.
/// </summary>
public static class TenantSessionExtensions
{
    /// <summary>
    /// Creates a tenant-aware session context for a client session.
    /// </summary>
    public static TenantAwareSessionContext CreateTenantAwareContext(
        this ClientSession session,
        string tenantId,
        DatabaseAuthorizationService authService,
        TenantCatalogRepository catalogRepository,
        ILogger<TenantAwareSessionContext> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return new TenantAwareSessionContext(session, tenantId, authService, catalogRepository, logger);
    }

    /// <summary>
    /// Enforces database grants during session creation for a specific tenant.
    /// </summary>
    public static async Task EnforceTenantGrantsOnConnectAsync(
        this ClientSession session,
        string tenantId,
        DatabaseAuthorizationService authService,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(session.UserName);

        logger.LogInformation(
            "Enforcing tenant grants on connect for user '{User}' to database '{Database}' (tenant: {TenantId})",
            session.UserName, session.DatabaseInstance.Configuration.Name, tenantId);

        // Check if user has Connect permission for this tenant/database
        var hasConnectPermission = await authService.AuthorizeOperationAsync(
            tenantId,
            session.DatabaseInstance.Configuration.Name,
            session.UserName,
            DatabasePermission.Connect,
            cancellationToken);

        if (!hasConnectPermission)
        {
            logger.LogWarning(
                "Connection denied for user '{User}' to tenant '{TenantId}' database '{Database}': insufficient permissions",
                session.UserName, tenantId, session.DatabaseInstance.Configuration.Name);

            throw new UnauthorizedAccessException(
                $"User '{session.UserName}' does not have permission to connect to database '{session.DatabaseInstance.Configuration.Name}' for tenant '{tenantId}'");
        }

        logger.LogInformation(
            "Grant check passed for user '{User}' connecting to tenant '{TenantId}' database",
            session.UserName, tenantId);
    }
}
