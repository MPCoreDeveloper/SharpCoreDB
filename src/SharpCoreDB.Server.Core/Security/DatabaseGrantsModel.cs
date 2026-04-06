// <copyright file="DatabaseGrantsModel.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB.Server.Core.Observability;
using System.Globalization;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Per-database grants model for tenant-scoped authorization.
/// Defines fine-grained access control for database operations per tenant.
/// </summary>
public sealed record DatabaseGrant(
    string GrantId,
    string TenantId,
    string DatabaseName,
    string Principal,  // user, role, or service account
    DatabasePermission Permission,
    bool IsGrantable,
    DateTime CreatedAt,
    DateTime ExpiresAt)
{
    /// <summary>
    /// Creates a new database grant.
    /// </summary>
    public static DatabaseGrant Create(
        string tenantId,
        string databaseName,
        string principal,
        DatabasePermission permission,
        bool isGrantable = false,
        int expirationDays = 365)
    {
        return new DatabaseGrant(
            GrantId: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            DatabaseName: databaseName,
            Principal: principal,
            Permission: permission,
            IsGrantable: isGrantable,
            CreatedAt: DateTime.UtcNow,
            ExpiresAt: DateTime.UtcNow.AddDays(expirationDays));
    }
}

/// <summary>
/// Represents database-level permissions for fine-grained access control.
/// </summary>
[Flags]
public enum DatabasePermission
{
    /// <summary>No permissions.</summary>
    None = 0,

    /// <summary>Connect to database.</summary>
    Connect = 1 << 0,

    /// <summary>Create tables and schemas.</summary>
    CreateTable = 1 << 1,

    /// <summary>Select data from tables.</summary>
    Select = 1 << 2,

    /// <summary>Insert data into tables.</summary>
    Insert = 1 << 3,

    /// <summary>Update existing data.</summary>
    Update = 1 << 4,

    /// <summary>Delete data from tables.</summary>
    Delete = 1 << 5,

    /// <summary>Execute stored procedures.</summary>
    Execute = 1 << 6,

    /// <summary>Create indexes.</summary>
    CreateIndex = 1 << 7,

    /// <summary>Alter tables and schemas.</summary>
    Alter = 1 << 8,

    /// <summary>Drop tables and objects.</summary>
    Drop = 1 << 9,

    /// <summary>Manage database users and permissions.</summary>
    ManageUsers = 1 << 10,

    /// <summary>Full database administrative control.</summary>
    Admin = 1 << 11,

    /// <summary>All permissions combined.</summary>
    All = Connect | CreateTable | Select | Insert | Update | Delete | Execute |
          CreateIndex | Alter | Drop | ManageUsers | Admin
}

/// <summary>
/// Repository for managing per-database grants.
/// </summary>
public sealed class DatabaseGrantsRepository(
    DatabaseInstance masterDatabase,
    TenantSecurityAuditService securityAuditService,
    ILogger<DatabaseGrantsRepository> logger) : IAsyncDisposable
{
    private readonly Lock _grantsLock = new();

    /// <summary>
    /// Initializes the grants schema in master database.
    /// </summary>
    public async Task InitializeGrantsSchemaAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Initializing database grants schema");

        try
        {
            const string createGrantsTable = """
                IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'database_grants')
                BEGIN
                    CREATE TABLE database_grants (
                        grant_id NVARCHAR(128) PRIMARY KEY,
                        tenant_id NVARCHAR(128) NOT NULL,
                        database_name NVARCHAR(128) NOT NULL,
                        principal NVARCHAR(256) NOT NULL,
                        permission BIGINT NOT NULL,
                        is_grantable BIT NOT NULL DEFAULT 0,
                        created_at DATETIME NOT NULL DEFAULT GETUTCDATE(),
                        expires_at DATETIME NOT NULL,
                        revoked_at DATETIME
                    );
                    
                    CREATE INDEX idx_grants_tenant ON database_grants(tenant_id);
                    CREATE INDEX idx_grants_principal ON database_grants(principal);
                    CREATE INDEX idx_grants_database ON database_grants(database_name);
                    CREATE INDEX idx_grants_expires ON database_grants(expires_at);
                END
                """;

            masterDatabase.Database.ExecuteSQL(createGrantsTable);
            logger.LogInformation("Database grants schema initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize grants schema");
            throw;
        }
    }

    /// <summary>
    /// Grants database permissions to a principal.
    /// </summary>
    public async Task<DatabaseGrant> GrantPermissionAsync(
        string tenantId,
        string databaseName,
        string principal,
        DatabasePermission permission,
        bool isGrantable = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);

        logger.LogInformation(
            "Granting {Permission} on database '{DatabaseName}' to principal '{Principal}' for tenant '{TenantId}'",
            permission, databaseName, principal, tenantId);

        var grant = DatabaseGrant.Create(tenantId, databaseName, principal, permission, isGrantable);

        lock (_grantsLock)
        {
            try
            {
                var row = new Dictionary<string, object>
                {
                    { "grant_id", grant.GrantId },
                    { "tenant_id", grant.TenantId },
                    { "database_name", grant.DatabaseName },
                    { "principal", grant.Principal },
                    { "permission", (long)grant.Permission },
                    { "is_grantable", isGrantable ? 1 : 0 },
                    { "created_at", grant.CreatedAt },
                    { "expires_at", grant.ExpiresAt }
                };

                if (masterDatabase.Database.TryGetTable("database_grants", out var table))
                {
                    table.Insert(row);
                    logger.LogInformation("Permission granted successfully with ID '{GrantId}'", grant.GrantId);

                    securityAuditService.Emit(new TenantSecurityAuditEvent(
                        TimestampUtc: DateTime.UtcNow,
                        EventType: TenantSecurityEventType.GrantChanged,
                        TenantId: tenantId,
                        DatabaseName: databaseName,
                        Principal: principal,
                        Protocol: "Catalog",
                        IsAllowed: true,
                        DecisionCode: "GRANT_CREATED",
                        Reason: $"Granted permission {permission}"));
                }
                else
                {
                    throw new InvalidOperationException("database_grants table not found");
                }

                return grant;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to grant permission");
                throw;
            }
        }
    }

    /// <summary>
    /// Checks if a principal has specific permissions on a database.
    /// </summary>
    public async Task<bool> HasPermissionAsync(
        string tenantId,
        string databaseName,
        string principal,
        DatabasePermission requiredPermission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);

        lock (_grantsLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("database_grants", out var table))
                {
                    return false;
                }

                logger.LogDebug(
                    "Checking permission {Permission} for principal '{Principal}' on database '{DatabaseName}'",
                    requiredPermission, principal, databaseName);

                var utcNow = DateTime.UtcNow;
                var grants = table.Select();

                foreach (var row in grants)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!RowMatchesGrantScope(row, tenantId, databaseName, principal, utcNow))
                    {
                        continue;
                    }

                    var grantedPermission = ReadDatabasePermission(row, "permission");
                    if ((grantedPermission & DatabasePermission.Admin) == DatabasePermission.Admin)
                    {
                        return true;
                    }

                    if ((grantedPermission & requiredPermission) == requiredPermission)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to check permissions");
                return false;
            }
        }
    }

    /// <summary>
    /// Revokes a specific grant.
    /// </summary>
    public async Task RevokeGrantAsync(
        string grantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);

        logger.LogInformation("Revoking grant '{GrantId}'", grantId);

        lock (_grantsLock)
        {
            try
            {
                var sql = $"UPDATE database_grants SET revoked_at = GETUTCDATE() WHERE grant_id = '{EscapeSql(grantId)}'";
                masterDatabase.Database.ExecuteSQL(sql);
                logger.LogInformation("Grant '{GrantId}' revoked", grantId);

                securityAuditService.Emit(new TenantSecurityAuditEvent(
                    TimestampUtc: DateTime.UtcNow,
                    EventType: TenantSecurityEventType.GrantChanged,
                    TenantId: "unknown",
                    DatabaseName: "unknown",
                    Principal: grantId,
                    Protocol: "Catalog",
                    IsAllowed: true,
                    DecisionCode: "GRANT_REVOKED",
                    Reason: "Grant revoked"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to revoke grant");
                throw;
            }
        }
    }

    /// <summary>
    /// Gets all active grants for a principal.
    /// </summary>
    public async Task<IReadOnlyList<DatabaseGrant>> GetPrincipalGrantsAsync(
        string principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);

        lock (_grantsLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("database_grants", out var table))
                {
                    return [];
                }

                var utcNow = DateTime.UtcNow;
                var grants = new List<DatabaseGrant>();

                foreach (var row in table.Select())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rowPrincipal = ReadString(row, "principal");
                    if (!string.Equals(rowPrincipal, principal, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsRevoked(row))
                    {
                        continue;
                    }

                    var expiresAt = ReadDateTime(row, "expires_at", DateTime.MaxValue);
                    if (expiresAt <= utcNow)
                    {
                        continue;
                    }

                    grants.Add(new DatabaseGrant(
                        GrantId: ReadString(row, "grant_id") ?? string.Empty,
                        TenantId: ReadString(row, "tenant_id") ?? string.Empty,
                        DatabaseName: ReadString(row, "database_name") ?? string.Empty,
                        Principal: rowPrincipal ?? string.Empty,
                        Permission: ReadDatabasePermission(row, "permission"),
                        IsGrantable: ReadBool(row, "is_grantable"),
                        CreatedAt: ReadDateTime(row, "created_at", utcNow),
                        ExpiresAt: expiresAt));
                }

                return grants;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve grants for principal '{Principal}'", principal);
                throw;
            }
        }
    }

    private static bool RowMatchesGrantScope(
        Dictionary<string, object> row,
        string tenantId,
        string databaseName,
        string principal,
        DateTime utcNow)
    {
        if (!string.Equals(ReadString(row, "tenant_id"), tenantId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(ReadString(row, "database_name"), databaseName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(ReadString(row, "principal"), principal, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsRevoked(row))
        {
            return false;
        }

        return ReadDateTime(row, "expires_at", DateTime.MaxValue) > utcNow;
    }

    private static bool IsRevoked(Dictionary<string, object> row)
    {
        if (!row.TryGetValue("revoked_at", out var value) || value is null || value == DBNull.Value)
        {
            return false;
        }

        if (value is string text)
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        return true;
    }

    private static DatabasePermission ReadDatabasePermission(Dictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return DatabasePermission.None;
        }

        return value switch
        {
            long l => (DatabasePermission)l,
            int i => (DatabasePermission)i,
            short s => (DatabasePermission)s,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                => (DatabasePermission)parsed,
            _ => DatabasePermission.None
        };
    }

    private static DateTime ReadDateTime(Dictionary<string, object> row, string key, DateTime defaultValue)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return defaultValue;
        }

        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                => parsed.ToUniversalTime(),
            _ => defaultValue
        };
    }

    private static string? ReadString(Dictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return null;
        }

        return value.ToString();
    }

    private static bool ReadBool(Dictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return false;
        }

        return value switch
        {
            bool b => b,
            byte bt => bt != 0,
            short s => s != 0,
            int i => i != 0,
            long l => l != 0,
            string text when bool.TryParse(text, out var parsedBool) => parsedBool,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong)
                => parsedLong != 0,
            _ => false
        };
    }

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}

/// <summary>
/// Service for managing database-level authorization and grant enforcement.
/// Integrates with session management and query execution.
/// </summary>
public sealed class DatabaseAuthorizationService(
    DatabaseGrantsRepository grantsRepository,
    ILogger<DatabaseAuthorizationService> logger)
{
    /// <summary>
    /// Validates that a principal has required permissions for a database operation.
    /// </summary>
    public async Task<bool> AuthorizeOperationAsync(
        string tenantId,
        string databaseName,
        string principal,
        DatabasePermission requiredPermission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);

        logger.LogDebug(
            "Authorizing {Permission} for principal '{Principal}' on database '{DatabaseName}'",
            requiredPermission, principal, databaseName);

        try
        {
            var hasPermission = await grantsRepository.HasPermissionAsync(
                tenantId, databaseName, principal, requiredPermission, cancellationToken);

            if (!hasPermission)
            {
                logger.LogWarning(
                    "Authorization denied: {Permission} for principal '{Principal}' on database '{DatabaseName}'",
                    requiredPermission, principal, databaseName);
            }

            return hasPermission;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Authorization check failed");
            return false;
        }
    }

    /// <summary>
    /// Gets permissions summary for a principal on a database.
    /// </summary>
    public async Task<(DatabasePermission Permissions, bool IsGrantable)> GetPermissionsAsync(
        string tenantId,
        string databaseName,
        string principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal);

        try
        {
            var grants = await grantsRepository.GetPrincipalGrantsAsync(principal, cancellationToken);

            var relevantGrants = grants
                .Where(g => g.TenantId == tenantId && g.DatabaseName == databaseName)
                .ToList();

            if (relevantGrants.Count == 0)
            {
                return (DatabasePermission.None, false);
            }

            // Combine all permissions
            var combined = DatabasePermission.None;
            var isGrantable = false;

            foreach (var grant in relevantGrants)
            {
                combined |= grant.Permission;
                isGrantable |= grant.IsGrantable;
            }

            return (combined, isGrantable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get permissions summary");
            return (DatabasePermission.None, false);
        }
    }
}
