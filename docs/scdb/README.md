# SCDB Single-File Storage Format

## Overview

The `.scdb` format is a **single-file, uncompressed, page-aligned storage format** for SharpCoreDB designed for maximum performance on modern SSDs. It consolidates the current multi-file architecture into a single random-access file optimized for zero-copy I/O and memory-mapping.

## 📁 Files

- **[SCDB_FILE_FORMAT_DESIGN.md](./FILE_FORMAT_DESIGN.md)** - Complete design specification
- **[ScdbStructures.cs](../../src/SharpCoreDB/Storage/Scdb/ScdbStructures.cs)** - Binary format definitions (C# 14 structs)
- **[ScdbFile.cs](../../src/SharpCoreDB/Storage/Scdb/ScdbFile.cs)** - Implementation skeleton

## 🎯 Design Goals

1. ✅ **Zero Compression Overhead** - All data stored in native binary format
2. ✅ **SSD-Optimized** - 4KB-aligned pages for direct I/O
3. ✅ **Memory-Mappable** - Fixed offsets, no varints, direct struct access
4. ✅ **Fragmentation Avoidance** - Built-in Free Space Map (FSM)
5. ✅ **Extensible** - Reserved fields, versioned headers
6. ✅ **Crash-Safe** - Per-block checksums, embedded WAL
7. ✅ **Maintainable** - Self-describing format with block registry

## 📊 Performance Expectations

| Metric | Current (multi-file) | SCDB (single-file) | Improvement |
|--------|---------------------|-------------------|-------------|
| **Startup Time** | 100ms (100 files) | 10ms (1 file) | 10x faster |
| **Write Throughput** | 50k ops/sec | 100k ops/sec | 2x faster |
| **Defragmentation** | 60s full rewrite | 600ms incremental | 100x faster |
| **Crash Recovery** | 500ms | 100ms | 5x faster |
| **File Handles** | 100+ | 1 | 100x fewer |

## 🏗️ File Structure

```
┌─────────────────────────────────────────────────────────┐
│                   SCDB FILE LAYOUT                      │
├─────────────────────────────────────────────────────────┤
│ [0x0000] File Header (512 bytes)                        │
│   - Magic: "SCDB\x10\x00\x00\x00"                      │
│   - Page size: 4096 (default, tunable)                  │
│   - Block registry offset                               │
│   - FSM offset (Free Space Map)                         │
│   - WAL offset (Write-Ahead Log)                        │
│   - Table directory offset                              │
├─────────────────────────────────────────────────────────┤
│ [0x0200] Block Registry (variable, page-aligned)        │
│   - Self-describing index of all blocks                 │
│   - Namespaced block names:                             │
│     * "table:users:data"                                │
│     * "table:users:index:pk"                            │
│     * "sys:fsm"                                         │
│     * "sys:wal"                                         │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Free Space Map (FSM)                          │
│   - Two-level bitmap (inspired by PostgreSQL)           │
│   - L1: 1 bit per page (allocated/free)                 │
│   - L2: Extent map for large allocations                │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Write-Ahead Log (WAL)                         │
│   - Circular buffer of 4KB entries                      │
│   - Transaction boundaries                              │
│   - Redo/undo logs for crash recovery                   │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Table Directory                               │
│   - Table metadata                                      │
│   - Schema definitions                                  │
│   - Index pointers                                      │
├─────────────────────────────────────────────────────────┤
│ [Aligned] Data Blocks (namespaced)                      │
│   - Reuses existing PageBased format (8KB pages)        │
│   - Reuses existing Columnar format (.dat)              │
│   - Per-block SHA-256 checksums                         │
└─────────────────────────────────────────────────────────┘
```

## 🔧 C# 14 Features Used

### 1. **Readonly Structs with Fixed Buffers**

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScdbFileHeader
{
    public readonly ulong Magic;
    public readonly ushort PageSize;
    public readonly unsafe fixed byte Nonce[12];
    public readonly unsafe fixed byte Reserved[240];
    
    // Zero-allocation parsing
    public static ScdbFileHeader Parse(ReadOnlySpan<byte> data)
    {
        return MemoryMarshal.Read<ScdbFileHeader>(data);
    }
}
```

### 2. **Field Keyword for Property Backing**

```csharp
// Before (C# 12):
private bool _disposed;
public bool IsDisposed => _disposed;

// After (C# 14):
public bool IsDisposed { get => field; } // Implicit backing field
```

### 3. **Primary Constructors**

```csharp
public sealed class ScdbFile(string path, FileStream fs) : IDisposable
{
    public string FilePath => path; // Direct parameter access
    
    public void Flush() => fs.Flush(flushToDisk: true);
}
```

### 4. **Pattern Matching with Switch Expressions**

```csharp
var fileAccess = mode switch
{
    var m when m.HasFlag(ScdbOpenMode.ReadWrite) => FileAccess.ReadWrite,
    var m when m.HasFlag(ScdbOpenMode.Write) => FileAccess.Write,
    _ => FileAccess.Read
};
```

### 5. **ReadOnlySpan<byte> for Zero-Copy Parsing**

```csharp
public unsafe ReadOnlySpan<byte> ReadBlock(string name)
{
    // Memory-mapped I/O: zero allocations
    using var accessor = _mmf.CreateViewAccessor(offset, length);
    byte* ptr = null;
    accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
    return new ReadOnlySpan<byte>(ptr, (int)length);
}
```

## 🚀 Usage Examples

### Opening a Database

```csharp
// Create new .scdb file
using var db = ScdbFile.Open("mydb.scdb", ScdbOpenMode.Create | ScdbOpenMode.ReadWrite);

// Open existing with memory-mapping (zero-copy reads)
using var db = ScdbFile.Open("mydb.scdb", ScdbOpenMode.ReadWrite | ScdbOpenMode.MemoryMapped);
```

### Writing Data

```csharp
var tableData = SerializeTable(userTable);
db.WriteBlock("table:users:data", BlockType.TableData, tableData);

var indexData = SerializeIndex(primaryKeyIndex);
db.WriteBlock("table:users:index:pk", BlockType.IndexData, indexData);

db.Flush(); // Commit to disk
```

### Reading Data

```csharp
// Zero-copy read via memory mapping
ReadOnlySpan<byte> data = db.ReadBlock("table:users:data");

// Deserialize directly from span
var table = DeserializeTable(data);
```

### Incremental VACUUM

```csharp
// Background defragmentation (non-blocking)
await db.VacuumIncrementalAsync(maxPagesToMove: 1000);

// Full VACUUM (creates new file)
await db.VacuumFullAsync();
```

## 📈 Comparison with Other Formats

### SQLite

```
✅ Single file
✅ Page-aligned
❌ No embedded WAL (separate file)
❌ No FSM (fragmentation issues)
❌ VACUUM requires full rewrite
```

### LiteDB

```
✅ Single file
❌ No page alignment (bad for SSDs)
❌ BSON parsing overhead
❌ No FSM
❌ No embedded WAL
```

### SCDB

```
✅ Single file
✅ Page-aligned (SSD-optimized)
✅ Embedded WAL (atomic commits)
✅ FSM (incremental VACUUM)
✅ Zero-copy reads (memory-mapped)
✅ Per-block checksums
✅ Namespaced blocks
```

## 🔐 Security Features

### 1. **Per-Block Checksums**

```csharp
// SHA-256 checksum stored in block entry
public readonly unsafe fixed byte Checksum[32];

// Validation on read
if (!blockEntry.ValidateChecksum(blockData))
{
    throw new InvalidDataException("Block checksum mismatch");
}
```

### 2. **Optional Encryption**

```csharp
// AES-256-GCM per-block encryption
var header = ScdbFileHeader.CreateDefault() with
{
    EncryptionMode = 1, // AES-256-GCM
    EncryptionKeyId = keyId
};
```

### 3. **Transaction Integrity**

```csharp
// WAL ensures crash safety
db.BeginTransaction();
db.WriteBlock("table:orders:data", ...);
db.CommitTransaction(); // Atomic via WAL checkpoint
```

## 🧪 Testing Strategy

### 1. **Unit Tests**

```csharp
[Fact]
public void FileHeader_Parse_ValidatesCorrectly()
{
    var header = ScdbFileHeader.CreateDefault();
    Span<byte> buffer = stackalloc byte[512];
    header.WriteTo(buffer);
    
    var parsed = ScdbFileHeader.Parse(buffer);
    
    Assert.True(parsed.IsValid);
    Assert.Equal(ScdbFileHeader.MAGIC, parsed.Magic);
}
```

### 2. **Integration Tests**

```csharp
[Fact]
public void ScdbFile_WriteAndRead_RoundTrip()
{
    using var db = ScdbFile.Open("test.scdb", ScdbOpenMode.Create);
    
    var testData = Encoding.UTF8.GetBytes("Hello, SCDB!");
    db.WriteBlock("test:data", BlockType.TableData, testData);
    
    var readData = db.ReadBlock("test:data");
    
    Assert.True(readData.SequenceEqual(testData));
}
```

### 3. **Crash Recovery Tests**

```csharp
[Fact]
public void ScdbFile_CrashRecovery_RestoresState()
{
    // Simulate crash during write
    using (var db = ScdbFile.Open("crash.scdb", ScdbOpenMode.Create))
    {
        db.BeginTransaction();
        db.WriteBlock("test:data", ...);
        // Simulate crash (no commit)
    }
    
    // Reopen and verify WAL recovery
    using var db2 = ScdbFile.Open("crash.scdb");
    var data = db2.ReadBlock("test:data");
    
    Assert.True(data.IsEmpty); // Transaction was rolled back
}
```

## 🛣️ Migration Path

### Option 1: Parallel Support (Recommended)

```csharp
public interface IStorageProvider
{
    StorageMode Mode { get; } // MultiFile or SingleFile
}

// Auto-detect by file extension
IStorageProvider storage = path.EndsWith(".scdb")
    ? new ScdbStorageProvider(path)
    : new MultiFileStorageProvider(path);
```

### Option 2: Migration Tool

```bash
# Convert existing database to .scdb
sharpcoredb migrate --from mydb/ --to mydb.scdb

# Progress: 10,000 records in ~30 seconds
# [█████████████████████████████] 100% (10,000 / 10,000)
```

### Option 3: Hybrid Mode

```csharp
// Use .scdb for new tables, keep legacy format for old tables
var config = new DatabaseConfig
{
    DefaultStorageFormat = StorageFormat.Scdb,
    AllowLegacyFormats = true
};
```

## 📚 Implementation Roadmap

### Phase 1: Core Format (Week 1-2)

- [x] Define binary structures (`ScdbStructures.cs`)
- [x] Implement file header read/write
- [ ] Implement block registry
- [ ] Basic read/write operations

### Phase 2: FSM & Allocation (Week 3-4)

- [ ] Free Space Map implementation
- [ ] Page allocation/deallocation
- [ ] Extent tracking for large allocations

### Phase 3: WAL & Recovery (Week 5-6)

- [ ] WAL entry structure
- [ ] Transaction logging
- [ ] Crash recovery mechanism
- [ ] Checkpoint implementation

### Phase 4: Integration (Week 7-8)

- [ ] Integrate with existing `PageBasedEngine`
- [ ] Integrate with existing `ColumnarEngine`
- [ ] Migration tool (multi-file → .scdb)
- [ ] Performance benchmarks

### Phase 5: Production Hardening (Week 9-10)

- [ ] Comprehensive error handling
- [ ] Edge case testing
- [ ] Corruption detection & repair
- [ ] Documentation & examples

## 🤝 Contributing

When implementing features, follow these guidelines:

1. **Zero-allocation hot paths**: Use `Span<byte>` and `stackalloc`
2. **Page alignment**: All offsets must be multiples of page size
3. **Checksums**: Always validate checksums on read
4. **WAL logging**: All writes must be logged to WAL first
5. **Thread safety**: Use locks for file I/O, lock-free for caching

## 📖 References

### Design Inspirations

- **PostgreSQL FSM**: Two-level bitmap for efficient page allocation
- **SQLite B-tree**: Page-aligned, fixed-size pages
- **LiteDB Collections**: BSON-style document storage
- **RocksDB WAL**: Write-ahead logging for crash recovery

### Related Reading

- [SQLite File Format](https://www.sqlite.org/fileformat.html)
- [PostgreSQL FSM](https://www.postgresql.org/docs/current/storage-fsm.html)
- [LiteDB Storage](https://github.com/mbdavid/LiteDB/wiki/Storage)
- [RocksDB WAL](https://github.com/facebook/rocksdb/wiki/Write-Ahead-Log)

## 📝 License

MIT License - See LICENSE file for details.

---

**Status:** ✅ Design Complete | 🚧 Implementation In Progress

**Next Steps:**
1. Review design document
2. Implement block registry
3. Add unit tests for struct serialization
4. Benchmark header read/write performance
