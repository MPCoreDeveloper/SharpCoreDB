namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Represents a schema object shown in the explorer.
/// </summary>
public sealed record DatabaseObjectEntry(string Name, string ObjectType)
{
    /// <summary>
    /// True when this object is a SQL view.
    /// </summary>
    public bool IsView => string.Equals(ObjectType, "view", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// User-facing display label.
    /// </summary>
    public string DisplayLabel => IsView ? $"{Name} (VIEW)" : $"{Name} (TABLE)";
}
