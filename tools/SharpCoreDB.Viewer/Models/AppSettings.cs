namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Application settings model
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Current UI language (e.g., "en-US", "nl-NL")
    /// </summary>
    public string Language { get; set; } = "en-US";

    /// <summary>
    /// Current theme variant ("Light" or "Dark")
    /// </summary>
    public string Theme { get; set; } = "Light";

    /// <summary>
    /// Recent connection profiles for quick reconnect.
    /// </summary>
    public List<ConnectionProfile> RecentConnections { get; set; } = [];
}

/// <summary>
/// Saved connection profile metadata for quick reconnection.
/// </summary>
public class ConnectionProfile
{
    /// <summary>
    /// Friendly display name for UI lists.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to database file or directory.
    /// </summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>
    /// Last used storage mode label (Directory or SingleFile).
    /// </summary>
    public string StorageMode { get; set; } = "Directory";

    /// <summary>
    /// Last successful connect timestamp in UTC.
    /// </summary>
    public DateTimeOffset LastConnectedUtc { get; set; } = DateTimeOffset.UtcNow;
}
