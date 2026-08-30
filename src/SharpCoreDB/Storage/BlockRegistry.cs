// <copyright file="BlockRegistry.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.Services;
using SharpCoreDB.Storage.Scdb;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Block registry for O(1) block name lookups with batched flushing.
/// Maintains in-memory hash table and persisted index.
/// Thread-safe via ConcurrentDictionary.
/// Format: [Header(64B)] [Entry1(64B)] [Entry2(64B)] ... [EntryN(64B)]
/// ✅ Phase 1 Optimization: Batched registry flushes reduce I/O from 500 to ~10 per batch.
/// C# 14: Uses PeriodicTimer, Lock class, and modern async patterns.
/// </summary>
internal sealed class BlockRegistry : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private ulong _registryOffset;
    private ulong _registryLength;
    private readonly ConcurrentDictionary<string, BlockEntry> _blocks;
    private readonly Lock _registryLock = new();

    // ✅ Issue #345: serializes the whole registry flush (snapshot + write) so a stale snapshot
    // from a background flush can never land after a fresher ForceFlush and overwrite it with
    // older registry content (which previously lost the trailing block entries on reopen).
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private volatile bool _inFlush;
    
    // ✅ NEW: Batching infrastructure
    private int _dirtyCount;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private readonly PeriodicTimer _flushTimer;
    private readonly Task _flushTask;
    private readonly CancellationTokenSource _flushCts = new();
    
    // ✅ Performance counters
    private long _totalFlushes;
    private long _totalBlocksWritten;
    private long _batchedFlushes;
    
    // ✅ Configuration - Phase 3 optimized for bulk operations
    private const int BATCH_THRESHOLD = 200;           // Flush after N dirty blocks (increased from 50)
    private const int FLUSH_INTERVAL_MS = 500;         // Or flush every 500ms (increased from 100ms)
    
    private bool _disposed;

    public BlockRegistry(SingleFileStorageProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _registryOffset = provider.RootRegistryOffset;
        _registryLength = provider.RootRegistryLength;
        _blocks = new ConcurrentDictionary<string, BlockEntry>(StringComparer.Ordinal);
        
        // ✅ C# 14: Start periodic flush task
        _flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(FLUSH_INTERVAL_MS));
        _flushTask = Task.Run(PeriodicFlushLoopAsync, _flushCts.Token);
        
        // Load existing registry from disk
        if (_registryLength > 0)
        {
            LoadRegistry();
        }
    }

    /// <summary>
    /// Updates the in-memory on-disk location of the registry block after a grow/relocate.
    /// </summary>
    internal void UpdateLocation(ulong offset, ulong length)
    {
        lock (_registryLock)
        {
            _registryOffset = offset;
            _registryLength = length;
        }
    }

    public int Count => _blocks.Count;

    internal bool HasDirtyEntries => Interlocked.CompareExchange(ref _dirtyCount, 0, 0) > 0;
    
    /// <summary>
    /// Gets performance metrics for monitoring.
    /// Exposed for testing and performance analysis.
    /// </summary>
    public (long TotalFlushes, long BatchedFlushes, long BlocksWritten, int DirtyCount) GetMetrics()
    {
        return (
            Interlocked.Read(ref _totalFlushes),
            Interlocked.Read(ref _batchedFlushes),
            Interlocked.Read(ref _totalBlocksWritten),
            Interlocked.CompareExchange(ref _dirtyCount, 0, 0)
        );
    }

    public bool TryGetBlock(string blockName, out BlockEntry entry)
    {
        return _blocks.TryGetValue(blockName, out entry);
    }

    /// <summary>
    /// ✅ OPTIMIZED: Batched update that defers flush.
    /// Phase 1: Reduces registry flushes from 500 to ~10 per batch.
    /// </summary>
    public void AddOrUpdateBlock(string blockName, BlockEntry entry)
    {
        _blocks[blockName] = entry;
        
        var dirtyCount = Interlocked.Increment(ref _dirtyCount);
        
        // Only trigger flush if batch threshold exceeded (and not in explicit batch)
        if (dirtyCount >= BATCH_THRESHOLD && _batchDepth == 0)
        {
            // Signal flush needed (non-blocking)
            _ = Task.Run(async () => await FlushAsync(CancellationToken.None), _flushCts.Token);
            Interlocked.Increment(ref _batchedFlushes);
        }
    }
    
    // ✅ Phase 4.1: Explicit batch control for ExecuteBatchSQL optimization
    private int _batchDepth = 0;
    
    /// <summary>
    /// Begins an explicit batch operation. Defers all registry flushes until EndBatch().
    /// Can be nested - flush only occurs when outermost batch completes.
    /// </summary>
    public void BeginBatch()
    {
        Interlocked.Increment(ref _batchDepth);
    }
    
    /// <summary>
    /// Ends an explicit batch operation. Flushes registry if this is the outermost batch.
    /// </summary>
    public async Task EndBatchAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Decrement(ref _batchDepth) == 0)
        {
            // Outermost batch complete - flush all pending updates
            if (Interlocked.CompareExchange(ref _dirtyCount, 0, 0) > 0)
            {
                await FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public bool RemoveBlock(string blockName)
    {
        var removed = _blocks.TryRemove(blockName, out _);
        if (removed)
        {
            Interlocked.Increment(ref _dirtyCount);
        }
        return removed;
    }

    public IEnumerable<string> EnumerateBlockNames()
    {
        return _blocks.Keys;
    }

    /// <summary>
    /// ✅ C# 14: Periodic flush background task using PeriodicTimer.
    /// Ensures dirty blocks are flushed even if threshold not reached.
    /// </summary>
    private async Task PeriodicFlushLoopAsync()
    {
        try
        {
            while (await _flushTimer.WaitForNextTickAsync(_flushCts.Token))
            {
                var dirtyCount = Interlocked.CompareExchange(ref _dirtyCount, 0, 0);
                if (dirtyCount > 0)
                {
                    await FlushAsync(_flushCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal
        }
        catch (Exception ex)
        {
            // Log error but don't crash background task
            System.Diagnostics.Debug.WriteLine($"[BlockRegistry] Periodic flush error: {ex.Message}");
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _dirtyCount, 0, 0) == 0)
            return; // Not dirty

        // Retry loop: if the registry outgrows its current block we grow (relocate) and retry.
        for (var attempt = 0; attempt < 16; attempt++)
        {
            // ✅ Issue #345: serialize the whole flush (snapshot + write) so a stale background flush
            // can never overwrite a fresher ForceFlush. The provider also loops until the registry is
            // clean, so entries added during this flush are picked up by the next iteration.
            await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            _inFlush = true;
            try
            {
                byte[]? buffer;
                int writeSize = 0;
                int totalSize;
                KeyValuePair<string, BlockEntry>[] entriesSnapshot;
                var metadataEncrypted = _provider.IsMetadataEncrypted;

                lock (_registryLock)
                {
                    if (Interlocked.Exchange(ref _dirtyCount, 0) == 0)
                        return; // Double-check after acquiring gate + lock

                    entriesSnapshot = _blocks.ToArray();

                    var entrySize = Unsafe.SizeOf<BlockEntry>();
                    totalSize = RegistryChunkHeader.SIZE + (entriesSnapshot.Length * entrySize);

                    var usableSize = metadataEncrypted
                        ? (int)_registryLength - AesGcmEncryption.OverheadSize
                        : (int)_registryLength;

                    if (totalSize > usableSize)
                    {
                        Interlocked.Increment(ref _dirtyCount);
                        buffer = null;
                    }
                    else
                    {
                        writeSize = metadataEncrypted ? (int)_registryLength : totalSize;

                        buffer = ArrayPool<byte>.Shared.Rent(writeSize);
                        var span = buffer.AsSpan(0, writeSize);
                        span.Clear();

                        var header = new RegistryChunkHeader
                        {
                            Magic = RegistryChunkHeader.MAGIC,
                            Version = RegistryChunkHeader.CURRENT_VERSION,
                            EntryCount = (ulong)entriesSnapshot.Length,
                            NextChunkOffset = 0,
                            NextChunkLength = 0
                        };
                        MemoryMarshal.Write(span[..RegistryChunkHeader.SIZE], in header);

                        var offset = RegistryChunkHeader.SIZE;
                        foreach (var (blockName, blockEntry) in entriesSnapshot)
                        {
                            var namedEntry = BlockEntry.WithName(blockName, blockEntry);

                            if (offset + entrySize > totalSize)
                            {
                                throw new InvalidOperationException(
                                    $"Block registry write overflow: offset={offset} entrySize={entrySize} totalSize={totalSize} entries={entriesSnapshot.Length}");
                            }

                            var entrySpan = span.Slice(offset, entrySize);
                            MemoryMarshal.Write(entrySpan, in namedEntry);
                            offset += entrySize;
                        }
                    }
                }
                if (buffer is null)
                {
                    await _provider.GrowRegistryBlockAsync(
                        totalSize + (metadataEncrypted ? AesGcmEncryption.OverheadSize : 0),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    if (metadataEncrypted)
                    {
                        _provider.EncryptRegion(buffer.AsSpan(0, writeSize));
                    }

                    _provider.WriteAt((long)_registryOffset, buffer.AsSpan(0, writeSize));

                    var fileStream = GetFileStream();

                    if (!_flushCts.Token.IsCancellationRequested)
                    {
                        await fileStream.FlushAsync(cancellationToken);
                    }

                    _lastFlushTime = DateTime.UtcNow;
                    Interlocked.Increment(ref _totalFlushes);
                    Interlocked.Add(ref _totalBlocksWritten, entriesSnapshot.Length);
                    return;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: metadataEncrypted);
                }
            }
            finally
            {
                _inFlush = false;
                _flushGate.Release();
            }
        }

        throw new InvalidOperationException("Block registry could not be flushed after multiple growth attempts.");
    }

    /// <summary>
    /// True while a registry flush holds the flush gate. Used to break the re-entrant
    /// deadlock where a registry flush (growing) triggers an FSM flush that would try to
    /// flush the registry again (issue #345 dynamic metadata).
    /// </summary>
    internal bool InFlush => _inFlush;


    /// <summary>
    /// ✅ NEW: Force immediate flush (for transaction commit, disposal).
    /// Ensures all dirty blocks are persisted to disk with full sync.
    /// Internal for testing purposes.
    /// </summary>
    internal async Task ForceFlushAsync(CancellationToken cancellationToken = default)
    {
        var dirtyCount = Interlocked.CompareExchange(ref _dirtyCount, 0, 0);
        if (dirtyCount > 0)
        {
            await FlushAsync(cancellationToken);
            
            var fileStream = GetFileStream();
            fileStream.Flush(flushToDisk: true); // Full disk sync
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            // Stop background flush task
            _flushCts.Cancel();
            _flushTimer.Dispose();
            
            // Wait for background task to complete
            try
            {
                _flushTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            
            // Final flush if dirty
            var dirtyCount = Interlocked.CompareExchange(ref _dirtyCount, 0, 0);
            if (dirtyCount > 0)
            {
                ForceFlushAsync().GetAwaiter().GetResult();
            }

#if DEBUG
            var (totalFlushes, batchedFlushes, blocksWritten, _) = GetMetrics();
            System.Diagnostics.Debug.WriteLine(
                $"[BlockRegistry] Disposed - TotalFlushes: {totalFlushes}, " +
                $"BatchedFlushes: {batchedFlushes}, BlocksWritten: {blocksWritten}");
#endif
        }
        finally
        {
            _flushCts.Dispose();
            _flushGate.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Loads the block registry from disk (dynamic-metadata layout, format v2).
    /// The registry is a single growable block: [RegistryChunkHeader(64)] [BlockEntry...].
    /// For full-at-rest encrypted files the block is read and decrypted in place; a
    /// decryption failure (wrong key / tampered region) is rethrown so the database fails
    /// loudly instead of silently starting with an empty registry.
    /// </summary>
    private void LoadRegistry()
    {
        try
        {
            if (_registryLength == 0)
            {
                return; // No registry block yet (fresh file)
            }

            var fileStream = GetFileStream();
            if (fileStream.Length < (long)(_registryOffset + RegistryChunkHeader.SIZE))
            {
                return; // Empty registry
            }

            var metadataEncrypted = _provider.IsMetadataEncrypted;
            var regionSize = checked((int)_registryLength);
            var buffer = ArrayPool<byte>.Shared.Rent(regionSize);
            try
            {
                var regionSpan = buffer.AsSpan(0, regionSize);
                fileStream.Position = (long)_registryOffset;
                fileStream.ReadExactly(regionSpan);

                // An all-zero region means the registry was never initialized.
                if (regionSpan.IndexOfAnyExcept((byte)0) < 0)
                {
                    return;
                }

                if (metadataEncrypted)
                {
                    // Decrypt in place. Throws on GCM authentication failure (wrong key / tamper).
                    _provider.DecryptRegion(regionSpan);
                }

                var header = RegistryChunkHeader.Parse(regionSpan[..RegistryChunkHeader.SIZE]);
                if (!header.IsValid || header.EntryCount == 0)
                {
                    return;
                }

                var totalEntrySize = checked((int)(header.EntryCount * (ulong)BlockEntry.SIZE));
                if (RegistryChunkHeader.SIZE + totalEntrySize > regionSpan.Length)
                {
                    return; // Header claims more entries than the block can hold
                }

                for (var i = 0; i < (int)header.EntryCount; i++)
                {
                    var offset = RegistryChunkHeader.SIZE + (i * BlockEntry.SIZE);
                    var entry = BlockEntry.Parse(regionSpan.Slice(offset, BlockEntry.SIZE));
                    var blockName = entry.GetName();
                    if (!string.IsNullOrEmpty(blockName))
                    {
                        _blocks[blockName] = entry;
                    }
                }

#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    $"[BlockRegistry] Loaded {_blocks.Count} blocks from disk");
#endif
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: metadataEncrypted);
            }
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Wrong key or tampered region — fail loudly rather than silently opening empty.
            _blocks.Clear();
            throw;
        }
        catch (Exception ex)
        {
            // If loading fails, start with empty registry
            _blocks.Clear();
            System.Diagnostics.Debug.WriteLine(
                $"[BlockRegistry] Failed to load registry: {ex.Message}");
        }
    }

    /// <summary>
    /// Reloads the registry from disk (used after a full VACUUM or key-rotation file swap).
    /// </summary>
    internal void Reload(ulong registryOffset, ulong registryLength)
    {
        _blocks.Clear();
        _registryOffset = registryOffset;
        _registryLength = registryLength > 0 ? registryLength : _registryLength;
        LoadRegistry();
    }

    /// <summary>
    /// Gets the underlying FileStream from the provider.
    /// </summary>
    private FileStream GetFileStream()
    {
        return _provider.GetInternalFileStream();
    }
}
