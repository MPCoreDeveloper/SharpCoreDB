#nullable enable

namespace SharpCoreDB.Provider.Sync.Extensions;

/// <summary>
/// Factory for creating configured SharpCoreDBSyncProvider instances.
/// Follows the factory pattern for consistent provider instantiation.
/// </summary>
public sealed class SyncProviderFactory
{
    /// <summary>
    /// Creates a new SharpCoreDBSyncProvider instance with the given configuration.
    /// </summary>
    /// <param name="connectionString">SharpCoreDB connection string</param>
    /// <param name="options">Sync provider options</param>
    /// <returns>A new configured SharpCoreDBSyncProvider instance</returns>
    /// <exception cref="ArgumentNullException">If connectionString or options is null</exception>
    public SharpCoreDBSyncProvider CreateProvider(string connectionString, SyncProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ArgumentNullException.ThrowIfNullOrEmpty(connectionString);
        ArgumentNullException.ThrowIfNull(options);

        return new SharpCoreDBSyncProvider(connectionString, options);
    }
}
