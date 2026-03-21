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

    /// <summary>
    /// Collation name for the column (e.g., "NOCASE", "RTRIM").
    /// Null means the default collation (Binary) applies.
    /// ✅ COLLATE Phase 1: Exposed via metadata discovery for ADO.NET/EF Core providers.
    /// </summary>
    public string? Collation { get; init; }

    /// <summary>
    /// Whether this column is a hidden internal column (e.g., auto-generated <c>_rowid</c>).
    /// Hidden columns are not returned by <c>SELECT *</c> but can be explicitly queried.
    /// ✅ AUTO-ROWID: Marks the internal ULID primary key as hidden.
    /// </summary>
    public bool IsHidden { get; init; }
}
