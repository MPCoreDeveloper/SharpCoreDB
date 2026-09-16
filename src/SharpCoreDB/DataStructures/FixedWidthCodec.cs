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

        // Variable-length values are collected first and handed to the arena in ONE call, then their offsets
        // are patched into the slots. The previous shape called arena.Write per value, and every one of those
        // opens the arena file with FileOptions.WriteThrough (measured 0.4597 ms per value) — three TEXT
        // columns meant three file opens per row, ~1.4 ms of the ~1.43 ms a fixed-width INSERT cost.
        List<int>? variableColumns = null;
        List<byte[]>? variablePayloads = null;

        for (int i = 0; i < columns.Count; i++)
        {
            var value = row.TryGetValue(columns[i], out var v) ? v : DBNull.Value;
            var slot = span.Slice(layout.Offsets[i], layout.SlotSizes[i]);

            if (!layout.IsVariable[i])
            {
                _ = Table.WriteTypedValueToSpan(slot, value, types[i]);
            }
            else if (value == null || value == DBNull.Value)
            {
                WriteNullVariableSlot(slot);
            }
            else
            {
                var payload = Table.EncodeVariablePayload(types[i], value);
                if (TryWriteInlineVariableSlot(slot, layout, payload))
                {
                    continue;   // stored in the record: no arena traffic at all for this value
                }

                (variableColumns ??= []).Add(i);
                (variablePayloads ??= []).Add(payload);
            }
        }

        PatchVariableOffsets(span, layout, variableColumns, variablePayloads, arena);
        return buffer;
    }

    /// <summary>Serializes a column-ordered object[] row (full table column order) with the fixed-width codec.</summary>
    public static byte[] SerializeRow(
        object[] row,
        IReadOnlyList<DataType> types,
        FixedWidthRecordLayout layout,
        IOverflowArena arena)
    {
        var buffer = new byte[layout.FixedSize];
        var span = buffer.AsSpan();

        List<int>? variableColumns = null;
        List<byte[]>? variablePayloads = null;

        for (int i = 0; i < row.Length && i < types.Count; i++)
        {
            var value = row[i];
            var slot = span.Slice(layout.Offsets[i], layout.SlotSizes[i]);

            if (!layout.IsVariable[i])
            {
                _ = Table.WriteTypedValueToSpan(slot, value, types[i]);
            }
            else if (value == null || value == DBNull.Value)
            {
                WriteNullVariableSlot(slot);
            }
            else
            {
                var payload = Table.EncodeVariablePayload(types[i], value);
                if (TryWriteInlineVariableSlot(slot, layout, payload))
                {
                    continue;   // stored in the record: no arena traffic at all for this value
                }

                (variableColumns ??= []).Add(i);
                (variablePayloads ??= []).Add(payload);
            }
        }

        PatchVariableOffsets(span, layout, variableColumns, variablePayloads, arena);
        return buffer;
    }

    /// <summary>Writes the "no block" marker into a variable-length slot.</summary>
    private static void WriteNullVariableSlot(Span<byte> slot)
    {
        slot[0] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(slot[1..], 0);
    }

    /// <summary>
    /// Writes a variable-length payload into the slot itself when the layout has inline capacity and the payload
    /// fits (plan §4b), returning <see langword="false"/> when it must go to the overflow arena instead. The slot
    /// layout is the historical <c>[null-flag(1)][offset(4)]</c> prefix with <c>[length(2)][payload(N)]</c> appended,
    /// so a NUL or overflow slot is byte-identical to what previous versions wrote.
    /// </summary>
    internal static bool TryWriteInlineVariableSlot(Span<byte> slot, FixedWidthRecordLayout layout, byte[] payload)
    {
        if (layout.InlineValueBytes <= 0 || payload.Length > layout.InlineValueBytes)
        {
            return false;
        }

        slot[0] = 2;                                                        // inline payload
        BinaryPrimitives.WriteInt32LittleEndian(slot[1..], 0);               // offset unused
        BinaryPrimitives.WriteInt16LittleEndian(slot[5..], (short)payload.Length);
        payload.CopyTo(slot[7..]);

        // Zero the unused tail: otherwise a shorter value leaves the previous value's bytes behind the length,
        // which the decoder never reads but a byte-level reader would.
        slot.Slice(7 + payload.Length, layout.InlineValueBytes - payload.Length).Clear();
        return true;
    }

    /// <summary>
    /// Reads a variable-length slot in one place, so no reader can support the overflow encoding and miss the
    /// inline one. Returns <see langword="false"/> when the slot is NULL, in which case
    /// <paramref name="value"/> is <see cref="DBNull"/>.
    /// </summary>
    internal static bool TryReadVariableSlot(
        ReadOnlySpan<byte> slot,
        FixedWidthRecordLayout layout,
        DataType type,
        IOverflowArena arena,
        out object value)
    {
        if (slot[0] == 0)
        {
            value = DBNull.Value;
            return false;
        }

        if (slot[0] == 2 && layout.InlineValueBytes > 0)
        {
            var length = BinaryPrimitives.ReadInt16LittleEndian(slot[5..]);
            var payload = length > 0 ? slot.Slice(7, length).ToArray() : [];
            value = Table.DecodeVariablePayload(type, payload);
            return true;
        }

        var offset = BinaryPrimitives.ReadInt32LittleEndian(slot[1..]);
        var block = arena.Read(offset);
        if (block is null)
        {
            value = DBNull.Value;
            return false;
        }

        value = Table.DecodeVariablePayload(type, block);
        return true;
    }

    /// <summary>
    /// Writes the collected variable-length payloads to the arena in a single call and stores each returned
    /// offset in its column's slot. Does nothing when the row had no variable-length values.
    /// </summary>
    private static void PatchVariableOffsets(
        Span<byte> span,
        FixedWidthRecordLayout layout,
        List<int>? variableColumns,
        List<byte[]>? variablePayloads,
        IOverflowArena arena)
    {
        if (variablePayloads is null)
        {
            return;
        }

        var offsets = arena.WriteMany(variablePayloads);
        for (int k = 0; k < offsets.Length; k++)
        {
            int column = variableColumns![k];
            var slot = span.Slice(layout.Offsets[column], layout.SlotSizes[column]);
            slot[0] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(slot[1..], (int)offsets[k]);
        }
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
                // One place for NULL, the overflow encoding (flag 1) and the inline encoding (flag 2, plan §4b), so
                // a reader cannot implement one and miss the other.
                _ = TryReadVariableSlot(slot, layout, types[i], arena, out var variableValue);
                row[columns[i]] = variableValue;
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
            // Flags: 0 = NULL, 1 = overflow offset, 2 = inline payload (plan §4b). Only flag 1 carries an arena
            // offset — collecting an inline payload as one would keep or free the wrong block.
            if (slot + 5 > record.Length || record[slot] is 0 or 2)
            {
                continue; // truncated, null, or inline — no arena block to collect
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
            // Flags: 0 = NULL, 1 = overflow offset, 2 = inline payload (plan §4b) — only flag 1 holds an offset, so
            // an inline slot must never be re-pointed through the compaction mapping.
            if (slot + 5 > record.Length || record[slot] is 0 or 2)
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
