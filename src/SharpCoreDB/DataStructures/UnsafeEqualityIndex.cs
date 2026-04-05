// <copyright file="UnsafeEqualityIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Unsafe, zero-allocation equality index optimized for high-cardinality lookups.
/// Uses open addressing with linear probing for key slots and unmanaged linked lists for RowId chains.
///
/// Storage model:
/// - Key bytes are stored once in unmanaged key arena.
/// - Slot table stores hash + key location + RowId chain head.
/// - RowIds are appended to unmanaged node arena (linked-list per key).
///
/// Thread safety:
/// - Mutation and lookup operations are synchronized through <see cref="Lock"/>.
/// - Designed for predictable low-GC behavior in hot paths.
/// </summary>
public sealed unsafe class UnsafeEqualityIndex : IDisposable
{
    private const byte SlotEmpty = 0;
    private const byte SlotOccupied = 1;
    private const float MaxLoadFactor = 0.72f;

    [StructLayout(LayoutKind.Sequential)]
    private struct Slot
    {
        public ulong Hash;
        public int KeyOffset;
        public int KeyLength;
        public int RowHead;
        public byte State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RowNode
    {
        public long RowId;
        public int Next;
    }

    private readonly Lock _gate = new();

    private Slot* _slots;
    private int _slotCount;
    private int _slotUsed;

    private RowNode* _rowNodes;
    private int _rowCapacity;
    private int _rowUsed;

    private byte* _keyArena;
    private nuint _keyCapacity;
    private nuint _keyUsed;

    private bool _disposed;

    /// <summary>
    /// Initializes a new index instance.
    /// </summary>
    /// <param name="initialCapacity">Initial distinct-key slot capacity.</param>
    /// <param name="initialRowCapacity">Initial row-node capacity.</param>
    /// <param name="initialKeyBytes">Initial key-arena size in bytes.</param>
    public UnsafeEqualityIndex(int initialCapacity = 1 << 15, int initialRowCapacity = 1 << 16, int initialKeyBytes = 1 << 20)
    {
        if (initialCapacity < 16) initialCapacity = 16;
        if (initialRowCapacity < 16) initialRowCapacity = 16;
        if (initialKeyBytes < 1024) initialKeyBytes = 1024;

        _slotCount = NextPowerOfTwo(initialCapacity);
        _slots = (Slot*)NativeMemory.AllocZeroed((nuint)_slotCount, (nuint)sizeof(Slot));

        _rowCapacity = initialRowCapacity;
        _rowNodes = (RowNode*)NativeMemory.AllocZeroed((nuint)_rowCapacity, (nuint)sizeof(RowNode));

        _keyCapacity = (nuint)initialKeyBytes;
        _keyArena = (byte*)NativeMemory.AllocZeroed(_keyCapacity);
    }

    /// <summary>Gets the number of distinct keys.</summary>
    public int DistinctKeyCount => _slotUsed;

    /// <summary>Gets the number of row nodes stored.</summary>
    public int RowNodeCount => _rowUsed;

    /// <summary>
    /// Adds a key-row mapping.
    /// </summary>
    /// <param name="key">Binary key bytes.</param>
    /// <param name="rowId">Row identifier.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(ReadOnlySpan<byte> key, long rowId)
    {
        ThrowIfDisposed();
        if (key.Length == 0)
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        lock (_gate)
        {
            EnsureSlotCapacity();

            var hash = Hash64(key);
            var slotIndex = FindSlotForInsert(hash, key, out var exists);

            if (!exists)
            {
                var keyOffset = AppendKey(key);
                ref var slot = ref _slots[slotIndex];
                slot.Hash = hash;
                slot.KeyOffset = keyOffset;
                slot.KeyLength = key.Length;
                slot.RowHead = -1;
                slot.State = SlotOccupied;
                _slotUsed++;
            }

            var nodeIndex = AppendRowNode(rowId, _slots[slotIndex].RowHead);
            _slots[slotIndex].RowHead = nodeIndex;
        }
    }

    /// <summary>
    /// Removes one specific key-row mapping.
    /// </summary>
    /// <param name="key">Binary key bytes.</param>
    /// <param name="rowId">Row identifier to remove.</param>
    /// <returns>True when a mapping was removed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool Remove(ReadOnlySpan<byte> key, long rowId)
    {
        ThrowIfDisposed();
        if (key.Length == 0)
            return false;

        lock (_gate)
        {
            var hash = Hash64(key);
            var slotIndex = FindSlotForLookup(hash, key);
            if (slotIndex < 0)
                return false;

            var prev = -1;
            var current = _slots[slotIndex].RowHead;

            while (current >= 0)
            {
                if (_rowNodes[current].RowId == rowId)
                {
                    if (prev < 0)
                        _slots[slotIndex].RowHead = _rowNodes[current].Next;
                    else
                        _rowNodes[prev].Next = _rowNodes[current].Next;

                    if (_slots[slotIndex].RowHead < 0)
                    {
                        BackshiftDelete(slotIndex);
                        _slotUsed--;
                    }

                    return true;
                }

                prev = current;
                current = _rowNodes[current].Next;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets row ids for the provided key into caller-provided destination span.
    /// </summary>
    /// <param name="key">Binary key bytes.</param>
    /// <param name="destination">Destination span for row ids.</param>
    /// <returns>Number of row ids written.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int GetRowIdsForValue(ReadOnlySpan<byte> key, Span<long> destination)
    {
        ThrowIfDisposed();
        if (key.Length == 0 || destination.Length == 0)
            return 0;

        lock (_gate)
        {
            var slotIndex = FindSlotForLookup(Hash64(key), key);
            if (slotIndex < 0)
                return 0;

            var written = 0;
            var current = _slots[slotIndex].RowHead;
            while (current >= 0 && written < destination.Length)
            {
                destination[written++] = _rowNodes[current].RowId;
                current = _rowNodes[current].Next;
            }

            return written;
        }
    }

    /// <summary>
    /// Clears all slots, keys, and row mappings.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            NativeMemory.Clear(_slots, (nuint)(_slotCount * sizeof(Slot)));
            NativeMemory.Clear(_rowNodes, (nuint)(_rowCapacity * sizeof(RowNode)));
            NativeMemory.Clear(_keyArena, _keyCapacity);

            _slotUsed = 0;
            _rowUsed = 0;
            _keyUsed = 0;
        }
    }

    /// <summary>
    /// Adds multiple key-row mappings in a single lock acquisition.
    /// Reduces lock overhead from O(n) to O(1) for batch inserts.
    /// </summary>
    /// <param name="keys">Pre-serialized key byte arrays (null entries are skipped).</param>
    /// <param name="rowIds">Corresponding row identifiers.</param>
    /// <param name="count">Number of entries to process.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal void AddBatch(byte[][] keys, long[] rowIds, int count)
    {
        ThrowIfDisposed();
        if (count <= 0) return;

        lock (_gate)
        {
            for (int i = 0; i < count; i++)
            {
                var k = keys[i];
                if (k is null || k.Length == 0) continue;

                EnsureSlotCapacity();

                ReadOnlySpan<byte> key = k;
                var hash = Hash64(key);
                var slotIndex = FindSlotForInsert(hash, key, out var exists);

                if (!exists)
                {
                    var keyOffset = AppendKey(key);
                    ref var slot = ref _slots[slotIndex];
                    slot.Hash = hash;
                    slot.KeyOffset = keyOffset;
                    slot.KeyLength = key.Length;
                    slot.RowHead = -1;
                    slot.State = SlotOccupied;
                    _slotUsed++;
                }

                var nodeIndex = AppendRowNode(rowIds[i], _slots[slotIndex].RowHead);
                _slots[slotIndex].RowHead = nodeIndex;
            }
        }
    }

    private void EnsureSlotCapacity()
    {
        var threshold = (int)(_slotCount * MaxLoadFactor);
        if (_slotUsed + 1 <= threshold)
            return;

        ResizeSlots(_slotCount << 1);
    }

    private void ResizeSlots(int newCapacity)
    {
        var nextCapacity = NextPowerOfTwo(newCapacity);
        var newSlots = (Slot*)NativeMemory.AllocZeroed((nuint)nextCapacity, (nuint)sizeof(Slot));

        var oldSlots = _slots;
        var oldCount = _slotCount;

        _slots = newSlots;
        _slotCount = nextCapacity;
        _slotUsed = 0;

        for (int i = 0; i < oldCount; i++)
        {
            if (oldSlots[i].State != SlotOccupied)
                continue;

            var index = FindInsertByStoredKey(oldSlots[i].Hash);
            _slots[index] = oldSlots[i];
            _slots[index].State = SlotOccupied;
            _slotUsed++;
        }

        NativeMemory.Free(oldSlots);
    }

    private int FindInsertByStoredKey(ulong hash)
    {
        var mask = _slotCount - 1;
        var idx = (int)hash & mask;

        while (_slots[idx].State == SlotOccupied)
        {
            idx = (idx + 1) & mask;
        }

        return idx;
    }

    private int FindSlotForInsert(ulong hash, ReadOnlySpan<byte> key, out bool exists)
    {
        var mask = _slotCount - 1;
        var idx = (int)hash & mask;

        while (true)
        {
            ref var slot = ref _slots[idx];
            if (slot.State == SlotEmpty)
            {
                exists = false;
                return idx;
            }

            if (slot.Hash == hash && KeyEquals(slot.KeyOffset, slot.KeyLength, key))
            {
                exists = true;
                return idx;
            }

            idx = (idx + 1) & mask;
        }
    }

    private int FindSlotForLookup(ulong hash, ReadOnlySpan<byte> key)
    {
        var mask = _slotCount - 1;
        var idx = (int)hash & mask;

        while (true)
        {
            ref var slot = ref _slots[idx];
            if (slot.State == SlotEmpty)
                return -1;

            if (slot.Hash == hash && KeyEquals(slot.KeyOffset, slot.KeyLength, key))
                return idx;

            idx = (idx + 1) & mask;
        }
    }

    private void BackshiftDelete(int deletedIndex)
    {
        var mask = _slotCount - 1;
        var free = deletedIndex;
        var i = (free + 1) & mask;

        while (_slots[i].State == SlotOccupied)
        {
            var home = (int)_slots[i].Hash & mask;
            var distCurrent = (i - home + _slotCount) & mask;
            var distFree = (i - free + _slotCount) & mask;

            if (distCurrent >= distFree)
            {
                _slots[free] = _slots[i];
                free = i;
            }

            i = (i + 1) & mask;
        }

        _slots[free] = default;
    }

    private int AppendRowNode(long rowId, int next)
    {
        if (_rowUsed >= _rowCapacity)
            GrowRowNodes(_rowCapacity << 1);

        var idx = _rowUsed++;
        _rowNodes[idx].RowId = rowId;
        _rowNodes[idx].Next = next;
        return idx;
    }

    private void GrowRowNodes(int newCapacity)
    {
        var nextCapacity = Math.Max(16, newCapacity);
        var newNodes = (RowNode*)NativeMemory.AllocZeroed((nuint)nextCapacity, (nuint)sizeof(RowNode));

        Buffer.MemoryCopy(
            source: _rowNodes,
            destination: newNodes,
            destinationSizeInBytes: (long)((nuint)nextCapacity * (nuint)sizeof(RowNode)),
            sourceBytesToCopy: (long)((nuint)_rowCapacity * (nuint)sizeof(RowNode)));

        NativeMemory.Free(_rowNodes);
        _rowNodes = newNodes;
        _rowCapacity = nextCapacity;
    }

    private int AppendKey(ReadOnlySpan<byte> key)
    {
        var needed = (nuint)key.Length;
        if (_keyUsed + needed > _keyCapacity)
            GrowKeyArena(Math.Max(_keyCapacity << 1, _keyUsed + needed));

        var offset = (int)_keyUsed;
        fixed (byte* source = key)
        {
            Buffer.MemoryCopy(source, _keyArena + offset, (long)(_keyCapacity - _keyUsed), key.Length);
        }

        _keyUsed += needed;
        return offset;
    }

    private void GrowKeyArena(nuint newCapacity)
    {
        var nextCapacity = newCapacity < 1024 ? (nuint)1024 : newCapacity;
        var newArena = (byte*)NativeMemory.AllocZeroed(nextCapacity);

        Buffer.MemoryCopy(_keyArena, newArena, (long)nextCapacity, (long)_keyUsed);

        NativeMemory.Free(_keyArena);
        _keyArena = newArena;
        _keyCapacity = nextCapacity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool KeyEquals(int keyOffset, int keyLength, ReadOnlySpan<byte> key)
    {
        if (keyLength != key.Length)
            return false;

        var stored = new ReadOnlySpan<byte>(_keyArena + keyOffset, keyLength);
        return stored.SequenceEqual(key);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static ulong Hash64(ReadOnlySpan<byte> data)
    {
        const ulong prime = 1099511628211UL;
        ulong hash = 14695981039346656037UL;

        fixed (byte* ptr = data)
        {
            var i = 0;
            var length = data.Length;

            while (i + sizeof(ulong) <= length)
            {
                var chunk = Unsafe.ReadUnaligned<ulong>(ptr + i);
                hash ^= chunk;
                hash *= prime;
                i += sizeof(ulong);
            }

            while (i < length)
            {
                hash ^= ptr[i++];
                hash *= prime;
            }
        }

        return hash == 0 ? 1UL : hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPowerOfTwo(int value)
    {
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UnsafeEqualityIndex));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_slots != null)
            {
                NativeMemory.Free(_slots);
                _slots = null;
            }

            if (_rowNodes != null)
            {
                NativeMemory.Free(_rowNodes);
                _rowNodes = null;
            }

            if (_keyArena != null)
            {
                NativeMemory.Free(_keyArena);
                _keyArena = null;
            }

            _slotCount = 0;
            _slotUsed = 0;
            _rowCapacity = 0;
            _rowUsed = 0;
            _keyCapacity = 0;
            _keyUsed = 0;
        }
    }
}
