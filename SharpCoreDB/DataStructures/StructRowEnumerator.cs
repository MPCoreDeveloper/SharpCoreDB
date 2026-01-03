#nullable enable

using System;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Ref struct enumerator for StructRowEnumerable, providing allocation-free iteration.
/// </summary>
public ref struct StructRowEnumerator
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly StructRowSchema _schema;
    private readonly int _rowCount;
    private readonly bool _enableCaching;
    private int _currentRowIndex;

    /// <summary>
    /// Initializes a new instance of StructRowEnumerator.
    /// </summary>
    /// <param name="data">The raw byte data.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="enableCaching">Whether to enable caching in StructRow instances.</param>
    public StructRowEnumerator(ReadOnlyMemory<byte> data, StructRowSchema schema, int rowCount, bool enableCaching = false)
    {
        _data = data;
        _schema = schema;
        _rowCount = rowCount;
        _enableCaching = enableCaching;
        _currentRowIndex = -1;
    }

    /// <summary>
    /// Advances the enumerator to the next row.
    /// </summary>
    /// <returns>True if there are more rows, otherwise false.</returns>
    public bool MoveNext()
    {
        _currentRowIndex++;
        return _currentRowIndex < _rowCount;
    }

    /// <summary>
    /// Gets the current StructRow.
    /// </summary>
    public StructRow Current => new StructRow(_data, _schema, _currentRowIndex * _schema.RowSizeBytes, _enableCaching);
}
