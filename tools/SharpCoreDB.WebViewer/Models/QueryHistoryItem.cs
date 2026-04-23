namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents a query execution history entry.
/// </summary>
public sealed record class QueryHistoryItem
{
    public required Guid Id { get; init; }

    public required string SqlPreview { get; init; }

    public string ParametersJson { get; init; } = string.Empty;

    public ViewerConnectionMode ConnectionMode { get; init; }

    public string? TargetKey { get; init; }

    public string? TargetDisplay { get; init; }

    public bool Succeeded { get; init; }

    public string? StatusMessage { get; init; }

    public DateTimeOffset ExecutedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
