#nullable enable

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Interface for managing deleted row tombstone cleanup.
/// </summary>
public interface ITombstoneManager
{
    /// <summary>
    /// Removes tombstones older than the retention period.
    /// </summary>
    /// <param name="retentionDays">Days to retain tombstones</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of tombstones cleaned</returns>
    Task<int> CleanTombstonesAsync(int retentionDays, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of tombstones for a specific table.
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of tombstones</returns>
    Task<int> GetTombstoneCountAsync(string tableName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Concrete implementation of ITombstoneManager (stub).
/// </summary>
public sealed class TombstoneManager : ITombstoneManager
{
    /// <inheritdoc />
    public Task<int> CleanTombstonesAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Phase 2.6: TombstoneManager.CleanTombstonesAsync");
    }

    /// <inheritdoc />
    public Task<int> GetTombstoneCountAsync(string tableName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Phase 2.6: TombstoneManager.GetTombstoneCountAsync");
    }
}
