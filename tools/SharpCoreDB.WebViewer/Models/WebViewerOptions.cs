namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Provides configuration options for local web viewer runtime behavior.
/// </summary>
public sealed class WebViewerOptions
{
    public const string SectionName = "WebViewer";

    public string BindAddress { get; set; } = "localhost";

    public int HttpsPort { get; set; } = 5443;

    public int QueryTimeoutSeconds { get; set; } = 30;

    public int ResultRowLimit { get; set; } = 200;

    public int MaxRecentConnections { get; set; } = 8;

    public int MaxSavedQueries { get; set; } = 50;

    public int MaxQueryHistoryItems { get; set; } = 100;

    /// <summary>
    /// Name of the default local database created on first launch.
    /// </summary>
    public string DefaultDatabaseName { get; set; } = "scdb";

    /// <summary>
    /// Password used for the default local database and built-in sample databases.
    /// </summary>
    public string DefaultDatabasePassword { get; set; } = "scdb";

    /// <summary>
    /// Full path to the default database. When empty, resolves to
    /// %LOCALAPPDATA%\SharpCoreDB.WebViewer\Data\<see cref="DefaultDatabaseName"/>.
    /// </summary>
    public string DefaultDatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Root directory for built-in sample databases (Contoso, AdventureWorks).
    /// When empty, resolves to %LOCALAPPDATA%\SharpCoreDB.WebViewer\Data.
    /// </summary>
    public string SampleDatabasesDirectory { get; set; } = string.Empty;
}