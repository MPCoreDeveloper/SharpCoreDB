#nullable enable

using System.Data.Common;
using Dotmim.Sync;
using Dotmim.Sync.Builders;

namespace SharpCoreDB.Provider.Sync.Builders;

/// <summary>
/// Database-level provisioning for SharpCoreDB sync.
/// Manages database existence checks, table enumeration, and schema-level operations.
/// </summary>
public sealed class SharpCoreDBDatabaseBuilder : DbDatabaseBuilder
{
    /// <inheritdoc />
    public override async Task EnsureDatabaseAsync(DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB database files are created automatically; no-op
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<SyncTable> EnsureTableAsync(string tableName, string? schemaName, DbConnection connection, DbTransaction transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";

        var result = await existsCommand.ExecuteScalarAsync().ConfigureAwait(false);
        var exists = Convert.ToInt32(result) > 0;

        if (!exists)
            throw new InvalidOperationException($"Table '{tableName}' does not exist. Create it first using SharpCoreDB DDL.");

        var syncTable = new SyncTable(tableName, schemaName);
        return syncTable;
    }

    /// <inheritdoc />
    public override async Task<SyncSetup> GetAllTablesAsync(DbConnection connection, DbTransaction transaction)
    {
        var setup = new SyncSetup();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '%_tracking' AND name NOT IN ('scope_info', 'scope_info_client')";

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var tableName = reader.GetString(0);
            setup.Tables.Add(tableName, null);
        }

        return setup;
    }

    /// <inheritdoc />
    public override async Task<(string DatabaseName, string Version)> GetHelloAsync(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 'SharpCoreDB' AS db, '1.0' AS version";

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            var dbName = reader.GetString(0);
            var version = reader.GetString(1);
            return (dbName, version);
        }

        return ("SharpCoreDB", "1.0");
    }

    /// <inheritdoc />
    public override async Task<SyncTable> GetTableAsync(string tableName, string? schemaName, DbConnection connection, DbTransaction transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info([{tableName}])";

        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var syncTable = new SyncTable(tableName, schemaName);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var columnName = reader.GetString(1);
            var dataType = reader.GetString(2);
            var notNull = reader.GetInt32(3) == 1;
            var isPk = reader.GetInt32(5) == 1;

            var syncColumn = new SyncColumn(columnName)
            {
                DataType = dataType,
                AllowDBNull = !notNull
            };

            syncTable.Columns.Add(syncColumn);

            if (isPk)
            {
                syncTable.PrimaryKeys.Add(columnName);
            }
        }

        return syncTable;
    }

    /// <inheritdoc />
    public override async Task<bool> ExistsTableAsync(string tableName, string? schemaName, DbConnection connection, DbTransaction transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";

        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
    }

    /// <inheritdoc />
    public override async Task DropsTableIfExistsAsync(string tableName, string? schemaName, DbConnection connection, DbTransaction transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TABLE IF EXISTS [{tableName}]";

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task RenameTableAsync(string tableName, string? schemaName, string newTableName, string? newSchemaName, DbConnection connection, DbTransaction transaction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTableName);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE [{tableName}] RENAME TO [{newTableName}]";

        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
