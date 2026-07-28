using LinqToDB;
using LinqToDB.Data;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// DataConnection for SharpCoreDB with linq2db.
/// Provides direct LINQ query capabilities over SharpCoreDB databases.
/// SharpCoreDB is SQLite-compatible at the SQL level.
/// 
/// Note: linq2db's SQLite provider adapter may require Microsoft.Data.Sqlite to be present
/// in the application for full provider initialization and certain DDL operations.
/// For best results with SharpCoreDB-specific connection strings, prefer the functional
/// wrapper or raw Execute for schema operations.
/// C# 14: Simple primary style constructors with automatic mapping schema.
/// </summary>
public sealed class SharpCoreDBDataConnection : DataConnection
{
    /// <summary>
    /// Initializes using a SharpCoreDB connection string.
    /// Uses linq2db SQLite dialect for SQL generation.
    /// </summary>
    public SharpCoreDBDataConnection(string connectionString)
        : base(new DataOptions()
            .UseConnectionString(ProviderName.SQLite, connectionString)
            .UseMappingSchema(SharpCoreDBMappingSchema.Instance))
    {
        // Ensure the underlying SQLite connection is opened so that tests using GetUnderlyingConnection().CreateCommand() succeed immediately.
        // This is especially important on Windows where connection state can be closed by default.
        EnsureConnectionOpen();
    }

    /// <summary>
    /// Initializes using pre-built DataOptions (recommended for DI).
    /// </summary>
    public SharpCoreDBDataConnection(DataOptions options)
        : base(options.UseMappingSchema(SharpCoreDBMappingSchema.Instance))
    {
        // Ensure the underlying SQLite connection is opened so that tests using GetUnderlyingConnection().CreateCommand() succeed immediately.
        EnsureConnectionOpen();
    }

    private void EnsureConnectionOpen()
    {
        // Explicitly open the connection so tests using GetUnderlyingConnection().CreateCommand() succeed on first use.
        // The underlying Microsoft.Data.Sqlite.SqliteConnection must be Open for ExecuteNonQuery in test constructors.
        // Note: .Connection is deprecated in linq2db v7+; this is kept for v6 compatibility and Windows file-handle behavior.
        if (Connection?.State != System.Data.ConnectionState.Open)
        {
            Connection?.Open();
        }
    }

    /// <summary>
    /// Gets a table reference for LINQ queries.
    /// </summary>
    public ITable<T> GetTable<T>() where T : class => LinqToDB.DataExtensions.GetTable<T>(this);
}
