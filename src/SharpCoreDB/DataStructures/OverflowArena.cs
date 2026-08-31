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
/// cached in memory for the lifetime of the table; freed blocks are reclaimed by a copy-on-compact
/// pass (persistent free-list / in-place block reuse is a follow-up optimization).
/// </summary>
public sealed class OverflowArena : IDisposable
{
    private readonly IStorage _storage;
    private readonly string _filePath;
    private readonly Dictionary<long, byte[]> _cache = new();
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

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _cache.Clear();

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
    /// Appends a payload to the arena and returns the block offset (the position of the storage
    /// record's length prefix — the value stored in a fixed-width record's variable slot).
    /// </summary>
    public long Write(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EnsureLoaded();

        var offset = _storage.AppendBytes(_filePath, payload);
        _cache[offset] = payload;
        return offset;
    }

    /// <summary>Reads the payload stored at <paramref name="offset"/>, or null when absent.</summary>
    public byte[]? Read(long offset)
    {
        EnsureLoaded();
        return _cache.TryGetValue(offset, out var payload) ? payload : null;
    }

    /// <summary>Drops the block at <paramref name="offset"/> from the live cache (its disk space is
    /// reclaimed by the next copy-on-compact pass).</summary>
    public void Free(long offset)
    {
        EnsureLoaded();
        _cache.Remove(offset);
    }

    /// <summary>
    /// Copy-on-compact: rewrites the live blocks (those in <paramref name="activeOffsets"/>) into a
    /// fresh arena file and returns a mapping from old offset to new offset. Callers must update
    /// the fixed-width records that reference the moved blocks. The free (dropped) blocks are
    /// reclaimed and the cache is rebuilt from the compacted file.
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
    }
}
