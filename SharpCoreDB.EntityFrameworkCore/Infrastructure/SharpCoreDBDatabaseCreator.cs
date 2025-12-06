using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using SharpCoreDB.EntityFrameworkCore.Storage;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Database creator implementation for SharpCoreDB.
/// Handles database and table creation/deletion.
/// </summary>
public class SharpCoreDBDatabaseCreator : RelationalDatabaseCreator
{
    private readonly IRelationalConnection _connection;
    private readonly IModel _model;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBDatabaseCreator class.
    /// </summary>
    public SharpCoreDBDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IRelationalConnection connection,
        IModel model)
        : base(dependencies)
    {
        _connection = connection;
        _model = model;
    }

    /// <inheritdoc />
    public override bool Exists()
    {
        try
        {
            _connection.Open();
            _connection.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.OpenAsync(cancellationToken);
            await _connection.CloseAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override void Create()
    {
        // SharpCoreDB creates the database automatically on first connection
        // Just ensure we can connect
        _connection.Open();
        _connection.Close();
    }

    /// <inheritdoc />
    public override async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        // SharpCoreDB creates the database automatically on first connection
        // Just ensure we can connect
        await _connection.OpenAsync(cancellationToken);
        await _connection.CloseAsync();
    }

    /// <inheritdoc />
    public override void Delete()
    {
        // Get the database path from connection and delete it
        if (_connection.DbConnection is SharpCoreDBConnection sharpConnection)
        {
            var dbPath = sharpConnection.DataSource;
            if (!string.IsNullOrEmpty(dbPath) && Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
        }
    }

    /// <inheritdoc />
    public override Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        Delete();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override bool HasTables()
    {
        // For SharpCoreDB, check if any entity tables exist
        try
        {
            _connection.Open();
            // SharpCoreDB doesn't have a built-in way to check tables
            // We'll return true if connection works
            _connection.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.OpenAsync(cancellationToken);
            await _connection.CloseAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override void CreateTables()
    {
        _connection.Open();

        try
        {
            // Create tables for each entity type in the model
            foreach (var entityType in _model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                if (string.IsNullOrEmpty(tableName))
                    continue;

                // Build CREATE TABLE SQL
                var sql = BuildCreateTableSql(entityType);

                // Execute via the connection
                if (_connection.DbConnection is SharpCoreDBConnection sharpConnection &&
                    sharpConnection.DbInstance != null)
                {
                    sharpConnection.DbInstance.ExecuteSQL(sql);
                }
            }
        }
        finally
        {
            _connection.Close();
        }
    }

    /// <inheritdoc />
    public override async Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        await _connection.OpenAsync(cancellationToken);

        try
        {
            // Create tables for each entity type in the model
            foreach (var entityType in _model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                if (string.IsNullOrEmpty(tableName))
                    continue;

                // Build CREATE TABLE SQL
                var sql = BuildCreateTableSql(entityType);

                // Execute via the connection
                if (_connection.DbConnection is SharpCoreDBConnection sharpConnection &&
                    sharpConnection.DbInstance != null)
                {
                    await sharpConnection.DbInstance.ExecuteSQLAsync(sql);
                }
            }
        }
        finally
        {
            await _connection.CloseAsync();
        }
    }

    private string BuildCreateTableSql(IEntityType entityType)
    {
        var tableName = entityType.GetTableName();
        var columns = new List<string>();

        foreach (var property in entityType.GetProperties())
        {
            var columnName = property.GetColumnName();
            var columnType = GetColumnType(property);
            var nullable = property.IsNullable ? "" : " NOT NULL";
            var primaryKey = property.IsPrimaryKey() ? " PRIMARY KEY" : "";

            columns.Add($"{columnName} {columnType}{nullable}{primaryKey}");
        }

        return $"CREATE TABLE {tableName} ({string.Join(", ", columns)})";
    }

    private string GetColumnType(IProperty property)
    {
        var clrType = property.ClrType;
        var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        return underlyingType.Name switch
        {
            nameof(Int32) => "INTEGER",
            nameof(Int64) => "LONG",
            nameof(String) => "TEXT",
            nameof(Boolean) => "BOOLEAN",
            nameof(DateTime) => "DATETIME",
            nameof(Decimal) => "DECIMAL",
            nameof(Double) => "REAL",
            nameof(Guid) => "GUID",
            _ => "TEXT"
        };
    }
}
