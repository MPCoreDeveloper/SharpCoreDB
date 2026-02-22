#nullable enable

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Interface for managing change tracking setup and execution.
/// </summary>
public interface IChangeTrackingManager
{
    /// <summary>
    /// Provisions change tracking for a table (creates shadow table and triggers).
    /// </summary>
    /// <param name="tableName">Name of the table to track</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ProvisionTrackingAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes change tracking from a table (drops shadow table and triggers).
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeprovisionTrackingAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a table has change tracking provisioned.
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if tracking is provisioned</returns>
    Task<bool> IsProvisionedAsync(string tableName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Concrete implementation of IChangeTrackingManager (stub).
/// </summary>
public sealed class ChangeTrackingManager : IChangeTrackingManager
{
    /// <inheritdoc />
    public Task ProvisionTrackingAsync(string tableName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Phase 2.2: ChangeTrackingManager.ProvisionTrackingAsync");
    }

    /// <inheritdoc />
    public Task DeprovisionTrackingAsync(string tableName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Phase 2.2: ChangeTrackingManager.DeprovisionTrackingAsync");
    }

    /// <inheritdoc />
    public Task<bool> IsProvisionedAsync(string tableName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Phase 2.2: ChangeTrackingManager.IsProvisionedAsync");
    }
}
