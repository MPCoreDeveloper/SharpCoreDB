using LinqToDB;
using LinqToDB.Data;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Extension methods for configuring SharpCoreDB with linq2db (linq2db 6+).
/// SharpCoreDB is SQLite-compatible; we use the SQLite SQL dialect + custom type mappings.
/// </summary>
public static class SharpCoreDBProviderExtensions
{
    /// <summary>
    /// Creates DataOptions pre-configured for SharpCoreDB (SQLite dialect + custom mappings).
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>DataOptions instance ready to be passed to DataConnection.</returns>
    public static DataOptions CreateSharpCoreDBOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return new DataOptions()
            .UseConnectionString(ProviderName.SQLite, connectionString)
            .UseMappingSchema(SharpCoreDBMappingSchema.Instance);
    }
}

