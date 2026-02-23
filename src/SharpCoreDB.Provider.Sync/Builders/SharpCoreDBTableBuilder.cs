#nullable enable

using System.Data;
using System.Data.Common;
using Dotmim.Sync;
using Dotmim.Sync.Builders;
using Dotmim.Sync.Manager;

namespace SharpCoreDB.Provider.Sync.Builders;

/// <summary>
/// Table-level provisioning for SharpCoreDB sync.
/// Manages tracking tables, triggers, and stored procedures (stub) per sync table.
/// </summary>
public sealed class SharpCoreDBTableBuilder(SyncTable tableDescription, ScopeInfo scopeInfo) : DbTableBuilder(tableDescription, scopeInfo)
{
    private readonly SyncTable _tableDescription = tableDescription ?? throw new ArgumentNullException(nameof(tableDescription));
    private readonly ScopeInfo _scopeInfo = scopeInfo ?? throw new ArgumentNullException(nameof(scopeInfo));

    /// <inheritdoc />
    public override DbTableNames GetParsedTableNames()
    {
        var tableName = _tableDescription.TableName;
        var schemaName = _tableDescription.SchemaName;
        var normalizedName = string.IsNullOrEmpty(schemaName) ? tableName : $"{schemaName}.{tableName}";

        return new DbTableNames('[', ']', tableName, normalizedName, tableName,
            $"[{tableName}]", $"[{normalizedName}]", schemaName);
    }

    /// <inheritdoc />
    public override DbTableNames GetParsedTrackingTableNames()
    {
        var tableName = _tableDescription.TableName;
        var trackingTableName = $"{tableName}_tracking";
        var schemaName = _tableDescription.SchemaName;
        var normalizedName = string.IsNullOrEmpty(schemaName) ? trackingTableName : $"{schemaName}.{trackingTableName}";

        return new DbTableNames('[', ']', trackingTableName, normalizedName, trackingTableName,
            $"[{trackingTableName}]", $"[{normalizedName}]", schemaName);
    }

    /// <inheritdoc />
    public override DbColumnNames GetParsedColumnNames(SyncColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var columnName = column.ColumnName;
        return new DbColumnNames($"[{columnName}]", columnName);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetCreateSchemaCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB does not support schemas; return no-op command
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetCreateTableCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        // Base table creation is handled by SharpCoreDB DDL; return no-op
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetExistsTableCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{_tableDescription.TableName}'";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetExistsSchemaCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB does not support schemas
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetDropTableCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TABLE IF EXISTS [{_tableDescription.TableName}]";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetExistsColumnCommandAsync(string columnName, DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info([{_tableDescription.TableName}])";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetAddColumnCommandAsync(string columnName, DbConnection connection, DbTransaction transaction)
    {
        // Column addition requires ALTER TABLE ADD COLUMN
        throw new NotSupportedException("Dynamic column addition not supported in sync provisioning.");
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetDropColumnCommandAsync(string columnName, DbConnection connection, DbTransaction transaction)
    {
        throw new NotSupportedException("Dynamic column removal not supported in sync provisioning.");
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetExistsStoredProcedureCommandAsync(DbStoredProcedureType storedProcedureType, SyncFilter? filter, DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB does not use stored procedures for sync
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 0";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetCreateStoredProcedureCommandAsync(DbStoredProcedureType storedProcedureType, SyncFilter? filter, DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB does not use stored procedures for sync
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetDropStoredProcedureCommandAsync(DbStoredProcedureType storedProcedureType, SyncFilter? filter, DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB does not use stored procedures for sync
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetCreateTrackingTableCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        var trackingTableName = $"{_tableDescription.TableName}_tracking";
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{_tableDescription.TableName}' must have a primary key.");

        var pkSyncColumn = _tableDescription.Columns[pkColumn];
        var pkType = MapDbTypeToSqlType(pkSyncColumn.DbType);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            CREATE TABLE IF NOT EXISTS {trackingTableName} (
                {pkColumn} {pkType} PRIMARY KEY NOT NULL,
                update_scope_id TEXT,
                timestamp BIGINT NOT NULL,
                sync_row_is_tombstone INTEGER NOT NULL DEFAULT 0,
                last_change_datetime TEXT NOT NULL
            )";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetDropTrackingTableCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        var trackingTableName = $"{_tableDescription.TableName}_tracking";
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TABLE IF EXISTS [{trackingTableName}]";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetExistsTrackingTableCommandAsync(DbConnection connection, DbTransaction transaction)
    {
        var trackingTableName = $"{_tableDescription.TableName}_tracking";
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{trackingTableName}'";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetExistsTriggerCommandAsync(DbTriggerType triggerType, DbConnection connection, DbTransaction transaction)
    {
        var triggerName = triggerType switch
        {
            DbTriggerType.Insert => $"trg_{_tableDescription.TableName}_insert_tracking",
            DbTriggerType.Update => $"trg_{_tableDescription.TableName}_update_tracking",
            DbTriggerType.Delete => $"trg_{_tableDescription.TableName}_delete_tracking",
            _ => throw new ArgumentOutOfRangeException(nameof(triggerType))
        };

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND name='{triggerName}'";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetCreateTriggerCommandAsync(DbTriggerType triggerType, DbConnection connection, DbTransaction transaction)
    {
        var tableName = _tableDescription.TableName;
        var trackingTableName = $"{tableName}_tracking";
        var pkColumn = _tableDescription.PrimaryKeys.FirstOrDefault()
            ?? throw new InvalidOperationException($"Table '{tableName}' must have a primary key.");

        var triggerName = triggerType switch
        {
            DbTriggerType.Insert => $"trg_{tableName}_insert_tracking",
            DbTriggerType.Update => $"trg_{tableName}_update_tracking",
            DbTriggerType.Delete => $"trg_{tableName}_delete_tracking",
            _ => throw new ArgumentOutOfRangeException(nameof(triggerType))
        };

        var triggerSql = triggerType switch
        {
            DbTriggerType.Insert => $@"
                CREATE TRIGGER IF NOT EXISTS [{triggerName}] AFTER INSERT ON [{tableName}] BEGIN
                    INSERT OR REPLACE INTO [{trackingTableName}] ([{pkColumn}], [update_scope_id], [timestamp], [sync_row_is_tombstone], [last_change_datetime])
                    VALUES (NEW.[{pkColumn}], NULL, SYNC_TIMESTAMP(), 0, CURRENT_TIMESTAMP);
                END",

            DbTriggerType.Update => $@"
                CREATE TRIGGER IF NOT EXISTS [{triggerName}] AFTER UPDATE ON [{tableName}] BEGIN
                    UPDATE [{trackingTableName}] SET [update_scope_id] = NULL, [timestamp] = SYNC_TIMESTAMP(), [sync_row_is_tombstone] = 0, [last_change_datetime] = CURRENT_TIMESTAMP
                    WHERE [{pkColumn}] = OLD.[{pkColumn}];
                END",

            DbTriggerType.Delete => $@"
                CREATE TRIGGER IF NOT EXISTS [{triggerName}] AFTER DELETE ON [{tableName}] BEGIN
                    UPDATE [{trackingTableName}] SET [update_scope_id] = NULL, [timestamp] = SYNC_TIMESTAMP(), [sync_row_is_tombstone] = 1, [last_change_datetime] = CURRENT_TIMESTAMP
                    WHERE [{pkColumn}] = OLD.[{pkColumn}];
                END",

            _ => throw new ArgumentOutOfRangeException(nameof(triggerType))
        };

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = triggerSql;
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<DbCommand> GetDropTriggerCommandAsync(DbTriggerType triggerType, DbConnection connection, DbTransaction transaction)
    {
        var triggerName = triggerType switch
        {
            DbTriggerType.Insert => $"trg_{_tableDescription.TableName}_insert_tracking",
            DbTriggerType.Update => $"trg_{_tableDescription.TableName}_update_tracking",
            DbTriggerType.Delete => $"trg_{_tableDescription.TableName}_delete_tracking",
            _ => throw new ArgumentOutOfRangeException(nameof(triggerType))
        };

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TRIGGER IF EXISTS [{triggerName}]";
        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public override Task<IEnumerable<SyncColumn>> GetColumnsAsync(DbConnection connection, DbTransaction transaction)
    {
        // Columns already provided by SyncTable from orchestrator
        return Task.FromResult<IEnumerable<SyncColumn>>(_tableDescription.Columns);
    }

    /// <inheritdoc />
    public override Task<IEnumerable<DbRelationDefinition>> GetRelationsAsync(DbConnection connection, DbTransaction transaction)
    {
        // SharpCoreDB foreign keys are enforced via application logic; return empty
        return Task.FromResult<IEnumerable<DbRelationDefinition>>([]);
    }

    /// <inheritdoc />
    public override Task<IEnumerable<SyncColumn>> GetPrimaryKeysAsync(DbConnection connection, DbTransaction transaction)
    {
        var pkColumns = _tableDescription.PrimaryKeys
            .Select(pkName => _tableDescription.Columns[pkName])
            .ToList();
        return Task.FromResult<IEnumerable<SyncColumn>>(pkColumns);
    }

    private static string MapDbTypeToSqlType(int dbType)
    {
        var type = (DbType)dbType;
        return type switch
        {
            DbType.Int32 or DbType.Int16 or DbType.Byte or DbType.SByte or DbType.UInt16 or DbType.Boolean => "INTEGER",
            DbType.Int64 or DbType.UInt32 or DbType.UInt64 => "BIGINT",
            DbType.String or DbType.StringFixedLength or DbType.AnsiString or DbType.AnsiStringFixedLength => "TEXT",
            DbType.Double or DbType.Single => "REAL",
            DbType.Decimal or DbType.Currency or DbType.VarNumeric => "DECIMAL",
            DbType.DateTime or DbType.DateTime2 or DbType.DateTimeOffset or DbType.Date or DbType.Time => "DATETIME",
            DbType.Guid => "GUID",
            DbType.Binary or DbType.Object => "BLOB",
            _ => "TEXT"
        };
    }
}
