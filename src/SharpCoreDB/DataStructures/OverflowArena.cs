// <copyright file="OverflowArena.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Diagnostics;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Concurrent;
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

    // THREAD SAFETY: the arena is written from the parallel row-serialization path —
    // Table.ValidateAndSerializeBatchOutsideLock runs a Parallel.For over batches > 10,000 rows, and
    // each variable-length value reaches Write() through FixedWidthCodec.WriteSlot. A plain
    // Dictionary corrupted under that concurrency (measured: "concurrent update on a non-concurrent
    // collection" for a fixed-width table with a TEXT column). _cache is therefore concurrent
    // (lock-free single-key reads/writes), and the compound operations below hold _gate.
    private readonly ConcurrentDictionary<long, byte[]> _cache = new();

    /// <summary>
    /// Reusable scratch buffers for <see cref="WriteMany"/>, which the fixed-width codec calls once per
    /// row. Safe as instance state because that method only touches them while holding <c>_gate</c> — and
    /// they are cleared at that point, never after the lock is released: the arena is shared by the
    /// <c>Parallel.For</c> serialisation path, so a finishing thread clearing them on its way out wiped the
    /// list a second thread was still filling (caught by <c>OverflowArenaConcurrencyTests</c>).
    /// </summary>
    private readonly List<int> _appendIndexScratch = [];
    private readonly List<byte[]> _appendPayloadScratch = [];

    // B6: freed block offsets grouped by their payload length, for exact-length in-place reuse.
    // Guarded by _gate (claim/release are compound operations).
    private readonly Dictionary<int, List<long>> _freeByLength = new();

    /// <summary>Serialises the compound arena operations — free-list claim, offset allocation
    /// (<see cref="IStorage.AppendBytes"/> returns the file offset, so two interleaved appends could
    /// otherwise resolve to the same position) and the cache record — so parallel serialization
    /// cannot corrupt or double-claim the shared state.</summary>
    private readonly object _gate = new();

    private int _blockReuses;
    private volatile bool _loaded;

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
            lock (_gate)
            {
                int total = 0;
                foreach (var list in _freeByLength.Values)
                {
                    total += list.Count;
                }

                return total;
            }
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_gate)
        {
            if (_loaded)
            {
                return;
            }

            // §2 instrumentation (2026-09-15): the arena load is guarded by _loaded, so it *should* be one
            // O(arena) pass per arena instance. It is stamped anyway because an O(arena) step per statement
            // is one of the candidate explanations for the per-statement cost the multi-row INSERT benchmark
            // measures, and "should be once" is not the same as "is once".
            long loadStart = WritePathProfiler.Stamp();

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
            WritePathProfiler.Add(WritePathProfiler.Stage.ArenaLoad, loadStart);
        }
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

        // §2 instrumentation (2026-09-15): a fixed-width table routes every variable-length value through
        // here, and the multi-row INSERT profiler attributed 100% of its time to the coarse
        // validate-and-serialize stamps without being able to say whether the arena was the cost. This stamp
        // covers the whole call; the append inside it is stamped separately.
        long arenaStart = WritePathProfiler.Stamp();

        EnsureLoaded();

        // The arena is shared mutable state: ValidateAndSerializeBatchOutsideLock serialises batches
        // > 10,000 rows with Parallel.For, so concurrent Write calls reach here. The whole compound
        // operation is serialised — free-list claim, offset allocation (AppendBytes returns the file
        // offset, so two interleaved appends could otherwise resolve to the same position) and the
        // cache record — so parallel serialization can neither corrupt nor double-claim it.
        lock (_gate)
        {
            if (TryReuseFreeBlock(payload, out var reusedOffset))
            {
                WritePathProfiler.Add(WritePathProfiler.Stage.ArenaWrite, arenaStart);
                return reusedOffset;
            }

            long appendStart = WritePathProfiler.Stamp();
            var offset = _storage.AppendBytes(_filePath, payload);
            WritePathProfiler.Add(WritePathProfiler.Stage.ArenaAppend, appendStart);

            _cache[offset] = payload;
            WritePathProfiler.Add(WritePathProfiler.Stage.ArenaWrite, arenaStart);
            return offset;
        }
    }

    /// <summary>
    /// Writes several payloads and returns their block offsets in the same order — the batched form of
    /// <see cref="Write"/>. Freed blocks of the exact payload length are still reused in place, but every
    /// payload that has to be appended goes out in ONE <c>AppendBytesMultiple</c> call rather than one
    /// <c>AppendBytes</c> per value.
    /// <para>
    /// That single change is the point: <c>AppendBytes</c> opens the arena file with
    /// <c>FileOptions.WriteThrough</c> on every call, which the write-path profiler measured at
    /// **0.4597 ms per value** on the multi-row INSERT workload — with three variable-length columns that is
    /// ~1.4 ms per row, i.e. ~97% of the INSERT. One call for the whole list removes all but one of those
    /// opens.
    /// </para>
    /// </summary>
    /// <param name="payloads">Payloads to write, in the order the returned offsets must follow.</param>
    /// <returns>One block offset per payload, in the same order.</returns>
    public long[] WriteMany(IReadOnlyList<byte[]> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        if (payloads.Count == 0)
        {
            return [];
        }

        long arenaStart = WritePathProfiler.Stamp();
        EnsureLoaded();

        var offsets = new long[payloads.Count];

        lock (_gate)
        {
            // Scratch buffers rather than fresh lists: everything below runs under _gate, so these are
            // single-threaded by construction. They are cleared again before the method returns so the
            // payload references do not outlive the call.
            List<int> appendIndexes = _appendIndexScratch;
            List<byte[]> appendPayloads = _appendPayloadScratch;
            appendIndexes.Clear();
            appendPayloads.Clear();
            // Reuse first: an in-place overwrite of a freed block of the same length costs no append, so it
            // stays a per-value decision. Everything else is deferred into a single storage call below.
            for (int i = 0; i < payloads.Count; i++)
            {
                var payload = payloads[i];
                ArgumentNullException.ThrowIfNull(payload);

                if (TryReuseFreeBlock(payload, out var reusedOffset))
                {
                    offsets[i] = reusedOffset;
                    continue;
                }

                appendIndexes.Add(i);
                appendPayloads.Add(payload);
            }

            if (appendPayloads.Count > 0)
            {
                long appendStart = WritePathProfiler.Stamp();
                var appended = _storage.AppendBytesMultiple(_filePath, appendPayloads);
                WritePathProfiler.Add(WritePathProfiler.Stage.ArenaAppend, appendStart);

                for (int k = 0; k < appended.Length; k++)
                {
                    int index = appendIndexes[k];
                    offsets[index] = appended[k];
                    _cache[appended[k]] = appendPayloads[k];
                }
            }
        }

        WritePathProfiler.Add(WritePathProfiler.Stage.ArenaWrite, arenaStart);
        return offsets;
    }

    /// <summary>
    /// B6: attempts to reuse a freed block of the exact same payload length via an in-place
    /// overwrite. Returns false when no suitable block is free or the storage refuses the
    /// in-place write (e.g. inside a transaction) — the caller then appends.
    /// Caller must hold <c>_gate</c> (the claim/release pair is compound).
    /// </summary>
    private bool TryReuseFreeBlock(byte[] payload, out long offset)
    {
        offset = 0;
        if (!_freeByLength.TryGetValue(payload.Length, out var offsets) || offsets.Count == 0)
        {
            return false;
        }

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

        // In-place overwrite refused (e.g. transaction active): keep the block free for a later
        // write and fall back to appending.
        offsets.Add(offset);
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

        lock (_gate)
        {
            if (!_freeByLength.TryGetValue(payload.Length, out var offsets))
            {
                offsets = [];
                _freeByLength[payload.Length] = offsets;
            }

            offsets.Add(offset);
        }
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

            // ✅ Buffered append mode: the arena file below is DELETED and the temp file moved over it, so
            // the temp blocks must be on disk first (a buffered append would move nothing and then flush
            // into the stale path). (No-op by default.)
            _storage.FlushPendingAppends();

            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            if (newCache.Count > 0)
            {
                File.Move(tempPath, _filePath);
            }

            // The arena file was REPLACED — cached handles would keep reading the deleted one.
            _storage.InvalidateFileHandles(_filePath);

            lock (_gate)
            {
                _cache.Clear();
                foreach (var (newOffset, payload) in newCache)
                {
                    _cache[newOffset] = payload;
                }

                _freeByLength.Clear(); // freed blocks were dropped by the compact pass
                _loaded = true;
            }

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
        lock (_gate)
        {
            _cache.Clear();
            _freeByLength.Clear();
        }
    }
}
