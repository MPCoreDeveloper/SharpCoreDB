namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents a saved SQL query entry for quick reuse.
/// </summary>
public sealed record class SavedQueryItem
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Sql { get; init; }

    public string ParametersJson { get; init; } = string.Empty;

    public ViewerConnectionMode ConnectionMode { get; init; }

    public string? TargetKey { get; init; }

    public string? TargetDisplay { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;
}
