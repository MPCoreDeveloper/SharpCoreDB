using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;

namespace SharpCoreDB.Functional.Linq2DB;

/// <summary>
/// Extension methods for configuring SharpCoreDB with linq2db (linq2db 6+).
/// SharpCoreDB is SQLite-compatible, so linq2db's SQLite dialect generates the SQL; the connection, the storage
/// and the encryption are SharpCoreDB's.
/// </summary>
public static class SharpCoreDBProviderExtensions
{
    /// <summary>
    /// Creates DataOptions pre-configured for SharpCoreDB (SQLite dialect over a SharpCoreDB connection, plus the
    /// custom type mappings).
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <returns>DataOptions instance ready to be passed to DataConnection.</returns>
    public static DataOptions CreateSharpCoreDBOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Delegate to the adapter's single definition so the options can never drift from what
        // SharpCoreDBDataConnection(string) builds.
        return SharpCoreDBDataConnection.CreateOptions(connectionString);
    }
}
