using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Manages the active local or server database connection state for the web viewer browser session.
/// </summary>
public interface IViewerConnectionService
{
    /// <summary>
    /// Gets the current viewer session state if a connection is active.
    /// </summary>
    /// <returns>The current session state, or <see langword="null"/> when disconnected.</returns>
    ViewerSessionState? GetCurrentSession();

    /// <summary>
    /// Validates and stores a new connection request for the current browser session.
    /// </summary>
    /// <param name="request">Connection input.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored active session state.</returns>
    Task<ViewerSessionState> ConnectAsync(ConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the active connection state for the current browser session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a local ADO.NET connection string for the supplied local-mode session.
    /// </summary>
    /// <param name="session">Active viewer session.</param>
    /// <returns>The local provider connection string.</returns>
    string BuildLocalConnectionString(ViewerSessionState session);

    /// <summary>
    /// Builds a network client connection string for the supplied server-mode session.
    /// </summary>
    /// <param name="session">Active viewer session.</param>
    /// <returns>The network client connection string.</returns>
    string BuildServerConnectionString(ViewerSessionState session);
}
