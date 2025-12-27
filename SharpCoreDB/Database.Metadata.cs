using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB;

/// <summary>
/// Database metadata implementation (tables/columns) for schema discovery.
/// </summary>
public partial class Database : IMetadataProvider
{
    /// <inheritdoc />
    public IReadOnlyList<TableInfo> GetTables()
    {
        if (tables is null || tables.Count == 0)
        {
            return [];
        }

        var list = new List<TableInfo>(tables.Count);
        foreach (var kvp in tables)
        {
            list.Add(new TableInfo
            {
                Name = kvp.Key,
                Type = "TABLE"
            });
        }
        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<ColumnInfo> GetColumns(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!tables.TryGetValue(tableName, out var table))
        {
            return [];
        }

        var columns = table.Columns;
        var types = table.ColumnTypes;
        var list = new List<ColumnInfo>(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            list.Add(new ColumnInfo
            {
                Table = tableName,
                Name = columns[i],
                DataType = types[i].ToString(),
                Ordinal = i,
                IsNullable = true // SharpCoreDB schema currently does not track nullability
            });
        }

        return list;
    }
}
