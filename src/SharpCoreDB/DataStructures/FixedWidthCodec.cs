// <copyright file="FixedWidthCodec.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using System.Buffers.Binary;
using System.Collections.Generic;

/// <summary>
/// Shared fixed-width record codec (out-of-line overflow model). Every column occupies a constant
/// slot in the record's fixed part: fixed-size columns store <c>[null-flag(1)][payload]</c> inline,
/// variable-length columns (String / Blob) store a 5-byte slot <c>[null-flag(1)][arena-offset(4)]</c>
/// referencing a block in the overflow arena. Used by both the directory-mode <see cref="Table"/>
/// and the single-file (<c>.scdb</c>) table so the two storage modes share one record format.
/// </summary>
public static class FixedWidthCodec
{
    /// <summary>Serializes a row dictionary into a fixed-width record (variable values → arena).</summary>
    public static byte[] SerializeRow(
        Dictionary<string, object> row,
        IReadOnlyList<string> columns,
        IReadOnlyList<DataType> types,
        FixedWidthRecordLayout layout,
        IOverflowArena arena)
    {
        var buffer = new byte[layout.FixedSize];
        var span = buffer.AsSpan();

        for (int i = 0; i < columns.Count; i++)
        {
            var slot = span.Slice(layout.Offsets[i], layout.SlotSizes[i]);
            var value = row.TryGetValue(columns[i], out var v) ? v : DBNull.Value;

            if (layout.IsVariable[i])
            {
                if (value == null || value == DBNull.Value)
                {
                    slot[0] = 0;
                    BinaryPrimitives.WriteInt32LittleEndian(slot[1..], 0);
                }
                else
                {
                    var payload = Table.EncodeVariablePayload(types[i], value);
                    var offset = arena.Write(payload);
                    slot[0] = 1;
                    BinaryPrimitives.WriteInt32LittleEndian(slot[1..], (int)offset);
                }
            }
            else
            {
                _ = Table.WriteTypedValueToSpan(slot, value, types[i]);
            }
        }

        return buffer;
    }

    /// <summary>
    /// Serializes a column-ordered <c>object[]</c> row (values already aligned to the table's full
    /// column order) into a fixed-width record. Dedicated overload for the SQL batch-INSERT fast
    /// path so it never falls back to the variable-length encoding on a fixed-width table.
    /// </summary>
    public static byte[] SerializeRow(
        object[] row,
        IReadOnlyList<DataType> types,
        FixedWidthRecordLayout layout,
        IOverflowArena arena)
    {
        var buffer = new byte[layout.FixedSize];
        var span = buffer.AsSpan();

        for (int i = 0; i < row.Length && i < types.Count; i++)
        {
            var slot = span.Slice(layout.Offsets[i], layout.SlotSizes[i]);
            var value = row[i];

            if (layout.IsVariable[i])
            {
                if (value == null || value == DBNull.Value)
                {
                    slot[0] = 0;
                    BinaryPrimitives.WriteInt32LittleEndian(slot[1..], 0);
                }
                else
                {
                    var payload = Table.EncodeVariablePayload(types[i], value);
                    var offset = arena.Write(payload);
                    slot[0] = 1;
                    BinaryPrimitives.WriteInt32LittleEndian(slot[1..], (int)offset);
                }
            }
            else
            {
                _ = Table.WriteTypedValueToSpan(slot, value, types[i]);
            }
        }

        return buffer;
    }

    /// <summary>Deserializes a fixed-width record into a row dictionary (variable values ← arena).</summary>
    public static Dictionary<string, object> DeserializeRow(
        ReadOnlySpan<byte> data,
        IReadOnlyList<string> columns,
        IReadOnlyList<DataType> types,
        FixedWidthRecordLayout layout,
        IOverflowArena arena)
    {
        var row = new Dictionary<string, object>(columns.Count, System.StringComparer.Ordinal);

        for (int i = 0; i < columns.Count; i++)
        {
            if (layout.Offsets[i] + layout.SlotSizes[i] > data.Length)
            {
                break; // truncated / corrupt record
            }

            var slot = data.Slice(layout.Offsets[i], layout.SlotSizes[i]);
            if (layout.IsVariable[i])
            {
                if (slot[0] == 0)
                {
                    row[columns[i]] = DBNull.Value;
                }
                else
                {
                    var offset = BinaryPrimitives.ReadInt32LittleEndian(slot[1..]);
                    var payload = arena.Read(offset);
                    row[columns[i]] = payload is null ? DBNull.Value : Table.DecodeVariablePayload(types[i], payload);
                }
            }
            else
            {
                row[columns[i]] = Table.ReadTypedValueFromSpan(slot, types[i], out _);
            }
        }

        return row;
    }

    /// <summary>Collects the arena offsets referenced by a fixed-width record's variable slots.</summary>
    public static void CollectVariableOffsets(byte[] record, FixedWidthRecordLayout layout, HashSet<long> live)
    {
        for (int i = 0; i < layout.ColumnCount; i++)
        {
            if (!layout.IsVariable[i])
            {
                continue;
            }

            var slot = layout.Offsets[i];
            if (slot + 5 > record.Length || record[slot] == 0)
            {
                continue; // truncated or null slot
            }

            var blockOffset = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(slot + 1, 4));
            // NOTE: offset 0 is a valid block offset (first arena block) — the flag byte above
            // already excluded NULL slots, so collect every referenced offset unconditionally.
            live.Add(blockOffset);
        }
    }

    /// <summary>
    /// Returns a copy of a fixed-width record with its variable slots re-pointed through the
    /// compaction mapping, or null when no slot moved.
    /// </summary>
    public static byte[]? RepointVariableSlots(byte[] record, FixedWidthRecordLayout layout, Dictionary<long, long> mapping)
    {
        byte[]? result = null;

        for (int i = 0; i < layout.ColumnCount; i++)
        {
            if (!layout.IsVariable[i])
            {
                continue;
            }

            var slot = layout.Offsets[i];
            if (slot + 5 > record.Length || record[slot] == 0)
            {
                continue;
            }

            var blockOffset = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(slot + 1, 4));
            // NOTE: offset 0 is a valid block offset (first arena block) — re-point it like any other.
            if (mapping.TryGetValue(blockOffset, out var newOffset) && newOffset != blockOffset)
            {
                result ??= (byte[])record.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(slot + 1, 4), (int)newOffset);
            }
        }

        return result;
    }
}
