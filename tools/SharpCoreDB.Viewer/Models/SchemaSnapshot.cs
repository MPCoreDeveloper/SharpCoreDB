namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Exported schema snapshot used for import and compare operations.
/// </summary>
public sealed class SchemaSnapshot
{
    public List<SchemaObjectEntry> Objects { get; init; } = [];
}

/// <summary>
/// Represents a schema object and its SQL definition.
/// </summary>
public sealed class SchemaObjectEntry
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Sql { get; init; } = string.Empty;
}
