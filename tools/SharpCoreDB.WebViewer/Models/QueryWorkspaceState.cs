namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents the persisted query workspace state (saved queries and execution history).
/// </summary>
public sealed record class QueryWorkspaceState
{
    public IReadOnlyList<SavedQueryItem> SavedQueries { get; init; } = [];

    public IReadOnlyList<QueryHistoryItem> History { get; init; } = [];
}
