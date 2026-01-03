#nullable enable

using System;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Struct-based enumerable for StructRow, providing zero-allocation enumeration.
/// </summary>
public struct StructRowEnumerable
{
    /// <summary>
    /// The raw byte data containing all rows.
    /// </summary>
    public readonly ReadOnlyMemory<byte> _data;

    /// <summary>
    /// The schema defining the column layout.
    /// </summary>
    public readonly StructRowSchema _schema;

    /// <summary>
    /// The number of rows in the data.
    /// </summary>
    public readonly int _rowCount;

    /// <summary>
    /// Whether to enable caching in StructRow instances.
    /// </summary>
    public readonly bool _enableCaching;

    /// <summary>
    /// Initializes a new instance of StructRowEnumerable.
    /// </summary>
    /// <param name="data">The raw byte data.</param>
    /// <param name="schema">The schema.</param>
    /// <param name="rowCount">The number of rows.</param>
    /// <param name="enableCaching">Whether to enable caching in StructRow instances.</param>
    public StructRowEnumerable(ReadOnlyMemory<byte> data, StructRowSchema schema, int rowCount, bool enableCaching = false)
    {
        _data = data;
        _schema = schema;
        _rowCount = rowCount;
        _enableCaching = enableCaching;
    }

    /// <summary>
    /// Gets the enumerator for this enumerable.
    /// </summary>
    /// <returns>A StructRowEnumerator for iteration.</returns>
    public StructRowEnumerator GetEnumerator() => new StructRowEnumerator(_data, _schema, _rowCount, _enableCaching);
}
