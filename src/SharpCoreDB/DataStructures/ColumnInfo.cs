namespace SharpCoreDB.DataStructures;

/// <summary>
/// Describes a database column for metadata discovery.
/// </summary>
public sealed record ColumnInfo
{
    /// <summary>
    /// Table name the column belongs to.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// Column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Data type name.
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Ordinal position (0-based).
    /// </summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Whether the column can contain null values.
    /// </summary>
    public bool IsNullable { get; init; } = true;
}
