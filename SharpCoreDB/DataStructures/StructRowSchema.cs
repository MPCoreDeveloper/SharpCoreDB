#nullable enable

using System;

namespace SharpCoreDB.DataStructures;

/// <summary>
/// Schema defining the layout of columns in a StructRow.
/// Provides metadata for zero-copy deserialization.
/// </summary>
public readonly struct StructRowSchema
{
    /// <summary>
    /// Initializes a new instance of StructRowSchema.
    /// </summary>
    /// <param name="columnNames">The column names.</param>
    /// <param name="columnTypes">The column types.</param>
    /// <param name="columnOffsets">The column offsets.</param>
    /// <param name="rowSizeBytes">The total row size in bytes.</param>
    internal StructRowSchema(string[] columnNames, DataType[] columnTypes, int[] columnOffsets, int rowSizeBytes)
    {
        _columnNames = columnNames;
        _columnTypes = columnTypes;
        _columnOffsets = columnOffsets;
        _rowSizeBytes = rowSizeBytes;
    }

    private readonly string[] _columnNames;
    private readonly DataType[] _columnTypes;
    private readonly int[] _columnOffsets;
    private readonly int _rowSizeBytes;

    /// <summary>
    /// Gets the column names.
    /// </summary>
    internal string[] ColumnNames => _columnNames;

    /// <summary>
    /// Gets the column types.
    /// </summary>
    internal DataType[] ColumnTypes => _columnTypes;

    /// <summary>
    /// Gets the column offsets within the row data.
    /// </summary>
    internal int[] ColumnOffsets => _columnOffsets;

    /// <summary>
    /// Gets the total size of a row in bytes.
    /// </summary>
    internal int RowSizeBytes => _rowSizeBytes;

    /// <summary>
    /// Gets the column names as a read-only span.
    /// </summary>
    public ReadOnlySpan<string> ColumnNamesSpan => _columnNames;

    /// <summary>
    /// Gets the column types as a read-only span.
    /// </summary>
    public ReadOnlySpan<DataType> ColumnTypesSpan => _columnTypes;

    /// <summary>
    /// Gets the column offsets as a read-only span.
    /// </summary>
    public ReadOnlySpan<int> ColumnOffsetsSpan => _columnOffsets;
}
