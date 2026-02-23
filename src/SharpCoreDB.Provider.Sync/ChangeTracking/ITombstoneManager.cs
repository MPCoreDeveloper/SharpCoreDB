#nullable enable

using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Interface for managing deleted row tombstone cleanup.
/// </summary>
public interface ITombstoneManager
{
    /// <summary>
    /// Removes tombstones older than the retention period.
    /// </summary>
    /// <param name="database">Database instance.</param>
    /// <param name="tableName">Source table name.</param>
    /// <param name="retentionDays">Days to retain tombstones.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of tombstones cleaned.</returns>
    Task<int> CleanTombstonesAsync(IDatabase database, string tableName, int retentionDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of tombstones for a specific table.
    /// </summary>
    /// <param name="database">Database instance.</param>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of tombstones.</returns>
    Task<int> GetTombstoneCountAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default);
}
