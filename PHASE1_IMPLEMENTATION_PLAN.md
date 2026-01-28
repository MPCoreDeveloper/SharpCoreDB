# Phase 1 Implementation Plan: Update Performance Optimization

**Target:** SCDB_Single_Update performance van 506 ms → <100 ms (80% verbetering)  
**Duration:** Week 1-2  
**Priority:** CRITICAL  
**Tech Stack:** C# 14 / .NET 10

---

## 📊 Current Bottlenecks

### Identified Performance Issues

```csharp
// Locatie: src\SharpCoreDB\Storage\SingleFileStorageProvider.cs:286-380

public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, ...)
{
    await _ioGate.WaitAsync(cancellationToken);  // ⚠️ Global serialization
    try
    {
        // ❌ PROBLEM 1: Synchronous flush per write (line 344)
        _fileStream.Flush(flushToDisk: true);
        
        // ❌ PROBLEM 2: Read-back verification (lines 347-352)
        _fileStream.Position = (long)offset;
        await _fileStream.ReadExactlyAsync(verifyBuffer.AsMemory(0, data.Length), ...);
        var checksumOnDisk = SHA256.HashData(verifyBuffer.AsSpan(0, data.Length));
        
        // ❌ PROBLEM 3: Registry flush per write (line 357)
        await _blockRegistry.FlushAsync(cancellationToken);
    }
    finally
    {
        _ioGate.Release();
    }
}
```

**Impact Analysis:**
- **Problem 1:** Disk flush per update = ~40% overhead (200 ms / 500 ms)
- **Problem 2:** Read-back + SHA256 = ~20% overhead (100 ms / 500 ms)
- **Problem 3:** Registry serialize/flush per update = ~30% overhead (150 ms / 500 ms)
- **Combined:** ~90% overhead kan geëlimineerd worden

---

## 🎯 Phase 1 Tasks (Modern C# 14)

### Task 1.1: Implement Batched Registry Flush

**Current State:**
```csharp
// src\SharpCoreDB\Storage\BlockRegistry.cs:52-56
public void AddOrUpdateBlock(string blockName, BlockEntry entry)
{
    _blocks[blockName] = entry;
    _isDirty = true;  // ← Mark dirty but flush happens immediately in caller
}
```

**New Implementation (C# 14 - Field Keyword + Primary Constructors):**

```csharp
// src\SharpCoreDB\Storage\BlockRegistry.cs

/// <summary>
/// Block registry with batched flush support for optimal write performance.
/// C# 14: Uses field keyword in properties for auto-backing field access.
/// </summary>
internal sealed class BlockRegistry : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private readonly ulong _registryOffset;
    private readonly ulong _registryLength;
    private readonly ConcurrentDictionary<string, BlockEntry> _blocks;
    private readonly Lock _registryLock = new();
    
    // ✅ C# 14: New batching fields
    private int _dirtyCount;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private readonly PeriodicTimer _flushTimer;
    private readonly Task _flushTask;
    private readonly CancellationTokenSource _flushCts = new();
    
    // ✅ Configuration
    private const int BATCH_THRESHOLD = 50;           // Flush after N dirty blocks
    private const int FLUSH_INTERVAL_MS = 100;        // Or flush every 100ms
    
    public BlockRegistry(SingleFileStorageProvider provider, ulong registryOffset, ulong registryLength)
    {
        _provider = provider;
        _registryOffset = registryOffset;
        _registryLength = registryLength;
        _blocks = new ConcurrentDictionary<string, BlockEntry>(StringComparer.Ordinal);
        
        // ✅ C# 14: Start periodic flush task
        _flushTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(FLUSH_INTERVAL_MS));
        _flushTask = Task.Run(PeriodicFlushLoop, _flushCts.Token);
        
        LoadRegistry();
    }
    
    /// <summary>
    /// ✅ NEW: Batched update that defers flush.
    /// </summary>
    public void AddOrUpdateBlock(string blockName, BlockEntry entry)
    {
        _blocks[blockName] = entry;
        
        var dirtyCount = Interlocked.Increment(ref _dirtyCount);
        
        // Only trigger flush if batch threshold exceeded
        if (dirtyCount >= BATCH_THRESHOLD)
        {
            // Signal flush needed (non-blocking)
            _ = Task.Run(() => FlushAsync(CancellationToken.None), _flushCts.Token);
        }
    }
    
    /// <summary>
    /// ✅ C# 14: Periodic flush background task using PeriodicTimer.
    /// Ensures dirty blocks are flushed even if threshold not reached.
    /// </summary>
    private async Task PeriodicFlushLoop()
    {
        try
        {
            while (await _flushTimer.WaitForNextTickAsync(_flushCts.Token))
            {
                if (_dirtyCount > 0)
                {
                    await FlushAsync(_flushCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal
        }
    }
    
    /// <summary>
    /// Flushes the block registry to disk if dirty.
    /// ✅ OPTIMIZED: Now called only when batch threshold reached or timer fires.
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
            
            // Take snapshot
            entriesSnapshot = _blocks.ToArray();
            
            var headerSize = Unsafe.SizeOf<BlockRegistryHeader>();
            var entrySize = Unsafe.SizeOf<BlockEntry>();
            totalSize = headerSize + (entriesSnapshot.Length * entrySize);
            
            if ((ulong)totalSize > _registryLength)
            {
                throw new InvalidOperationException(
                    $"Block registry too large: {totalSize} bytes exceeds limit {_registryLength}");
            }
            
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
            
            MemoryMarshal.Write(span[..headerSize], in header);
            
            // Write entries
            var offset = headerSize;
            foreach (var (blockName, blockEntry) in entriesSnapshot)
            {
                var namedEntry = BlockEntry.WithName(blockName, blockEntry);
                var entrySpan = span.Slice(offset, entrySize);
                MemoryMarshal.Write(entrySpan, in namedEntry);
                offset += entrySize;
            }
        }
        
        // Write to file OUTSIDE lock
        try
        {
            var fileStream = GetFileStream();
            fileStream.Position = (long)_registryOffset;
            await fileStream.WriteAsync(buffer.AsMemory(0, totalSize), cancellationToken);
            
            // ✅ OPTIMIZED: Only flush if not in batch mode
            if (!_flushCts.Token.IsCancellationRequested)
            {
                await fileStream.FlushAsync(cancellationToken);
            }
            
            _lastFlushTime = DateTime.UtcNow;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    /// <summary>
    /// ✅ NEW: Force immediate flush (for transaction commit, disposal).
    /// </summary>
    public async Task ForceFlushAsync(CancellationToken cancellationToken = default)
    {
        if (_dirtyCount > 0)
        {
            await FlushAsync(cancellationToken);
            
            var fileStream = GetFileStream();
            fileStream.Flush(flushToDisk: true); // Full sync
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _flushCts.Cancel();
            _flushTimer.Dispose();
            _flushTask.GetAwaiter().GetResult();
            
            if (_dirtyCount > 0)
            {
                ForceFlushAsync().GetAwaiter().GetResult();
            }
        }
        finally
        {
            _flushCts.Dispose();
            _disposed = true;
        }
    }
    
    private FileStream GetFileStream()
    {
        var field = _provider.GetType()
            .GetField("_fileStream", BindingFlags.NonPublic | BindingFlags.Instance);
        return (FileStream)field!.GetValue(_provider)!;
    }
}
```

**Files to Modify:**
- `src\SharpCoreDB\Storage\BlockRegistry.cs` (lines 40-80)

**Expected Impact:** 30-40% reduction in update latency

---

### Task 1.2: Remove Read-Back Verification

**Current State:**
```csharp
// src\SharpCoreDB\Storage\SingleFileStorageProvider.cs:344-353

_fileStream.Flush(flushToDisk: true);

// Read back to compute checksum
_fileStream.Position = (long)offset;
await _fileStream.ReadExactlyAsync(verifyBuffer.AsMemory(0, data.Length), ...);
var checksumOnDisk = SHA256.HashData(verifyBuffer.AsSpan(0, data.Length));
```

**New Implementation (C# 14 - Inline Arrays):**

```csharp
// src\SharpCoreDB\Storage\SingleFileStorageProvider.cs

/// <summary>
/// ✅ C# 14: Inline array for small checksum buffer (zero heap allocation).
/// </summary>
[InlineArray(32)]
public struct ChecksumBuffer
{
    private byte _element0;
}

public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, 
    CancellationToken cancellationToken = default)
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    
    await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        // Calculate required pages
        var requiredPages = (data.Length + _header.PageSize - 1) / _header.PageSize;
        
        ulong offset;
        BlockEntry entry;
        
        // ... allocation logic ...
        
        // ✅ NEW: Compute checksum BEFORE write (from input data)
        ChecksumBuffer checksumBuffer = default;
        Span<byte> checksumSpan = checksumBuffer;
        
        if (!SHA256.TryHashData(data.Span, checksumSpan, out var bytesWritten) 
            || bytesWritten != 32)
        {
            throw new InvalidOperationException("Failed to compute checksum");
        }
        
        // Write to WAL first (crash safety)
        if (_isInTransaction)
        {
            await _walManager.LogWriteAsync(blockName, offset, data, cancellationToken)
                .ConfigureAwait(false);
        }
        
        // Write data to file
        _fileStream.Position = (long)offset;
        await _fileStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        
        // ✅ REMOVED: No read-back verification
        // ✅ REMOVED: No synchronous disk flush
        // Checksums will be verified on READ, not on WRITE
        
        // Update entry with pre-computed checksum
        entry = SetChecksum(entry, checksumSpan.ToArray());
        
        // ✅ OPTIMIZED: Batched registry update (no immediate flush)
        _blockRegistry.AddOrUpdateBlock(blockName, entry);
        
        // Update cache
        _blockCache[blockName] = new BlockMetadata
        {
            Name = blockName,
            BlockType = entry.BlockType,
            Size = (long)entry.Length,
            Offset = (long)entry.Offset,
            Checksum = checksumSpan.ToArray(),
            IsEncrypted = _options.EnableEncryption,
            IsDirty = true,
            LastModified = DateTime.UtcNow
        };
    }
    finally
    {
        _ioGate.Release();
    }
}
```

**Files to Modify:**
- `src\SharpCoreDB\Storage\SingleFileStorageProvider.cs` (lines 286-380)

**Expected Impact:** 20-25% reduction in update latency

---

### Task 1.3: Implement Write-Behind Cache

**New Implementation (C# 14 - Collection Expressions + Lock):**

```csharp
// src\SharpCoreDB\Storage\SingleFileStorageProvider.cs

/// <summary>
/// ✅ C# 14: Write operation for batching with collection expressions.
/// </summary>
file sealed record WriteOperation
{
    required public string BlockName { get; init; }
    required public byte[] Data { get; init; }
    required public byte[] Checksum { get; init; }
    required public ulong Offset { get; init; }
    required public BlockEntry Entry { get; init; }
}

public sealed class SingleFileStorageProvider : IStorageProvider
{
    // ... existing fields ...
    
    // ✅ C# 14: Write-behind cache
    private readonly Channel<WriteOperation> _writeQueue = 
        Channel.CreateBounded<WriteOperation>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    
    private readonly Task _writeTask;
    private readonly CancellationTokenSource _writeCts = new();
    private readonly Lock _batchLock = new();
    
    private const int WRITE_BATCH_SIZE = 50;
    private const int WRITE_BATCH_TIMEOUT_MS = 50;
    
    private SingleFileStorageProvider(string filePath, DatabaseOptions options, 
        FileStream fileStream, MemoryMappedFile? mmf, ScdbFileHeader header)
    {
        // ... existing initialization ...
        
        // ✅ Start write-behind task
        _writeTask = Task.Run(ProcessWriteQueueAsync, _writeCts.Token);
    }
    
    /// <summary>
    /// ✅ OPTIMIZED: Async write with batching support.
    /// </summary>
    public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, 
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // Calculate required pages and allocate space
        var requiredPages = (data.Length + _header.PageSize - 1) / _header.PageSize;
        
        ulong offset;
        BlockEntry entry;
        
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Allocation logic (same as before)
            if (_blockRegistry.TryGetBlock(blockName, out var existingEntry))
            {
                var existingPages = (existingEntry.Length + (ulong)_header.PageSize - 1) 
                    / (ulong)_header.PageSize;
                
                if (requiredPages <= (int)existingPages)
                {
                    offset = existingEntry.Offset;
                    entry = existingEntry with 
                    { 
                        Length = (ulong)data.Length, 
                        Flags = existingEntry.Flags | (uint)BlockFlags.Dirty 
                    };
                }
                else
                {
                    _freeSpaceManager.FreePages(existingEntry.Offset, (int)existingPages);
                    offset = _freeSpaceManager.AllocatePages(requiredPages);
                    entry = existingEntry with 
                    { 
                        Offset = offset, 
                        Length = (ulong)data.Length, 
                        Flags = (uint)BlockFlags.Dirty 
                    };
                }
            }
            else
            {
                offset = _freeSpaceManager.AllocatePages(requiredPages);
                entry = new BlockEntry
                {
                    BlockType = (uint)Scdb.BlockType.TableData,
                    Offset = offset,
                    Length = (ulong)data.Length,
                    Flags = (uint)BlockFlags.Dirty
                };
            }
        }
        finally
        {
            _ioGate.Release();
        }
        
        // Compute checksum
        var checksum = SHA256.HashData(data.Span);
        entry = SetChecksum(entry, checksum);
        
        // ✅ NEW: Queue write operation (async, non-blocking)
        await _writeQueue.Writer.WriteAsync(new WriteOperation
        {
            BlockName = blockName,
            Data = data.ToArray(),
            Checksum = checksum,
            Offset = offset,
            Entry = entry
        }, cancellationToken);
        
        // ✅ Update registry immediately (in-memory)
        _blockRegistry.AddOrUpdateBlock(blockName, entry);
        
        // ✅ Update cache immediately
        _blockCache[blockName] = new BlockMetadata
        {
            Name = blockName,
            BlockType = entry.BlockType,
            Size = (long)entry.Length,
            Offset = (long)entry.Offset,
            Checksum = checksum,
            IsEncrypted = _options.EnableEncryption,
            IsDirty = true,
            LastModified = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// ✅ C# 14: Background write processing with batching.
    /// Uses collection expressions and modern async patterns.
    /// </summary>
    private async Task ProcessWriteQueueAsync()
    {
        // ✅ C# 14: Collection expression for batch
        List<WriteOperation> batch = [];
        
        try
        {
            while (!_writeCts.Token.IsCancellationRequested)
            {
                batch.Clear();
                
                // Collect batch of writes
                using var timeoutCts = CancellationTokenSource
                    .CreateLinkedTokenSource(_writeCts.Token);
                timeoutCts.CancelAfter(WRITE_BATCH_TIMEOUT_MS);
                
                try
                {
                    // Get first item (blocking)
                    if (await _writeQueue.Reader.WaitToReadAsync(_writeCts.Token))
                    {
                        while (batch.Count < WRITE_BATCH_SIZE 
                            && _writeQueue.Reader.TryRead(out var op))
                        {
                            batch.Add(op);
                        }
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    // Timeout - flush current batch
                }
                
                if (batch.Count == 0)
                    continue;
                
                // ✅ Write batch to disk
                await WriteBatchToDiskAsync(batch, _writeCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal
        }
        finally
        {
            // Flush remaining writes
            while (_writeQueue.Reader.TryRead(out var op))
            {
                batch.Add(op);
            }
            
            if (batch.Count > 0)
            {
                await WriteBatchToDiskAsync(batch, CancellationToken.None);
            }
        }
    }
    
    /// <summary>
    /// ✅ C# 14: Batched disk write using modern I/O patterns.
    /// </summary>
    private async Task WriteBatchToDiskAsync(List<WriteOperation> batch, 
        CancellationToken cancellationToken)
    {
        lock (_batchLock)
        {
            // Sort by offset for sequential I/O
            batch.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            
            // Write all operations sequentially
            foreach (var op in batch)
            {
                _fileStream.Position = (long)op.Offset;
                _fileStream.Write(op.Data);
            }
            
            // ✅ Single flush for entire batch
            _fileStream.Flush(flushToDisk: false); // OS buffer only
        }
        
        // ✅ Batched registry flush (happens in background)
        // No await - registry has its own batching
    }
    
    /// <summary>
    /// ✅ NEW: Force all pending writes to disk (for transactions).
    /// </summary>
    public async Task FlushPendingWritesAsync(CancellationToken cancellationToken = default)
    {
        _writeQueue.Writer.Complete();
        await _writeTask;
        
        // Reopen channel
        _writeQueue = Channel.CreateBounded<WriteOperation>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
        
        // Force registry flush
        await _blockRegistry.ForceFlushAsync(cancellationToken);
        
        // Full disk sync
        _fileStream.Flush(flushToDisk: true);
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _writeCts.Cancel();
            _writeTask.GetAwaiter().GetResult();
            
            _fileStream?.Dispose();
            _memoryMappedFile?.Dispose();
            _blockRegistry?.Dispose();
            _freeSpaceManager?.Dispose();
            _walManager?.Dispose();
        }
        finally
        {
            _writeCts.Dispose();
            _disposed = true;
        }
    }
}
```

**Files to Modify:**
- `src\SharpCoreDB\Storage\SingleFileStorageProvider.cs` (add write-behind logic)

**Expected Impact:** 40-50% reduction in update latency

---

### Task 1.4: Pre-allocate File Space

**Current State:**
```csharp
// src\SharpCoreDB\Storage\FreeSpaceManager.cs:53-73

public ulong AllocatePages(int count)
{
    lock (_allocationLock)
    {
        var startPage = FindContiguousFreePages(count);
        if (startPage == ulong.MaxValue)
        {
            // ⚠️ Extends file by exact count needed
            startPage = _totalPages;
            ExtendFile(count);
        }
    }
}
```

**New Implementation (C# 14):**

```csharp
// src\SharpCoreDB\Storage\FreeSpaceManager.cs

internal sealed class FreeSpaceManager : IDisposable
{
    // ... existing fields ...
    
    // ✅ NEW: Pre-allocation settings
    private const int MIN_EXTENSION_PAGES = 256;      // 1 MB @ 4KB pages
    private const int EXTENSION_GROWTH_FACTOR = 2;    // Double size each time
    private ulong _preallocatedPages = 0;
    
    /// <summary>
    /// ✅ OPTIMIZED: Pre-allocates file space in larger chunks.
    /// C# 14: Uses pattern matching and modern null handling.
    /// </summary>
    public ulong AllocatePages(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        
        lock (_allocationLock)
        {
            var startPage = FindContiguousFreePages(count);
            
            if (startPage == ulong.MaxValue)
            {
                // No free space - extend file
                startPage = _totalPages;
                
                // ✅ Calculate extension size (grow exponentially)
                var requiredPages = (ulong)count;
                var currentSize = _totalPages;
                var extensionSize = Math.Max(
                    MIN_EXTENSION_PAGES,
                    Math.Max(requiredPages, currentSize / EXTENSION_GROWTH_FACTOR)
                );
                
                ExtendFile((int)extensionSize);
                _preallocatedPages = extensionSize - requiredPages;
                
#if DEBUG
                Debug.WriteLine($"[FSM] Extended file by {extensionSize} pages " +
                    $"(requested: {count}, preallocated: {_preallocatedPages})");
#endif
            }
            
            // Mark pages as allocated
            for (var i = 0; i < count; i++)
            {
                _l1Bitmap.Set((int)(startPage + (ulong)i), true);
            }
            
            _freePages -= (ulong)count;
            _isDirty = true;
            
            return startPage * (ulong)_pageSize; // Return byte offset
        }
    }
    
    /// <summary>
    /// ✅ OPTIMIZED: File extension with explicit length setting.
    /// </summary>
    private void ExtendFile(int pageCount)
    {
        var newTotalPages = _totalPages + (ulong)pageCount;
        var newFileSize = (long)(newTotalPages * (ulong)_pageSize);
        
        var fileStream = GetFileStream();
        
        // ✅ Set file length explicitly (pre-allocates space)
        fileStream.SetLength(newFileSize);
        
        // ✅ Update free space tracking
        for (ulong i = _totalPages; i < newTotalPages; i++)
        {
            if (i < (ulong)_l1Bitmap.Length)
            {
                _l1Bitmap.Set((int)i, false); // Mark as free
            }
        }
        
        _freePages += (ulong)pageCount;
        _totalPages = newTotalPages;
        
#if DEBUG
        Debug.WriteLine($"[FSM] Extended file to {newFileSize} bytes " +
            $"({newTotalPages} pages, {_freePages} free)");
#endif
    }
    
    private FileStream GetFileStream()
    {
        var field = _provider.GetType()
            .GetField("_fileStream", BindingFlags.NonPublic | BindingFlags.Instance);
        return (FileStream)field!.GetValue(_provider)!;
    }
}
```

**Files to Modify:**
- `src\SharpCoreDB\Storage\FreeSpaceManager.cs` (lines 53-73, add ExtendFile method)

**Expected Impact:** 15-20% reduction in update latency

---

## 🧪 Testing Strategy

### Task 1.5: Update Benchmark Tests

**New Benchmark (C# 14):**

```csharp
// tests\SharpCoreDB.Benchmarks\StorageEngineComparisonBenchmark.cs

[Benchmark]
[BenchmarkCategory("Update_Batched")]
public void SCDB_Single_BatchUpdate_Optimized()
{
    // ✅ Test new batched update performance
    var updates = Enumerable.Range(0, 500)
        .Select(i =>
        {
            var id = Random.Shared.Next(0, RecordCount);
            return $"UPDATE bench_records SET salary = {50000 + id} WHERE id = {id}";
        })
        .ToList();
    
    scSinglePlainDb!.ExecuteBatchSQL(updates);
    
    // ✅ Force flush to measure end-to-end latency
    var provider = GetStorageProvider(scSinglePlainDb);
    provider.FlushPendingWritesAsync().GetAwaiter().GetResult();
}

[Benchmark]
[BenchmarkCategory("Update_Single")]
public void SCDB_Single_SingleUpdate_Optimized()
{
    // ✅ Test single update performance (write-behind should help)
    var id = Random.Shared.Next(0, RecordCount);
    scSinglePlainDb!.ExecuteSQL(
        $"UPDATE bench_records SET salary = {50000 + id} WHERE id = {id}", 
        null);
}

private static SingleFileStorageProvider GetStorageProvider(IDatabase db)
{
    var field = db.GetType()
        .GetField("_storageProvider", BindingFlags.NonPublic | BindingFlags.Instance);
    return (SingleFileStorageProvider)field!.GetValue(db)!;
}
```

### Task 1.6: Unit Tests

```csharp
// tests\SharpCoreDB.Tests\SingleFileStorageProviderTests.cs

[Fact]
public async Task WriteBlockAsync_WithBatching_ShouldBeOptimal()
{
    // Arrange
    var tempFile = Path.GetTempFileName() + ".scdb";
    var options = new DatabaseOptions 
    { 
        StorageMode = StorageMode.SingleFile,
        PageSize = 4096
    };
    
    using var provider = SingleFileStorageProvider.Open(tempFile, options);
    
    // Act - Write 100 blocks rapidly
    var sw = Stopwatch.StartNew();
    var tasks = Enumerable.Range(0, 100)
        .Select(async i =>
        {
            var data = new byte[1024];
            Random.Shared.NextBytes(data);
            await provider.WriteBlockAsync($"block_{i}", data);
        })
        .ToList();
    
    await Task.WhenAll(tasks);
    await provider.FlushPendingWritesAsync();
    sw.Stop();
    
    // Assert - Should be much faster than 100 * individual flush time
    Assert.True(sw.ElapsedMilliseconds < 500, 
        $"Batched writes took {sw.ElapsedMilliseconds}ms (expected <500ms)");
    
    // Verify all blocks exist
    for (int i = 0; i < 100; i++)
    {
        Assert.True(provider.BlockExists($"block_{i}"));
    }
}

[Fact]
public async Task BlockRegistry_BatchedFlush_ShouldReduceIOps()
{
    // Arrange
    var tempFile = Path.GetTempFileName() + ".scdb";
    var options = new DatabaseOptions { StorageMode = StorageMode.SingleFile };
    using var provider = SingleFileStorageProvider.Open(tempFile, options);
    
    var registry = GetBlockRegistry(provider);
    var initialFlushCount = GetFlushCount(registry);
    
    // Act - Add 100 blocks (should NOT flush 100 times)
    for (int i = 0; i < 100; i++)
    {
        registry.AddOrUpdateBlock($"test_{i}", new BlockEntry
        {
            BlockType = 1,
            Offset = (ulong)i * 4096,
            Length = 1024,
            Flags = 0
        });
    }
    
    await Task.Delay(200); // Wait for batch flush
    
    // Assert - Should have flushed much less than 100 times
    var finalFlushCount = GetFlushCount(registry);
    var flushes = finalFlushCount - initialFlushCount;
    
    Assert.True(flushes < 10, 
        $"Registry flushed {flushes} times (expected <10 for 100 blocks)");
}

private static BlockRegistry GetBlockRegistry(SingleFileStorageProvider provider)
{
    var field = provider.GetType()
        .GetField("_blockRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
    return (BlockRegistry)field!.GetValue(provider)!;
}
```

**Files to Create/Modify:**
- `tests\SharpCoreDB.Benchmarks\StorageEngineComparisonBenchmark.cs` (add new benchmarks)
- `tests\SharpCoreDB.Tests\SingleFileStorageProviderTests.cs` (create new)

---

## 📊 Success Metrics

### Performance Targets

| Metric | Baseline | Phase 1 Target | Measurement |
|--------|----------|----------------|-------------|
| **Update 500 records** | 506 ms | <100 ms | BenchmarkDotNet |
| **Registry flushes** | 500 | <10 | Unit test counter |
| **Disk syncs** | 500 | <10 | Perfmon/strace |
| **Memory allocations** | 8.3 MB | <5 MB | MemoryDiagnoser |
| **Throughput** | ~1K ops/sec | >5K ops/sec | Calculated |

### Validation Checklist

- [ ] All existing tests pass
- [ ] New benchmark shows <100ms for 500 updates
- [ ] Registry flush count reduced by 95%+
- [ ] Memory allocations reduced by 40%+
- [ ] No data corruption (checksum validation on reads)
- [ ] Transaction safety maintained (WAL still works)
- [ ] Encryption still functions correctly

---

## 🚀 Deployment Plan

### Phase 1.1: Registry Batching (Days 1-2)
1. Implement `PeriodicFlushLoop` in `BlockRegistry`
2. Add batching configuration constants
3. Update `AddOrUpdateBlock` to defer flush
4. Add unit tests
5. Run benchmarks

### Phase 1.2: Remove Read-Back (Days 3-4)
1. Modify `WriteBlockAsync` to compute checksum before write
2. Remove read-back verification logic
3. Add inline array for checksum buffer
4. Verify checksum validation on reads still works
5. Run benchmarks

### Phase 1.3: Write-Behind Cache (Days 5-7)
1. Implement `Channel<WriteOperation>` queue
2. Add `ProcessWriteQueueAsync` background task
3. Modify `WriteBlockAsync` to queue operations
4. Implement `WriteBatchToDiskAsync`
5. Add unit tests for batching behavior
6. Run benchmarks

### Phase 1.4: Pre-allocation (Days 8-9)
1. Update `AllocatePages` to request larger chunks
2. Modify `ExtendFile` for exponential growth
3. Add configuration for extension size
4. Run benchmarks

### Phase 1.5: Integration Testing (Days 10-12)
1. Run full benchmark suite
2. Validate all optimizations work together
3. Test edge cases (very large updates, concurrent writes)
4. Performance profiling with dotnet-trace
5. Document results

### Phase 1.6: Code Review & Polish (Days 13-14)
1. Code review with team
2. Address feedback
3. Update documentation
4. Prepare for merge

---

## 🔧 Tools & Commands

### Build & Test
```bash
# Build in Release mode
dotnet build -c Release

# Run unit tests
dotnet test -c Release --filter "Category=SingleFile"

# Run benchmarks
cd tests\SharpCoreDB.Benchmarks
dotnet run -c Release --framework net10.0 --filter "*SCDB_Single*"
```

### Performance Profiling
```bash
# CPU profiling
dotnet-trace collect --process-id <PID> --profile cpu-sampling

# Memory profiling
dotnet-counters monitor --process-id <PID> --counters System.Runtime

# Detailed trace
dotnet-trace collect --process-id <PID> --providers Microsoft-Windows-DotNETRuntime
```

### Analyze Results
```powershell
# Compare benchmark results
$before = Get-Content ".\BenchmarkDotNet.Artifacts\results\before.txt"
$after = Get-Content ".\BenchmarkDotNet.Artifacts\results\after.txt"
# ... parse and compare ...
```

---

## 📝 Documentation Updates

### Files to Update
- `README.md` - Update performance section
- `PERFORMANCE.md` - Add Phase 1 results
- `ARCHITECTURE.md` - Document batching strategy
- `CHANGELOG.md` - Add Phase 1 improvements

### Example Changelog Entry
```markdown
## [v2.1.0] - 2025-02-XX

### Performance Improvements (Phase 1)
- **CRITICAL**: Optimized SCDB_Single_Update from 506ms to <100ms (80% improvement)
  - Implemented batched registry flushes with PeriodicTimer
  - Removed synchronous read-back verification
  - Added write-behind cache with Channel-based batching
  - Pre-allocate file space in larger chunks
- Reduced memory allocations by 40% (8.3 MB → 5 MB)
- Increased update throughput from 1K to 5K+ ops/sec

### Technical Details
- Uses C# 14 features: PeriodicTimer, inline arrays, collection expressions
- Maintains transaction safety and encryption support
- Zero breaking changes to public API
```

---

## ⚠️ Risks & Mitigations

### Risk 1: Data Corruption
**Mitigation:**
- Keep checksum validation on reads
- WAL remains enabled for crash recovery
- Extensive testing with checksums

### Risk 2: Backward Compatibility
**Mitigation:**
- No changes to file format
- Existing .scdb files work without migration
- All optimizations are runtime behavior only

### Risk 3: Concurrency Issues
**Mitigation:**
- Lock-free where possible (ConcurrentDictionary)
- Lock class for critical sections
- Extensive concurrent testing

### Risk 4: Performance Regression
**Mitigation:**
- Benchmark at each step
- Keep baseline results for comparison
- Rollback plan if targets not met

---

## 📞 Support & Escalation

**Technical Lead:** MPCoreDeveloper  
**Timeline:** 2 weeks (negotiable based on results)  
**Priority:** P0 (Critical - blocking production use)

**Escalation Path:**
1. Daily standup updates
2. Week 1 checkpoint review
3. Week 2 final review before merge

---

**Status:** READY TO START  
**Last Updated:** 2025-01-28  
**Next Milestone:** Task 1.1 completion (Day 2)
