using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.DataStructures;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Represents a connection to a SharpCoreDB database.
/// Connection string format: "Path=C:\data\mydb.scdb;Password=secret"
/// </summary>
public sealed class SharpCoreDBConnection : DbConnection
{
    private ConnectionState _state;
    private IDatabase? _database;
    private IServiceProvider? _serviceProvider;
    private string? _connectionString;
    private string? _dataSource;
    private string? _password;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBConnection"/> class.
    /// </summary>
    public SharpCoreDBConnection()
    {
        _state = ConnectionState.Closed;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBConnection"/> class with a connection string.
    /// </summary>
    /// <param name="connectionString">Connection string in format: Path=..;Password=..</param>
    public SharpCoreDBConnection(string connectionString) : this()
    {
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public override string? ConnectionString
    {
        get => _connectionString ?? string.Empty;
        set
        {
            if (_state != ConnectionState.Closed)
                throw new InvalidOperationException("Cannot change connection string while connection is open.");

            _connectionString = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                ParseConnectionString(value);
            }
        }
    }

    /// <summary>
    /// Gets the name of the current database (the database file path).
    /// </summary>
    public override string Database => _dataSource ?? string.Empty;

    /// <summary>
    /// Gets the data source (the database file path).
    /// </summary>
    public override string DataSource => _dataSource ?? string.Empty;

    /// <summary>
    /// Gets the server version.
    /// </summary>
    public override string ServerVersion => "SharpCoreDB 1.0.0";

    /// <summary>
    /// Gets the current state of the connection.
    /// </summary>
    public override ConnectionState State => _state;

    /// <summary>
    /// Gets the underlying SharpCoreDB database instance.
    /// </summary>
    public IDatabase? DbInstance => _database;

    /// <summary>
    /// Changes the current database. Not supported by SharpCoreDB.
    /// </summary>
    public override void ChangeDatabase(string databaseName)
    {
        throw new NotSupportedException("SharpCoreDB does not support changing databases. Create a new connection instead.");
    }

    /// <summary>
    /// Opens the database connection.
    /// </summary>
    public override void Open()
    {
        if (_state == ConnectionState.Open)
            return;

        if (string.IsNullOrWhiteSpace(_dataSource))
            throw new InvalidOperationException("Connection string must specify a Path.");

        try
        {
            _state = ConnectionState.Connecting;

            // Set up dependency injection for SharpCoreDB
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            _serviceProvider = services.BuildServiceProvider();

            // Create database instance
            var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
            _database = factory.Create(_dataSource, _password ?? "default", isReadOnly: false);

            _state = ConnectionState.Open;
        }
        catch (Exception ex)
        {
            _state = ConnectionState.Broken;
            
            // ? FIX: Provide clear error message if sqlite_master is mentioned
            if (ex.Message.Contains("sqlite_master", StringComparison.OrdinalIgnoreCase))
            {
                throw new SharpCoreDBException(
                    "SharpCoreDB is not SQLite. The error 'Table sqlite_master does not exist' indicates that " +
                    "some tooling or code is trying to query SQLite system tables. " +
                    "SharpCoreDB uses its own metadata system via IMetadataProvider. " +
                    $"Original error: {ex.Message}", ex);
            }
            
            throw new SharpCoreDBException($"Failed to open connection to SharpCoreDB: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Closes the database connection.
    /// </summary>
    public override void Close()
    {
        if (_state == ConnectionState.Closed)
            return;

        try
        {
            // ? FIX: FORCE save metadata on close to ensure persistence
            if (_database != null)
            {
                try
                {
                    _database.ForceSave();
                }
                catch (Exception ex)
                {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"{ex.Message}");
#endif
                    // Suppress exception to allow connection to close gracefully
                    _ = ex; // Avoid unused variable warning
                }
            }
            
            _database = null;
            
            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();
            
            _serviceProvider = null;
            _state = ConnectionState.Closed;
        }
        catch (Exception ex)
        {
            _state = ConnectionState.Broken;
            throw new SharpCoreDBException("Failed to close connection.", ex);
        }
    }

    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open to begin a transaction.");

        return new SharpCoreDBTransaction(this, isolationLevel);
    }

    /// <summary>
    /// Creates a new command associated with this connection.
    /// </summary>
    protected override DbCommand CreateDbCommand()
    {
        return new SharpCoreDBCommand
        {
            Connection = this
        };
    }

    /// <summary>
    /// Gets schema information for the database (for SSMS browsing).
    /// </summary>
    public override DataTable GetSchema()
    {
        return GetSchema("MetaDataCollections");
    }

    /// <summary>
    /// Gets schema information for a specific collection.
    /// </summary>
    public override DataTable GetSchema(string collectionName)
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open to retrieve schema information.");

        return collectionName.ToUpperInvariant() switch
        {
            "METADATACOLLECTIONS" => GetMetaDataCollectionsSchema(),
            "TABLES" => GetTablesSchema(),
            "COLUMNS" => GetColumnsSchema(),
            _ => throw new ArgumentException($"The requested collection '{collectionName}' is not defined.", nameof(collectionName))
        };
    }

    /// <summary>
    /// Gets schema information for a specific collection with restrictions.
    /// </summary>
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues)
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection must be open to retrieve schema information.");

        var upper = collectionName.ToUpperInvariant();
        if (upper == "COLUMNS" && restrictionValues.Length > 0 && !string.IsNullOrWhiteSpace(restrictionValues[0]))
        {
            return GetColumnsSchema(restrictionValues[0]!);
        }

        return GetSchema(collectionName);
    }

    private static DataTable GetMetaDataCollectionsSchema()
    {
        var table = new DataTable("MetaDataCollections");
        table.Columns.Add("CollectionName", typeof(string));
        table.Columns.Add("NumberOfRestrictions", typeof(int));
        table.Columns.Add("NumberOfIdentifierParts", typeof(int));

        table.Rows.Add("MetaDataCollections", 0, 0);
        table.Rows.Add("Tables", 1, 1);
        table.Rows.Add("Columns", 2, 2);

        return table;
    }

    private DataTable GetTablesSchema()
    {
        var table = new DataTable("Tables");
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("TABLE_TYPE", typeof(string));

        if (_database is IMetadataProvider metadata)
        {
            try
            {
                foreach (var tbl in metadata.GetTables())
                {
                    table.Rows.Add(tbl.Name, tbl.Type);
                }
            }
            catch
            {
                // Ignore errors and return whatever was collected
            }
        }

        return table;
    }

    private static DataTable GetColumnsSchema()
    {
        var table = new DataTable("Columns");
        table.Columns.Add("TABLE_NAME", typeof(string));
        table.Columns.Add("COLUMN_NAME", typeof(string));
        table.Columns.Add("DATA_TYPE", typeof(string));
        table.Columns.Add("ORDINAL_POSITION", typeof(int));
        table.Columns.Add("IS_NULLABLE", typeof(bool));

        return table;
    }

    private DataTable GetColumnsSchema(string tableName)
    {
        var table = GetColumnsSchema();

        if (_database is IMetadataProvider metadata)
        {
            try
            {
                foreach (var column in metadata.GetColumns(tableName))
                {
                    table.Rows.Add(column.Table, column.Name, column.DataType, column.Ordinal, column.IsNullable);
                }
            }
            catch
            {
                // Ignore errors and return whatever was collected
            }
        }

        return table;
    }

    private void ParseConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var builder = new SharpCoreDBConnectionStringBuilder { ConnectionString = connectionString };
        _dataSource = builder.Path;
        _password = builder.Password;
    }

    /// <summary>
    /// Disposes the connection.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }
}
