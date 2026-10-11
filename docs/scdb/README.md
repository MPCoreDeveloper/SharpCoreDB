# SCDB Single-File Storage Format

## Overview

The `.scdb` format is SharpCoreDB's **single-file, page-aligned storage container** and the default
storage mode of the 2.1 line: one randomly-addressable file holding the block registry, an embedded
write-ahead log, the table directory and the data blocks.

- **Encryption** — optional AES-256-GCM covering the whole file (block data, registry, free-space map
  and WAL). New encrypted files use `EncryptionMode = 2`; see [`ENCRYPTION.md`](./ENCRYPTION.md).
- **Compression** — the container header is uncompressed (`ScdbFileHeader.CompressionMode` is 0 for
  `.scdb`); columnar blocks written through the columnar adapter are Brotli-compressed by default.
- **Integrity** — a SHA-256 checksum of the file in the header, plus a SHA-256 checksum per block
  registry entry.

Pass a `.scdb` path to select single-file mode automatically; a directory path selects the classic
multi-file layout (`DirectoryStorageProvider`). Both are first-class in the 2.1 line.

## 📁 Files

- **[PRODUCTION_GUIDE.md](./PRODUCTION_GUIDE.md)** - configuration, backup, disaster recovery and tuning.
- **[ENCRYPTION.md](./ENCRYPTION.md)** - full at-rest encryption, key model, password/key rotation.
- **[../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md](../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md)** - record/column layout and free-space behaviour.
- **[ScdbStructures.cs](../../src/SharpCoreDB/Storage/Scdb/ScdbStructures.cs)** - the on-disk binary layout (`ScdbFileHeader`, `RegistryChunkHeader`, `BlockEntry`, `WalHeader`, `FreeSpaceMapHeader`).
- **[SingleFileStorageProvider.cs](../../src/SharpCoreDB/Storage/SingleFileStorageProvider.cs)** - the engine that owns the block registry, free-space map and WAL.

> ⚠️ **`Storage/Scdb/ScdbFile.cs` is a low-level prototype, not the production path.** Its
> `ReadBlock` returns an empty span and its `WriteBlock` registers neither the block nor a WAL entry;
> nothing in the product or the test suite uses it. Production single-file access goes through
> `SingleFileStorageProvider` / `DatabaseOptions.CreateSingleFileDefault()` - see the Usage Examples
> section below.

## 🎯 Design Goals

1. **Page-aligned** - 4 KiB pages by default (`DatabaseOptions.PageSize`, `ScdbFileHeader.PageSize`).
2. **Memory-mappable** - fixed offsets, no varints, struct-parsed headers (`DatabaseOptions.EnableMemoryMapping`).
3. **Fragmentation-aware** - a free-space map tracks page allocation, so `VACUUM` runs incrementally instead of rewriting the file.
4. **Extensible** - versioned headers, reserved fields, feature flags.
5. **Crash-safe** - embedded WAL plus per-block checksums; recovery replays from the last checkpoint.
6. **Self-describing** - a namespaced block registry (`table:users:data`, `table:users:index:pk`) maps names to offsets.

## 📊 Performance

The format is what makes the measured numbers possible, and the measured numbers live in one place:
[`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md). The current v2.1.0 publication reports, on the
fair-`INTEGER PRIMARY KEY` arm and against SQLite in the same run:

| Operation | Ratio (SharpCoreDB ÷ SQLite) | Spread across reps |
|-----------|------------------------------|--------------------|
| INSERT | **1,17×** (encrypted default posture) | 1,03–1,27 |
| UPDATE | **0,53×** | 0,42–0,53 |

Quote the ratios, never lone absolutes: absolute ops/sec move between runs on the same machine, and
SQLite's own reference drifts. The raw per-rep JSON behind the publication is in
`tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/`.

## 🏗️ File Structure

```
[0x0000]   File header - ScdbFileHeader, 512 bytes, fixed size
           magic "SCDB\x10\x00\x00\x00" + format version (v2), page size (4096 default),
           encryption mode + key id + nonce, file SHA-256 checksum, feature flags,
           transaction state (last txn id, last checkpoint LSN), allocated pages,
           RegistryRootOffset/Length, WalOffset/Length, TableDirOffset/Length
[Registry] Block registry - growable chain of page-aligned chunks (format v2, issue #345)
           [RegistryChunkHeader (64 B, magic "BRGC")][BlockEntry * EntryCount], each chunk
           linking to the next via NextChunkOffset/NextChunkLength
           BlockEntry (96 B): Name[32] "namespace:identifier:subtype", BlockType, Offset,
           Length, Checksum[32] (SHA-256 of the block data), Flags
[WAL]      Embedded write-ahead log - WalHeader plus fixed-size entries with head/tail offsets;
           checkpointed in place, so there is no sidecar .wal file to back up
[TableDir] Table directory (sys:tabledir) - table metadata, schemas, index pointers
[Blocks]   Page-aligned data blocks - table data, indexes, columnar payloads (Brotli-compressed),
           overflow chains; every block is page-aligned and carries its own SHA-256 checksum
```

The metadata regions are **named blocks resolved through the registry**, not fixed offsets: the
free-space map (`sys:fsm`), the WAL (`sys:wal`), the table directory (`sys:tabledir`) and the
metadata payload (`sys:metadata`). Format v1 used fixed `FsmOffset`/`FsmLength` header fields; those
two slots are now `ReservedRegion0`/`ReservedRegion1`, which is why the free-space map can grow with
the file (see `SingleFileStorageProvider`'s dynamic-metadata path).

## 🔧 C# 15 Features Used

> The snippets below illustrate the language features the implementation leans on; the authoritative
> declarations live in [`ScdbStructures.cs`](../../src/SharpCoreDB/Storage/Scdb/ScdbStructures.cs),
> [`ScdbFile.cs`](../../src/SharpCoreDB/Storage/Scdb/ScdbFile.cs) and
> [`SingleFileStorageProvider.cs`](../../src/SharpCoreDB/Storage/SingleFileStorageProvider.cs).

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

// After (C# 15):
public bool IsDisposed { get => field; } // Implicit backing field
```

### 3. **Primary Constructors**

```csharp
// Real example: src/SharpCoreDB/Storage/Columnar/ColumnCodec.cs
public sealed class ColumnCodec(ColumnFormat format)
{
    private readonly ColumnFormat _format = format ?? throw new ArgumentNullException(nameof(format));

    public byte[] EncodeColumn(string columnName, object?[] values) { /* ... */ }
}
```

### 4. **Pattern Matching with Switch Expressions**

```csharp
// Real example: src/SharpCoreDB/Storage/Overflow/StorageStrategy.cs
// (this StorageMode is the overflow strategy enum, not DatabaseOptions.StorageMode)
public static string GetDescription(StorageMode mode) => mode switch
{
    StorageMode.Inline => "Stored inline in data page (fastest)",
    StorageMode.Overflow => "Stored in overflow page chain (medium)",
    StorageMode.FileStream => "Stored in external file (unlimited size)",
    _ => "Unknown storage mode",
};
```

### 5. **ReadOnlySpan<byte> for Zero-Copy Parsing**

```csharp
// Real example: src/SharpCoreDB/Storage/Scdb/ScdbStructures.cs - the header is reinterpreted in
// place: no allocation, no copy
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static unsafe ScdbFileHeader Parse(ReadOnlySpan<byte> data)
{
    if (data.Length < HEADER_SIZE)
        throw new InvalidDataException($"Header too small: {data.Length} < {HEADER_SIZE}");

    fixed (byte* ptr = data)
    {
        return *(ScdbFileHeader*)ptr;
    }
}
```

## 🚀 Usage Examples

### Opening a database

```csharp
// A .scdb path selects single-file mode automatically (DatabaseExtensions.DetectStorageMode),
// so the ordinary construction path is all you need:
using var db = new Database(services, "./data/myapp.scdb", "your-master-password");

db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
```

Tuned defaults come from `DatabaseOptions.CreateSingleFileDefault()`. When you need block-level access,
drive the provider directly - it is what owns the registry, the free-space map and the WAL:

```csharp
var options = DatabaseOptions.CreateSingleFileDefault();          // page size, WAL pages, FSM pages
using var provider = SingleFileStorageProvider.Open("./data/myapp.scdb", options);

await provider.WriteBlockAsync("table:users:data", payload);      // registered and WAL-logged
var block = await provider.ReadBlockAsync("table:users:data");
await provider.CheckpointAsync();                                 // flush the WAL into the data blocks
```

### Block names and types

Blocks are namespaced strings; the type travels in the registry entry (`BlockType`):

| Block name | `BlockType` | Owner |
|------------|-------------|-------|
| `sys:fsm` | `FreeSpaceMap` | `FreeSpaceManager` |
| `sys:wal` | `WriteAheadLog` | `WalManager` |
| `sys:tabledir` | `TableDirectory` | `TableDirectoryManager` |
| `table:<name>:data` | `TableData` | table writer |
| `table:<name>:index:<column>` | `IndexData` | index writer |
| `blob:*` / `temp:*` | `BlobData` / `TemporaryData` | overflow and temporary writers |

### Maintenance

```csharp
await db.VacuumAsync(VacuumMode.Quick);        // reclaim obvious holes (the default)
await db.VacuumAsync(VacuumMode.Incremental);  // page-by-page defragmentation
await db.VacuumAsync(VacuumMode.Full);         // rewrite the file
```

The `VACUUM` statement is parsed as well, so `db.ExecuteSQL("VACUUM")` works. The synchronous
`ScdbFile.VacuumIncremental(int maxPagesToMove = 1000)` / `ScdbFile.VacuumFull()` pair belongs to the
prototype described at the top of this page, not to the engine.

## 📈 How it compares

- **vs SQLite** - both are single-file, page-aligned engines. SQLite keeps its WAL in a sidecar
  `-wal` file and its `VACUUM` is a full rewrite; `.scdb` keeps the WAL inside the database file and
  reclaims space incrementally. On the fair-PK arm SQLite still leads on `UPDATE`/`DELETE`
  throughput - the measured ratios and their caveats are in
  [`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md).
- **vs LiteDB** - LiteDB is a document store (BSON with a query API); `.scdb` is a relational engine
  with a SQL surface, so the difference is mostly the data model rather than the storage layout.
- **vs JSON-over-files** - the format is binary and page-aligned, so there is no parse cost per read
  and no per-table file handle.

This page deliberately limits competitor claims to what is measured or structurally verifiable; the
numbers and the method live in [`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md).

## 🔐 Security Features

### 1. **Checksums**

```csharp
// Header: SHA-256 of the whole file, excluding this field
public unsafe fixed byte FileChecksum[32];   // ScdbFileHeader

// Registry entry: SHA-256 of the block data
public unsafe fixed byte Checksum[32];       // BlockEntry
```

`CorruptionDetector` validates at four levels - `Quick` (header only), `Standard` (header, blocks and
checksums), `Deep` (adds the WAL) and `Paranoid` (re-reads and verifies everything) - and `RepairTool`
acts on the resulting `CorruptionReport`, optionally taking a backup first.

### 2. **Optional Encryption (full at-rest)**

> ⚠️ **Format note:** `EncryptionMode = 1` (AES-256-GCM over block data only, issue #341) is
> still readable for backward compatibility. **New encrypted files are created with
> `EncryptionMode = 2`**, which additionally encrypts the block registry, free-space map and WAL
> so no metadata (block/table names, offsets, lengths, allocation patterns) leaks in plaintext.

```csharp
// AES-256-GCM full at-rest encryption (new files)
var options = DatabaseOptions.CreateSingleFileDefault(enableEncryption: true, encryptionKey: key);
// or envelope-encryption (password mode):
options.EncryptionPassword = "correct horse battery staple";
```

See **[`ENCRYPTION.md`](./ENCRYPTION.md)** for the complete feature documentation — key model,
password & key rotation, crash-safety, AOT/Sonar compliance notes and a full worked example.

### 3. **Transaction Integrity**

```csharp
// The provider owns the WAL: a checkpoint is the durability boundary, and recovery replays the
// entries written since the last checkpoint.
await provider.CheckpointAsync();

// After a crash, recovery runs through RecoveryManager (WAL replay from the last checkpoint LSN).
```

## 🧪 Testing Strategy

The format is covered by the suites that must stay green on every storage change:

| Suite | What it pins |
|-------|--------------|
| `ReopenRoundTripMatrixTests` | close/reopen round-trips across the column shapes and index kinds |
| `SingleFileDirectoryParityTests` | `.scdb` results equal directory-mode results for the same workload |
| `CrashRecoveryTests` | WAL replay and recovery after a simulated crash |
| `FixedWidthPatchTests`, `FixedWidthBulkUpdateTests`, `FixedWidthBulkDeleteTests` | in-place delta/patch paths |
| `FormatCompatPolicyTests` | format acceptance (v1 readable, v2 written) |

Run them with the Microsoft.Testing.Platform executable (`dotnet test` does not work in this
repository - see `AGENTS.md`):

```powershell
tests/SharpCoreDB.Tests/bin/Release/net11.0/SharpCoreDB.Tests.exe -filterVSTest 'FullyQualifiedName~ReopenRoundTripMatrixTests'
```

## 🛣️ Migration Path

### Option 1: Parallel support (recommended)

Both layouts implement one interface, `IStorageProvider` (`SingleFileStorageProvider`,
`DirectoryStorageProvider`), and `Database` picks one from the path it is given
(`DatabaseExtensions.DetectStorageMode`: a `.scdb` file - or any existing file - means single-file).
`StorageFormatSwitcher` covers the case where the active provider has to change at runtime.

```csharp
// Nothing to select by hand in the normal case:
//   "./data/mydb.scdb" -> SingleFileStorageProvider
//   "./data/mydb"      -> DirectoryStorageProvider
using var db = new Database(services, "./data/mydb.scdb", "your-master-password");
```

### Option 2: Migration API

```csharp
// directory -> single file
var toSingleFile = await DatabaseMigrator.MigrateToSingleFileAsync(
    sourceDirectoryPath: "./data/mydb",
    targetScdbPath: "./data/mydb.scdb",
    password: "your-master-password",
    progress: new Progress<double>(p => Console.WriteLine($"{p:P0}")));

// single file -> directory
var toDirectory = await DatabaseMigrator.MigrateToDirectoryAsync(
    sourceScdbPath: "./data/mydb.scdb",
    targetDirectoryPath: "./data/mydb-restored",
    password: "your-master-password");
```

The same two operations exist on a live database (`db.MigrateToSingleFileAsync`,
`db.MigrateToDirectoryAsync`) and on `ScdbMigrator`. There is **no `sharpcoredb` CLI** in this
repository - the migration entry points are the APIs above.

### Option 3: Hybrid deployments

Storage mode is a per-database choice, not a per-table one, so a hybrid deployment keeps a directory
database and a `.scdb` database side by side and moves a table across when it is ready.

## 📚 Implementation Status

The format this page describes is implemented and shipped; the phase checklist this section used to
carry was a design-time plan for the `ScdbFile` prototype, and the engine was built along a different
path. The mapping from that plan to shipped code:

| Planned item | Where it ships |
|--------------|----------------|
| Binary structures, file header read/write | `ScdbStructures.cs` (`ScdbFileHeader` 512 B, `RegistryChunkHeader` 64 B, `BlockEntry` 96 B, `WalHeader`, `FreeSpaceMapHeader`) |
| Block registry | `Storage/BlockRegistry.cs` - growable chunk chain rooted at `ScdbFileHeader.RegistryRootOffset` (format v2, issue #345) |
| Basic read/write operations | `SingleFileStorageProvider`, `SingleFileDatabase`, `SingleFileTable` |
| Free Space Map, page allocation, extent tracking | `Storage/FreeSpaceManager.cs` with `Storage/Scdb/ExtentAllocator.cs` (`DatabaseOptions.FsmSizePages`, `FsmStatistics`) |
| WAL entries, transaction logging, crash recovery, checkpoint | `Storage/WalManager.cs`, `Storage/Scdb/RecoveryManager.cs`, `SingleFileStorageProvider.CheckpointAsync` |
| Integrate with the page-based and columnar engines | `Storage/Scdb/PageBasedAdapter.cs`, `Storage/Scdb/ColumnarAdapter.cs` |
| Migration tool (multi-file → `.scdb`) | `Migration/DatabaseMigrator.cs`, `Storage/Scdb/ScdbMigrator.cs` |
| Error handling, corruption detection and repair | `Storage/Scdb/ScdbExceptions.cs`, `CorruptionDetector.cs`, `RepairTool.cs` |
| Edge-case testing, benchmarks | the canary suites listed under Testing Strategy, plus `tests/benchmarks/` |

Open work is tracked in [`../../ROADMAP.md`](../../ROADMAP.md) and
[`../PROJECT_STATUS.md`](../PROJECT_STATUS.md): this page documents the format, it is not a task list.

**The one genuinely unfinished item** is the public `ScdbFile` prototype - its registry lookup,
free-space allocation, block registration and WAL logging are `NOTE:`-marked stubs, and nothing in the
product or the test suite uses it. It is listed in `ROADMAP.md` under **In Progress** so that it does
not disappear again.

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

**Status:** shipped and maintained. New databases in the 2.1 line store as `.scdb`; the format is read
by every package in the family, and `FormatCompatPolicyTests` keeps v1 files readable while new files
are written as format v2.

**See also:**
1. [`PRODUCTION_GUIDE.md`](./PRODUCTION_GUIDE.md) - configuration, backup, recovery, tuning
2. [`ENCRYPTION.md`](./ENCRYPTION.md) - encryption model and key rotation
3. [`../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md`](../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md) - record and column layout
4. [`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md) - measured ratios and method
