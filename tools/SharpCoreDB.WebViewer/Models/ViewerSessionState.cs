namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents the active in-memory browser session state for the web viewer.
/// </summary>
public sealed record class ViewerSessionState
{
    public required string Name { get; init; }

    public required ViewerConnectionMode ConnectionMode { get; init; }

    public string? LocalDatabasePath { get; init; }

    public DatabaseStorageMode LocalStorageMode { get; init; } = DatabaseStorageMode.Directory;

    public bool LocalReadOnly { get; init; }

    public string? ServerHost { get; init; }

    public int ServerPort { get; init; } = 5001;

    public bool ServerUseSsl { get; init; } = true;

    public bool ServerPreferHttp3 { get; init; } = true;

    public string? ServerDatabase { get; init; }

    public string? ServerUsername { get; init; }

    public required string Password { get; init; }

    public DateTimeOffset ConnectedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayTarget => ConnectionMode == ViewerConnectionMode.Server
        ? $"{ServerHost}:{ServerPort}/{ServerDatabase}"
        : LocalDatabasePath ?? string.Empty;
}
