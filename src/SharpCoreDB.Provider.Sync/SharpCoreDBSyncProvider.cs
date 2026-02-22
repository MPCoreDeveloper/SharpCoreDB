#nullable enable

using Dotmim.Sync;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Provider.Sync;

/// <summary>
/// Dotmim.Sync provider for SharpCoreDB encrypted database engine.
/// Enables bidirectional synchronization between SharpCoreDB and any Dotmim.Sync-supported database
/// (PostgreSQL, SQL Server, SQLite, MySQL) with multi-tenant filtering support.
/// </summary>
/// <remarks>
/// This is a CoreProvider implementation that works as an add-in to Dotmim.Sync.
/// It leverages shadow tables and triggers for change tracking, matching the approach used
/// by the SQLite and MySQL Dotmim.Sync providers.
/// 
/// **Key Insight:** SharpCoreDB's AES-256-GCM encryption is at-rest only. By the time the provider
/// reads data through ITable.Select() or ExecuteQuery(), the CryptoService has already decrypted it.
/// The provider operates on plaintext rows in memory, with no special encryption handling needed.
/// </remarks>
public sealed class SharpCoreDBSyncProvider : CoreProvider
{
    private readonly string _connectionString;
    private readonly SyncProviderOptions _options;
    private SharpCoreDBConnection? _connection;

    /// <summary>
    /// Gets the connection string for the SharpCoreDB database.
    /// </summary>
    public string ConnectionString => _connectionString;

    /// <summary>
    /// Gets the sync provider options for this instance.
    /// </summary>
    public SyncProviderOptions Options => _options;

    /// <summary>
    /// Initializes a new instance of the SharpCoreDBSyncProvider.
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string (e.g., "Path=C:\data\local.scdb;Password=secret")</param>
    /// <param name="options">Configuration options for sync behavior</param>
    /// <exception cref="ArgumentNullException">If connectionString or options is null</exception>
    public SharpCoreDBSyncProvider(string connectionString, SyncProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        _connectionString = connectionString;
        _options = options;
    }

    /// <summary>
    /// Creates a new DbConnection for communicating with the SharpCoreDB database.
    /// </summary>
    /// <returns>A new SharpCoreDBConnection instance</returns>
    public override DbConnection CreateConnection()
    {
        _connection = new SharpCoreDBConnection(_connectionString);
        return _connection;
    }

    /// <summary>
    /// Gets the database name from the connection string.
    /// For SharpCoreDB, this is the file path (without extension).
    /// </summary>
    /// <returns>Database name/path</returns>
    public override string GetDatabaseName()
    {
        // Extract the Path value from connection string
        // Format: "Path=C:\data\local.scdb;Password=secret"
        var parts = _connectionString.Split(';');
        var pathPart = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase));
        
        if (pathPart == null)
            return "SharpCoreDB";

        var path = pathPart.Substring(5); // Skip "Path="
        return Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>
    /// Gets the builder for database-level operations.
    /// </summary>
    public override DbConnectionStringBuilder GetConnectionStringBuilder()
    {
        var builder = new DbConnectionStringBuilder();
        // SharpCoreDB connection strings are custom format; just store as-is
        builder.Add("ConnectionString", _connectionString);
        return builder;
    }

    /// <summary>
    /// Clears the provider's internal state.
    /// </summary>
    public override void Dispose()
    {
        _connection?.Dispose();
        base.Dispose();
    }
}
