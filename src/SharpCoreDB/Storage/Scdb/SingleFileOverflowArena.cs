// <copyright file="SingleFileOverflowArena.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Storage.Scdb;

using SharpCoreDB.DataStructures;
using System.Buffers.Binary;
using System.Collections.Generic;

/// <summary>
/// In-memory overflow arena for single-file (.scdb) fixed-width tables. The arena is serialized to
/// a dedicated provider block (<c>table:{name}:overflow</c>) as a contiguous stream of
/// <c>[length(4)][payload]</c> entries; block offsets are the byte positions of the length prefixes
/// (identical semantics to the directory-mode <see cref="OverflowArena"/>). Freed blocks keep their
/// slot so offsets stay valid, are reused in place when a new payload has the exact same length
/// (free-list), and are reclaimed by a copy-on-compact pass when the dead space grows.
/// </summary>
public sealed class SingleFileOverflowArena : IOverflowArena
{
    private readonly Dictionary<long, byte[]> _blocks = new();
    private readonly Dictionary<int, List<long>> _freeByLength = new();
    private readonly Dictionary<string, long> _contentIndex = new(System.StringComparer.Ordinal);
    // Dead (freed) block slots that must be re-emitted as tombstone markers on every serialize so
    // the byte stream stays aligned for the sequential deserializer. Populated on load (markers)
    // and on Free; cleared when a slot is reused in place or the arena is compacted.
    private readonly Dictionary<long, int> _deadSlots = new();
    private long _nextOffset;
    private int _blockReuses;

    /// <summary>Gets the number of times a freed block was reused in place (diagnostics).</summary>
    public int BlockReuses => _blockReuses;

    /// <summary>Gets the number of freed blocks currently tracked for in-place reuse (diagnostics).</summary>
    public int FreeBlockCount
    {
        get
        {
            int total = 0;
            foreach (var list in _freeByLength.Values)
            {
                total += list.Count;
            }

            return total;
        }
    }

    /// <summary>Gets the number of currently live blocks.</summary>
    public int LiveCount
    {
        get
        {
            var live = new HashSet<long>(_blocks.Keys);
            foreach (var list in _freeByLength.Values)
            {
                foreach (var offset in list)
                {
                    live.Remove(offset);
                }
            }

            return live.Count;
        }
    }

    /// <summary>Gets the total number of blocks (live + freed).</summary>
    public int TotalCount => _blocks.Count;

    /// <summary>
    /// Frees every block not referenced by the current rows' records. The single-file table
    /// re-serializes its whole row cache on every flush, so unreferenced blocks (values that
    /// changed or rows that were deleted) become free for exact-length in-place reuse.
    /// </summary>
    public void FreeUnreferenced(IReadOnlyCollection<long> liveOffsets)
    {
        var live = liveOffsets as HashSet<long> ?? new HashSet<long>(liveOffsets);
        foreach (var offset in _blocks.Keys.Where(k => !live.Contains(k)).ToList())
        {
            Free(offset);
        }
    }

    /// <inheritdoc />
    public long Write(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // B6: idempotent re-serialization — the single-file table flushes its whole row cache, so
        // an unchanged value must not allocate a new block. A live block with the exact same
        // payload content is reused (values are immutable payloads, so sharing is safe here; the
        // single-file arena never frees a shared block).
        var contentKey = System.Text.Encoding.Latin1.GetString(payload);
        if (_contentIndex.TryGetValue(contentKey, out var dedupedOffset))
        {
            return dedupedOffset;
        }

        if (_freeByLength.TryGetValue(payload.Length, out var offsets) && offsets.Count > 0)
        {
            var offset = offsets[^1];
            offsets.RemoveAt(offsets.Count - 1);
            if (offsets.Count == 0)
            {
                _freeByLength.Remove(payload.Length);
            }

            _deadSlots.Remove(offset); // reused in place: no longer a dead slot
            _blocks[offset] = payload;
            _contentIndex[contentKey] = offset;
            _blockReuses++;
            return offset;
        }

        var newOffset = _nextOffset;
        _blocks[newOffset] = payload;
        _contentIndex[contentKey] = newOffset;
        _nextOffset += 4 + payload.Length;
        return newOffset;
    }

    /// <inheritdoc />
    public byte[]? Read(long offset) => _blocks.TryGetValue(offset, out var payload) ? payload : null;

    /// <inheritdoc />
    public void Free(long offset)
    {
        if (!_blocks.Remove(offset, out var payload))
        {
            return; // already freed (or unknown) — never double-track
        }

        var contentKey = System.Text.Encoding.Latin1.GetString(payload);
        if (_contentIndex.TryGetValue(contentKey, out var indexedOffset) && indexedOffset == offset)
        {
            _contentIndex.Remove(contentKey); // only drop the index when this block was its sole owner
        }

        if (!_freeByLength.TryGetValue(payload.Length, out var offsets))
        {
            offsets = [];
            _freeByLength[payload.Length] = offsets;
        }

        offsets.Add(offset);
        _deadSlots[offset] = 4 + payload.Length;
    }

    /// <summary>Serializes all blocks (live and freed) as a contiguous <c>[length][payload]</c> stream.</summary>
    public byte[] Serialize()
    {
        if (_blocks.Count == 0)
        {
            return [];
        }

        var buffer = new byte[checked((int)_nextOffset)];
        foreach (var (offset, payload) in _blocks)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan((int)offset, 4), payload.Length);
            payload.CopyTo(buffer, (int)offset + 4);
        }

        // Dead slots (freed blocks from this session or loaded from a previous one) are written as
        // tombstone markers (negative length = total slot span to skip). Without them a dead region
        // would be serialized as a zero-filled gap that misaligns the sequential deserializer and
        // drops every later block on reload.
        foreach (var (offset, slotSize) in _deadSlots)
        {
            if (offset >= 0 && offset + 4 <= buffer.Length)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan((int)offset, 4), -slotSize);
            }
        }

        return buffer;
    }

    /// <summary>
    /// Loads the arena from a serialized provider block. Every block in the file is treated as
    /// live; freed blocks that were not reused before a flush are harmless dead weight until the
    /// next copy-on-compact pass (no record references them).
    /// </summary>
    public static SingleFileOverflowArena Deserialize(byte[]? data)
    {
        var arena = new SingleFileOverflowArena();
        if (data is null || data.Length == 0)
        {
            return arena;
        }

        long position = 0;
        while (position + 4 <= data.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)position, 4));
            if (length < 0)
            {
                // Tombstone marker: the negative value encodes the whole slot span to skip
                // (freed overflow blocks are serialized as markers, not zero-filled gaps).
                int slotSize = -length;
                if (slotSize < 4 || position + slotSize > data.Length)
                {
                    break; // truncated / corrupt
                }

                // Track the dead slot so this flush re-emits the marker (a marker consumed on load
                // would otherwise come back as a zero-filled gap on the next serialize).
                arena._deadSlots[position] = slotSize;
                int deadPayloadLength = slotSize - 4;
                if (!arena._freeByLength.TryGetValue(deadPayloadLength, out var deadOffsets))
                {
                    deadOffsets = [];
                    arena._freeByLength[deadPayloadLength] = deadOffsets;
                }

                deadOffsets.Add(position);
                position += slotSize;
                continue;
            }

            if (position + 4 + length > data.Length)
            {
                break; // truncated / corrupt
            }

            var payload = data.AsSpan((int)position + 4, length).ToArray();
            arena._blocks[position] = payload;
            arena._contentIndex[System.Text.Encoding.Latin1.GetString(payload)] = position;
            position += 4 + length;
        }

        arena._nextOffset = position;
        return arena;
    }

    /// <summary>
    /// Copy-on-compact: rewrites the live blocks (those in <paramref name="activeOffsets"/>) into a
    /// fresh arena and returns the old → new offset mapping. Callers must re-point the fixed-width
    /// records that reference the moved blocks. Freed blocks are reclaimed and the free-list cleared.
    /// </summary>
    public Dictionary<long, long> Compact(IReadOnlyCollection<long> activeOffsets)
    {
        var mapping = new Dictionary<long, long>(activeOffsets.Count);
        var newBlocks = new Dictionary<long, byte[]>(activeOffsets.Count);
        long offset = 0;

        foreach (var activeOffset in activeOffsets)
        {
            if (_blocks.TryGetValue(activeOffset, out var payload))
            {
                newBlocks[offset] = payload;
                mapping[activeOffset] = offset;
                offset += 4 + payload.Length;
            }
        }

        _blocks.Clear();
        _freeByLength.Clear();
        _deadSlots.Clear();
        _contentIndex.Clear();
        foreach (var (newOffset, payload) in newBlocks)
        {
            _blocks[newOffset] = payload;
            _contentIndex[System.Text.Encoding.Latin1.GetString(payload)] = newOffset;
        }

        _nextOffset = offset;
        return mapping;
    }
}
