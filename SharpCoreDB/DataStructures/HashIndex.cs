public class HashIndex
{
    private readonly Dictionary<object, List<long>> _index = new();
    private readonly string _columnName;

    public HashIndex(string tableName, string columnName) => _columnName = columnName;

    public void Add(Dictionary<string, object> row, long position)
    {
        var key = row[_columnName];
        if (!_index.TryGetValue(key, out var list))
        {
            list = new List<long>();
            _index[key] = list;
        }
        list.Add(position);
    }

    public void Remove(Dictionary<string, object> row)
    {
        var key = row[_columnName];
        _index.Remove(key);
    }

    public List<long> LookupPositions(object key)
        => _index.TryGetValue(key, out var list) ? list : new();

    public int Count => _index.Count;

    public bool ContainsKey(object key) => key != null && _index.ContainsKey(key);

    public void Clear() => _index.Clear();

    public void Rebuild(List<Dictionary<string, object>> rows)
    {
        Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            Add(rows[i], i);
        }
    }

    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey) GetStatistics()
    {
        var uniqueKeys = _index.Count;
        var totalRows = _index.Values.Sum(list => list.Count);
        var avgRowsPerKey = uniqueKeys > 0 ? (double)totalRows / uniqueKeys : 0;
        return (uniqueKeys, totalRows, avgRowsPerKey);
    }
}
