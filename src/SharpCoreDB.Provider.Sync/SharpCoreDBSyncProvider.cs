#nullable enable

using System.Data.Common;
using Dotmim.Sync;
using Dotmim.Sync.Builders;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Manager;
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
public sealed class SharpCoreDBSyncProvider(string connectionString, SyncProviderOptions options) : CoreProvider
{
    private string _connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(connectionString))
        : connectionString;

    private readonly SyncProviderOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Gets the sync provider options for this instance.
    /// </summary>
    public SyncProviderOptions Options => _options;

    /// <inheritdoc />
    public override string ConnectionString
    {
        get => _connectionString;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Connection string cannot be null or whitespace.", nameof(value));

            _connectionString = value;
        }
    }

    /// <inheritdoc />
    public override bool CanBeServerProvider => true;

    /// <inheritdoc />
    public override ConstraintsLevelAction ConstraintsLevelAction => ConstraintsLevelAction.OnDatabaseLevel;

    /// <summary>
    /// Creates a new DbConnection for communicating with the SharpCoreDB database.
    /// </summary>
    /// <returns>A new SharpCoreDBConnection instance</returns>
    public override DbConnection CreateConnection() => new SharpCoreDBConnection(_connectionString);

    /// <summary>
    /// Gets the database name from the connection string.
    /// For SharpCoreDB, this is the file path (without extension).
    /// </summary>
    /// <returns>Database name/path</returns>
    public override string GetDatabaseName()
    {
        var parts = _connectionString.Split(';');
        var pathPart = parts.FirstOrDefault(p => p.StartsWith("Path=", StringComparison.OrdinalIgnoreCase));

        if (pathPart == null)
            return "SharpCoreDB";

        var path = pathPart.Substring(5); // Skip "Path="
        return Path.GetFileNameWithoutExtension(path);
    }

    /// <inheritdoc />
    public override DbDatabaseBuilder GetDatabaseBuilder() =>
        throw new NotImplementedException("Phase 2.5: SharpCoreDBDatabaseBuilder implementation pending");

    /// <inheritdoc />
    public override DbScopeBuilder GetScopeBuilder(string scopeName) =>
        throw new NotImplementedException("Phase 2.3: SharpCoreDBScopeInfoBuilder implementation pending");

    /// <inheritdoc />
    public override DbSyncAdapter GetSyncAdapter(SyncTable table, ScopeInfo scopeInfo) =>
        throw new NotImplementedException("Phase 3.2: SharpCoreDBSyncAdapter implementation pending");

    /// <inheritdoc />
    public override DbMetadata GetMetadata() =>
        throw new NotImplementedException("Phase 1.3: SharpCoreDBDbMetadata implementation pending");

    /// <inheritdoc />
    public override string GetProviderTypeName() => "SharpCoreDB.Provider.Sync";

    /// <inheritdoc />
    public override string GetShortProviderTypeName() => "SharpCoreDB";
}
