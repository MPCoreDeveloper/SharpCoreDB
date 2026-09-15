// <copyright file="Storage.Append.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

/// <summary>
/// Storage implementation - Append partial class.
/// Handles append operations with CRITICAL transaction support for batch inserts.
/// THIS IS WHERE THE 680x PERFORMANCE FIX HAPPENS!
///
/// ✅ Known Issue 1 FIX: When encryption is enabled (!NoEncryptMode), each appended record
/// is encrypted with AES-256-GCM BEFORE it is written to disk. New encrypted table files
/// carry an 8-byte magic header (see <see cref="PersistenceConstants.EncryptedTableMagic"/>);
/// legacy plaintext files (no header) remain fully readable — full backward compatibility.
///
/// FORMAT INVARIANT: the byte offset returned/stored as the record position is the literal
/// file offset of the 4-byte length prefix. For encrypted files this prefix is immediately
/// after the 8-byte magic header for the first record. Readers (ReadBytesFrom / ReadAllRecords)
/// read at that offset and decrypt the payload, so B-tree-indexed positions stay consistent
/// with point lookups and RebuildPrimaryKeyIndexFromDisk.
/// </summary>
public partial class Storage
{
    /// <summary>Maximum accepted record/ciphertext length (1 GB).</summary>
    private const int MaxRecordSize = 1_000_000_000;

    /// <summary>Upper bound for the whole-file tombstone-marker range read (DELETE commit path).</summary>
    private const long RangeMarkerReadLimitBytes = 32 * 1024 * 1024;

    /// <summary>AES-GCM overhead = nonce(12) + tag(16).</summary>
    private const int GcmOverhead = CryptoConstants.GCM_NONCE_SIZE + CryptoConstants.GCM_TAG_SIZE;

    // Track buffered appends during transaction
    private readonly Dictionary<string, List<(byte[] data, long position)>> bufferedAppends = new();
    private readonly Dictionary<string, long> cachedFileLengths = new();  // ✅ NEW: Cache file lengths

    /// <summary>
    /// Position → payload index over the rows currently in <see cref="bufferedAppends"/>, so a point
    /// lookup that arrives before the flush returns the buffered row instead of reading past the end of
    /// the file. Kept beside the ordered list (which the flush writes in append order) and lock-free for
    /// readers, mirroring <c>bufferedOverwrites</c>. Mutated under <c>appendLock</c>.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, byte[]>> bufferedAppendLookup =
        new(StringComparer.Ordinal);

    // Bytes buffered since the last flush, and when the current buffer first grew. Together with
    // DatabaseConfig.AppendBufferFlushThresholdBytes / AppendBufferFlushIntervalMs these bound the
    // durability window and the memory footprint of the opt-in mode. Guarded by appendLock.
    private long pendingAppendBytes;
    private long appendBufferSinceTimestamp;


    // ✅ B7: Write-behind log for in-place overwrites made inside a transaction. The original
    // bytes stay on disk until commit (nothing is overwritten early), so rollback is simply
    // dropping this buffer — no undo data needs to be stored. On commit the buffered records
    // are written once per file. Previously every update inside ExecuteBatchSQL fell back to
    // append because OverwriteRecordAt refused to write inside a transaction.
    private readonly ConcurrentDictionary<string, Dictionary<long, byte[]>> bufferedOverwrites = new(StringComparer.Ordinal);

    // ✅ Commit-time tombstones: physical offsets of records deleted inside the current transaction.
    // The marker is NOT written at delete time — a rollback must keep the row. NOSONAR:S125 (prose, not dead code)
    // ApplyBufferedTombstones writes the in-place negative-prefix markers when the transaction
    // commits, after the buffered appends are on disk. Rollback discards the buffer.
    private readonly Dictionary<string, List<long>> bufferedTombstones = new(StringComparer.Ordinal);

    // Base file length captured at the first buffered operation of the transaction. In-place
    // overwrites are only safe below this boundary (records already flushed to disk); offsets
    // at or above it belong to still-buffered appends and must fall back to append.
    private readonly Dictionary<string, long> bufferedFileBaseLengths = new(StringComparer.Ordinal);

    // ✅ NEW: Tracks which buffered files still need the 8-byte magic header written on flush
    // (only for brand-new files created while encryption is enabled).
    private readonly HashSet<string> headerPendingFiles = new(StringComparer.Ordinal);

    // ✅ Known Issue 1 FIX (backward compatibility): Files that already exist WITHOUT the
    // encrypted magic header (legacy databases / NoEncryptMode) must keep receiving plaintext
    // appends. Encrypting new records into a legacy plaintext file would silently corrupt it
    // because the header-detection would not mark the file as encrypted.
    private readonly HashSet<string> legacyPlaintextFiles = new(StringComparer.OrdinalIgnoreCase);

    // ✅ PERFORMANCE: Per-path decision cache (true = encrypt writes to this file). The
    // decision is taken once per file per Storage instance (first append), avoiding a
    // repeated File.Exists + header-read stat on every AppendBytes call — the hot path.
    private readonly ConcurrentDictionary<string, bool> _encryptModeCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock appendLock = new();

    /// <summary>
    /// ✅ Known Issue 1 FIX (opt-in): gate per-record at-rest encryption behind
    /// DatabaseConfig.EnableAtRestRecordEncryption (default false). When disabled, behavior is
    /// byte-for-byte identical to the original engine (plaintext length-prefixed records).
    /// </summary>
    private readonly bool enableAtRestRecordEncryption;

    /// <summary>
    /// Returns true when the current configuration wants per-record at-rest encryption.
    /// </summary>
    private bool UseRecordEncryption => enableAtRestRecordEncryption && !noEncryption;

    /// <summary>
    /// Determines whether writes to <paramref name="path"/> should be encrypted.
    /// New files (or files already carrying the encrypted magic header) are encrypted in
    /// encrypted mode; pre-existing legacy plaintext files stay plaintext forever so an
    /// upgrade never corrupts an existing database. The decision is cached per path.
    /// </summary>
    private bool ShouldEncryptWrites(string path)
    {
        if (!UseRecordEncryption)
        {
            return false;
        }

        return _encryptModeCache.GetOrAdd(path, static (p, self) => self.DecideEncryptWrites(p), this);
    }

    /// <summary>
    /// Evaluates the per-file encryption decision on first encounter. A file that already
    /// exists without the encrypted magic header is treated as legacy plaintext for the
    /// lifetime of this Storage instance.
    /// </summary>
    private bool DecideEncryptWrites(string path)
    {
        if (!File.Exists(path))
        {
            return true; // Brand-new file → write the magic header + encrypted records.
        }

        if (HeaderProbeForWriteDecision(path))
        {
            return true; // Existing encrypted per-record file → keep encrypting.
        }

        // ✅ Known Issue 1 FIX (DDL interplay): CREATE TABLE pre-creates an empty .dat file.
        // A 0-byte file has no legacy records, so it must be treated as brand-new (encrypt),
        // NOT as legacy plaintext. Only files with actual length and no header are legacy.
        if (new FileInfo(path).Length == 0)
        {
            return true;
        }

        legacyPlaintextFiles.Add(path);
        return false; // Legacy plaintext file → never mix encrypted records into it.
    }

    /// <summary>
    /// Same probe as <see cref="FileHasEncryptedHeader"/> but with a SHORT-LIVED open. Used only for the
    /// once-per-path write decision (<see cref="DecideEncryptWrites"/>), never on a per-record path.
    /// </summary>
    /// <remarks>
    /// The cached read handle that <see cref="FileHasEncryptedHeader"/> keeps would otherwise be created by
    /// the first INSERT and then held for the lifetime of the Storage — and on Windows a live handle on a
    /// data file makes the DROP TABLE path fail: its pre-delete probe opens the file with
    /// <c>FileShare.None</c>, which cannot succeed while any other handle (even a read handle that permits
    /// deletion) is open. The decision is memoised per path, so the extra open costs nothing per record,
    /// while the per-record read probe still uses the cached handle it needs.
    /// </remarks>
    private static bool HeaderProbeForWriteDecision(string path)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[PersistenceConstants.EncryptedTableMagicLength];
            return fs.Read(header) == header.Length &&
                header.SequenceEqual(PersistenceConstants.EncryptedTableMagic);
        }
        catch
        {
            // Missing/short/locked → not an encrypted table file (same answer as FileHasEncryptedHeader).
            return false;
        }
    }

    /// <summary>
    /// Encrypts a single record with AES-256-GCM (passthrough when encryption is disabled or
    /// the target is a legacy plaintext file). Output format: [nonce(12)][ciphertext][tag(16)].
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] EncryptRecord(byte[] data, bool encryptWrites) =>
        encryptWrites ? crypto.Encrypt(key, data) : data;

    /// <summary>
    /// Decrypts a single per-record payload. Passthrough when the configuration does not
    /// enable encryption.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[]? DecryptRecord(byte[] payload)
    {
        if (!UseRecordEncryption)
        {
            return payload;
        }

        try
        {
            return crypto.Decrypt(key, payload);
        }
        catch
        {
            // Corrupt or legacy data — surface null so readers treat it as an invalid record.
            return null;
        }
    }

    /// <inheritdoc />
    public byte[]? DecryptRecordPayload(byte[] payload) => DecryptRecord(payload);

    /// <summary>
    /// Looks up a not-yet-flushed buffered append by its physical position. Lock-free (the index is a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/>), mirroring how buffered overwrites are read.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetBufferedAppend(string path, long offset, out byte[] payload)
    {
        if (bufferedAppendLookup.IsEmpty ||
            !bufferedAppendLookup.TryGetValue(path, out var byOffset) ||
            !byOffset.TryGetValue(offset, out payload!))
        {
            payload = null!;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the caller-owned plaintext of a buffered append. The buffer stores exactly what the flush
    /// will write — ciphertext when this path encrypts writes (encrypt-then-buffer, same order as
    /// <see cref="AppendBytes"/>) — so an encrypted file must be decrypted here.
    /// </summary>
    /// <remarks>
    /// The on-disk magic-header probe is deliberately not used: a brand-new encrypted file may not exist
    /// yet while its first records are buffered, so the probe would answer "plaintext" and hand out raw
    /// ciphertext. The memoised per-file write decision answers the same question and is correct for a
    /// file that does not exist yet (it will be created with the header on the flush).
    /// </remarks>
    private byte[]? DecryptBufferedAppendPayload(string path, byte[] payload)
    {
        if (UseRecordEncryption && ShouldEncryptWrites(path))
        {
            return DecryptRecord(payload);
        }

        // Copy: the buffer owns this array, and the caller owns the result (fast paths patch it in place).
        var plaintext = new byte[payload.Length];
        Buffer.BlockCopy(payload, 0, plaintext, 0, payload.Length);
        return plaintext;
    }

    /// <summary>
    /// Yields the not-yet-flushed buffered appends for <paramref name="path"/> in physical offset order,
    /// decrypted for encrypted files. Empty when nothing is buffered — i.e. always, by default.
    /// </summary>
    private IEnumerable<(long RecordOffset, byte[] Data)> ReadBufferedAppends(string path)
    {
        if (bufferedAppendLookup.IsEmpty ||
            !bufferedAppendLookup.TryGetValue(path, out var byOffset) ||
            byOffset.IsEmpty)
        {
            yield break;
        }

        // Order by position: the index is a hash map, and callers (index rebuild, compaction, arena
        // reload) depend on the same file order the flush will produce.
        foreach (var offset in byOffset.Keys.OrderBy(static offset => offset))
        {
            if (byOffset.TryGetValue(offset, out var payload))
            {
                byte[]? record = DecryptBufferedAppendPayload(path, payload);
                if (record is not null)
                {
                    yield return (offset, record);
                }
            }
        }
    }

    /// <summary>
    /// Detects whether <paramref name="path"/> is an encrypted per-record table file by
    /// checking for the 8-byte magic header. Missing/empty/short files → false.
    /// </summary>
    /// <remarks>
    /// PERF: this probe runs on EVERY per-record read once record encryption is on
    /// (<c>ReadBytesFrom</c>/<c>ReadAllRecords</c>), so it must not do more than one cached-handle read:
    /// it used to open a <see cref="FileStream"/> per call, and even a <c>File.Exists</c> +
    /// <c>FileInfo.Length</c> pair costs two metadata syscalls per read. The measured effect of those
    /// syscalls on the two-arm <c>--dual-mode</c> harness was ~11× slower READ/UPDATE/DELETE for the
    /// encrypted default versus <c>NoEncryptMode=true</c>, whose short-circuit skips the probe.
    /// A short read is the same answer the removed length check gave, and the handle open failure the
    /// same answer <c>File.Exists</c> gave.
    /// </remarks>
    private bool FileHasEncryptedHeader(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[PersistenceConstants.EncryptedTableMagicLength];
            int read;
            try
            {
                read = RandomAccess.Read(GetOrOpenReadHandle(path), header, 0);
            }
            catch
            {
                // Stale cache entry (file replaced/removed) — evict and retry once, like ReadBytesRange.
                _readHandleCache.TryRemove(path, out _);
                read = RandomAccess.Read(GetOrOpenReadHandle(path), header, 0);
            }

            return read == header.Length &&
                header.SequenceEqual(PersistenceConstants.EncryptedTableMagic);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initializes the buffered-append state for <paramref name="path"/> on first use inside a
    /// transaction. Detects whether the file is brand-new so a magic header is written on flush
    /// (only for encrypted mode), and caches the current file length once per transaction.
    /// ✅ FORMAT INVARIANT (Known Issue 1): For a brand-new encrypted file the cached length is
    /// initialized to the 8-byte magic header size, so buffered record positions equal the real
    /// on-disk offsets produced when FlushBufferedAppends writes the header first.
    /// </summary>
    /// <param name="path">The table data file path.</param>
    /// <param name="encryptWrites">Whether this file receives encrypted per-record writes.</param>
    private void EnsureAppendInitialized(string path, bool encryptWrites)
    {
        // Get or create buffer for this file
        if (!bufferedAppends.TryGetValue(path, out var fileBuffer))
        {
            fileBuffer = new List<(byte[], long)>();
            bufferedAppends[path] = fileBuffer;

            // ✅ CRITICAL OPTIMIZATION: Cache file length ONCE per file per transaction.
            // This saves ~5 seconds for 10K inserts!
            bool fileExists = File.Exists(path);
            long fileLength = fileExists ? new FileInfo(path).Length : 0;
            long initialLength = fileLength;

            // B7: remember the flushed boundary for this file so in-place overwrites inside
            // the transaction only touch records that already exist on disk.
            bufferedFileBaseLengths[path] = fileLength;

            // ✅ Known Issue 1 FIX: Brand-new encrypted files (absent OR empty, since DDL
            // pre-creates empty .dat files) start after the 8-byte magic header so buffered
            // record positions match the real on-disk offsets after FlushBufferedAppends.
            bool isBrandNew = !fileExists || fileLength == 0;
            if (isBrandNew && encryptWrites)
            {
                initialLength += PersistenceConstants.EncryptedTableMagicLength;
                headerPendingFiles.Add(path);
            }

            cachedFileLengths[path] = initialLength;
        }
    }

    /// <summary>
    /// Writes the 8-byte encrypted-table magic header at the current stream position.
    /// Only called for brand-new files while record encryption is enabled.
    /// </summary>
    private static void WriteEncryptedHeader(FileStream fs)
    {
        fs.Write(PersistenceConstants.EncryptedTableMagic);
    }

    // ✅ NEW: Batch encryption support
    private Optimizations.BufferedAesEncryption? _batchEncryption;
    private readonly bool enableBatchEncryption;
    private readonly int batchEncryptionSizeKB;

    // ✅ PERF: Cached read handles — avoids a kernel CreateFile/CloseHandle per ReadBytesFrom call.
    // Opened with FileShare.ReadWrite|Delete so writers can append while we hold the handle,
    // and the temp directory can be deleted after the database is disposed.
    private readonly ConcurrentDictionary<string, SafeFileHandle> _readHandleCache = new();

    // B7: cached write handles for in-place record overwrites (OverwriteRecordAt). Without this,
    // every in-place UPDATE inside a transaction opened a fresh FileStream per call — measurably
    // slower than the buffered-append path (10k updates: 0.26s → 1.3s). A handle per table file
    // brings the overwrite path back to a single open per file.
    private readonly ConcurrentDictionary<string, SafeFileHandle> _writeHandleCache = new();

    /// <summary>
    /// Returns (or opens) a cached <see cref="SafeFileHandle"/> for random-access reads on <paramref name="path"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SafeFileHandle GetOrOpenReadHandle(string path) =>
        _readHandleCache.GetOrAdd(path, static p =>
            File.OpenHandle(
                p,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None));

    /// <summary>
    /// B7: returns (or opens) a cached <see cref="SafeFileHandle"/> for random-access in-place
    /// overwrites on <paramref name="path"/>. Sharing flags match the read handle so readers see
    /// overwritten bytes immediately.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SafeFileHandle GetOrOpenWriteHandle(string path) =>
        _writeHandleCache.GetOrAdd(path, static p =>
            File.OpenHandle(
                p,
                FileMode.Open,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None));

    /// <summary>
    /// B7: performs an in-place overwrite of a length-prefixed record through the cached write handle
    /// (table data files and the overflow arena alike — the older .ovf branch opened a stream per
    /// overwrite, see plan §4a).
    /// </summary>
    private void WriteRecordInPlace(string path, long offset, ReadOnlySpan<byte> lengthPrefix, ReadOnlySpan<byte> record)
    {
        // PERF: one cached write handle for every table file, INCLUDING the overflow arena. The .ovf
        // branch used to open (and close) a FileStream per overwrite, which made a same-length TEXT
        // update — the shape that reuses a freed arena block instead of appending — ~8x slower than the
        // different-length one (123 us vs 16 us per row; plan §4a). A cached handle for .ovf is safe now
        // that whole-file replacement drops handles (IStorage.InvalidateFileHandles, called by the arena
        // compaction, the table compaction and the fixed-width migration), which is what the special case
        // was presumably guarding against.
        SafeFileHandle writeHandle = GetOrOpenWriteHandle(path);
        RandomAccess.Write(writeHandle, lengthPrefix, offset);
        RandomAccess.Write(writeHandle, record, offset + 4);
    }

    /// <summary>
    /// Closes all cached write handles (paired with <see cref="CloseReadHandles"/>).
    /// </summary>
    public void CloseWriteHandles()
    {
        foreach (var (handleKey, handle) in _writeHandleCache)
        {
            if (_writeHandleCache.TryRemove(handleKey, out _))
            {
                handle.Dispose();
            }
        }
    }

    /// <summary>
    /// Closes and removes all cached read handles.
    /// Call this when the database is disposed so temp directories can be deleted on Windows.
    /// </summary>
    public void CloseReadHandles()
    {
        foreach (var (key, handle) in _readHandleCache)
        {
            if (_readHandleCache.TryRemove(key, out _))
            {
                handle.Dispose();
            }
        }

        CloseWriteHandles();
    }

    /// <inheritdoc />
    public void InvalidateFileHandles(string path)
    {
        if (_readHandleCache.TryRemove(path, out var readHandle))
        {
            readHandle.Dispose();
        }

        if (_writeHandleCache.TryRemove(path, out var writeHandle))
        {
            writeHandle.Dispose();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long AppendBytes(string path, byte[] data)
    {
        // ✅ Known Issue 1 FIX: Encrypt the record BEFORE buffering/writing so the payload
        // never reaches disk as plaintext (unless NoEncryptMode is active, OR the target is
        // a legacy plaintext file that predates this upgrade — those keep plaintext forever
        // to avoid corrupting existing databases).
        bool encryptWrites = ShouldEncryptWrites(path);
        byte[] record = EncryptRecord(data, encryptWrites);
        int recordLength = record.Length;

        // ✅ CRITICAL FIX: Check if in transaction - if so, BUFFER the append!
        // ✅ Buffered append mode: the SAME buffer serves single-row appends outside a transaction — one
        // open/write/close per flush boundary instead of a write-through open/close per row (~512 µs →
        // ~4.5 µs per 64-byte row for the write itself). Engaged by DatabaseConfig.EnableBufferedAppends
        // (explicit opt-in) or by DatabaseConfig.WalDurabilityMode = Async, which the performance presets
        // set and now actually get; see BuffersAppends for the durability contract.
        if (IsInTransaction || BuffersAppends)
        {
            long futurePosition;
            lock (appendLock)
            {
                bool bufferWasEmpty = bufferedAppends.Count == 0;

                EnsureAppendInitialized(path, encryptWrites);

                // ✅ OPTIMIZED: Use cached file length instead of recalculating
                long currentFileLength = cachedFileLengths[path];

                // This is where this data WILL be written when we flush
                futurePosition = currentFileLength;

                // Buffer the append and update cached length
                bufferedAppends[path].Add((record, futurePosition));
                cachedFileLengths[path] = currentFileLength + 4 + recordLength;  // Update cache

                // Read-your-writes: the row is not on disk yet, so the read paths must be able to find
                // it by position (ReadBytesFrom) and in order (ReadAllRecords).
                bufferedAppendLookup
                    .GetOrAdd(path, static _ => new ConcurrentDictionary<long, byte[]>())
                    [futurePosition] = record;

                if (bufferWasEmpty)
                {
                    appendBufferSinceTimestamp = Environment.TickCount64;
                }

                pendingAppendBytes += 4 + recordLength;

                // Opt-in auto-flush (byte threshold or max age) — bounds the durability window and the
                // memory the buffer can hold. Never inside a transaction: a transaction owns its buffer
                // until commit, and FlushTransactionBuffer() is its explicit intermediate flush.
                if (!IsInTransaction && ShouldAutoFlushAppends())
                {
                    FlushBufferedAppends();
                }
            }

            return futurePosition;
        }

        // Normal append (not in transaction) - write immediately
        // B7: FileShare.ReadWrite|Delete so the cached in-place-overwrite write handle and the
        // append path can coexist (a FileShare.Read open would fail while the write handle is open).
        // NOTE: a CACHED write-through append handle is not an option — see plan §5: a live write handle
        // (access=Write) makes an ordinary reader (File.ReadAllBytes, share=Read) fail with a sharing
        // violation, which the suite caught in 10 tests. The per-call open is what keeps the file
        // readable by other processes, and its cost is the remaining item on the INSERT path.
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.WriteThrough);
        long position = fs.Position;

        // ✅ Known Issue 1 FIX: brand-new encrypted files (position 0) receive the 8-byte
        // magic header; records then start immediately after it.
        if (position == 0 && encryptWrites)
        {
            WriteEncryptedHeader(fs);
            position = fs.Position;
        }

        // Write length prefix (ciphertext length for encrypted files, data length otherwise)
        Span<byte> lengthBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, recordLength);
        fs.Write(lengthBuffer);

        // Write encrypted (or plaintext) data
        fs.Write(record.AsSpan());

        // Invalidate cache
        if (this.pageCache != null)
        {
            int pageId = ComputePageId(path, position);
            this.pageCache.EvictPage(pageId);
        }

        return position;
    }
    /// <summary>
    /// Overwrites a length-prefixed record in place at <paramref name="offset"/> (in-place UPDATE).
    /// Returns true only when the new (encrypted) record fits the existing slot — i.e. the stored
    /// length is unchanged, so every following record stays at a valid offset. When the lengths
    /// differ the caller must fall back to <see cref="AppendBytes"/>. Not available inside a
    /// transaction (buffered appends + rollback are append-only by design).
    /// </summary>
    /// <param name="path">The table data file path.</param>
    /// <param name="offset">The physical file offset of the record's 4-byte length prefix.</param>
    /// <param name="data">The plaintext record data to write.</param>
    /// <returns>True when the record was overwritten in place; false when it did not fit.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool OverwriteRecordAt(string path, long offset, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        bool encryptWrites = ShouldEncryptWrites(path);
        byte[] record = EncryptRecord(data, encryptWrites);
        int recordLength = record.Length;

        try
        {
            // Read the existing length prefix via the cached read handle (opened once per table
            // file — cheap), then overwrite via a per-call WRITE-only stream. The original
            // read-write FileStream open measured ~5-8 ms per call on Windows (on-access filters
            // on read-write intent); a write-only open is as fast as the append path's open.
            SafeFileHandle readHandle;
            try
            {
                readHandle = GetOrOpenReadHandle(path);
            }
            catch
            {
                _readHandleCache.TryRemove(path, out _);
                readHandle = GetOrOpenReadHandle(path);
            }

            if (RandomAccess.GetLength(readHandle) < offset + 4)
            {
                return false;
            }

            // Read the existing record's length prefix at the offset (ciphertext length for
            // encrypted files, plaintext length otherwise — identical to AppendBytes).
            Span<byte> lengthBuffer = stackalloc byte[4];
            if (RandomAccess.Read(readHandle, lengthBuffer, offset) != 4)
            {
                return false;
            }

            int existingLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (existingLength != recordLength)
            {
                return false;
            }

            // B7: inside a transaction, buffer the overwrite (write-behind) instead of writing to
            // disk per row; outside one, write it immediately. Nothing is written to disk before
            // commit in the transactional case, so rollback needs no undo data.
            return BufferOrWriteOverwriteInPlace(path, offset, record);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// B7: buffers (inside a transaction) or writes (outside one) an in-place overwrite of a
    /// length-prefixed record whose payload is <paramref name="record"/> (already encrypted when
    /// applicable). The caller guarantees the new payload length equals the stored payload length,
    /// so no length-prefix read/verification is needed.
    /// </summary>
    private bool BufferOrWriteOverwriteInPlace(string path, long offset, byte[] record)
    {
        bool inTransaction = IsInTransaction;
        int recordLength = record.Length;

        try
        {
            if (inTransaction)
            {
                // Only records already flushed to disk (offset below the buffered-appends boundary)
                // can be overwritten in place; still-buffered records fall back to append.
                if (!bufferedFileBaseLengths.TryGetValue(path, out long baseLength))
                {
                    baseLength = File.Exists(path) ? new FileInfo(path).Length : 0;
                    bufferedFileBaseLengths[path] = baseLength;
                }

                if (offset + 4 + recordLength > baseLength)
                {
                    return false;
                }

                lock (appendLock)
                {
                    if (!bufferedOverwrites.TryGetValue(path, out var overwrites))
                    {
                        overwrites = new Dictionary<long, byte[]>();
                        bufferedOverwrites[path] = overwrites;
                    }

                    overwrites[offset] = record;
                }
            }
            else
            {
                Span<byte> lengthBuffer = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, recordLength);
                WriteRecordInPlace(path, offset, lengthBuffer, record);
            }
        }
        catch (IOException)
        {
            return false;
        }

        // Invalidate app-level page cache (mirrors AppendBytes).
        if (this.pageCache != null)
        {
            int pageId = ComputePageId(path, offset);
            this.pageCache.EvictPage(pageId);
        }

        return true;
    }

    /// <summary>
    /// Overwrites a length-prefixed record in place at <paramref name="offset"/> when the caller
    /// guarantees the new plaintext payload has the same length as the stored one (e.g. an in-place
    /// field patch built from the existing record bytes). Skips the length-prefix read/verification
    /// that <see cref="OverwriteRecordAt"/> performs — one less per-row syscall in the batch-DML
    /// hot path.
    /// </summary>
    /// <param name="path">The table data file path.</param>
    /// <param name="offset">The physical file offset of the record's 4-byte length prefix.</param>
    /// <param name="data">The plaintext record data to write (same length as the stored payload).</param>
    /// <returns>True when the record was overwritten/buffered in place.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool OverwriteRecordAtSameLength(string path, long offset, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        bool encryptWrites = ShouldEncryptWrites(path);
        byte[] record = EncryptRecord(data, encryptWrites);

        return BufferOrWriteOverwriteInPlace(path, offset, record);
    }

    /// <inheritdoc />
    public bool HasBufferedAppends(string path) =>
        !bufferedAppendLookup.IsEmpty && bufferedAppendLookup.ContainsKey(path);

    /// <inheritdoc />
    public void FlushPendingAppends() => FlushBufferedAppends();

    /// <inheritdoc />
    public bool HasBufferedOverwrite(string path) =>
        !bufferedOverwrites.IsEmpty && bufferedOverwrites.ContainsKey(path);

    /// <inheritdoc />
    public bool HasBufferedOverwriteAt(string path, long offset) =>
        bufferedOverwrites.TryGetValue(path, out var overwrites) && overwrites.ContainsKey(offset);

    /// <inheritdoc />
    public bool AreRecordsEncrypted(string path) => UseRecordEncryption && FileHasEncryptedHeader(path);

    /// <inheritdoc />
    public void BufferTombstoneForCommit(string path, long offset)
    {
        if (offset < 0)
        {
            return;
        }

        lock (appendLock)
        {
            if (!bufferedTombstones.TryGetValue(path, out var list))
            {
                list = new List<long>();
                bufferedTombstones[path] = list;
            }

            list.Add(offset);
        }
    }

    /// <summary>
    /// Applies every buffered commit-time tombstone as an in-place negative-prefix marker. Runs
    /// from <see cref="FlushBufferedAppendsAndOverwrites"/> (the commit path) AFTER the buffered
    /// appends are on disk, so offsets of rows that were appended AND deleted in the same
    /// transaction are valid. Rollback discards the buffer instead (<see cref="ClearBufferedAppends"/>).
    /// </summary>
    private void ApplyBufferedTombstones()
    {
        if (bufferedTombstones.Count == 0)
        {
            return;
        }

        foreach (var (path, offsets) in bufferedTombstones)
        {
            if (offsets.Count == 0)
            {
                continue;
            }

            TombstoneRecords(path, offsets.ToArray());
        }

        bufferedTombstones.Clear();
    }

    /// <inheritdoc />
    public bool TombstoneRecord(string path, long offset)
    {
        // Read the current slot size so the marker can encode the exact number of bytes to skip
        // (4-byte prefix + payload), keeping every record enumerator aligned.
        int slotSize;
        try
        {
            SafeFileHandle readHandle = GetOrOpenReadHandle(path);
            Span<byte> lengthBuffer = stackalloc byte[4];
            if (RandomAccess.Read(readHandle, lengthBuffer, offset) != 4)
            {
                return false;
            }

            int currentLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (currentLength <= 0)
            {
                return false; // already tombstoned or invalid
            }

            slotSize = 4 + currentLength;
        }
        catch (IOException)
        {
            return false;
        }

        Span<byte> marker = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(marker, -slotSize);

        try
        {
            WriteRecordInPlace(path, offset, marker, ReadOnlySpan<byte>.Empty);
        }
        catch (IOException)
        {
            return false;
        }

        // Invalidate the app-level page cache (mirrors the other in-place writers).
        if (this.pageCache != null)
        {
            int pageId = ComputePageId(path, offset);
            this.pageCache.EvictPage(pageId);
        }

        return true;
    }

    /// <inheritdoc />
    public void TombstoneRecords(string path, long[] offsets)
    {
        if (offsets == null || offsets.Length == 0)
        {
            return;
        }

        HashSet<int>? pagesToEvict = this.pageCache != null ? new HashSet<int>() : null;

        try
        {
            SafeFileHandle readHandle = GetOrOpenReadHandle(path);
            Span<byte> lengthBuffer = stackalloc byte[4];
            Span<byte> marker = stackalloc byte[4];

            // Range fast path: for a large batch on a bounded file, read the whole file ONCE and
            // resolve every record length from the buffer instead of one pread per offset (the
            // per-marker pread dominated the DELETE commit in profiling: ~50ms/10K markers).
            long fileLength = 0;
            byte[]? wholeFile = null;
            if (offsets.Length >= 64)
            {
                fileLength = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (fileLength > 0 && fileLength <= RangeMarkerReadLimitBytes)
                {
                    wholeFile = new byte[(int)fileLength];
                    if (RandomAccess.Read(readHandle, wholeFile, 0) != fileLength)
                    {
                        wholeFile = null;
                    }
                }
            }

            if (wholeFile is not null)
            {
                WriteTombstoneMarkersBatched(path, offsets, wholeFile, pagesToEvict);
            }
            else
            {
                foreach (var offset in offsets)
                {
                    if (offset < 0)
                    {
                        continue;
                    }

                    if (RandomAccess.Read(readHandle, lengthBuffer, offset) != 4)
                    {
                        continue; // offset at/beyond EOF — not a physical record
                    }

                    int currentLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
                    if (currentLength <= 0)
                    {
                        continue; // already tombstoned or invalid
                    }

                    BinaryPrimitives.WriteInt32LittleEndian(marker, -(4 + currentLength));
                    WriteRecordInPlace(path, offset, marker, ReadOnlySpan<byte>.Empty);

                    pagesToEvict?.Add(ComputePageId(path, offset));
                }
            }
        }
        catch (IOException)
        {
            return;
        }

        if (pagesToEvict != null)
        {
            foreach (var pageId in pagesToEvict)
            {
                this.pageCache!.EvictPage(pageId);
            }
        }
    }

    /// <summary>
    /// Applies commit-time tombstone markers for a batch whose record lengths are already resolved
    /// from a whole-file snapshot. The snapshot is patched in memory first (a marker is just a
    /// 4-byte negative length-prefix flip, possibly crossing a storage-page boundary), then every
    /// touched page is flushed with a single write — #373 batched the per-marker length reads; this
    /// batches the marker writes (previously one 4-byte pwrite per marker, which dominated the
    /// DELETE commit phase on dense batches). A flushed page differs from the on-disk bytes only in
    /// its marker words, so the full-page write is byte-for-byte equivalent to the individual
    /// marker writes it replaces.
    /// </summary>
    /// <remarks>
    /// Safety: this helper runs under the same lock as the old per-marker loop. In the transaction
    /// commit path that is <see cref="appendLock"/> (only the committing thread writes the file);
    /// in the durable non-transactional DELETE path the caller already holds the table write lock.
    /// Because each page is written at most once and always from the patched in-memory snapshot, a
    /// rollback can never observe a partially applied marker.
    /// </remarks>
    private void WriteTombstoneMarkersBatched(string path, long[] offsets, byte[] wholeFile, HashSet<int>? pagesToEvict)
    {
        int pageSizeInt = this.pageSize > 0 ? this.pageSize : 4096;
        long pageBytes = pageSizeInt;

        // Pass 1 — patch every valid marker into the snapshot buffer and record the pages touched.
        // A marker's 4 bytes may straddle a page boundary (records are not page-aligned), which is
        // why the patch happens on the contiguous buffer and NOT on per-page copies.
        var pages = new HashSet<long>();
        for (int i = 0; i < offsets.Length; i++)
        {
            long offset = offsets[i];
            if (offset < 0 || offset + 4 > wholeFile.Length)
            {
                continue;
            }

            int currentLength = BinaryPrimitives.ReadInt32LittleEndian(wholeFile.AsSpan((int)offset, 4));
            if (currentLength <= 0)
            {
                continue; // already tombstoned or invalid
            }

            BinaryPrimitives.WriteInt32LittleEndian(wholeFile.AsSpan((int)offset, 4), -(4 + currentLength));
            pages.Add((offset / pageBytes) * pageBytes);
            pages.Add(((offset + 3) / pageBytes) * pageBytes);
        }

        if (pages.Count == 0)
        {
            return;
        }

        FlushTombstonedPages(path, pagesToEvict, wholeFile, pages, pageBytes);
    }

    /// <summary>
    /// Pass 2 of <see cref="WriteTombstoneMarkersBatched"/>: flushes each touched page once from
    /// the already-patched in-memory snapshot. Pages are written in ascending order (one write per
    /// page) directly to the file or the overflow stream for <c>.ovf</c> arenas.
    /// </summary>
    private void FlushTombstonedPages(string path, HashSet<int>? pagesToEvict, byte[] wholeFile, HashSet<long> pages, long pageBytes)
    {
        bool isOvf = path.EndsWith(".ovf", StringComparison.OrdinalIgnoreCase);
        SafeFileHandle? writeHandle = isOvf ? null : GetOrOpenWriteHandle(path);
        FileStream? ovfStream = null;
        try
        {
            if (isOvf)
            {
                ovfStream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None);
            }

            var sortedPages = new long[pages.Count];
            pages.CopyTo(sortedPages);
            Array.Sort(sortedPages);

            foreach (long pageStart in sortedPages)
            {
                int writeLength = (int)Math.Min(pageBytes, wholeFile.Length - pageStart);
                if (writeLength <= 0)
                {
                    continue;
                }

                pagesToEvict?.Add(ComputePageId(path, pageStart));
                if (isOvf)
                {
                    ovfStream!.Position = pageStart;
                    ovfStream.Write(wholeFile, (int)pageStart, writeLength);
                }
                else
                {
                    RandomAccess.Write(writeHandle!, wholeFile.AsSpan((int)pageStart, writeLength), pageStart);
                }
            }
        }
        finally
        {
            ovfStream?.Dispose();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long[] AppendBytesMultiple(string path, List<byte[]> dataBlocks)
    {
        if (dataBlocks == null || dataBlocks.Count == 0)
            return Array.Empty<long>();

        // ✅ CRITICAL FIX: Check if in transaction - if so, BUFFER all appends!
        if (IsInTransaction || BuffersAppends)
        {
            var result = new long[dataBlocks.Count];  // ✅ FIXED: Renamed to 'result' to avoid variable name conflict

            for (int i = 0; i < dataBlocks.Count; i++)
            {
                result[i] = AppendBytes(path, dataBlocks[i]);
            }

            return result;
        }

        // Normal batch append (not in transaction) - write immediately
        var positions = new long[dataBlocks.Count];

        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.WriteThrough);

        Span<byte> lengthBuffer = stackalloc byte[4];

        for (int i = 0; i < dataBlocks.Count; i++)
        {
            var data = dataBlocks[i];

            // ✅ Known Issue 1 FIX: Encrypt each record individually before writing.
            bool encryptWrites = ShouldEncryptWrites(path);
            byte[] record = EncryptRecord(data, encryptWrites);

            positions[i] = fs.Position;

            if (positions[i] == 0 && encryptWrites)
            {
                WriteEncryptedHeader(fs);
                positions[i] = fs.Position;
            }

            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, record.Length);
            fs.Write(lengthBuffer);

            fs.Write(record.AsSpan());

            if (this.pageCache != null)
            {
                int pageId = ComputePageId(path, positions[i]);
                this.pageCache.EvictPage(pageId);
            }
        }

        return positions;
    }

    /// <summary>
    /// Flushes all buffered appends to disk during transaction commit.
    /// CRITICAL PERFORMANCE: This writes ALL buffered inserts in ONE operation!
    /// ✅ NEW: If batch encryption is enabled, encrypts entire batch at once!
    /// ✅ Known Issue 1 FIX: Records are already individually encrypted by AppendBytes, so the
    /// flushed bytes are ciphertext — nothing is written as plaintext. Brand-new encrypted files
    /// receive the 8-byte magic header before their first record, matching the buffered offsets
    /// computed by EnsureAppendInitialized.
    /// </summary>
    internal void FlushBufferedAppends()
    {
        lock (appendLock)
        {
            if (bufferedAppends.Count == 0)
            {
                // Buffered in-place overwrites are flushed by CommitSync/CommitAsync, NOT by
                // intermediate flushes (FlushTransactionBuffer) — an intermediate flush must not
                // make rollback impossible.
                return;
            }

            // ✅ NEW: If batch encryption enabled, encrypt entire batch at once.
            // Per-record encryption already guarantees ciphertext-at-rest; this call is retained
            // for statistics parity with call sites that use BeginBatchEncryption() explicitly.
            if (enableBatchEncryption && _batchEncryption != null && _batchEncryption.HasPendingData)
            {
                byte[]? encryptedBatch = _batchEncryption.FlushBatch();
                _ = encryptedBatch;
            }

            Span<byte> lengthBuffer = stackalloc byte[4];

            foreach (var (path, appends) in bufferedAppends)
            {
                if (appends.Count == 0)
                    continue;

                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536);

                // ✅ Known Issue 1 FIX: Write the 8-byte magic header when this was a
                // brand-new file created while encryption is enabled.
                if (headerPendingFiles.Contains(path))
                {
                    WriteEncryptedHeader(fs);
                    headerPendingFiles.Remove(path);
                }

                foreach (var (data, _) in appends)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, data.Length);
                    fs.Write(lengthBuffer);
                    fs.Write(data.AsSpan());
                }

                fs.Flush(flushToDisk: false);
            }

            bufferedAppends.Clear();
            cachedFileLengths.Clear();
            headerPendingFiles.Clear();
            bufferedFileBaseLengths.Clear();

            // ✅ Buffered append mode: the rows are on disk now, so the position index and the
            // threshold counters reset with the buffer (a brand-new file's header was written above).
            bufferedAppendLookup.Clear();
            pendingAppendBytes = 0;
            appendBufferSinceTimestamp = 0;
        }
    }

    /// <summary>
    /// True when the opt-in append buffer has reached its byte threshold, or has been pending longer
    /// than the configured interval. Only consulted outside a transaction, and only from the append
    /// path — the time bound is therefore "flushed by the next append", not by a timer thread. The
    /// explicit boundaries (commit, <c>Database.Flush()</c>, every structural operation, dispose) do
    /// not depend on it.
    /// </summary>
    private bool ShouldAutoFlushAppends()
    {
        if (!BuffersAppends || pendingAppendBytes == 0)
        {
            return false;
        }

        if (appendBufferFlushThresholdBytes > 0 && pendingAppendBytes >= appendBufferFlushThresholdBytes)
        {
            return true;
        }

        return appendBufferFlushIntervalMs > 0 &&
            Environment.TickCount64 - appendBufferSinceTimestamp >= appendBufferFlushIntervalMs;
    }

    /// <summary>
    /// B7: flushes buffered appends AND buffered in-place overwrites. Only the true commit path
    /// (CommitSync/CommitAsync) calls this — intermediate flushes keep overwrites buffered so
    /// rollback stays possible.
    /// </summary>
    internal void FlushBufferedAppendsAndOverwrites()
    {
        lock (appendLock)
        {
            FlushBufferedAppends();
            FlushBufferedOverwrites();
            ApplyBufferedTombstones();
        }
    }

    /// <summary>
    /// ✅ NEW: Begins batch encryption for bulk operations.
    /// Call at transaction start to enable accumulated plaintext encryption.
    /// </summary>
    public void BeginBatchEncryption()
    {
        if (enableBatchEncryption && !noEncryption)
        {
            _batchEncryption = new Optimizations.BufferedAesEncryption(key, batchEncryptionSizeKB);
        }
    }

    /// <summary>
    /// ✅ NEW: Ends batch encryption and returns encrypted data if needed.
    /// </summary>
    public byte[]? EndBatchEncryption()
    {
        if (_batchEncryption != null)
        {
            byte[]? result = _batchEncryption.FlushBatch();
            _batchEncryption.Dispose();
            _batchEncryption = null;
            return result;
        }
        return null;
    }

    /// <summary>
    /// ✅ NEW: Clears batch encryption without encrypting (for rollback).
    /// </summary>
    public void ClearBatchEncryption()
    {
        if (_batchEncryption != null)
        {
            _batchEncryption.ClearBatch();
            _batchEncryption.Dispose();
            _batchEncryption = null;
        }
    }

    /// <summary>
    /// ✅ NEW: Gets batch encryption statistics.
    /// </summary>
    public (int PlaintextBytes, int MaxSize, decimal FillPercent)? GetBatchEncryptionStats()
    {
        return _batchEncryption?.GetBatchStats();
    }

    /// <summary>
    /// Flushes transaction buffer to disk without committing the transaction.
    /// Used for intermediate flushes during bulk insert operations to prevent excessive memory buildup.
    /// OPTIMIZATION: For HighSpeedInsertMode, flush every GroupCommitSize rows.
    /// </summary>
    public void FlushTransactionBuffer()
    {
        FlushBufferedAppends();
    }

    /// <summary>
    /// Clears all buffered appends during transaction rollback.
    /// B7: in-place overwrites made inside the transaction are restored first (undo log), so a
    /// rollback returns the table file to its pre-transaction state.
    /// </summary>
    internal void ClearBufferedAppends()
    {
        lock (appendLock)
        {
            RestoreBufferedOverwrites();

            bufferedAppends.Clear();
            cachedFileLengths.Clear();  // ✅ Clear cache too
            headerPendingFiles.Clear(); // ✅ Clear pending header markers on rollback
            bufferedFileBaseLengths.Clear();
            bufferedTombstones.Clear(); // Rollback: discard pending commit-time tombstones
            bufferedAppendLookup.Clear(); // Rollback: the buffered rows are discarded, so is the index
            pendingAppendBytes = 0;
            appendBufferSinceTimestamp = 0;
        }
    }

    /// <summary>
    /// B7: discards the buffered in-place overwrites on rollback. Because overwrites are
    /// write-behind (nothing was written to disk), the file already holds the original bytes —
    /// no restore work is needed.
    /// </summary>
    private void RestoreBufferedOverwrites()
    {
        bufferedOverwrites.Clear();
    }

    /// <summary>
    /// B7: writes every buffered in-place overwrite to disk. Called when the transaction is
    /// committed (after the buffered appends are flushed).
    /// </summary>
    private void FlushBufferedOverwrites()
    {
        foreach (var (path, overwrites) in bufferedOverwrites)
        {
            if (overwrites.Count == 0)
            {
                continue;
            }

            try
            {
                if (!TryFlushBufferedOverwritesBatched(path, overwrites))
                {
                    Span<byte> lengthPrefix = stackalloc byte[4];
                    foreach (var (offset, record) in overwrites)
                    {
                        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, record.Length);
                        WriteRecordInPlace(path, offset, lengthPrefix, record);
                    }
                }
            }
            catch (IOException)
            {
                // The in-place overwrite is best-effort; the append path remains authoritative.
            }
        }

        bufferedOverwrites.Clear();
    }

    /// <summary>
    /// Batch-flushes buffered in-place overwrites with one write per touched storage page instead
    /// of two pwrites per row (the UPDATE commit previously did one length-prefix write + one
    /// payload write per buffered row). Current on-disk page content is read once, row payloads are
    /// patched into the copy, and the page is written once; length prefixes are unchanged
    /// (same-length overwrites only). Cross-page payloads take the direct per-record path first so
    /// later page reads already include them.
    /// </summary>
    /// <remarks>
    /// Safety: runs under <see cref="appendLock"/> in the commit path (single writer per file).
    /// Returns false when batching is not applicable so the caller falls back to the per-record
    /// loop — re-writing the same bytes is idempotent.
    /// </remarks>
    /// <returns>True when every overwrite was flushed by this method.</returns>
    private bool TryFlushBufferedOverwritesBatched(string path, Dictionary<long, byte[]> overwrites)
    {
        if (overwrites.Count < 64 || path.EndsWith(".ovf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int pageBytes = this.pageSize > 0 ? this.pageSize : 4096;
        long fileLength = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (fileLength <= 0)
        {
            return false;
        }

        var entries = new (long Offset, byte[] Payload)[overwrites.Count];
        int n = CollectValidOverwriteEntries(overwrites, fileLength, entries);

        if (n == 0)
        {
            return false;
        }

        Array.Sort(entries, 0, n, Comparer<(long Offset, byte[] Payload)>.Create(static (a, b) => a.Offset.CompareTo(b.Offset)));

        var pages = new Dictionary<long, List<(int RelOffset, byte[] Payload)>>();
        var pageStarts = new List<long>();
        var direct = new List<(long Offset, byte[] Payload)>();
        BucketOverwritesByPage(entries, n, pageBytes, pages, pageStarts, direct);

        Span<byte> lengthPrefix = stackalloc byte[4];
        foreach (var (offset, payload) in direct)
        {
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);
            WriteRecordInPlace(path, offset, lengthPrefix, payload);
        }

        if (pages.Count == 0)
        {
            return direct.Count > 0;
        }

        return FlushOverwritePages(path, fileLength, pageBytes, pages, pageStarts);
    }

    /// <summary>
    /// Copies the in-bounds overwrites (offset ≥ 0 and the full [prefix + payload] range inside the
    /// file) into <paramref name="entries"/> and returns how many were copied.
    /// </summary>
    private static int CollectValidOverwriteEntries(Dictionary<long, byte[]> overwrites, long fileLength, (long Offset, byte[] Payload)[] entries)
    {
        int n = 0;
        foreach (var (offset, payload) in overwrites)
        {
            if (offset >= 0 && offset + 4 + payload.Length <= fileLength)
            {
                entries[n++] = (offset, payload);
            }
        }

        return n;
    }

    /// <summary>
    /// Classifies each sorted overwrite entry as either a page-local patch (its full range stays
    /// inside one storage page — recorded for the batched flush) or a cross-page record (added to
    /// <paramref name="direct"/> for the per-record fallback write).
    /// </summary>
    private static void BucketOverwritesByPage(
        (long Offset, byte[] Payload)[] entries,
        int n,
        int pageBytes,
        Dictionary<long, List<(int RelOffset, byte[] Payload)>> pages,
        List<long> pageStarts,
        List<(long Offset, byte[] Payload)> direct)
    {
        for (int i = 0; i < n; i++)
        {
            long offset = entries[i].Offset;
            byte[] payload = entries[i].Payload;
            long pageStart = (offset / pageBytes) * pageBytes;
            if (offset + 4 + payload.Length - pageStart > pageBytes)
            {
                direct.Add((offset, payload));
                continue;
            }

            if (!pages.TryGetValue(pageStart, out var patches))
            {
                patches = new List<(int, byte[])>();
                pages[pageStart] = patches;
                pageStarts.Add(pageStart);
            }

            patches.Add(((int)(offset - pageStart + 4), payload));
        }
    }

    /// <summary>
    /// Flushes every patched page once: the current on-disk page bytes are read into a pooled
    /// buffer, every page-local overwrite payload is copied in, and the page is written back.
    /// Returns false on a partial read so the caller can fall back to the idempotent per-record loop.
    /// </summary>
    private bool FlushOverwritePages(string path, long fileLength, int pageBytes, Dictionary<long, List<(int RelOffset, byte[] Payload)>> pages, List<long> pageStarts)
    {
        SafeFileHandle readHandle = GetOrOpenReadHandle(path);
        SafeFileHandle writeHandle = GetOrOpenWriteHandle(path);
        byte[]? pageBuffer = null;
        try
        {
            pageBuffer = ArrayPool<byte>.Shared.Rent(pageBytes);
            pageStarts.Sort();
            foreach (long pageStart in pageStarts)
            {
                int writeLength = (int)Math.Min(pageBytes, fileLength - pageStart);
                if (writeLength <= 0)
                {
                    continue;
                }

                if (RandomAccess.Read(readHandle, pageBuffer.AsSpan(0, writeLength), pageStart) != writeLength)
                {
                    return false; // partial read — fall back to the idempotent per-record loop
                }

                foreach (var (relOffset, payload) in pages[pageStart])
                {
                    payload.CopyTo(pageBuffer.AsSpan(relOffset, payload.Length));
                }

                RandomAccess.Write(writeHandle, pageBuffer.AsSpan(0, writeLength), pageStart);
                if (this.pageCache != null)
                {
                    this.pageCache.EvictPage(ComputePageId(path, pageStart));
                }
            }
        }
        finally
        {
            if (pageBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(pageBuffer);
            }
        }

        return true;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? ReadBytesFrom(string path, long offset)
    {
        // ✅ Buffered append mode: the row was appended in this session but not flushed yet, so its bytes
        // exist only in the buffer — the disk read below would run past the end of the file/record.
        if (TryGetBufferedAppend(path, offset, out var bufferedAppend))
        {
            return DecryptBufferedAppendPayload(path, bufferedAppend);
        }

        // B7: inside a transaction, a buffered in-place overwrite takes precedence over the disk
        // version (the overwrite is written to disk only at commit). The buffer holds the payload
        // only (its length is the record's stored length).
        if (!bufferedOverwrites.IsEmpty &&
            bufferedOverwrites.TryGetValue(path, out var buffered) &&
            buffered.TryGetValue(offset, out var newRecord) &&
            newRecord.Length is > 0 and <= MaxRecordSize)
        {
            byte[] bufferedPayload = new byte[newRecord.Length];
            Buffer.BlockCopy(newRecord, 0, bufferedPayload, 0, newRecord.Length);

            if (UseRecordEncryption && FileHasEncryptedHeader(path))
            {
                return DecryptRecord(bufferedPayload);
            }

            return bufferedPayload;
        }

        // PERF: Use cached SafeFileHandle + RandomAccess instead of opening a new
        // FileStream for every point-lookup call.  Reusing a handle drops kernel
        // overhead from ~50-100 µs to a single pread/ReadFile syscall (~1-5 µs).
        // Skip File.Exists() — if the handle opens, the file exists.
        SafeFileHandle handle;
        try
        {
            handle = GetOrOpenReadHandle(path);
        }
        catch
        {
            // Handle may have been closed by CloseReadHandles() concurrently — evict and retry once.
            _readHandleCache.TryRemove(path, out _);
            try
            {
                handle = GetOrOpenReadHandle(path);
            }
            catch
            {
                return null;
            }
        }

        // Read length prefix (4 bytes) — the offset is the physical file offset of the
        // 4-byte length prefix (identical for legacy plaintext and encrypted files).
        Span<byte> lengthBuffer = stackalloc byte[4];
        int bytesRead = RandomAccess.Read(handle, lengthBuffer, offset);
        if (bytesRead < 4) return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);

        if (length <= 0 || length > MaxRecordSize) return null;

        byte[] payload = new byte[length];
        bytesRead = RandomAccess.Read(handle, payload.AsSpan(), offset + 4);
        if (bytesRead != length) return null;

        // ✅ Known Issue 1 FIX (opt-in): Per-record AES-256-GCM decryption when the file carries
        // the encrypted magic header AND the at-rest encryption flag is enabled. When the flag
        // is off, files are returned byte-for-byte identical to the original engine (plaintext).
        if (UseRecordEncryption && FileHasEncryptedHeader(path))
        {
            return DecryptRecord(payload);
        }

        return payload;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ✅ Known Issue 1 FIX: Overrides the plaintext-only interface default with a version
    /// that understands per-record AES-256-GCM encryption. Yields the literal physical
    /// file offset of each 4-byte length prefix (the same offset returned by AppendBytes)
    /// together with the decrypted record payload, so B-tree PK positions built from this
    /// enumeration always match point-lookup offsets (ReadBytesFrom).
    /// </remarks>
    public IEnumerable<(long RecordOffset, byte[] Data)> ReadAllRecords(string path)
    {
        if (!File.Exists(path))
        {
            // ✅ Buffered append mode: a brand-new file has no bytes on disk while its first records are
            // still buffered, so the buffered tail IS the whole table in that case.
            foreach (var bufferedOnly in ReadBufferedAppends(path))
            {
                yield return bufferedOnly;
            }

            yield break;
        }

        // ✅ Known Issue 1 FIX (opt-in): only treat a magic-headered file as encrypted when the
        // at-rest encryption flag is enabled; otherwise parse the original plaintext layout.
        bool encrypted = UseRecordEncryption && FileHasEncryptedHeader(path);
        long position = encrypted ? PersistenceConstants.EncryptedTableMagicLength : 0;
        long fileLength = new FileInfo(path).Length;

        // PERF: read the file ONCE into a buffer instead of using the per-record helpers below, which
        // each open a FileStream — two handle open/close pairs PER RECORD. This was originally applied
        // only to encrypted files, which left plaintext files paying ~2 file opens per record: measured
        // ~69 µs/record (RebuildPrimaryKeyIndexFromDisk over a 20K-row plaintext table took 1380 ms,
        // against 36 ms for the same table at rest). The buffer is now used for both layouts, capped by
        // MaxBufferedRecordWalkBytes so an arbitrarily large file still works through the incremental
        // walk (buffer stays null above the cap). Every caller of this method already materialises the
        // whole file (`ReadBytesWithRecordOffsets`, `DecryptTableFileToPlaintext`, the index build and
        // compaction), so this adds no new worst case.
        byte[]? buffer = null;
        if (fileLength > 0 && fileLength <= MaxBufferedRecordWalkBytes)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                buffer = new byte[fs.Length];
                fs.ReadExactly(buffer);
            }
            catch (IOException)
            {
                yield break;
            }
        }

        while (position + 4 <= fileLength)
        {
            bool haveLength = buffer is not null
                ? TryReadInt32FromBuffer(buffer, position, out int length)
                : TryReadInt32At(path, position, out length);
            if (!haveLength)
            {
                yield break;
            }

            if (length < 0)
            {
                // Tombstoned (deleted) record: the prefix stores the negative slot size to skip.
                int slotSize = -length;
                if (slotSize < 4)
                {
                    yield break;
                }

                position += slotSize;
                continue;
            }

            if (length == 0)
            {
                // Valid empty record (a zero-length payload — e.g. an overflow-arena block written
                // for an empty TEXT/BLOB value). There are no payload bytes to read, but the block
                // occupies a real offset, so yield an empty payload and keep scanning. Treating it
                // as the end-of-file would silently drop every later record/block on reload.
                yield return (position, []);
                position += 4;
                continue;
            }

            if (length > MaxRecordSize || position + 4 + length > fileLength)
            {
                yield break; // Invalid or incomplete record tail
            }

            byte[] payload = new byte[length];
            bool havePayload = buffer is not null
                ? TryCopyPayloadFromBuffer(buffer, position + 4, payload)
                : TryReadPayloadAt(path, position + 4, payload);
            if (!havePayload)
            {
                yield break;
            }

            byte[]? recordData = encrypted ? DecryptRecord(payload) : payload;
            if (recordData is null)
            {
                yield break; // Decryption failed — wrong key or corruption
            }

            yield return (position, recordData);
            position += 4 + length;
        }

        // ✅ Buffered append mode: rows appended this session but not flushed yet are not on disk, so the
        // walk above cannot see them. Their positions are strictly increasing and all above the flushed
        // length, so appending them in offset order keeps this enumeration in file order — which the
        // primary-key index rebuild and compaction depend on.
        foreach (var bufferedTail in ReadBufferedAppends(path))
        {
            yield return bufferedTail;
        }
    }

    /// <summary>
    /// Reads a 4-byte integer at <paramref name="position"/> with a dedicated file stream.
    /// Returns false when fewer than 4 bytes could be read.
    /// </summary>
    private static bool TryReadInt32At(string path, long position, out int value)
    {
        value = 0;
        Span<byte> buffer = stackalloc byte[4];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Position = position;
        if (fs.Read(buffer) < 4)
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(buffer);
        return true;
    }

    /// <summary>
    /// Reads <paramref name="payload"/>.Length bytes at <paramref name="position"/> into the
    /// given buffer with a dedicated file stream. Returns false on a partial/incomplete read.
    /// </summary>
    private static bool TryReadPayloadAt(string path, long position, byte[] payload)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        fs.Position = position;
        return fs.Read(payload, 0, payload.Length) == payload.Length;
    }

    /// <summary>
    /// Upper bound for the single-read buffered record walk in <see cref="ReadAllRecords"/>; larger
    /// at-rest files fall back to the per-record reads (the same 512 MB ceiling
    /// <c>ReadBytesRange</c> uses). Every at-rest caller already materialises the whole file, so the
    /// buffered walk is the normal case and the fallback exists only for very large files.
    /// </summary>
    private const long MaxBufferedRecordWalkBytes = 512L * 1024 * 1024;

    /// <summary>Reads a 4-byte length prefix out of the buffered record walk. False past the end.</summary>
    private static bool TryReadInt32FromBuffer(byte[] buffer, long position, out int value)
    {
        value = 0;
        if (position + 4 > buffer.Length)
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan((int)position, 4));
        return true;
    }

    /// <summary>Copies a record payload out of the buffered record walk. False past the end.</summary>
    private static bool TryCopyPayloadFromBuffer(byte[] buffer, long position, byte[] payload)
    {
        if (position + payload.Length > buffer.Length)
        {
            return false;
        }

        Array.Copy(buffer, position, payload, 0, payload.Length);
        return true;
    }
}