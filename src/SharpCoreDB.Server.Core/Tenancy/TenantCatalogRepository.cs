// <copyright file="TenantCatalogRepository.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System.Globalization;
using System.Text.Json;

namespace SharpCoreDB.Server.Core.Tenancy;

/// <summary>
/// Repository for tenant catalog operations in the master database.
/// Provides CRUD operations for tenants, database mappings, and lifecycle events.
/// C# 14: Uses primary constructor for dependencies.
/// </summary>
public sealed class TenantCatalogRepository(
    DatabaseInstance masterDatabase,
    ILogger<TenantCatalogRepository> logger) : IAsyncDisposable
{
    private readonly Lock _catalogLock = new();

    /// <summary>
    /// Initializes the catalog by creating schema tables if they don't exist.
    /// </summary>
    public async Task InitializeCatalogAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Initializing tenant catalog schema in master database");

        try
        {
            lock (_catalogLock)
            {
                // Create catalog tables using ExecuteSQL
                masterDatabase.Database.ExecuteSQL(TenantCatalogSchema.CreateCatalogTables);
            }

            logger.LogInformation("Tenant catalog schema initialized successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize tenant catalog schema");
            throw;
        }
    }

    /// <summary>
    /// Creates a new tenant and registers it in the catalog.
    /// </summary>
    /// <returns>The created tenant.</returns>
    public async Task<TenantInfo> CreateTenantAsync(
        string tenantKey,
        string displayName,
        string? planTier = null,
        string? createdBy = null,
        JsonDocument? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        logger.LogInformation("Creating tenant with key '{TenantKey}' and name '{DisplayName}'", tenantKey, displayName);

        var tenant = TenantInfo.Create(tenantKey, displayName, planTier, createdBy, metadata);

        lock (_catalogLock)
        {
            try
            {
                var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : "null";
                var row = new Dictionary<string, object>
                {
                    { "tenant_id", tenant.TenantId },
                    { "tenant_key", tenant.TenantKey },
                    { "display_name", tenant.DisplayName },
                    { "status", tenant.Status.ToString() },
                    { "plan_tier", tenant.PlanTier ?? (object?)DBNull.Value },
                    { "created_at", tenant.CreatedAt ?? DateTime.UtcNow },
                    { "updated_at", tenant.UpdatedAt ?? DateTime.UtcNow },
                    { "created_by", tenant.CreatedBy ?? (object?)DBNull.Value },
                    { "metadata", metadataJson }
                };

                if (masterDatabase.Database.TryGetTable("tenants", out var table))
                {
                    table.Insert(row);
                    logger.LogInformation("Tenant '{TenantId}' created successfully", tenant.TenantId);
                }
                else
                {
                    logger.LogError("Tenants table not found in master database");
                    throw new InvalidOperationException("Tenants table not found in master database");
                }

                return tenant;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create tenant '{TenantKey}'", tenantKey);
                throw;
            }
        }
    }

    /// <summary>
    /// Retrieves a tenant by its unique ID.
    /// </summary>
    public async Task<TenantInfo?> GetTenantByIdAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenants", out var table))
                {
                    return null;
                }

                // For now, scan table (optimization: add filtering in future)
                foreach (var row in ScanTable(table))
                {
                    if (row["tenant_id"]?.ToString() == tenantId)
                    {
                        return MapRowToTenant(row);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve tenant '{TenantId}'", tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Retrieves a tenant by its unique key.
    /// </summary>
    public async Task<TenantInfo?> GetTenantByKeyAsync(
        string tenantKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);

        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenants", out var table))
                {
                    return null;
                }

                foreach (var row in ScanTable(table))
                {
                    if (row["tenant_key"]?.ToString() == tenantKey)
                    {
                        return MapRowToTenant(row);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve tenant by key '{TenantKey}'", tenantKey);
                throw;
            }
        }
    }

    /// <summary>
    /// Lists all active tenants.
    /// </summary>
    public async Task<IReadOnlyList<TenantInfo>> ListTenantsAsync(
        TenantStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenants", out var table))
                {
                    return [];
                }

                var tenants = new List<TenantInfo>();
                foreach (var row in ScanTable(table))
                {
                    if (statusFilter == null || Enum.Parse<TenantStatus>((string)row["status"]!) == statusFilter)
                    {
                        tenants.Add(MapRowToTenant(row));
                    }
                }

                return tenants.OrderByDescending(t => t.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to list tenants");
                throw;
            }
        }
    }

    /// <summary>
    /// Updates a tenant's status and metadata.
    /// </summary>
    public async Task UpdateTenantAsync(
        string tenantId,
        TenantStatus? newStatus = null,
        JsonDocument? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        logger.LogInformation("Updating tenant '{TenantId}'", tenantId);

        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenants", out var table))
                {
                    throw new InvalidOperationException("Tenants table not found");
                }

                // For now, perform update via SQL since direct row updates aren't exposed
                var updates = new List<string> { "updated_at = datetime('now')" };

                if (newStatus.HasValue)
                {
                    updates.Add($"status = '{newStatus.Value}'");
                }

                if (metadata != null)
                {
                    var metadataJson = JsonSerializer.Serialize(metadata).Replace("'", "''");
                    updates.Add($"metadata = '{metadataJson}'");
                }

                var sql = $"UPDATE tenants SET {string.Join(", ", updates)} WHERE tenant_id = '{EscapeSql(tenantId)}'";
                masterDatabase.Database.ExecuteSQL(sql);

                logger.LogInformation("Tenant '{TenantId}' updated successfully", tenantId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update tenant '{TenantId}'", tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Registers a database for a tenant.
    /// </summary>
    public async Task<TenantDatabaseMapping> RegisterTenantDatabaseAsync(
        string tenantId,
        string databaseName,
        string databasePath,
        bool isPrimary = true,
        string storageMode = "SingleFile",
        bool encryptionEnabled = false,
        string? encryptionKeyReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        logger.LogInformation("Registering database '{DatabaseName}' for tenant '{TenantId}'", databaseName, tenantId);

        lock (_catalogLock)
        {
            try
            {
                var mapping = TenantDatabaseMapping.Create(
                    tenantId,
                    databaseName,
                    databasePath,
                    isPrimary,
                    storageMode,
                    encryptionEnabled,
                    encryptionKeyReference);

                var row = new Dictionary<string, object>
                {
                    { "mapping_id", mapping.MappingId },
                    { "tenant_id", mapping.TenantId },
                    { "database_name", mapping.DatabaseName },
                    { "database_path", mapping.DatabasePath },
                    { "is_primary", isPrimary ? 1 : 0 },
                    { "storage_mode", storageMode },
                    { "created_at", mapping.CreatedAt ?? DateTime.UtcNow }
                };

                if (encryptionEnabled)
                {
                    row["encryption_enabled"] = 1;
                }

                if (!string.IsNullOrWhiteSpace(encryptionKeyReference))
                {
                    row["encryption_key_reference"] = encryptionKeyReference;
                }

                if (masterDatabase.Database.TryGetTable("tenant_databases", out var table))
                {
                    table.Insert(row);
                    logger.LogInformation("Database '{DatabaseName}' registered for tenant '{TenantId}'", databaseName, tenantId);
                }
                else
                {
                    throw new InvalidOperationException("tenant_databases table not found");
                }

                return mapping;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register database '{DatabaseName}' for tenant '{TenantId}'", databaseName, tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Retrieves a database mapping by tenant and database name.
    /// </summary>
    public async Task<TenantDatabaseMapping?> GetTenantDatabaseAsync(
        string tenantId,
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var databases = await GetTenantDatabasesAsync(tenantId, cancellationToken);
        return databases.FirstOrDefault(d => string.Equals(d.DatabaseName, databaseName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Updates encryption settings for a tenant database mapping.
    /// </summary>
    public async Task UpdateTenantDatabaseEncryptionAsync(
        string tenantId,
        string databaseName,
        bool encryptionEnabled,
        string? encryptionKeyReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        lock (_catalogLock)
        {
            try
            {
                var escapedTenantId = EscapeSql(tenantId);
                var escapedDatabaseName = EscapeSql(databaseName);
                var escapedReference = string.IsNullOrWhiteSpace(encryptionKeyReference)
                    ? "NULL"
                    : $"'{EscapeSql(encryptionKeyReference)}'";

                var sql = $"""
                    UPDATE tenant_databases
                    SET encryption_enabled = {(encryptionEnabled ? 1 : 0)},
                        encryption_key_reference = {escapedReference}
                    WHERE tenant_id = '{escapedTenantId}'
                      AND database_name = '{escapedDatabaseName}'
                    """;

                masterDatabase.Database.ExecuteSQL(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to update encryption settings for database '{DatabaseName}' tenant '{TenantId}'",
                    databaseName,
                    tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Updates tenant database location metadata.
    /// </summary>
    public async Task UpdateTenantDatabaseLocationAsync(
        string tenantId,
        string databaseName,
        string databasePath,
        string? storageMode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        lock (_catalogLock)
        {
            try
            {
                var escapedTenantId = EscapeSql(tenantId);
                var escapedDatabaseName = EscapeSql(databaseName);
                var escapedPath = EscapeSql(databasePath);
                var modeUpdate = string.IsNullOrWhiteSpace(storageMode)
                    ? string.Empty
                    : $", storage_mode = '{EscapeSql(storageMode)}'";

                var sql = $"""
                    UPDATE tenant_databases
                    SET database_path = '{escapedPath}'{modeUpdate}
                    WHERE tenant_id = '{escapedTenantId}'
                      AND database_name = '{escapedDatabaseName}'
                    """;

                masterDatabase.Database.ExecuteSQL(sql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to update location for database '{DatabaseName}' tenant '{TenantId}'",
                    databaseName,
                    tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Retrieves all databases for a tenant.
    /// </summary>
    public async Task<IReadOnlyList<TenantDatabaseMapping>> GetTenantDatabasesAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenant_databases", out var table))
                {
                    return [];
                }

                var databases = new List<TenantDatabaseMapping>();
                foreach (var row in ScanTable(table))
                {
                    if (row["tenant_id"]?.ToString() == tenantId)
                    {
                        databases.Add(MapRowToDatabaseMapping(row));
                    }
                }

                return databases.OrderByDescending(db => db.IsPrimary).ThenBy(db => db.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve databases for tenant '{TenantId}'", tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Records a lifecycle event for a tenant.
    /// </summary>
    public async Task RecordLifecycleEventAsync(
        string tenantId,
        string eventType,
        TenantEventStatus status = TenantEventStatus.Completed,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        lock (_catalogLock)
        {
            try
            {
                var @event = TenantLifecycleEvent.Create(tenantId, eventType, status, details);

                var row = new Dictionary<string, object>
                {
                    { "event_id", @event.EventId },
                    { "tenant_id", tenantId },
                    { "event_type", eventType },
                    { "event_status", status.ToString() },
                    { "event_details", details ?? (object?)DBNull.Value },
                    { "created_at", @event.CreatedAt ?? DateTime.UtcNow }
                };

                if (masterDatabase.Database.TryGetTable("tenant_lifecycle_events", out var table))
                {
                    table.Insert(row);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to record lifecycle event for tenant '{TenantId}'", tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Retrieves lifecycle events for a tenant.
    /// </summary>
    public async Task<IReadOnlyList<TenantLifecycleEvent>> GetLifecycleEventsAsync(
        string tenantId,
        int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenant_lifecycle_events", out var table))
                {
                    return [];
                }

                var events = new List<TenantLifecycleEvent>();
                var count = 0;
                foreach (var row in ScanTable(table).Reverse())
                {
                    if (count >= pageSize)
                        break;

                    if (row["tenant_id"]?.ToString() == tenantId)
                    {
                        events.Add(MapRowToLifecycleEvent(row));
                        count++;
                    }
                }

                return events;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve lifecycle events for tenant '{TenantId}'", tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Gets the configured quota policy for a tenant.
    /// </summary>
    public async Task<TenantQuotaPolicy?> GetTenantQuotaPolicyAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        lock (_catalogLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("tenant_quotas", out var table))
                {
                    return null;
                }

                foreach (var row in ScanTable(table))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.Equals(ReadString(row, "tenant_id"), tenantId, StringComparison.Ordinal))
                    {
                        return MapRowToTenantQuotaPolicy(row);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get quota policy for tenant '{TenantId}'", tenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Upserts the quota policy for a tenant.
    /// </summary>
    public async Task UpsertTenantQuotaPolicyAsync(
        TenantQuotaPolicy quotaPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quotaPolicy);

        lock (_catalogLock)
        {
            try
            {
                var escapedTenantId = EscapeSql(quotaPolicy.TenantId);

                var upsertSql = $"""
                    DELETE FROM tenant_quotas
                    WHERE tenant_id = '{escapedTenantId}';

                    INSERT INTO tenant_quotas (tenant_id, max_active_sessions, max_qps, max_storage_mb, max_batch_size, updated_at)
                    VALUES ('{escapedTenantId}', {quotaPolicy.MaxActiveSessions}, {quotaPolicy.MaxRequestsPerSecond}, {quotaPolicy.MaxStorageMb}, {quotaPolicy.MaxBatchSize}, datetime('now'));
                    """;

                masterDatabase.Database.ExecuteSQL(upsertSql);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upsert quota policy for tenant '{TenantId}'", quotaPolicy.TenantId);
                throw;
            }
        }
    }

    /// <summary>
    /// Scans all rows from a table using the public API.
    /// </summary>
    private static IEnumerable<Dictionary<string, object?>> ScanTable(ITable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Select().Select(row => row.ToDictionary(static kvp => kvp.Key, static kvp => (object?)kvp.Value));
    }

    /// <summary>
    /// Maps a data row to a TenantInfo object.
    /// </summary>
    private static TenantInfo MapRowToTenant(Dictionary<string, object?> row)
    {
        var metadata = ReadJsonMetadata(row, "metadata");

        return new TenantInfo(
            TenantId: ReadString(row, "tenant_id") ?? string.Empty,
            TenantKey: ReadString(row, "tenant_key") ?? string.Empty,
            DisplayName: ReadString(row, "display_name") ?? string.Empty,
            Status: ReadEnum(row, "status", TenantStatus.Active),
            PlanTier: ReadString(row, "plan_tier"),
            CreatedAt: ReadNullableDateTime(row, "created_at"),
            UpdatedAt: ReadNullableDateTime(row, "updated_at"),
            CreatedBy: ReadString(row, "created_by"),
            Metadata: metadata);
    }

    /// <summary>
    /// Maps a data row to a TenantDatabaseMapping object.
    /// </summary>
    private static TenantDatabaseMapping MapRowToDatabaseMapping(Dictionary<string, object?> row)
    {
        return new TenantDatabaseMapping(
            MappingId: ReadGuid(row, "mapping_id"),
            TenantId: ReadString(row, "tenant_id") ?? string.Empty,
            DatabaseName: ReadString(row, "database_name") ?? string.Empty,
            DatabasePath: ReadString(row, "database_path") ?? string.Empty,
            IsPrimary: ReadBool(row, "is_primary"),
            StorageMode: ReadString(row, "storage_mode") ?? "SingleFile",
            EncryptionEnabled: ReadBool(row, "encryption_enabled"),
            EncryptionKeyReference: ReadString(row, "encryption_key_reference"),
            CreatedAt: ReadNullableDateTime(row, "created_at"));
    }

    /// <summary>
    /// Maps a data row to a TenantLifecycleEvent object.
    /// </summary>
    private static TenantLifecycleEvent MapRowToLifecycleEvent(Dictionary<string, object?> row)
    {
        return new TenantLifecycleEvent(
            EventId: ReadGuid(row, "event_id"),
            TenantId: ReadString(row, "tenant_id") ?? string.Empty,
            EventType: ReadString(row, "event_type") ?? string.Empty,
            EventStatus: ReadEnum(row, "event_status", TenantEventStatus.Completed),
            EventDetails: ReadString(row, "event_details"),
            CreatedAt: ReadNullableDateTime(row, "created_at"));
    }

    private static TenantQuotaPolicy MapRowToTenantQuotaPolicy(Dictionary<string, object?> row)
    {
        return new TenantQuotaPolicy(
            TenantId: ReadString(row, "tenant_id") ?? string.Empty,
            MaxActiveSessions: ReadInt(row, "max_active_sessions", 100),
            MaxRequestsPerSecond: ReadInt(row, "max_qps", 200),
            MaxStorageMb: ReadLong(row, "max_storage_mb", 1024),
            MaxBatchSize: ReadInt(row, "max_batch_size", 1000),
            UpdatedAt: ReadNullableDateTime(row, "updated_at"));
    }

    private static JsonDocument? ReadJsonMetadata(Dictionary<string, object?> row, string key)
    {
        var json = ReadString(row, key);
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return null;
        }

        return value.ToString();
    }

    private static DateTime? ReadNullableDateTime(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return null;
        }

        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                => parsed.ToUniversalTime(),
            _ => null
        };
    }

    private static Guid ReadGuid(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return Guid.Empty;
        }

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsedGuid) => parsedGuid,
            _ => Guid.Empty
        };
    }

    private static bool ReadBool(Dictionary<string, object?> row, string key)
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

    private static TEnum ReadEnum<TEnum>(Dictionary<string, object?> row, string key, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        var value = ReadString(row, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : defaultValue;
    }

    private static int ReadInt(Dictionary<string, object?> row, string key, int defaultValue)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return defaultValue;
        }

        return value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            short s => s,
            byte b => b,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static long ReadLong(Dictionary<string, object?> row, string key, long defaultValue)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return defaultValue;
        }

        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Escapes SQL string values to prevent injection (basic approach).
    /// For production, use parameterized queries instead.
    /// </summary>
    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // No resources to dispose
        await Task.CompletedTask;
    }
}
