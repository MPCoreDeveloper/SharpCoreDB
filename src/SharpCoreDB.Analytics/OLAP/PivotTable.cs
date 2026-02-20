namespace SharpCoreDB.Analytics.OLAP;

/// <summary>
/// Represents a two-dimensional OLAP pivot table.
/// </summary>
public sealed class PivotTable(
    IReadOnlyList<string> rowHeaders,
    IReadOnlyList<string> columnHeaders,
    IReadOnlyDictionary<(string Row, string Column), object?> values)
{
    private readonly IReadOnlyDictionary<(string Row, string Column), object?> _values = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>Gets the row headers.</summary>
    public IReadOnlyList<string> RowHeaders { get; } = rowHeaders ?? throw new ArgumentNullException(nameof(rowHeaders));

    /// <summary>Gets the column headers.</summary>
    public IReadOnlyList<string> ColumnHeaders { get; } = columnHeaders ?? throw new ArgumentNullException(nameof(columnHeaders));

    /// <summary>
    /// Gets the value at the specified row and column.
    /// </summary>
    /// <param name="row">Row header.</param>
    /// <param name="column">Column header.</param>
    /// <returns>The pivot value, if present.</returns>
    public object? GetValue(string row, string column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);

        return _values.TryGetValue((row, column), out var value) ? value : null;
    }
}
