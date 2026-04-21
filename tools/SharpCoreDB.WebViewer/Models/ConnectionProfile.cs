namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents a saved non-sensitive connection profile for fast reconnect workflows.
/// </summary>
public sealed record class ConnectionProfile
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public DatabaseStorageMode StorageMode { get; init; }

    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public enum DatabaseStorageMode
{
    Directory = 0,
    SingleFile = 1
}
