using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Provides persistence operations for non-sensitive recent connection profiles.
/// </summary>
public interface IRecentConnectionsStore
{
    /// <summary>
    /// Loads recent connection profiles from local storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of recent connection profiles.</returns>
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves recent connection profiles to local storage.
    /// </summary>
    /// <param name="profiles">Profiles to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveAsync(IReadOnlyCollection<ConnectionProfile> profiles, CancellationToken cancellationToken = default);
}
