using System.Data;
using System.Data.Common;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Extensions.Processor;

/// <summary>
/// Executes migration SQL against SharpCoreDB using a registered migration SQL executor,
/// embedded <see cref="IDatabase"/>, or an injected <see cref="DbConnection"/>.
/// </summary>
public sealed class SharpCoreDbMigrationExecutor(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public void ExecuteSql(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (TryGetCustomExecutor(out var executor))
        {
            executor.ExecuteSql(sql);
            return;
        }

        if (TryGetDatabase(out var database))
        {
            database.ExecuteSQL(sql);
            return;
        }

        using var command = CreateCommand();
        command.CommandText = sql;
        _ = command.ExecuteNonQuery();
    }

    public object? ExecuteScalar(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (TryGetCustomExecutor(out var executor))
        {
            return executor.ExecuteScalar(sql);
        }

        if (TryGetDatabase(out var database))
        {
            var rows = database.ExecuteQuery(sql);
            return rows.Count == 0 ? null : rows[0].Values.FirstOrDefault();
        }

        using var command = CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public DataSet Read(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        if (TryGetCustomExecutor(out var executor))
        {
            return executor.Read(sql);
        }

        var dataSet = new DataSet();
        var table = new DataTable("Result");

        if (TryGetDatabase(out var database))
        {
            var rows = database.ExecuteQuery(sql);
            if (rows.Count == 0)
            {
                dataSet.Tables.Add(table);
                return dataSet;
            }

            foreach (var columnName in rows[0].Keys)
            {
                table.Columns.Add(columnName, typeof(object));
            }

            foreach (var row in rows)
            {
                var dataRow = table.NewRow();
                foreach (var kvp in row)
                {
                    dataRow[kvp.Key] = kvp.Value ?? DBNull.Value;
                }
                table.Rows.Add(dataRow);
            }

            dataSet.Tables.Add(table);
            return dataSet;
        }

        using var command = CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();

        for (var i = 0; i < reader.FieldCount; i++)
        {
            table.Columns.Add(reader.GetName(i), typeof(object));
        }

        while (reader.Read())
        {
            var row = table.NewRow();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }
            table.Rows.Add(row);
        }

        dataSet.Tables.Add(table);
        return dataSet;
    }

    public IDbConnection? GetOperationConnection()
    {
        if (TryGetCustomExecutor(out var executor))
        {
            return executor.GetOperationConnection();
        }

        if (TryGetConnection(out var connection))
        {
            EnsureOpen(connection);
            return connection;
        }

        if (TryGetDatabase(out var database))
        {
            var operationConnection = CreateEmbeddedOperationConnection(database);
            EnsureOpen(operationConnection);
            return operationConnection;
        }

        return null;
    }

    public static void EnsureVersionTable(SharpCoreDbMigrationExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        executor.ExecuteSql(
            "CREATE TABLE IF NOT EXISTS __SharpMigrations (Version BIGINT NOT NULL PRIMARY KEY, AppliedOn TEXT NOT NULL, Description TEXT NULL)");
    }

    private DbCommand CreateCommand()
    {
        if (!TryGetConnection(out var connection))
        {
            throw new InvalidOperationException("No SharpCoreDB execution source found. Register ISharpCoreDbMigrationSqlExecutor, IDatabase, or DbConnection.");
        }

        EnsureOpen(connection);
        return connection.CreateCommand();
    }

    private bool TryGetCustomExecutor(out ISharpCoreDbMigrationSqlExecutor executor)
    {
        var resolved = _serviceProvider.GetService(typeof(ISharpCoreDbMigrationSqlExecutor)) as ISharpCoreDbMigrationSqlExecutor;
        if (resolved is not null)
        {
            executor = resolved;
            return true;
        }

        executor = default!;
        return false;
    }

    private bool TryGetDatabase(out IDatabase database)
    {
        var resolved = _serviceProvider.GetService(typeof(IDatabase)) as IDatabase;
        if (resolved is not null)
        {
            database = resolved;
            return true;
        }

        database = default!;
        return false;
    }

    private bool TryGetConnection(out DbConnection connection)
    {
        var resolved = _serviceProvider.GetService(typeof(DbConnection)) as DbConnection;
        if (resolved is not null)
        {
            connection = resolved;
            return true;
        }

        connection = default!;
        return false;
    }

    private static DbConnection CreateEmbeddedOperationConnection(IDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        return new global::SharpCoreDB.Extensions.DapperConnection(database, "sharpcoredb://embedded");
    }

    private static void EnsureOpen(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
    }
}
