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
}
