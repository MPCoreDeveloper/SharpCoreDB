using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.Models;

public enum ExplorerNodeType
{
    Table = 0,
    Column = 1
}

public sealed class ExplorerNode
{
    public required ExplorerNodeType NodeType { get; init; }

    public required string Name { get; init; }

    public string? DataType { get; init; }

    public string? TableName { get; init; }

    public ObservableCollection<ExplorerNode> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;

    public string DisplayText => NodeType == ExplorerNodeType.Column && !string.IsNullOrWhiteSpace(DataType)
        ? $"{Name} ({DataType})"
        : Name;
}
