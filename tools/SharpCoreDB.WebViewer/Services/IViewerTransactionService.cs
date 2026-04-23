using SharpCoreDB.Data.Provider;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Manages transaction lifecycle for the active web viewer session.
/// </summary>
public interface IViewerTransactionService
{
    /// <summary>
    /// Gets the active transaction state for the session.
    /// </summary>
    /// <returns>The active transaction state, or <see langword="null"/> when no transaction is active.</returns>
    ViewerTransactionState? GetActiveTransaction();

    /// <summary>
    /// Starts a new transaction for the active session.
    /// </summary>
    /// <param name="startedBy">Optional initiator label for diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active transaction state.</returns>
    Task<ViewerTransactionState> BeginAsync(string? startedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the active transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the active transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears active transaction state without commit or rollback.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active local execution connection if a local transaction is active.
    /// </summary>
    /// <param name="connection">The active local connection.</param>
    /// <returns><see langword="true"/> when a local transaction connection is active.</returns>
    bool TryGetLocalExecutionConnection(out SharpCoreDBConnection? connection);

    /// <summary>
    /// Gets the active server execution connection if a server transaction is active.
    /// </summary>
    /// <param name="connection">The active server connection.</param>
    /// <returns><see langword="true"/> when a server transaction connection is active.</returns>
    bool TryGetServerExecutionConnection(out SharpCoreDB.Client.SharpCoreDBConnection? connection);
}
