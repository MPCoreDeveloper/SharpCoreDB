# Row Overflow Implementation Guide

## 🎯 Implementation Phases

This guide provides a step-by-step implementation plan for row overflow in SharpCoreDB.

### Phase Breakdown

| Phase | Components | Duration | Dependencies |
|-------|-----------|----------|-------------|
| **Phase 1** | Configuration & Structures | 1 day | None |
| **Phase 2** | OverflowPageManager Core | 2 days | Phase 1 |
| **Phase 3** | BinaryRowSerializer Integration | 1 day | Phase 2 |
| **Phase 4** | WAL & Recovery | 1 day | Phase 3 |
| **Phase 5** | Testing & Benchmarks | 2 days | Phase 4 |
| **Phase 6** | Documentation & Polish | 1 day | Phase 5 |

**Total**: ~8 days (1-2 sprints)

---

## Phase 1: Configuration & Structures (Day 1)

### 1.1 Update DatabaseOptions

**File**: `src/SharpCoreDB/DatabaseOptions.cs`

```csharp
// Add new properties
public bool EnableRowOverflow { get; set; } = true;
public int RowOverflowThresholdPercent { get; set; } = 75;
public bool CompressOverflowPages { get; set; } = false;
public CompressionAlgorithm OverflowCompressionAlgorithm { get; set; } = CompressionAlgorithm.Brotli;
public OverflowChainMode OverflowChainMode { get; set; } = OverflowChainMode.DoublyLinked;

// Update Validate() method
if (RowOverflowThresholdPercent < 50 || RowOverflowThresholdPercent > 95)
{
    throw new ArgumentException($"RowOverflowThresholdPercent must be between 50 and 95. Got: {RowOverflowThresholdPercent}");
}

if (CompressOverflowPages && !EnableRowOverflow)
{
    throw new ArgumentException("CompressOverflowPages requires EnableRowOverflow=true");
}
```

### 1.2 Add Enums

**File**: `src/SharpCoreDB/Storage/Overflow/OverflowEnums.cs` (new)

```csharp
namespace SharpCoreDB.Storage.Overflow;

public enum CompressionAlgorithm : byte
{
    None = 0,
    Brotli = 1,
    LZ4 = 2,
    Zstd = 3
}

public enum OverflowChainMode : byte
{
    SinglyLinked = 0,
    DoublyLinked = 1
}
```

### 1.3 Update ScdbFileHeader

**File**: `src/SharpCoreDB/Storage/Scdb/ScdbStructures.cs`

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ScdbFileHeader
{
    // ... existing fields (0x0000-0x009F) ...

    // === Overflow Configuration (16 bytes @ 0x00A0) ===
    public ushort FeatureFlags;          // 0x00A0
    public ushort OverflowThresholdBytes;// 0x00A2
    public byte CompressionAlgorithm;    // 0x00A4
    public byte OverflowChainMode;       // 0x00A5
    public unsafe fixed byte OverflowReserved[10]; // 0x00A6-0x00AF

    // Helper properties
    public readonly bool HasRowOverflow => (FeatureFlags & 0x01) != 0;
    public readonly bool HasOverflowCompression => (FeatureFlags & 0x02) != 0;
    public readonly bool HasDoublyLinkedOverflow => (FeatureFlags & 0x04) != 0;

    public void InitializeOverflowSettings(DatabaseOptions options)
    {
        if (options.EnableRowOverflow)
        {
            FeatureFlags |= 0x01;
            OverflowThresholdBytes = (ushort)(options.PageSize * options.RowOverflowThresholdPercent / 100);

            if (options.CompressOverflowPages)
            {
                FeatureFlags |= 0x02;
                CompressionAlgorithm = (byte)options.OverflowCompressionAlgorithm;
            }

            if (options.OverflowChainMode == OverflowChainMode.DoublyLinked)
            {
                FeatureFlags |= 0x04;
                OverflowChainMode = (byte)options.OverflowChainMode;
            }
        }
    }
}
```

### 1.4 Create Overflow Structures

**File**: `src/SharpCoreDB/Storage/Overflow/OverflowStructures.cs` (new)

```csharp
namespace SharpCoreDB.Storage.Overflow;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct OverflowPageHeader
{
    public uint Magic;                   // 0x4F564C46 ("OVLF")
    public byte PageType;                // 2 = Overflow
    public byte Flags;                   // 0x01=Compressed, 0x02=HasPrev, 0x04=HasNext
    public byte CompressionAlgorithm;    
    public byte Reserved1;               

    public ulong PrevPageOffset;         
    public ulong NextPageOffset;         

    public ushort UncompressedSize;      
    public ushort CompressedSize;        
    public uint Checksum;                

    public const uint MAGIC = 0x4F564C46;
    public const int SIZE = 32;

    public readonly bool IsCompressed => (Flags & 0x01) != 0;
    public readonly bool HasPrev => (Flags & 0x02) != 0;
    public readonly bool HasNext => (Flags & 0x04) != 0;
    public readonly bool IsValid => Magic == MAGIC && PageType == 2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RowOverflowMetadata
{
    public uint Magic;                   // 0x524F564C ("ROVL")
    public uint TotalSize;               
    public ushort OverflowPageCount;     
    public ushort InlineSize;            
    public ulong FirstOverflowPageOffset;

    public const uint MAGIC = 0x524F564C;
    public const int SIZE = 24;
    public readonly bool IsValid => Magic == MAGIC;
}
```

### ✅ Phase 1 Checklist

- [ ] Add properties to `DatabaseOptions`
- [ ] Create `OverflowEnums.cs`
- [ ] Update `ScdbFileHeader` with overflow fields
- [ ] Create `OverflowStructures.cs`
- [ ] Add unit tests for structures
- [ ] Update `DatabaseOptions.Validate()`

---

## Phase 2: OverflowPageManager Core (Days 2-3)

### 2.1 Create OverflowPageManager

**File**: `src/SharpCoreDB/Storage/Overflow/OverflowPageManager.cs` (new)

```csharp
namespace SharpCoreDB.Storage.Overflow;

using System;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.Storage;

public sealed class OverflowPageManager(
    SingleFileStorageProvider provider,
    FreeSpaceManager fsm,
    WalManager wal,
    PageCache cache,
    ScdbFileHeader header) : IDisposable
{
    private readonly Lock _overflowLock = new();
    private bool _disposed;

    /// <summary>
    /// Allocates overflow chain and returns first page offset.
    /// C# 14: Uses primary constructor, Span<T>, ArrayPool.
    /// </summary>
    public async Task<ulong> AllocateOverflowChainAsync(
        ReadOnlyMemory<byte> data,
        bool compress,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(data.Length);

        byte[]? processedData = null;
        try
        {
            // 1. Compress if requested
            ReadOnlyMemory<byte> finalData = data;
            bool isCompressed = false;

            if (compress && header.HasOverflowCompression)
            {
                processedData = await CompressDataAsync(data, 
                    (CompressionAlgorithm)header.CompressionAlgorithm, 
                    cancellationToken);

                // Only use compressed if ratio < 0.95 (5% improvement minimum)
                if (processedData.Length < data.Length * 0.95)
                {
                    finalData = processedData;
                    isCompressed = true;
                }
            }

            // 2. Calculate required pages
            int pageDataSize = header.PageSize - OverflowPageHeader.SIZE;
            int requiredPages = (int)Math.Ceiling((double)finalData.Length / pageDataSize);

            // 3. Allocate pages from FSM
            ulong firstPageOffset;
            lock (_overflowLock)
            {
                firstPageOffset = fsm.AllocatePages(requiredPages);
            }

            // 4. Write overflow chain
            ulong currentOffset = firstPageOffset;
            ulong prevOffset = 0;
            int dataOffset = 0;

            for (int i = 0; i < requiredPages; i++)
            {
                bool isLast = (i == requiredPages - 1);
                int chunkSize = Math.Min(pageDataSize, finalData.Length - dataOffset);

                var chunk = finalData.Slice(dataOffset, chunkSize);
                ulong nextOffset = isLast ? 0 : (currentOffset + (ulong)header.PageSize);

                await WriteOverflowPageAsync(
                    currentOffset, 
                    prevOffset, 
                    nextOffset, 
                    chunk, 
                    isCompressed,
                    cancellationToken);

                // Log to WAL
                await wal.LogPageWriteAsync(currentOffset, cancellationToken);

                prevOffset = currentOffset;
                currentOffset = nextOffset;
                dataOffset += chunkSize;

                if (isLast) break;
            }

            return firstPageOffset;
        }
        finally
        {
            if (processedData is not null)
            {
                ArrayPool<byte>.Shared.Return(processedData);
            }
        }
    }

    /// <summary>
    /// Reads entire overflow chain and reconstructs data.
    /// </summary>
    public async Task<byte[]> ReadOverflowChainAsync(
        ulong firstOffset,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(firstOffset);

        var chunks = new List<byte[]>();
        int totalSize = 0;
        bool isCompressed = false;

        ulong currentOffset = firstOffset;

        while (currentOffset != 0)
        {
            // Read overflow page
            var (header, data) = await ReadOverflowPageAsync(currentOffset, cancellationToken);

            chunks.Add(data);
            totalSize += data.Length;
            isCompressed = header.IsCompressed;
            currentOffset = header.NextPageOffset;
        }

        // Reconstruct data
        byte[] result = new byte[totalSize];
        int offset = 0;
        foreach (var chunk in chunks)
        {
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }

        // Decompress if needed
        if (isCompressed)
        {
            result = await DecompressDataAsync(result, 
                (CompressionAlgorithm)header.CompressionAlgorithm, 
                cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Frees entire overflow chain.
    /// </summary>
    public async Task FreeOverflowChainAsync(
        ulong firstOffset,
        CancellationToken cancellationToken = default)
    {
        if (firstOffset == 0) return;

        var offsets = new List<ulong>();
        ulong currentOffset = firstOffset;

        // Collect all page offsets
        while (currentOffset != 0)
        {
            var header = await ReadOverflowHeaderAsync(currentOffset, cancellationToken);
            offsets.Add(currentOffset);
            currentOffset = header.NextPageOffset;
        }

        // Free all pages
        lock (_overflowLock)
        {
            foreach (var offset in offsets)
            {
                int pageIndex = (int)(offset / (ulong)header.PageSize);
                fsm.FreePages(offset, 1);
                
                // Log to WAL
                wal.LogPageFreeAsync(offset, cancellationToken).GetAwaiter().GetResult();
                
                // Invalidate cache
                cache.InvalidatePage(offset);
            }
        }
    }

    // Private helper methods...
    private async Task WriteOverflowPageAsync(
        ulong offset,
        ulong prevOffset,
        ulong nextOffset,
        ReadOnlyMemory<byte> data,
        bool isCompressed,
        CancellationToken cancellationToken)
    {
        // Implementation details...
    }

    private async Task<(OverflowPageHeader header, byte[] data)> ReadOverflowPageAsync(
        ulong offset,
        CancellationToken cancellationToken)
    {
        // Implementation details...
    }

    private async Task<OverflowPageHeader> ReadOverflowHeaderAsync(
        ulong offset,
        CancellationToken cancellationToken)
    {
        // Implementation details...
    }

    private async Task<byte[]> CompressDataAsync(
        ReadOnlyMemory<byte> data,
        CompressionAlgorithm algorithm,
        CancellationToken cancellationToken)
    {
        // Implementation details...
    }

    private async Task<byte[]> DecompressDataAsync(
        byte[] data,
        CompressionAlgorithm algorithm,
        CancellationToken cancellationToken)
    {
        // Implementation details...
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
```

### ✅ Phase 2 Checklist

- [ ] Create `OverflowPageManager.cs`
- [ ] Implement `AllocateOverflowChainAsync()`
- [ ] Implement `ReadOverflowChainAsync()`
- [ ] Implement `FreeOverflowChainAsync()`
- [ ] Implement compression helpers
- [ ] Add unit tests for each method
- [ ] Test doubly-linked chain traversal
- [ ] Test compression/decompression

---

## Phase 3: BinaryRowSerializer Integration (Day 4)

### 3.1 Update BinaryRowSerializer

**File**: `src/SharpCoreDB/Core/Serialization/BinaryRowSerializer.cs`

```csharp
public static class BinaryRowSerializer
{
    /// <summary>
    /// Checks if row should overflow (fast size estimation).
    /// </summary>
    public static bool ShouldOverflow(Dictionary<string, object> row, int thresholdBytes)
    {
        int estimatedSize = sizeof(int); // Column count
        foreach (var (key, value) in row)
        {
            estimatedSize += sizeof(int) + Encoding.UTF8.GetByteCount(key);
            estimatedSize += sizeof(byte) + GetValueSize(value);
        }
        return estimatedSize > thresholdBytes;
    }

    /// <summary>
    /// Serializes row with overflow support.
    /// C# 14: Async all the way, primary constructors.
    /// </summary>
    public static async Task<byte[]> SerializeWithOverflowAsync(
        Dictionary<string, object> row,
        OverflowPageManager overflowMgr,
        int thresholdBytes,
        bool compress,
        CancellationToken cancellationToken = default)
    {
        // 1. Serialize full row
        byte[] fullData = Serialize(row);

        // 2. If below threshold, return as-is
        if (fullData.Length <= thresholdBytes)
        {
            return fullData;
        }

        // 3. Split into inline + overflow
        int inlineSize = thresholdBytes - RowOverflowMetadata.SIZE;
        var inlineData = fullData.AsMemory(0, inlineSize);
        var overflowData = fullData.AsMemory(inlineSize);

        // 4. Allocate overflow chain
        ulong firstOverflowOffset = await overflowMgr.AllocateOverflowChainAsync(
            overflowData, compress, cancellationToken);

        // 5. Create metadata
        var metadata = new RowOverflowMetadata
        {
            Magic = RowOverflowMetadata.MAGIC,
            TotalSize = (uint)fullData.Length,
            OverflowPageCount = (ushort)CalculatePageCount(overflowData.Length, 4064),
            InlineSize = (ushort)inlineSize,
            FirstOverflowPageOffset = firstOverflowOffset
        };

        // 6. Combine metadata + inline data
        byte[] result = new byte[RowOverflowMetadata.SIZE + inlineSize];
        MemoryMarshal.Write(result.AsSpan(0, RowOverflowMetadata.SIZE), in metadata);
        inlineData.CopyTo(result.AsMemory(RowOverflowMetadata.SIZE));

        return result;
    }

    /// <summary>
    /// Deserializes row with overflow support.
    /// </summary>
    public static async Task<Dictionary<string, object>> DeserializeWithOverflowAsync(
        ReadOnlyMemory<byte> data,
        OverflowPageManager overflowMgr,
        CancellationToken cancellationToken = default)
    {
        var span = data.Span;
        uint magic = MemoryMarshal.Read<uint>(span);
        
        if (magic != RowOverflowMetadata.MAGIC)
        {
            // Normal row
            return Deserialize(span);
        }

        // Read metadata
        var metadata = MemoryMarshal.Read<RowOverflowMetadata>(span);
        if (!metadata.IsValid)
        {
            throw new InvalidDataException("Invalid overflow metadata");
        }

        // Read inline data
        var inlineData = data.Slice(RowOverflowMetadata.SIZE, metadata.InlineSize);

        // Read overflow chain
        byte[] overflowData = await overflowMgr.ReadOverflowChainAsync(
            metadata.FirstOverflowPageOffset, cancellationToken);

        // Reconstruct full row
        byte[] fullData = new byte[metadata.TotalSize];
        inlineData.CopyTo(fullData.AsMemory(0, metadata.InlineSize));
        overflowData.CopyTo(fullData.AsMemory(metadata.InlineSize));

        return Deserialize(fullData);
    }

    private static int CalculatePageCount(int dataSize, int pageDataSize)
    {
        return (int)Math.Ceiling((double)dataSize / pageDataSize);
    }
}
```

### ✅ Phase 3 Checklist

- [ ] Add `ShouldOverflow()` method
- [ ] Implement `SerializeWithOverflowAsync()`
- [ ] Implement `DeserializeWithOverflowAsync()`
- [ ] Update `Database` class to use new methods
- [ ] Add unit tests for overflow serialization
- [ ] Test edge cases (row exactly at threshold, etc.)

---

## Phase 4: WAL & Recovery (Day 5)

### 4.1 Update WalManager

**File**: `src/SharpCoreDB/Storage/WalManager.cs`

```csharp
public sealed class WalManager
{
    /// <summary>
    /// Logs overflow page write to WAL.
    /// </summary>
    public async Task LogOverflowPageWriteAsync(
        ulong offset,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        var entry = new WalEntry
        {
            Type = WalEntryType.OverflowPageWrite,
            Offset = offset,
            Data = data
        };

        await WriteWalEntryAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Logs overflow page free to WAL.
    /// </summary>
    public async Task LogOverflowPageFreeAsync(
        ulong offset,
        CancellationToken cancellationToken = default)
    {
        var entry = new WalEntry
        {
            Type = WalEntryType.OverflowPageFree,
            Offset = offset
        };

        await WriteWalEntryAsync(entry, cancellationToken);
    }
}
```

### ✅ Phase 4 Checklist

- [ ] Add `WalEntryType.OverflowPageWrite`
- [ ] Add `WalEntryType.OverflowPageFree`
- [ ] Update WAL replay logic
- [ ] Test crash recovery with overflow pages
- [ ] Test rollback with overflow chains

---

## Phase 5: Testing & Benchmarks (Days 6-7)

### 5.1 Unit Tests

**File**: `tests/SharpCoreDB.Tests/OverflowTests.cs` (new)

```csharp
public class OverflowTests
{
    [Fact]
    public async Task SmallRow_DoesNotOverflow()
    {
        // Arrange: 2KB row, 3KB threshold
        // Act: Serialize
        // Assert: No overflow metadata
    }

    [Fact]
    public async Task LargeRow_Overflows()
    {
        // Arrange: 10KB row, 3KB threshold
        // Act: Serialize with overflow
        // Assert: Overflow metadata + chain created
    }

    [Fact]
    public async Task OverflowChain_CanBeRead()
    {
        // Arrange: Write 10KB row with overflow
        // Act: Read back
        // Assert: Data matches original
    }

    [Fact]
    public async Task OverflowChain_CanBeFreed()
    {
        // Arrange: Allocate overflow chain
        // Act: Free chain
        // Assert: FSM marks pages as free
    }

    [Fact]
    public async Task Compression_ReducesStorage()
    {
        // Arrange: 1MB JSON row
        // Act: Serialize with Brotli
        // Assert: Storage < 350KB (65% reduction)
    }

    [Fact]
    public async Task DoublyLinkedChain_CanTraverseBackward()
    {
        // Arrange: 3-page overflow chain
        // Act: Read last page, traverse backward
        // Assert: Can reach first page
    }
}
```

### 5.2 Benchmarks

**File**: `tests/SharpCoreDB.Benchmarks/OverflowBenchmarks.cs` (new)

```csharp
[MemoryDiagnoser]
public class OverflowBenchmarks
{
    [Benchmark]
    public async Task WriteRow_5KB_NoOverflow()
    {
        // 5KB row, 8KB threshold
    }

    [Benchmark]
    public async Task WriteRow_10KB_WithOverflow()
    {
        // 10KB row, 3KB threshold
    }

    [Benchmark]
    public async Task WriteRow_1MB_WithBrotli()
    {
        // 1MB JSON, Brotli compression
    }

    [Benchmark]
    public async Task ReadRow_WithOverflow()
    {
        // Read 10KB row from overflow chain
    }
}
```

### ✅ Phase 5 Checklist

- [ ] Create `OverflowTests.cs`
- [ ] Test all overflow scenarios
- [ ] Create `OverflowBenchmarks.cs`
- [ ] Run benchmarks and validate performance
- [ ] Test crash recovery
- [ ] Test with real-world data (JSON, logs)

---

## Phase 6: Documentation & Polish (Day 8)

### 6.1 Update Documentation

- [ ] Update `DESIGN.md` with final implementation details
- [ ] Add overflow examples to `README.md`
- [ ] Update `COMPRESSION_ANALYSIS.md` with benchmark results
- [ ] Add migration guide for existing databases
- [ ] Update API documentation (XML comments)

### 6.2 Polish

- [ ] Add logging for overflow operations
- [ ] Add telemetry (overflow page count, compression ratio)
- [ ] Optimize hot paths (profile and improve)
- [ ] Add configuration validation
- [ ] Review code for C# 14 compliance

### ✅ Phase 6 Checklist

- [ ] Documentation complete
- [ ] Code review
- [ ] Performance validation
- [ ] Backward compatibility verified
- [ ] Ready for merge

---

## 📊 Success Metrics

| Metric | Target | Validation |
|--------|--------|------------|
| **Compression ratio** | > 70% for JSON | Benchmark |
| **Write overhead** | < 20% for overflow rows | Benchmark |
| **Read overhead** | < 10% for overflow rows | Benchmark |
| **Recovery time** | < 100ms for 1000 overflow pages | Test |
| **Code coverage** | > 90% | Unit tests |

---

## 🚨 Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| **Performance regression** | High | Benchmark before/after |
| **Compression too slow** | Medium | Make optional, use LZ4 fallback |
| **WAL corruption** | High | Extensive recovery testing |
| **Cache thrashing** | Medium | Lower priority for overflow pages |
| **Backward incompatibility** | High | Feature flag + migration guide |

---

**Status**: Implementation ready  
**Next Step**: Start Phase 1  
**Last Updated**: 2025-01-28
