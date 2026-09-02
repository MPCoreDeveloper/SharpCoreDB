// <copyright file="FixedWidthRecordLayout.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System.Collections.Generic;

/// <summary>
/// Describes the fixed-width record layout for a table schema (out-of-line overflow model).
/// Every column gets a constant-size slot in the record's "fixed part":
/// - fixed-size columns (Integer, Long, Real, Boolean, DateTime, Decimal, Guid, Ulid) store their
///   value inline as <c>[null-flag(1)][payload]</c>;
/// - variable-length columns (String, Blob) store a 5-byte slot <c>[null-flag(1)][overflowOffset(4)]</c>
///   referencing a payload block in the table's overflow arena.
/// The record length is therefore constant per schema, so every update is an in-place overwrite.
/// </summary>
public sealed class FixedWidthRecordLayout
{
    /// <summary>Gets the per-column byte offset of each slot in the fixed part.</summary>
    public required int[] Offsets { get; init; }

    /// <summary>Gets the per-column slot size (fixed = 1 + fixed payload size; variable = 5).</summary>
    public required int[] SlotSizes { get; init; }

    /// <summary>Gets whether each column is variable-length (String / Blob → overflow arena).</summary>
    public required bool[] IsVariable { get; init; }

    /// <summary>Gets the fixed part size in bytes (constant per schema).</summary>
    public required int FixedSize { get; init; }

    /// <summary>Gets the number of columns.</summary>
    public int ColumnCount => Offsets.Length;

    /// <summary>
    /// Computes the fixed-width record layout for the given column types. Always succeeds — every
    /// supported column type maps to either an inline fixed slot or a 5-byte overflow reference.
    /// </summary>
    public static FixedWidthRecordLayout Compute(IReadOnlyList<DataType> columnTypes)
    {
        var count = columnTypes.Count;
        var offsets = new int[count];
        var sizes = new int[count];
        var isVariable = new bool[count];

        int offset = 0;
        for (int i = 0; i < count; i++)
        {
            int fixedSize = GetFixedEncodedSize(columnTypes[i]);
            if (fixedSize < 0)
            {
                // Variable-length column: [null-flag(1)][overflow offset(4)].
                isVariable[i] = true;
                sizes[i] = 5;
            }
            else
            {
                // Fixed column: [null-flag(1)][payload] — GetFixedEncodedSize already includes the flag.
                isVariable[i] = false;
                sizes[i] = fixedSize;
            }

            offsets[i] = offset;
            offset += sizes[i];
        }

        return new FixedWidthRecordLayout
        {
            Offsets = offsets,
            SlotSizes = sizes,
            IsVariable = isVariable,
            FixedSize = offset
        };
    }

    // Slot size including the 1-byte null flag — must match Table.Serialization.GetFixedEncodedSize.
    private static int GetFixedEncodedSize(DataType type) => type switch
    {
        DataType.Integer => 5,
        DataType.Long => 9,
        DataType.RowRef => 9,
        DataType.Real => 9,
        DataType.Boolean => 2,
        DataType.DateTime => 9,
        DataType.Decimal => 17,
        DataType.Ulid => 31,
        DataType.Guid => 17,
        _ => -1 // String / Blob are variable-length
    };
}
