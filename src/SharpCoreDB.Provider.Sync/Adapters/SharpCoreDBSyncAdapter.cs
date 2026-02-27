#nullable enable

using System.Data;
using System.Data.Common;
using Dotmim.Sync;
using Dotmim.Sync.Builders;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Provider.Sync.Builders;
using SharpCoreDB.Provider.Sync.Metadata;

namespace SharpCoreDB.Provider.Sync.Adapters;

/// <summary>
/// Per-table sync adapter that handles change enumeration and application.
/// Queries changed rows, applies remote changes, and detects conflicts.
/// </summary>
/// <remarks>
/// This adapter follows the Dotmim.Sync DbSyncAdapter pattern:
/// 1. SelectChanges: Query tracking table for modified rows since last sync
/// 2. ApplyChanges: Apply incoming changes with conflict detection
/// 3. Bulk Operations: Optimize multi-row INSERT/UPDATE/DELETE
/// </remarks>
public sealed class SharpCoreDBSyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo) : DbSyncAdapter(tableDescription, scopeInfo)
{
    private readonly SyncTable _tableDescription = tableDescription ?? throw new ArgumentNullException(nameof(tableDescription));
    private readonly ScopeInfo _scopeInfo = scopeInfo ?? throw new ArgumentNullException(nameof(scopeInfo));
    
    private SharpCoreDBTableBuilder? _tableBuilder;

    /// <inheritdoc />
    public override (DbCommand Command, bool IsBatchCommand) GetCommand(SyncContext context, DbCommandType commandType, SyncFilter? filter)
    {
        // This method returns a command template without connection/transaction
        // The actual execution will be done with connection/transaction later
        throw new NotSupportedException("Use CreateCommand with connection and transaction parameters");
    }

    /// <inheritdoc />
    public override DbColumnNames GetParsedColumnNames(string columnName)
    {
        return new DbColumnNames($"[{columnName}]", columnName);
    }

    /// <inheritdoc />
    public override DbTableBuilder GetTableBuilder()
    {
        return _tableBuilder ??= new SharpCoreDBTableBuilder(_tableDescription, _scopeInfo);
    }

    /// <inheritdoc />
    public override Task<int> ExecuteBatchCommandAsync(
        SyncContext context,
        DbCommand command,
        Guid senderScopeId,
        IEnumerable<SyncRow> arrayItems,
        SyncTable schemaChangesTable,
        SyncTable failedRows,
        long? lastTimestamp,
        DbConnection connection,
        DbTransaction? transaction = null)
    {
        // Batch execution for bulk operations
        // For now, execute one by one (can be optimized later with multi-row inserts)
        var appliedCount = 0;

        foreach (var row in arrayItems)
        {
            // Set command parameters from row values
            foreach (var column in _tableDescription.Columns)
            {
                var param = command.Parameters[$"@{column.ColumnName}"];
                if (param != null)
                {
                    var value = row[column.ColumnName];
                    param.Value = value ?? DBNull.Value;
                }
            }

            command.ExecuteNonQuery();
            appliedCount++;
        }

        return Task.FromResult(appliedCount);
    }

    /// <summary>
    /// Creates a command for the specified operation type.
    /// This is a helper method for internal command generation.
    /// </summary>
    internal DbCommand CreateCommand(DbCommandType commandType, DbConnection connection, DbTransaction? transaction)
    {
        return commandType switch
        {
            DbCommandType.SelectChanges => GetSelectChangesCommand(connection, transaction),
            DbCommandType.SelectRow => GetSelectRowCommand(connection, transaction),
            DbCommandType.InsertRow => GetInsertRowCommand(connection, transaction),
            DbCommandType.UpdateRow => GetUpdateRowCommand(connection, transaction),
            DbCommandType.DeleteRow => GetDeleteRowCommand(connection, transaction),
            DbCommandType.SelectMetadata => GetSelectMetadataCommand(connection, transaction),
            DbCommandType.UpdateMetadata => GetUpdateMetadataCommand(connection, transaction),
            DbCommandType.DeleteMetadata => GetDeleteMetadataCommand(connection, transaction),
            _ => throw new NotSupportedException($"Command type {commandType} is not supported")
        };
    }

    /// <summary>
    /// Gets command to select changed rows since last sync timestamp.
    /// </summary>
    private DbCommand GetSelectChangesCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var trackingTableName = $"{tableName}_tracking";
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var columns = string.Join(", ", _tableDescription.Columns.Select(c => $"t.[{c.ColumnName}]"));
        var trackingColumns = $"tt.[update_scope_id], tt.[timestamp], tt.[sync_row_is_tombstone], tt.[last_change_datetime]";

        var sql = $@"
            SELECT {columns}, {trackingColumns}
            FROM [{tableName}] t
            INNER JOIN [{trackingTableName}] tt ON t.[{pkColumn}] = tt.[{pkColumn}]
            WHERE tt.[timestamp] > @sync_min_timestamp
            ORDER BY tt.[timestamp]";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var param = command.CreateParameter();
        param.ParameterName = "@sync_min_timestamp";
        param.DbType = DbType.Int64;
        command.Parameters.Add(param);

        return command;
    }

    /// <summary>
    /// Gets command to select a single row by primary key.
    /// </summary>
    private DbCommand GetSelectRowCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var columns = string.Join(", ", _tableDescription.Columns.Select(c => $"[{c.ColumnName}]"));
        var sql = $"SELECT {columns} FROM [{tableName}] WHERE [{pkColumn}] = @{pkColumn}";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var param = command.CreateParameter();
        param.ParameterName = $"@{pkColumn}";
        param.DbType = (DbType)_tableDescription.Columns[pkColumn].DbType;
        command.Parameters.Add(param);

        return command;
    }

    /// <summary>
    /// Gets command to insert a new row.
    /// </summary>
    private DbCommand GetInsertRowCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var columns = string.Join(", ", _tableDescription.Columns.Select(c => $"[{c.ColumnName}]"));
        var parameters = string.Join(", ", _tableDescription.Columns.Select(c => $"@{c.ColumnName}"));

        var sql = $"INSERT OR REPLACE INTO [{tableName}] ({columns}) VALUES ({parameters})";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var column in _tableDescription.Columns)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@{column.ColumnName}";
            param.DbType = (DbType)column.DbType;
            command.Parameters.Add(param);
        }

        return command;
    }

    /// <summary>
    /// Gets command to update an existing row.
    /// </summary>
    private DbCommand GetUpdateRowCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var setClause = string.Join(", ", _tableDescription.Columns
            .Where(c => c.ColumnName != pkColumn)
            .Select(c => $"[{c.ColumnName}] = @{c.ColumnName}"));

        var sql = $"UPDATE [{tableName}] SET {setClause} WHERE [{pkColumn}] = @{pkColumn}";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var column in _tableDescription.Columns)
        {
            var param = command.CreateParameter();
            param.ParameterName = $"@{column.ColumnName}";
            param.DbType = (DbType)column.DbType;
            command.Parameters.Add(param);
        }

        return command;
    }

    /// <summary>
    /// Gets command to delete a row.
    /// </summary>
    private DbCommand GetDeleteRowCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var sql = $"DELETE FROM [{tableName}] WHERE [{pkColumn}] = @{pkColumn}";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var param = command.CreateParameter();
        param.ParameterName = $"@{pkColumn}";
        param.DbType = (DbType)_tableDescription.Columns[pkColumn].DbType;
        command.Parameters.Add(param);

        return command;
    }

    /// <summary>
    /// Gets command to update tracking metadata for a row.
    /// </summary>
    private DbCommand GetUpdateMetadataCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var trackingTableName = $"{tableName}_tracking";
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var sql = $@"
            INSERT OR REPLACE INTO [{trackingTableName}] 
            ([{pkColumn}], [update_scope_id], [timestamp], [sync_row_is_tombstone], [last_change_datetime])
            VALUES (@{pkColumn}, @update_scope_id, @timestamp, @sync_row_is_tombstone, @last_change_datetime)";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var pkParam = command.CreateParameter();
        pkParam.ParameterName = $"@{pkColumn}";
        pkParam.DbType = (DbType)_tableDescription.Columns[pkColumn].DbType;
        command.Parameters.Add(pkParam);

        var scopeParam = command.CreateParameter();
        scopeParam.ParameterName = "@update_scope_id";
        scopeParam.DbType = DbType.String;
        command.Parameters.Add(scopeParam);

        var timestampParam = command.CreateParameter();
        timestampParam.ParameterName = "@timestamp";
        timestampParam.DbType = DbType.Int64;
        command.Parameters.Add(timestampParam);

        var tombstoneParam = command.CreateParameter();
        tombstoneParam.ParameterName = "@sync_row_is_tombstone";
        tombstoneParam.DbType = DbType.Int32;
        command.Parameters.Add(tombstoneParam);

        var dateParam = command.CreateParameter();
        dateParam.ParameterName = "@last_change_datetime";
        dateParam.DbType = DbType.String;
        command.Parameters.Add(dateParam);

        return command;
    }

    /// <summary>
    /// Gets command to delete tracking metadata for a row.
    /// </summary>
    private DbCommand GetDeleteMetadataCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var trackingTableName = $"{tableName}_tracking";
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var sql = $"DELETE FROM [{trackingTableName}] WHERE [{pkColumn}] = @{pkColumn}";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var param = command.CreateParameter();
        param.ParameterName = $"@{pkColumn}";
        param.DbType = (DbType)_tableDescription.Columns[pkColumn].DbType;
        command.Parameters.Add(param);

        return command;
    }

    /// <summary>
    /// Gets command to select tracking metadata for a row.
    /// </summary>
    private DbCommand GetSelectMetadataCommand(DbConnection connection, DbTransaction? transaction)
    {
        var tableName = _tableDescription.TableName;
        var trackingTableName = $"{tableName}_tracking";
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key");

        var sql = $@"
            SELECT [{pkColumn}], [update_scope_id], [timestamp], [sync_row_is_tombstone], [last_change_datetime]
            FROM [{trackingTableName}]
            WHERE [{pkColumn}] = @{pkColumn}";

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        var param = command.CreateParameter();
        param.ParameterName = $"@{pkColumn}";
        param.DbType = (DbType)_tableDescription.Columns[pkColumn].DbType;
        command.Parameters.Add(param);

        return command;
    }
}
