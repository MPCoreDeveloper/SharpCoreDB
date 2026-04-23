using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Persists saved queries and execution history for the web viewer workspace.
/// </summary>
public interface IQueryWorkspaceStore
{
    /// <summary>
    /// Loads the persisted query workspace state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The query workspace state.</returns>
    Task<QueryWorkspaceState> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the query workspace state.
    /// </summary>
    /// <param name="state">The workspace state to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(QueryWorkspaceState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports the workspace state as JSON text.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exported JSON text.</returns>
    Task<string> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports workspace state from JSON text.
    /// </summary>
    /// <param name="json">Workspace JSON payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ImportAsync(string json, CancellationToken cancellationToken = default);
}
