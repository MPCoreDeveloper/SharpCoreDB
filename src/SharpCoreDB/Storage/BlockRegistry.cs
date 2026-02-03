// <copyright file="BlockRegistry.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

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
    private readonly ulong _registryOffset;
    private readonly ulong _registryLength;
    private readonly ConcurrentDictionary<string, BlockEntry> _blocks;
    private readonly Lock _registryLock = new();
    
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

    public BlockRegistry(SingleFileStorageProvider provider, ulong registryOffset, ulong registryLength)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _registryOffset = registryOffset;
        _registryLength = registryLength > 0 
            ? registryLength 
            : throw new ArgumentOutOfRangeException(nameof(registryLength));
        _blocks = new ConcurrentDictionary<string, BlockEntry>(StringComparer.Ordinal);
        
        // ✅ C# 14: Start periodic flush task
        _flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(FLUSH_INTERVAL_MS));
        _flushTask = Task.Run(PeriodicFlushLoopAsync, _flushCts.Token);
        
        // Load existing registry from disk
        LoadRegistry();
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

    /// <summary>
    /// Flushes the block registry to disk if dirty.
    /// ✅ OPTIMIZED: Now called only when batch threshold reached or timer fires.
    /// Format: [BlockRegistryHeader(64B)] [BlockEntry1(64B)] [BlockEntry2(64B)] ...
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _dirtyCount, 0, 0) == 0)
            return; // Not dirty

        byte[] buffer;
        int totalSize;
        KeyValuePair<string, BlockEntry>[] entriesSnapshot;

        // Prepare data inside lock (fast, synchronous)
        lock (_registryLock)
        {
            if (Interlocked.Exchange(ref _dirtyCount, 0) == 0)
                return; // Double-check after lock

            // Take a snapshot to avoid concurrent mutations during flush
            entriesSnapshot = _blocks.ToArray();

            // Compute struct sizes using runtime sizeof to avoid mismatches
            var headerSize = Unsafe.SizeOf<BlockRegistryHeader>();
            var entrySize = Unsafe.SizeOf<BlockEntry>();
            totalSize = headerSize + (entriesSnapshot.Length * entrySize);

            if ((ulong)totalSize > _registryLength)
            {
                throw new InvalidOperationException(
                    $"Block registry too large: {totalSize} bytes exceeds limit {_registryLength}");
            }

            // Rent buffer from ArrayPool for zero-allocation
            buffer = ArrayPool<byte>.Shared.Rent(totalSize);
            var span = buffer.AsSpan(0, totalSize);
            span.Clear();

            // Write header
            var header = new BlockRegistryHeader
            {
                Magic = BlockRegistryHeader.MAGIC,
                Version = BlockRegistryHeader.CURRENT_VERSION,
                BlockCount = (ulong)entriesSnapshot.Length,
                TotalSize = (ulong)totalSize,
                LastModified = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var headerSpan = span[..headerSize];
            MemoryMarshal.Write(headerSpan, in header);

            // Write block entries from snapshot
            var offset = headerSize;
            foreach (var (blockName, blockEntry) in entriesSnapshot)
            {
                var namedEntry = BlockEntry.WithName(blockName, blockEntry);

                // Bounds guard: ensure we have enough space for the entry
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

        // Write to file OUTSIDE lock (I/O is slow)
        try
        {
            var fileStream = GetFileStream();
            fileStream.Position = (long)_registryOffset;
            await fileStream.WriteAsync(buffer.AsMemory(0, totalSize), cancellationToken);
            
            // ✅ OPTIMIZED: Only flush if not in batch mode or if forced
            if (!_flushCts.Token.IsCancellationRequested)
            {
                await fileStream.FlushAsync(cancellationToken);
            }
            
            _lastFlushTime = DateTime.UtcNow;
            Interlocked.Increment(ref _totalFlushes);
            Interlocked.Add(ref _totalBlocksWritten, entriesSnapshot.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

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
            _disposed = true;
        }
    }

    /// <summary>
    /// Loads the block registry from disk.
    /// </summary>
    private void LoadRegistry()
    {
        try
        {
            var fileStream = GetFileStream();
            
            // Check if registry exists (file large enough)
            if (fileStream.Length < (long)(_registryOffset + BlockRegistryHeader.SIZE))
            {
                return; // Empty registry
            }

            // Read header
            fileStream.Position = (long)_registryOffset;
            Span<byte> headerBuffer = stackalloc byte[BlockRegistryHeader.SIZE];
            fileStream.ReadExactly(headerBuffer);
            
            var header = BlockRegistryHeader.Parse(headerBuffer);
            
            if (!header.IsValid)
            {
                return; // Invalid or empty registry
            }

            if (header.BlockCount == 0)
            {
                return; // No blocks
            }

            // Read all block entries
            var totalEntrySize = (int)(header.BlockCount * (ulong)BlockEntry.SIZE);
            var buffer = ArrayPool<byte>.Shared.Rent(totalEntrySize);
            try
            {
                var entrySpan = buffer.AsSpan(0, totalEntrySize);
                fileStream.ReadExactly(entrySpan);

                // Parse each entry
                for (var i = 0; i < (int)header.BlockCount; i++)
                {
                    var offset = i * BlockEntry.SIZE;
                    var entryData = entrySpan.Slice(offset, BlockEntry.SIZE);
                    var entry = BlockEntry.Parse(entryData);
                    
                    var blockName = entry.GetName();
                    if (!string.IsNullOrEmpty(blockName))
                    {
                        _blocks[blockName] = entry;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                $"[BlockRegistry] Loaded {_blocks.Count} blocks from disk");
#endif
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
    /// Gets the underlying FileStream from the provider.
    /// </summary>
    private FileStream GetFileStream()
    {
        return _provider.GetInternalFileStream();
    }
}
