namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents an active transaction for the current viewer session.
/// </summary>
public sealed record class ViewerTransactionState
{
    public required ViewerConnectionMode ConnectionMode { get; init; }

    public string? ServerTransactionId { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string? StartedBy { get; init; }
}
