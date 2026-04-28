using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Simple array-based row for Avalonia DataGrid.
/// </summary>
public class QueryResultRow : INotifyPropertyChanged
{
    private object?[] _values = Array.Empty<object?>();

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsNew { get; set; }

    /// <summary>
    /// The cell values as an array.
    /// Index corresponds to column position.
    /// </summary>
    public object?[] Values
    {
        get => _values;
        set
        {
            _values = value ?? Array.Empty<object?>();
            OnPropertyChanged();
            // Notify all indexer properties changed
            OnPropertyChanged("Item[]");
        }
    }

    /// <summary>
    /// Indexer for DataGrid binding: {Binding [0]}, {Binding [1]}, etc.
    /// This is the KEY to making dynamic columns work in Avalonia!
    /// </summary>
    public object? this[int index]
    {
        get
        {
#if DEBUG
            var value = index >= 0 && index < _values.Length ? _values[index] : null;
            System.Diagnostics.Debug.WriteLine($"[QueryResultRow] Indexer GET [{index}] = {value} (array length: {_values.Length})");
            return value;
#else
            return index >= 0 && index < _values.Length ? _values[index] : null;
#endif
        }
        set
        {
            if (index >= 0 && index < _values.Length)
            {
                _values[index] = value;
                OnPropertyChanged($"Item[{index}]");
            }
        }
    }

    /// <summary>
    /// Creates a new, empty row for the grid with the specified columns.
    /// </summary>
    public static QueryResultRow CreateEmpty(IReadOnlyList<string> columns)
    {
        var values = new object?[columns.Count];
        return new QueryResultRow
        {
            Values = values,
            IsNew = true
        };
    }

    /// <summary>
    /// Get the value of a cell by its column name.
    /// Ignores case for column name matching.
    /// </summary>
    public object? GetValue(string columnName, IReadOnlyList<string> columnOrder)
    {
        for (int i = 0; i < columnOrder.Count; i++)
        {
            if (columnOrder[i].Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return this[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Initialize from Dictionary (for compatibility with existing code).
    /// </summary>
    public static QueryResultRow FromDictionary(Dictionary<string, object> dict, List<string> columnOrder)
    {
        var values = new object?[columnOrder.Count];
        for (int i = 0; i < columnOrder.Count; i++)
        {
            values[i] = dict.TryGetValue(columnOrder[i], out var val) ? val : null;
        }

        return new QueryResultRow
        {
            Values = values,
            IsNew = false
        };
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public override string ToString()
    {
        return string.Join(" | ", _values.Select(v =>
            v == null || v == DBNull.Value ? "" : v.ToString()));
    }
}
