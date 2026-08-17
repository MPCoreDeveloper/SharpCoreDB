using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Provides built-in databases (default "scdb" and sample databases).
/// </summary>
public interface ISampleDatabaseCatalog
{
    /// <summary>
    /// Gets the resolved path of the default local database.
    /// </summary>
    /// <returns>Default database path.</returns>
    string GetDefaultDatabasePath();

    /// <summary>
    /// Gets the resolved path of a sample database.
    /// </summary>
    /// <param name="sampleName">Sample database name.</param>
    /// <returns>Sample database path.</returns>
    string GetSampleDatabasePath(string sampleName);

    /// <summary>
    /// Gets the resolved root directory under which viewer-managed databases are stored.
    /// </summary>
    /// <returns>Absolute data root directory.</returns>
    string GetDataRootDirectory();

    /// <summary>
    /// Lists the available sample databases.
    /// </summary>
    /// <returns>Available sample databases.</returns>
    IReadOnlyList<SampleDatabaseInfo> ListSamples();

    /// <summary>
    /// Creates the default local database when it does not exist yet.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the default database exists.</returns>
    Task EnsureDefaultDatabaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a sample database when it does not exist yet.
    /// </summary>
    /// <param name="sampleName">Sample database name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the sample database exists.</returns>
    Task EnsureSampleAsync(string sampleName, CancellationToken cancellationToken = default);
}