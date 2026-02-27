#nullable enable

using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Interface for managing change tracking setup and execution.
/// </summary>
public interface IChangeTrackingManager
{
    /// <summary>
    /// Provisions change tracking for a table (creates shadow table and triggers).
    /// </summary>
    /// <param name="database">Database instance.</param>
    /// <param name="tableName">Name of the table to track.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProvisionTrackingAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes change tracking from a table (drops shadow table and triggers).
    /// </summary>
    /// <param name="database">Database instance.</param>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeprovisionTrackingAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a table has change tracking provisioned.
    /// </summary>
    /// <param name="database">Database instance.</param>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if tracking is provisioned.</returns>
    Task<bool> IsProvisionedAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a row change in the tracking table.
    /// Used when the database engine does not support triggers (e.g., SingleFileDatabase).
    /// </summary>
    /// <param name="database">Database instance.</param>
    /// <param name="tableName">Name of the source table.</param>
    /// <param name="primaryKeyValue">Value of the primary key for the changed row.</param>
    /// <param name="isDelete">True if the row was deleted (tombstone).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordChangeAsync(IDatabase database, string tableName, string primaryKeyValue, bool isDelete, CancellationToken cancellationToken = default);
}
