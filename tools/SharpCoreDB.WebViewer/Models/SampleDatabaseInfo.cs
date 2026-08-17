namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Describes a built-in sample database that can be created in the viewer.
/// </summary>
public sealed class SampleDatabaseInfo
{
    /// <summary>
    /// Canonical name used for the storage directory (e.g. "contoso").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-friendly display name (e.g. "Contoso").
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Short description shown in the sample database picker.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Storage mode used when the sample database is created.
    /// </summary>
    public DatabaseStorageMode StorageMode { get; init; } = DatabaseStorageMode.Directory;
}