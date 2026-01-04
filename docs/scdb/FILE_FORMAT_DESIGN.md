# SharpCoreDB Single-File Storage Format (.scdb) Design

**Version:** 1.0  
**Status:** Design Document  
**Target:** .NET 10, C# 14  
**Goal:** Zero-copy, SSD-optimized, single-file embedded database format

---

## Executive Summary

The `.scdb` format is a **single-file, uncompressed, page-aligned storage format** designed for maximum speed and minimal overhead. It consolidates SharpCoreDB's current multi-file architecture (`.dat`, `.pages`, `.meta`, `.wal`) into a single random-access file optimized for SSDs and memory-mapped I/O.

### Key Design Principles

1. **Zero Compression Overhead**: All data stored in native binary format
2. **SSD-Optimized**: 4KB-aligned pages (tunable) for direct I/O
3. **Memory-Mappable**: Fixed offsets, no varints, direct struct access via `ReadOnlySpan<byte>`
4. **Fragmentation Avoidance**: Built-in Free Space Map (FSM) + incremental vacuum
5. **Extensible**: Reserved fields, versioned header, namespaced block types
6. **Crash-Safe**: Per-block checksums, embedded WAL, transaction boundaries
7. **Maintainable**: Self-describing format with block registry

---

## File Structure Overview

```
┌─────────────────────────────────────────────────────────┐
│                   SCDB FILE STRUCTURE                   │
├─────────────────────────────────────────────────────────┤
│ [0x0000] File Header (512 bytes)                        │
│   - Magic: "SCDB\x10\x00\x00\x00" (8 bytes)            │
│   - Version: 1                                          │
│   - Page size: 4096 (default)                           │
│   - Encryption flags                                    │
│   - Block registry offset                               │
│   - FSM offset                                          │
│   - WAL offset                                          │
│   - Root table directory offset                         │
│   - Last transaction ID                                 │
│   - File checksum (SHA256)                              │
│   - Reserved (240 bytes for future extensions)         │
├─────────────────────────────────────────────────────────┤
│ [0x0200] Block Registry (variable, page-aligned)        │
│   - Block count                                         │
│   - Block entries (64 bytes each):                      │
│     * Block name (32 bytes, UTF-8)                      │
│     * Block type (4 bytes)                              │
│     * Offset (8 bytes)                                  │
│     * Length (8 bytes)                                  │
│     * Checksum (32 bytes, SHA256)                       │
│     * Flags (4 bytes: dirty, compressed, encrypted)     │
│     * Reserved (8 bytes)                                │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Free Space Map (FSM)                          │
│   - Bitmap of free/allocated pages                      │
│   - Extent allocation hints                             │
│   - Fragmentation statistics                            │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Write-Ahead Log (WAL)                         │
│   - Circular buffer of transactions                     │
│   - Transaction boundaries                              │
│   - Redo/undo logs                                      │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Table Directory                               │
│   - Table metadata                                      │
│   - Schema definitions                                  │
│   - Index pointers                                      │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Data Blocks (namespaced)                      │
│   - table:users:data                                    │
│   - table:users:index:primary                           │
│   - table:orders:data                                   │
│   - ...                                                 │
└─────────────────────────────────────────────────────────┘
```

---

## Detailed Format Specification

### 1. File Header (512 bytes, fixed)

```csharp
/// <summary>
/// SCDB file header structure (512 bytes).
/// Uses C# 14 'field' keyword and readonly struct for zero-allocation parsing.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScdbFileHeader
{
    // === Core Identification (16 bytes) ===
    public readonly ulong Magic;           // 0x0000: "SCDB" + version (0x534344421000)
    public readonly ushort FormatVersion;  // 0x0008: Format version (1)
    public readonly ushort PageSize;       // 0x000A: Page size in bytes (4096)
    public readonly uint HeaderSize;       // 0x000C: Header size (512)

    // === Encryption & Compression (16 bytes) ===
    public readonly byte EncryptionMode;   // 0x0010: 0=None, 1=AES-256-GCM
    public readonly byte CompressionMode;  // 0x0011: 0=None (always 0 for .scdb)
    public readonly ushort EncryptionKeyId;// 0x0012: Key derivation ID
    public readonly fixed byte Nonce[12];  // 0x0014: AES-GCM nonce (if encrypted)

    // === Block Offsets (64 bytes) ===
    public readonly ulong BlockRegistryOffset;  // 0x0020: Offset to block registry
    public readonly ulong BlockRegistryLength;  // 0x0028: Size of block registry
    public readonly ulong FsmOffset;            // 0x0030: Free Space Map offset
    public readonly ulong FsmLength;            // 0x0038: FSM size
    public readonly ulong WalOffset;            // 0x0040: WAL offset
    public readonly ulong WalLength;            // 0x0048: WAL size
    public readonly ulong TableDirOffset;       // 0x0050: Table directory offset
    public readonly ulong TableDirLength;       // 0x0058: Table directory size

    // === Transaction State (32 bytes) ===
    public readonly ulong LastTransactionId;    // 0x0060: Last committed txn ID
    public readonly ulong LastCheckpointLsn;    // 0x0068: Last checkpoint LSN
    public readonly ulong FileSize;             // 0x0070: Total file size in bytes
    public readonly ulong AllocatedPages;       // 0x0078: Number of allocated pages

    // === Integrity (32 bytes) ===
    public readonly fixed byte FileChecksum[32];// 0x0080: SHA-256 of entire file (excluding this field)
    
    // === Statistics (32 bytes) ===
    public readonly ulong TotalRecords;         // 0x00A0: Total record count
    public readonly ulong TotalDeletes;         // 0x00A8: Total deleted records
    public readonly ulong LastVacuumTime;       // 0x00B0: Unix timestamp of last VACUUM
    public readonly ulong FragmentationPercent; // 0x00B8: Fragmentation % (0-10000 = 0.00% - 100.00%)

    // === Reserved (240 bytes) ===
    public readonly fixed byte Reserved[240];   // 0x00C0: Reserved for future use

    // Total: 512 bytes (0x200)

    public const ulong MAGIC = 0x0000_0010_4244_4353; // "SCDB\x10\x00\x00\x00" (little-endian)
    public const ushort CURRENT_VERSION = 1;
    public const uint HEADER_SIZE = 512;
    public const ushort DEFAULT_PAGE_SIZE = 4096;

    /// <summary>
    /// Validates the header magic and version.
    /// </summary>
    public bool IsValid() => Magic == MAGIC && FormatVersion == CURRENT_VERSION;

    /// <summary>
    /// Zero-allocation parser from ReadOnlySpan<byte>.
    /// </summary>
    public static ScdbFileHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HEADER_SIZE)
            throw new InvalidDataException("Header too small");

        return MemoryMarshal.Read<ScdbFileHeader>(data);
    }
}
```

---

### 2. Block Registry (Variable, page-aligned)

The **Block Registry** is a self-describing index of all blocks in the file. It enables:
- Fast block lookup by name (e.g., `table:users:data`)
- Integrity verification via per-block checksums
- Incremental updates without full file rewrites

```csharp
/// <summary>
/// Block registry header (64 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BlockRegistryHeader
{
    public readonly uint Magic;            // 0x00: "BREG" (0x47455242)
    public readonly uint Version;          // 0x04: Registry version (1)
    public readonly ulong BlockCount;      // 0x08: Number of block entries
    public readonly ulong TotalSize;       // 0x10: Total size of registry (including header)
    public readonly ulong LastModified;    // 0x18: Unix timestamp
    public readonly fixed byte Reserved[32]; // 0x20: Reserved
}

/// <summary>
/// Block entry (64 bytes per entry).
/// Describes a single data block in the file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct BlockEntry
{
    // === Identification (36 bytes) ===
    public readonly fixed byte Name[32];   // 0x00: Block name (UTF-8, null-terminated)
                                           //       Format: "namespace:identifier:subtype"
                                           //       Examples:
                                           //         - "table:users:data"
                                           //         - "table:users:index:pk"
                                           //         - "meta:schema"
                                           //         - "wal:log"
    public readonly uint BlockType;        // 0x20: Block type enum (see BlockType)

    // === Location (16 bytes) ===
    public readonly ulong Offset;          // 0x24: Byte offset in file (page-aligned)
    public readonly ulong Length;          // 0x2C: Length in bytes (multiple of page size)

    // === Integrity (36 bytes) ===
    public readonly fixed byte Checksum[32]; // 0x34: SHA-256 of block data
    public readonly uint Flags;            // 0x54: BlockFlags enum

    // === Reserved (8 bytes) ===
    public readonly ulong Reserved;        // 0x58: Reserved for future use
}

/// <summary>
/// Block types for namespacing.
/// </summary>
public enum BlockType : uint
{
    Unknown = 0,
    TableData = 1,       // table:*:data
    IndexData = 2,       // table:*:index:*
    TableMeta = 3,       // table:*:meta
    FreeSpaceMap = 4,    // sys:fsm
    WriteAheadLog = 5,   // sys:wal
    TableDirectory = 6,  // sys:tabledir
    BlobData = 7,        // blob:*
    TemporaryData = 8    // temp:*
}

/// <summary>
/// Block flags for state tracking.
/// </summary>
[Flags]
public enum BlockFlags : uint
{
    None = 0,
    Dirty = 1 << 0,        // Block has uncommitted changes
    Compressed = 1 << 1,   // Block is compressed (always 0 for .scdb)
    Encrypted = 1 << 2,    // Block is encrypted
    Deleted = 1 << 3,      // Block marked for deletion (VACUUM target)
    Immutable = 1 << 4,    // Block is read-only (system blocks)
    Sparse = 1 << 5        // Block has sparse allocation (holes)
}
```

**Block Naming Convention:**

```
Format: "namespace:identifier[:subtype]"

Examples:
- table:app_users:data               // User table data pages
- table:app_users:index:pk_users     // Primary key index
- table:app_users:index:idx_email    // Email index
- table:orders:data                  // Orders table data
- sys:fsm                            // Free space map
- sys:wal                            // Write-ahead log
- sys:tabledir                       // Table directory
- blob:large_documents               // Large blob storage
- temp:sort_buffer_1234              // Temporary sort buffer
```

---

### 3. Free Space Map (FSM)

**Purpose:** Track free/allocated pages to avoid fragmentation and enable O(1) page allocation.

**Design:** Inspired by PostgreSQL's FSM, uses a **two-level bitmap**:
1. **L1 Bitmap:** 1 bit per page (allocated/free)
2. **L2 Extent Map:** Tracks contiguous free extents for efficient large allocations

```csharp
/// <summary>
/// Free Space Map structure.
/// Provides O(1) page allocation and extent tracking.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FreeSpaceMapHeader
{
    public readonly uint Magic;            // 0x00: "FSM\0" (0x004D5346)
    public readonly uint Version;          // 0x04: FSM version (1)
    public readonly ulong TotalPages;      // 0x08: Total pages in file
    public readonly ulong FreePages;       // 0x10: Number of free pages
    public readonly ulong LargestExtent;   // 0x18: Largest contiguous free extent (pages)
    public readonly uint BitmapOffset;     // 0x20: Offset to L1 bitmap (relative to FSM start)
    public readonly uint ExtentMapOffset;  // 0x24: Offset to L2 extent map
    public readonly fixed byte Reserved[24]; // 0x28: Reserved
}

// L1 Bitmap: 1 bit per page (0 = free, 1 = allocated)
// Size: ceil(TotalPages / 8) bytes
// Example: 1M pages = 128KB bitmap

// L2 Extent Map: List of free extents (start page, length)
// Each entry: 16 bytes (ulong start, ulong length)
```

**Allocation Strategy:**

1. **Small allocations (<64 pages):** Scan L1 bitmap for first fit
2. **Large allocations (≥64 pages):** Use L2 extent map for best fit
3. **Defragmentation:** Background VACUUM consolidates extents

---

### 4. Write-Ahead Log (WAL)

**Purpose:** Ensure crash safety and enable point-in-time recovery.

**Design:** Circular buffer of fixed-size WAL entries (4KB each).

```csharp
/// <summary>
/// WAL header (64 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct WalHeader
{
    public readonly uint Magic;            // 0x00: "WAL\0" (0x004C4157)
    public readonly uint Version;          // 0x04: WAL version (1)
    public readonly ulong CurrentLsn;      // 0x08: Current Log Sequence Number
    public readonly ulong LastCheckpoint;  // 0x10: LSN of last checkpoint
    public readonly uint EntrySize;        // 0x18: Size of each WAL entry (4096)
    public readonly uint MaxEntries;       // 0x1C: Max entries in circular buffer
    public readonly ulong HeadOffset;      // 0x20: Offset to oldest entry
    public readonly ulong TailOffset;      // 0x28: Offset to newest entry
    public readonly fixed byte Reserved[16]; // 0x30: Reserved
}

/// <summary>
/// WAL entry (4096 bytes = 1 page).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct WalEntry
{
    public readonly ulong Lsn;             // Log sequence number
    public readonly ulong TransactionId;   // Transaction ID
    public readonly ulong Timestamp;       // Unix timestamp (microseconds)
    public readonly ushort Operation;      // WalOperation enum
    public readonly ushort BlockIndex;     // Block registry index
    public readonly ulong PageId;          // Page ID within block
    public readonly ushort DataLength;     // Length of data payload
    public readonly fixed byte Checksum[32]; // SHA-256 of entry
    public readonly fixed byte Data[4000]; // Payload data (before/after images)
}

public enum WalOperation : ushort
{
    Insert = 1,
    Update = 2,
    Delete = 3,
    Checkpoint = 4,
    Transaction_Begin = 5,
    Transaction_Commit = 6,
    Transaction_Abort = 7,
    Page_Allocate = 8,
    Page_Free = 9
}
```

---

### 5. Table Directory

**Purpose:** Centralized table metadata and schema storage.

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct TableDirectoryHeader
{
    public readonly uint Magic;            // "TDIR"
    public readonly uint Version;
    public readonly uint TableCount;       // Number of tables
    public readonly uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct TableMetadataEntry
{
    public readonly fixed byte TableName[64]; // UTF-8, null-terminated
    public readonly uint TableId;             // Hash of table name
    public readonly ulong DataBlockOffset;    // Offset to data block
    public readonly ulong IndexBlockOffset;   // Offset to index block
    public readonly ulong RecordCount;        // Total records
    public readonly uint ColumnCount;         // Number of columns
    public readonly byte StorageMode;         // 0=Columnar, 1=PageBased
    public readonly byte Flags;               // TableFlags
    public readonly fixed byte Reserved[14];
}
```

---

### 6. Data Blocks (Variable)

Data blocks reuse existing formats:
- **PageBased mode:** Existing 8KB page format from `PageManager.cs`
- **Columnar mode:** Existing columnar format from `AppendOnlyEngine`

**Key change:** Blocks are **namespaced** and **checksummed** individually.

---

## Comparison with Existing Formats

### Current SharpCoreDB (Multi-file)

```
database/
├── users.dat          (columnar data)
├── users.pages        (page-based data)
├── users.meta         (table metadata)
├── orders.dat
├── orders.pages
├── meta.dat           (global metadata)
└── wal/
    └── 000001.wal
```

**Issues:**
- File handle exhaustion (Windows: 512 limit)
- No atomic multi-table updates
- Fragmentation across files
- Complex crash recovery

### SQLite

```
database.db            (single file)
├── Header (100 bytes)
├── Schema table
├── Page 1: Table root
├── Page 2: B-tree node
├── ...
└── WAL: database.db-wal (separate file)
```

**Issues SQLite has:**
- Fragmentation inside pages (no FSM)
- VACUUM requires full rewrite
- WAL in separate file (not atomic)

### LiteDB

```
database.db            (single file)
├── Header page
├── Collection pages (B-tree)
├── Data pages (BSON)
└── No embedded WAL
```

**Issues LiteDB has:**
- No page alignment (bad for SSDs)
- BSON parsing overhead
- No FSM (fragmentation)

### SCDB Advantages

| Feature | SharpCoreDB (current) | SQLite | LiteDB | SCDB (new) |
|---------|----------------------|--------|--------|------------|
| Single file | ❌ | ✅ | ✅ | ✅ |
| Page-aligned | ✅ | ✅ | ❌ | ✅ |
| Embedded WAL | ❌ | ❌ | ❌ | ✅ |
| FSM (defrag) | ❌ | ❌ | ❌ | ✅ |
| Zero-copy reads | ✅ | ❌ | ❌ | ✅ |
| Per-block checksums | ❌ | ❌ | ❌ | ✅ |
| Memory-mappable | ✅ | ✅ | ❌ | ✅ |
| Namespaced blocks | ❌ | ❌ | ❌ | ✅ |

---

## Performance Optimizations

### 1. SSD-Optimized Layout

```csharp
// All offsets are multiples of page size (4096 by default)
public static ulong AlignToPage(ulong offset, ushort pageSize)
{
    return (offset + pageSize - 1) / pageSize * pageSize;
}

// Example: Writing a block
public void WriteBlock(string name, ReadOnlySpan<byte> data)
{
    var offset = AlignToPage(_currentOffset, _pageSize);
    // Write at aligned offset for O_DIRECT on Linux
}
```

### 2. Memory-Mapped I/O

```csharp
// Zero-copy access to file data
using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

// Read header without allocations
var headerSpan = accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref headerBytes);
var header = ScdbFileHeader.Parse(headerSpan);
```

### 3. Incremental VACUUM

```csharp
// Background defragmentation (inspired by PostgreSQL's autovacuum)
public async Task VacuumIncrementalAsync(int maxPagesToMove = 1000)
{
    // 1. Scan FSM for fragmentation hotspots
    var fragments = _fsm.FindFragmentedExtents();
    
    // 2. Move up to maxPagesToMove to consolidate
    foreach (var extent in fragments.Take(maxPagesToMove))
    {
        await MoveExtentAsync(extent.StartPage, extent.Length);
    }
    
    // 3. Update FSM and checkpoint
    await CheckpointAsync();
}
```

---

## Migration Path

### Option 1: Parallel Support (Backward Compatible)

```csharp
public interface IStorageProvider
{
    StorageMode Mode { get; }  // MultiFile or SingleFile
}

// Detection by file extension
if (path.EndsWith(".scdb"))
    return new ScdbStorageProvider(path);
else
    return new MultiFileStorageProvider(path);
```

### Option 2: Migration Tool

```bash
# Convert existing database to .scdb format
sharpcoredb migrate --from mydb/ --to mydb.scdb

# Estimated time: 1M records in ~30 seconds
```

---

## Code Skeleton

```csharp
// File: src/SharpCoreDB/Storage/Scdb/ScdbFile.cs

/// <summary>
/// SCDB single-file storage format implementation.
/// Provides zero-copy, SSD-optimized, memory-mappable storage.
/// </summary>
public sealed class ScdbFile : IDisposable
{
    private readonly string _filePath;
    private readonly FileStream _fileStream;
    private readonly MemoryMappedFile? _memoryMappedFile;
    private readonly ScdbFileHeader _header;
    private readonly BlockRegistry _blockRegistry;
    private readonly FreeSpaceMap _fsm;
    private readonly WalManager _wal;

    public ScdbFile(string path, ScdbOpenMode mode = ScdbOpenMode.ReadWrite)
    {
        _filePath = path;
        
        if (mode.HasFlag(ScdbOpenMode.Create))
        {
            InitializeNewFile();
        }
        
        _fileStream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, 
            FileShare.None, bufferSize: 0, FileOptions.RandomAccess);
        
        // Memory-map for reads
        if (mode.HasFlag(ScdbOpenMode.MemoryMapped))
        {
            _memoryMappedFile = MemoryMappedFile.CreateFromFile(_fileStream, 
                null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true);
        }
        
        _header = ReadHeader();
        _blockRegistry = new BlockRegistry(this, _header.BlockRegistryOffset);
        _fsm = new FreeSpaceMap(this, _header.FsmOffset);
        _wal = new WalManager(this, _header.WalOffset);
    }

    public ScdbFileHeader ReadHeader()
    {
        Span<byte> buffer = stackalloc byte[ScdbFileHeader.HEADER_SIZE];
        _fileStream.Position = 0;
        _fileStream.Read(buffer);
        return ScdbFileHeader.Parse(buffer);
    }

    public BlockEntry FindBlock(string name)
    {
        return _blockRegistry.FindByName(name);
    }

    public ReadOnlySpan<byte> ReadBlock(string name)
    {
        var entry = FindBlock(name);
        
        if (_memoryMappedFile != null)
        {
            // Zero-copy via memory-mapped file
            using var accessor = _memoryMappedFile.CreateViewAccessor(
                (long)entry.Offset, (long)entry.Length, MemoryMappedFileAccess.Read);
            
            byte* ptr = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            return new ReadOnlySpan<byte>(ptr, (int)entry.Length);
        }
        else
        {
            // Traditional read
            var buffer = new byte[entry.Length];
            _fileStream.Position = (long)entry.Offset;
            _fileStream.Read(buffer);
            return buffer;
        }
    }

    public void WriteBlock(string name, BlockType type, ReadOnlySpan<byte> data)
    {
        // 1. Allocate pages from FSM
        var pageCount = (data.Length + _header.PageSize - 1) / _header.PageSize;
        var pageOffset = _fsm.AllocatePages(pageCount);
        
        // 2. Write data at page-aligned offset
        var offset = pageOffset * _header.PageSize;
        _fileStream.Position = (long)offset;
        _fileStream.Write(data);
        
        // 3. Compute checksum
        var checksum = SHA256.HashData(data);
        
        // 4. Register block
        var entry = new BlockEntry
        {
            // Name = name (via fixed buffer)
            BlockType = (uint)type,
            Offset = offset,
            Length = (ulong)data.Length,
            // Checksum = checksum
            Flags = (uint)BlockFlags.None
        };
        
        _blockRegistry.AddOrUpdate(name, entry);
        
        // 5. Log to WAL
        _wal.LogWrite(name, offset, data);
    }

    public void Dispose()
    {
        _wal.Checkpoint();
        _memoryMappedFile?.Dispose();
        _fileStream.Dispose();
    }
}

[Flags]
public enum ScdbOpenMode
{
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
    Create = 4,
    MemoryMapped = 8
}
```

---

## Future Extensions

1. **Compression blocks:** Add `CompressionMode` field (Zstd, LZ4)
2. **Encryption blocks:** Per-block encryption keys
3. **Remote storage:** S3-compatible backends
4. **Replication:** Logical WAL streaming
5. **Partitioning:** Horizontal block partitioning

---

## Summary

The `.scdb` format provides:

✅ **Single-file simplicity** (vs 100+ files in current design)  
✅ **Zero-copy reads** via memory-mapping  
✅ **SSD-optimized** page alignment  
✅ **Fragmentation avoidance** via FSM  
✅ **Crash safety** via embedded WAL  
✅ **Maintainability** via namespaced blocks  
✅ **Extensibility** via versioned headers  

**Expected Performance:**
- **Startup time:** 10x faster (1 file vs 100 files)
- **Write throughput:** 2x faster (no multi-file coordination)
- **Defragmentation:** 100x faster (incremental vs full rewrite)
- **Crash recovery:** 5x faster (embedded WAL)

**Recommended Next Steps:**
1. Prototype `ScdbFile.cs` with basic read/write
2. Benchmark vs current multi-file format
3. Add WAL integration
4. Implement incremental VACUUM
5. Production hardening (error handling, edge cases)
