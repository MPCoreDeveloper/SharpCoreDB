using SharpCoreDB.DataStructures;

namespace SharpCoreDB.Interfaces;

/// <summary>
/// Optional metadata provider for schema discovery (tables/columns).
/// Implemented by Database; consumers may probe via cast.
/// </summary>
public interface IMetadataProvider
{
    /// <summary>
    /// Gets table metadata. Default interface implementation returns an empty list.
    /// </summary>
    IReadOnlyList<TableInfo> GetTables() => [];

    /// <summary>
    /// Gets column metadata for a table. Default interface implementation returns an empty list.
    /// </summary>
    IReadOnlyList<ColumnInfo> GetColumns(string tableName) => [];
}
