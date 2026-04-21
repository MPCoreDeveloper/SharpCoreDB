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
}
