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
        // Mapping schema is already applied via DataOptions for consistency with the DataOptions constructor and to avoid duplicate calls.
    }

    /// <summary>
    /// Initializes using pre-built DataOptions (recommended for DI).
    /// </summary>
    public SharpCoreDBDataConnection(DataOptions options)
        : base(options.UseMappingSchema(SharpCoreDBMappingSchema.Instance))
    {
    }

    /// <summary>
    /// Gets a table reference for LINQ queries.
    /// </summary>
    public ITable<T> GetTable<T>() where T : class => LinqToDB.DataExtensions.GetTable<T>(this);
}
