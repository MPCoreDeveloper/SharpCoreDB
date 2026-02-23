#nullable enable

using System.Data;
using System.Data.Common;
using Dotmim.Sync;
using Dotmim.Sync.Builders;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Provider.Sync.Builders;

/// <summary>
/// Scope metadata storage for SharpCoreDB.
/// Manages ScopeInfo and ScopeInfoClient tables required by Dotmim.Sync.
/// </summary>
public sealed class SharpCoreDBScopeInfoBuilder : DbScopeBuilder
{
    private const string ScopeInfoTableName = "scope_info";
    private const string ScopeInfoClientTableName = "scope_info_client";

    /// <inheritdoc />
    public override DbTableNames GetParsedScopeInfoTableNames()
    {
        return new DbTableNames('[', ']', ScopeInfoTableName, ScopeInfoTableName, ScopeInfoTableName,
            $"[{ScopeInfoTableName}]", $"[{ScopeInfoTableName}]", null!);
    }

    /// <inheritdoc />
    public override DbTableNames GetParsedScopeInfoClientTableNames()
    {
        return new DbTableNames('[', ']', ScopeInfoClientTableName, ScopeInfoClientTableName, ScopeInfoClientTableName,
            $"[{ScopeInfoClientTableName}]", $"[{ScopeInfoClientTableName}]", null!);
    }

    /// <inheritdoc />
    public override DbCommand GetExistsScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{ScopeInfoTableName}'";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetExistsScopeInfoClientTableCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{ScopeInfoClientTableName}'";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetCreateScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            CREATE TABLE [{ScopeInfoTableName}] (
                [sync_scope_id] TEXT PRIMARY KEY NOT NULL,
                [sync_scope_name] TEXT NOT NULL,
                [sync_scope_schema] TEXT NULL,
                [sync_scope_setup] TEXT NULL,
                [sync_scope_version] TEXT NULL,
                [scope_last_sync] INTEGER NULL,
                [scope_last_sync_timestamp] INTEGER NULL,
                [scope_last_server_sync_timestamp] INTEGER NULL,
                [scope_last_sync_duration] INTEGER NULL
            )";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetCreateScopeInfoClientTableCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            CREATE TABLE [{ScopeInfoClientTableName}] (
                [sync_scope_id] TEXT PRIMARY KEY NOT NULL,
                [sync_scope_name] TEXT NOT NULL,
                [sync_scope_hash] TEXT NOT NULL,
                [sync_scope_parameters] TEXT NULL,
                [scope_last_sync] INTEGER NULL,
                [scope_last_server_sync_timestamp] INTEGER NULL,
                [scope_last_sync_timestamp] INTEGER NULL,
                [scope_last_sync_duration] INTEGER NULL,
                [sync_scope_errors] TEXT NULL,
                [sync_scope_properties] TEXT NULL
            )";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetAllScopeInfosCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM [{ScopeInfoTableName}]";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetAllScopeInfoClientsCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM [{ScopeInfoClientTableName}]";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetScopeInfoCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM [{ScopeInfoTableName}] WHERE [sync_scope_name] = @sync_scope_name";
        
        var param = command.CreateParameter();
        param.ParameterName = "@sync_scope_name";
        param.DbType = DbType.String;
        command.Parameters.Add(param);
        
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT * FROM [{ScopeInfoClientTableName}] WHERE [sync_scope_id] = @sync_scope_id AND [sync_scope_name] = @sync_scope_name AND [sync_scope_hash] = @sync_scope_hash";
        
        var paramId = command.CreateParameter();
        paramId.ParameterName = "@sync_scope_id";
        paramId.DbType = DbType.String;
        command.Parameters.Add(paramId);

        var paramName = command.CreateParameter();
        paramName.ParameterName = "@sync_scope_name";
        paramName.DbType = DbType.String;
        command.Parameters.Add(paramName);

        var paramHash = command.CreateParameter();
        paramHash.ParameterName = "@sync_scope_hash";
        paramHash.DbType = DbType.String;
        command.Parameters.Add(paramHash);

        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetInsertScopeInfoCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            INSERT OR REPLACE INTO [{ScopeInfoTableName}]
            ([sync_scope_id], [sync_scope_name], [sync_scope_schema], [sync_scope_setup], [sync_scope_version],
             [scope_last_sync], [scope_last_sync_timestamp], [scope_last_server_sync_timestamp], [scope_last_sync_duration])
            VALUES
            (@sync_scope_id, @sync_scope_name, @sync_scope_schema, @sync_scope_setup, @sync_scope_version,
             @scope_last_sync, @scope_last_sync_timestamp, @scope_last_server_sync_timestamp, @scope_last_sync_duration)";

        AddScopeInfoParameters(command);
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetInsertScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            INSERT OR REPLACE INTO [{ScopeInfoClientTableName}]
            ([sync_scope_id], [sync_scope_name], [sync_scope_hash], [sync_scope_parameters],
             [scope_last_sync], [scope_last_server_sync_timestamp], [scope_last_sync_timestamp],
             [scope_last_sync_duration], [sync_scope_errors], [sync_scope_properties])
            VALUES
            (@sync_scope_id, @sync_scope_name, @sync_scope_hash, @sync_scope_parameters,
             @scope_last_sync, @scope_last_server_sync_timestamp, @scope_last_sync_timestamp,
             @scope_last_sync_duration, @sync_scope_errors, @sync_scope_properties)";

        AddScopeInfoClientParameters(command);
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetDeleteScopeInfoCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM [{ScopeInfoTableName}] WHERE [sync_scope_name] = @sync_scope_name";

        var param = command.CreateParameter();
        param.ParameterName = "@sync_scope_name";
        param.DbType = DbType.String;
        command.Parameters.Add(param);

        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetDeleteScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM [{ScopeInfoClientTableName}] WHERE [sync_scope_id] = @sync_scope_id AND [sync_scope_name] = @sync_scope_name AND [sync_scope_hash] = @sync_scope_hash";

        var paramId = command.CreateParameter();
        paramId.ParameterName = "@sync_scope_id";
        paramId.DbType = DbType.String;
        command.Parameters.Add(paramId);

        var paramName = command.CreateParameter();
        paramName.ParameterName = "@sync_scope_name";
        paramName.DbType = DbType.String;
        command.Parameters.Add(paramName);

        var paramHash = command.CreateParameter();
        paramHash.ParameterName = "@sync_scope_hash";
        paramHash.DbType = DbType.String;
        command.Parameters.Add(paramHash);

        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetUpdateScopeInfoCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            UPDATE [{ScopeInfoTableName}] SET
                [sync_scope_schema] = @sync_scope_schema,
                [sync_scope_setup] = @sync_scope_setup,
                [sync_scope_version] = @sync_scope_version,
                [scope_last_sync] = @scope_last_sync,
                [scope_last_sync_timestamp] = @scope_last_sync_timestamp,
                [scope_last_server_sync_timestamp] = @scope_last_server_sync_timestamp,
                [scope_last_sync_duration] = @scope_last_sync_duration
            WHERE [sync_scope_id] = @sync_scope_id AND [sync_scope_name] = @sync_scope_name";

        AddScopeInfoParameters(command);
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetUpdateScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $@"
            UPDATE [{ScopeInfoClientTableName}] SET
                [sync_scope_parameters] = @sync_scope_parameters,
                [scope_last_sync] = @scope_last_sync,
                [scope_last_server_sync_timestamp] = @scope_last_server_sync_timestamp,
                [scope_last_sync_timestamp] = @scope_last_sync_timestamp,
                [scope_last_sync_duration] = @scope_last_sync_duration,
                [sync_scope_errors] = @sync_scope_errors,
                [sync_scope_properties] = @sync_scope_properties
            WHERE [sync_scope_id] = @sync_scope_id AND [sync_scope_name] = @sync_scope_name AND [sync_scope_hash] = @sync_scope_hash";

        AddScopeInfoClientParameters(command);
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetLocalTimestampCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT SYNC_TIMESTAMP()";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetDropScopeInfoTableCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TABLE IF EXISTS [{ScopeInfoTableName}]";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetDropScopeInfoClientTableCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DROP TABLE IF EXISTS [{ScopeInfoClientTableName}]";
        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetExistsScopeInfoCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM [{ScopeInfoTableName}] WHERE [sync_scope_name] = @sync_scope_name";

        var param = command.CreateParameter();
        param.ParameterName = "@sync_scope_name";
        param.DbType = DbType.String;
        command.Parameters.Add(param);

        return command;
    }

    /// <inheritdoc />
    public override DbCommand GetExistsScopeInfoClientCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM [{ScopeInfoClientTableName}] WHERE [sync_scope_id] = @sync_scope_id AND [sync_scope_name] = @sync_scope_name AND [sync_scope_hash] = @sync_scope_hash";

        var paramId = command.CreateParameter();
        paramId.ParameterName = "@sync_scope_id";
        paramId.DbType = DbType.String;
        command.Parameters.Add(paramId);

        var paramName = command.CreateParameter();
        paramName.ParameterName = "@sync_scope_name";
        paramName.DbType = DbType.String;
        command.Parameters.Add(paramName);

        var paramHash = command.CreateParameter();
        paramHash.ParameterName = "@sync_scope_hash";
        paramHash.DbType = DbType.String;
        command.Parameters.Add(paramHash);

        return command;
    }

    private static void AddScopeInfoParameters(DbCommand command)
    {
        var paramId = command.CreateParameter();
        paramId.ParameterName = "@sync_scope_id";
        paramId.DbType = DbType.String;
        command.Parameters.Add(paramId);

        var paramName = command.CreateParameter();
        paramName.ParameterName = "@sync_scope_name";
        paramName.DbType = DbType.String;
        command.Parameters.Add(paramName);

        var paramSchema = command.CreateParameter();
        paramSchema.ParameterName = "@sync_scope_schema";
        paramSchema.DbType = DbType.String;
        command.Parameters.Add(paramSchema);

        var paramSetup = command.CreateParameter();
        paramSetup.ParameterName = "@sync_scope_setup";
        paramSetup.DbType = DbType.String;
        command.Parameters.Add(paramSetup);

        var paramVersion = command.CreateParameter();
        paramVersion.ParameterName = "@sync_scope_version";
        paramVersion.DbType = DbType.String;
        command.Parameters.Add(paramVersion);

        var paramLastSync = command.CreateParameter();
        paramLastSync.ParameterName = "@scope_last_sync";
        paramLastSync.DbType = DbType.Int64;
        command.Parameters.Add(paramLastSync);

        var paramLastSyncTimestamp = command.CreateParameter();
        paramLastSyncTimestamp.ParameterName = "@scope_last_sync_timestamp";
        paramLastSyncTimestamp.DbType = DbType.Int64;
        command.Parameters.Add(paramLastSyncTimestamp);

        var paramLastServerSyncTimestamp = command.CreateParameter();
        paramLastServerSyncTimestamp.ParameterName = "@scope_last_server_sync_timestamp";
        paramLastServerSyncTimestamp.DbType = DbType.Int64;
        command.Parameters.Add(paramLastServerSyncTimestamp);

        var paramLastSyncDuration = command.CreateParameter();
        paramLastSyncDuration.ParameterName = "@scope_last_sync_duration";
        paramLastSyncDuration.DbType = DbType.Int64;
        command.Parameters.Add(paramLastSyncDuration);
    }

    private static void AddScopeInfoClientParameters(DbCommand command)
    {
        var paramId = command.CreateParameter();
        paramId.ParameterName = "@sync_scope_id";
        paramId.DbType = DbType.String;
        command.Parameters.Add(paramId);

        var paramName = command.CreateParameter();
        paramName.ParameterName = "@sync_scope_name";
        paramName.DbType = DbType.String;
        command.Parameters.Add(paramName);

        var paramHash = command.CreateParameter();
        paramHash.ParameterName = "@sync_scope_hash";
        paramHash.DbType = DbType.String;
        command.Parameters.Add(paramHash);

        var paramParameters = command.CreateParameter();
        paramParameters.ParameterName = "@sync_scope_parameters";
        paramParameters.DbType = DbType.String;
        command.Parameters.Add(paramParameters);

        var paramLastSync = command.CreateParameter();
        paramLastSync.ParameterName = "@scope_last_sync";
        paramLastSync.DbType = DbType.Int64;
        command.Parameters.Add(paramLastSync);

        var paramLastServerSyncTimestamp = command.CreateParameter();
        paramLastServerSyncTimestamp.ParameterName = "@scope_last_server_sync_timestamp";
        paramLastServerSyncTimestamp.DbType = DbType.Int64;
        command.Parameters.Add(paramLastServerSyncTimestamp);

        var paramLastSyncTimestamp = command.CreateParameter();
        paramLastSyncTimestamp.ParameterName = "@scope_last_sync_timestamp";
        paramLastSyncTimestamp.DbType = DbType.Int64;
        command.Parameters.Add(paramLastSyncTimestamp);

        var paramLastSyncDuration = command.CreateParameter();
        paramLastSyncDuration.ParameterName = "@scope_last_sync_duration";
        paramLastSyncDuration.DbType = DbType.Int64;
        command.Parameters.Add(paramLastSyncDuration);

        var paramErrors = command.CreateParameter();
        paramErrors.ParameterName = "@sync_scope_errors";
        paramErrors.DbType = DbType.String;
        command.Parameters.Add(paramErrors);

        var paramProperties = command.CreateParameter();
        paramProperties.ParameterName = "@sync_scope_properties";
        paramProperties.DbType = DbType.String;
        command.Parameters.Add(paramProperties);
    }
}
