namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Describes explorer metadata for a selected table.
/// </summary>
public sealed record class TableMetadata
{
    public required string Name { get; init; }

    public IReadOnlyList<TableColumnMetadata> Columns { get; init; } = [];

    public IReadOnlyList<string> Indexes { get; init; } = [];

    public IReadOnlyList<string> Triggers { get; init; } = [];
}

/// <summary>
/// Describes column metadata for a selected table.
/// </summary>
public sealed record class TableColumnMetadata
{
    public required string Name { get; init; }

    public required string DataType { get; init; }

    public bool IsNullable { get; init; } = true;

    public bool IsHidden { get; init; }
}
