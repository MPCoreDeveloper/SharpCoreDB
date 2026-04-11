// <copyright file="RowLevelPolicyRepository.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using System.Globalization;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Repository for persisting row-level isolation policies in the master database.
/// Follows the same schema bootstrap pattern as <see cref="DatabaseGrantsRepository"/>.
/// C# 14: Uses primary constructor for dependency injection.
/// </summary>
public sealed class RowLevelPolicyRepository(
    DatabaseInstance masterDatabase,
    RowLevelPolicyEngine policyEngine,
    ILogger<RowLevelPolicyRepository> logger) : IAsyncDisposable
{
    private readonly Lock _policyLock = new();

    /// <summary>
    /// Initializes the row_level_policies schema in the master database.
    /// </summary>
    public async Task InitializePolicySchemaAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Initializing row-level policy schema");

        try
        {
            lock (_policyLock)
            {
                EnsurePolicyTableExists();
                CreateIndexIfMissing("idx_rlp_tenant", "CREATE INDEX idx_rlp_tenant ON row_level_policies(tenant_id)");
                CreateIndexIfMissing("idx_rlp_database", "CREATE INDEX idx_rlp_database ON row_level_policies(database_name)");
                CreateIndexIfMissing("idx_rlp_table", "CREATE INDEX idx_rlp_table ON row_level_policies(table_name)");
            }

            // Load existing policies into the engine
            await LoadPoliciesIntoEngineAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation("Row-level policy schema initialized");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize row-level policy schema");
            throw;
        }
    }

    /// <summary>
    /// Creates and persists a new row-level policy.
    /// </summary>
    public async Task<RowLevelPolicy> CreatePolicyAsync(
        string tenantId,
        string databaseName,
        string tableName,
        string discriminatorColumn,
        RowLevelPolicyMode mode = RowLevelPolicyMode.Enforced,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(discriminatorColumn);

        logger.LogInformation(
            "Creating row-level policy for {Database}.{Table} (tenant={TenantId}, discriminator={Column}, mode={Mode})",
            databaseName, tableName, tenantId, discriminatorColumn, mode);

        var policy = RowLevelPolicy.Create(tenantId, databaseName, tableName, discriminatorColumn, mode);

        lock (_policyLock)
        {
            try
            {
                if (masterDatabase.Database.TryGetTable("row_level_policies", out var table))
                {
                    var row = new Dictionary<string, object>
                    {
                        { "policy_id", policy.PolicyId },
                        { "tenant_id", policy.TenantId },
                        { "database_name", policy.DatabaseName },
                        { "table_name", policy.TableName },
                        { "discriminator_column", policy.DiscriminatorColumn },
                        { "mode", (int)policy.Mode },
                        { "created_at", policy.CreatedAt.ToString("O", CultureInfo.InvariantCulture) }
                    };

                    table.Insert(row);
                    policyEngine.RegisterPolicy(policy);

                    logger.LogInformation("Row-level policy '{PolicyId}' created for {Database}.{Table}", policy.PolicyId, databaseName, tableName);
                }
                else
                {
                    throw new InvalidOperationException("row_level_policies table not found");
                }

                return policy;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create row-level policy");
                throw;
            }
        }
    }

    /// <summary>
    /// Removes a row-level policy by database and table name.
    /// </summary>
    public async Task RemovePolicyAsync(
        string databaseName,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        logger.LogInformation("Removing row-level policy for {Database}.{Table}", databaseName, tableName);

        lock (_policyLock)
        {
            try
            {
                var sql = $"DELETE FROM row_level_policies WHERE database_name = '{EscapeSql(databaseName)}' AND table_name = '{EscapeSql(tableName)}'";
                masterDatabase.Database.ExecuteSQL(sql);
                policyEngine.RemovePolicy(databaseName, tableName);

                logger.LogInformation("Row-level policy removed for {Database}.{Table}", databaseName, tableName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove row-level policy");
                throw;
            }
        }
    }

    /// <summary>
    /// Gets all persisted policies for a given database.
    /// </summary>
    public async Task<IReadOnlyList<RowLevelPolicy>> GetPoliciesAsync(
        string databaseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        lock (_policyLock)
        {
            try
            {
                if (!masterDatabase.Database.TryGetTable("row_level_policies", out var table))
                {
                    return [];
                }

                var policies = new List<RowLevelPolicy>();

                foreach (var row in table.Select())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rowDb = ReadString(row, "database_name");
                    if (!string.Equals(rowDb, databaseName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    policies.Add(RowFromDictionary(row));
                }

                return policies;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get policies for database '{DatabaseName}'", databaseName);
                throw;
            }
        }
    }

    /// <summary>
    /// Loads all persisted policies into the in-memory engine on startup.
    /// </summary>
    private async Task LoadPoliciesIntoEngineAsync(CancellationToken cancellationToken)
    {
        lock (_policyLock)
        {
            if (!masterDatabase.Database.TryGetTable("row_level_policies", out var table))
            {
                return;
            }

            var count = 0;
            foreach (var row in table.Select())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var policy = RowFromDictionary(row);
                policyEngine.RegisterPolicy(policy);
                count++;
            }

            if (count > 0)
            {
                logger.LogInformation("Loaded {Count} row-level policies from master database", count);
            }
        }
    }

    private static RowLevelPolicy RowFromDictionary(Dictionary<string, object> row)
    {
        return new RowLevelPolicy(
            PolicyId: ReadString(row, "policy_id") ?? string.Empty,
            TenantId: ReadString(row, "tenant_id") ?? string.Empty,
            DatabaseName: ReadString(row, "database_name") ?? string.Empty,
            TableName: ReadString(row, "table_name") ?? string.Empty,
            DiscriminatorColumn: ReadString(row, "discriminator_column") ?? "tenant_id",
            Mode: ReadPolicyMode(row, "mode"),
            CreatedAt: ReadDateTime(row, "created_at", DateTime.UtcNow));
    }

    private void EnsurePolicyTableExists()
    {
        if (masterDatabase.Database.TryGetTable("row_level_policies", out _))
        {
            return;
        }

        masterDatabase.Database.ExecuteSQL(
            "CREATE TABLE row_level_policies (policy_id TEXT PRIMARY KEY, tenant_id TEXT, database_name TEXT, table_name TEXT, discriminator_column TEXT, mode INTEGER, created_at TEXT)");
    }

    private void CreateIndexIfMissing(string indexName, string createIndexSql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(createIndexSql);

        try
        {
            masterDatabase.Database.ExecuteSQL(createIndexSql);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Index creation skipped for '{IndexName}'", indexName);
        }
    }

    private static RowLevelPolicyMode ReadPolicyMode(Dictionary<string, object> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null || value == DBNull.Value)
        {
            return RowLevelPolicyMode.Disabled;
        }

        return value switch
        {
            int i => (RowLevelPolicyMode)i,
            long l => (RowLevelPolicyMode)(int)l,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                => (RowLevelPolicyMode)parsed,
            _ => RowLevelPolicyMode.Disabled
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

    private static string EscapeSql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
