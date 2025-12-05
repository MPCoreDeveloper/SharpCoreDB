namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a database index for fast lookups.
/// </summary>
public class DatabaseIndex
{
    private readonly Dictionary<object, List<int>> _indexData = new();
    private readonly string _tableName;
    private readonly string _columnName;
    private readonly bool _isUnique;

    /// <summary>
    /// Gets the name of the index.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the table name this index belongs to.
    /// </summary>
    public string TableName => _tableName;

    /// <summary>
    /// Gets the column name this index is on.
    /// </summary>
    public string ColumnName => _columnName;

    /// <summary>
    /// Gets whether this is a unique index.
    /// </summary>
    public bool IsUnique => _isUnique;

    /// <summary>
    /// Initializes a new instance of the DatabaseIndex class.
    /// </summary>
    /// <param name="name">The index name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="isUnique">Whether this is a unique index.</param>
    public DatabaseIndex(string name, string tableName, string columnName, bool isUnique = false)
    {
        Name = name;
        _tableName = tableName;
        _columnName = columnName;
        _isUnique = isUnique;
    }

    /// <summary>
    /// Adds a row to the index.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <param name="rowId">The row identifier.</param>
    public void Add(object key, int rowId)
    {
        if (key == null)
            return;

        if (!_indexData.ContainsKey(key))
        {
            _indexData[key] = new List<int>();
        }

        if (_isUnique && _indexData[key].Count > 0)
        {
            throw new InvalidOperationException($"Duplicate key value '{key}' violates unique constraint on index '{Name}'");
        }

        _indexData[key].Add(rowId);
    }

    /// <summary>
    /// Removes a row from the index.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <param name="rowId">The row identifier.</param>
    public void Remove(object key, int rowId)
    {
        if (key != null && _indexData.ContainsKey(key))
        {
            _indexData[key].Remove(rowId);
            if (_indexData[key].Count == 0)
            {
                _indexData.Remove(key);
            }
        }
    }

    /// <summary>
    /// Looks up rows by key value.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <returns>List of row identifiers.</returns>
    public List<int> Lookup(object key)
    {
        if (key == null || !_indexData.ContainsKey(key))
            return new List<int>();

        return _indexData[key];
    }

    /// <summary>
    /// Clears all index data.
    /// </summary>
    public void Clear()
    {
        _indexData.Clear();
    }

    /// <summary>
    /// Gets the size of the index (number of unique keys).
    /// </summary>
    public int Size => _indexData.Count;

    /// <summary>
    /// Rebuilds the index from table data.
    /// </summary>
    /// <param name="rows">The table rows with their identifiers.</param>
    public void Rebuild(List<(int RowId, Dictionary<string, object> Row)> rows)
    {
        Clear();
        foreach (var (rowId, row) in rows)
        {
            if (row.TryGetValue(_columnName, out var value) && value != null)
            {
                Add(value, rowId);
            }
        }
    }
}
