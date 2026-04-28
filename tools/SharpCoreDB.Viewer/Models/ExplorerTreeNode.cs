using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Tree node used by the database explorer.
/// </summary>
public sealed class ExplorerTreeNode(string displayName, string nodeType, string? objectName = null)
{
    public string DisplayName { get; } = displayName;

    public string NodeType { get; } = nodeType;

    public string? ObjectName { get; } = objectName;

    public bool IsLeaf => ObjectName is not null;

    public ObservableCollection<ExplorerTreeNode> Children { get; } = [];
}
