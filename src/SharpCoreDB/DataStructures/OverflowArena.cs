// <copyright file="OverflowArena.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Append-only arena for variable-length (TEXT/BLOB) record values in fixed-width-record tables
/// (the SQLite-model "out-of-line overflow"). Blocks are <c>[length(4)][payload]</c> appended to a
/// per-table <c>.ovf</c> file; a fixed-width record stores the block's offset in its fixed part, so
/// every record update stays in place (the record length is constant per schema). Payloads are
/// cached in memory for the lifetime of the table. B6: freed blocks are tracked in a free-list and
/// reused in place when a new payload has the exact same length (in-memory); the remaining dead
/// space is reclaimed by the copy-on-compact pass.
/// </summary>
public sealed class OverflowArena : IDisposable, IOverflowArena
{
    private readonly IStorage _storage;
    private readonly string _filePath;
    private readonly Dictionary<long, byte[]> _cache = new();
    // B6: freed block offsets grouped by their payload length, for exact-length in-place reuse.
    private readonly Dictionary<int, List<long>> _freeByLength = new();
    private int _blockReuses;
    private bool _loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverflowArena"/> class.
    /// </summary>
    /// <param name="storage">The storage provider used to read/write the arena file.</param>
    /// <param name="filePath">The arena file path (normally the table <c>.dat</c> path with a <c>.ovf</c> extension).</param>
    public OverflowArena(IStorage storage, string filePath)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>Gets the arena file path.</summary>
    public string FilePath => _filePath;

    /// <summary>Gets the number of payload blocks currently cached.</summary>
    public int Count => _cache.Count;

    /// <summary>Enumerates all block offsets currently cached (live + freed), loading the arena first.</summary>
    public IEnumerable<long> GetAllOffsets()
    {
        EnsureLoaded();
        return _cache.Keys;
    }

    /// <summary>B6: gets the number of times a freed block was reused in place (diagnostics).</summary>
    public int BlockReuses => _blockReuses;

    /// <summary>B6: gets the number of freed blocks currently tracked for in-place reuse (diagnostics).</summary>
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

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _cache.Clear();
        _freeByLength.Clear(); // in-memory free-list: rebuilt (empty) on a fresh session

        // ReadAllRecords yields (physical length-prefix offset, record payload) for both legacy
        // plaintext and per-record encrypted files (it handles the encryption magic header), so the
        // arena offsets stored in fixed-width records always resolve.
        foreach (var (offset, payload) in _storage.ReadAllRecords(_filePath))
        {
            _cache[offset] = payload;
        }

        _loaded = true;
    }

    /// <summary>
    /// Writes a payload to the arena and returns the block offset (the position of the storage
    /// record's length prefix — the value stored in a fixed-width record's variable slot). B6: when
    /// a previously freed block has the exact same payload length, it is reused in place (the
    /// storage layer requires identical plaintext length for in-place overwrites); otherwise the
    /// block is appended.
    /// </summary>
    public long Write(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EnsureLoaded();

        if (TryReuseFreeBlock(payload, out var reusedOffset))
        {
            return reusedOffset;
        }

        var offset = _storage.AppendBytes(_filePath, payload);
        _cache[offset] = payload;
        return offset;
    }

    /// <summary>
    /// B6: attempts to reuse a freed block of the exact same payload length via an in-place
    /// overwrite. Returns false when no suitable block is free or the storage refuses the
    /// in-place write (e.g. inside a transaction) — the caller then appends.
    /// </summary>
    private bool TryReuseFreeBlock(byte[] payload, out long offset)
    {
        offset = 0;
        if (!_freeByLength.TryGetValue(payload.Length, out var offsets))
        {
            return false;
        }

        while (offsets.Count > 0)
        {
            offset = offsets[^1];
            offsets.RemoveAt(offsets.Count - 1);

            if (_storage.OverwriteRecordAt(_filePath, offset, payload))
            {
                if (offsets.Count == 0)
                {
                    _freeByLength.Remove(payload.Length);
                }

                _cache[offset] = payload;
                _blockReuses++;
                return true;
            }

            // In-place overwrite refused (e.g. transaction active): keep the block free for a
            // later write and try the next candidate; if none succeeds we fall back to append.
            offsets.Add(offset);
            break;
        }

        offset = 0;
        return false;
    }

    /// <summary>Reads the payload stored at <paramref name="offset"/>, or null when absent.</summary>
    public byte[]? Read(long offset)
    {
        EnsureLoaded();
        return _cache.TryGetValue(offset, out var payload) ? payload : null;
    }

    /// <summary>Drops the block at <paramref name="offset"/> from the live cache. B6: the freed block
    /// is tracked for exact-length in-place reuse; otherwise its disk space is reclaimed by the next
    /// copy-on-compact pass.</summary>
    public void Free(long offset)
    {
        EnsureLoaded();
        if (!_cache.Remove(offset, out var payload))
        {
            return; // already freed (or unknown) — never double-track
        }

        if (!_freeByLength.TryGetValue(payload.Length, out var offsets))
        {
            offsets = [];
            _freeByLength[payload.Length] = offsets;
        }

        offsets.Add(offset);
    }

    /// <summary>
    /// Copy-on-compact: rewrites the live blocks (those in <paramref name="activeOffsets"/>) into a
    /// fresh arena file and returns a mapping from old offset to new offset. Callers must update
    /// the fixed-width records that reference the moved blocks. The free (dropped) blocks are
    /// reclaimed, the cache is rebuilt from the compacted file and the free-list is cleared.
    /// </summary>
    public Dictionary<long, long> Compact(IReadOnlyCollection<long> activeOffsets)
    {
        EnsureLoaded();

        var tempPath = _filePath + ".compact.tmp";
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            var mapping = new Dictionary<long, long>(activeOffsets.Count);
            var newCache = new Dictionary<long, byte[]>(activeOffsets.Count);
            foreach (var offset in activeOffsets)
            {
                if (_cache.TryGetValue(offset, out var payload))
                {
                    var newOffset = _storage.AppendBytes(tempPath, payload);
                    mapping[offset] = newOffset;
                    newCache[newOffset] = payload;
                }
            }

            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            if (newCache.Count > 0)
            {
                File.Move(tempPath, _filePath);
            }

            _cache.Clear();
            foreach (var (newOffset, payload) in newCache)
            {
                _cache[newOffset] = payload;
            }

            _freeByLength.Clear(); // freed blocks were dropped by the compact pass
            _loaded = true;
            return mapping;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache.Clear();
        _freeByLength.Clear();
    }
}
