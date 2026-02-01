# SCDB Phase 4: Integration Design

**Version:** 1.0  
**Date:** 2026-01-28  
**Status:** 📐 Design Complete

---

## 🎯 Phase 4 Goals

1. **PageBasedAdapter**: Integrate PageBasedEngine with SCDB SingleFileStorageProvider
2. **ColumnarAdapter**: Integrate ColumnStore with SCDB storage
3. **ScdbMigrator**: Migrate Directory-based databases to SCDB format
4. **Cross-Format Compatibility**: Seamless switching between storage formats

---

## 📐 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         Database                                 │
├─────────────────────────────────────────────────────────────────┤
│                    IStorageProvider                              │
├──────────────┬──────────────┬─────────────────┬─────────────────┤
│ Directory    │ SingleFile   │ PageBased       │ Columnar        │
│ Storage      │ Storage      │ Adapter         │ Adapter         │
│ Provider     │ Provider     │ (NEW)           │ (NEW)           │
│ (legacy)     │ (SCDB)       │                 │                 │
└──────────────┴──────────────┴─────────────────┴─────────────────┘
                      │
                      ▼
              ┌───────────────┐
              │ ScdbMigrator  │
              │ (NEW)         │
              └───────────────┘
              │               │
              ▼               ▼
        Directory       SingleFile
        Format          Format (SCDB)
```

---

## 📦 Component 1: PageBasedAdapter

**Purpose**: Wrap PageBasedEngine to write through SingleFileStorageProvider

### Class Design

```csharp
/// <summary>
/// Adapter that integrates PageBasedEngine with SCDB SingleFileStorageProvider.
/// Routes page operations through the unified storage layer.
/// C# 14: Uses primary constructor and Lock type.
/// </summary>
public sealed class PageBasedAdapter : IStorageEngine, IDisposable
{
    // Storage provider for persistence
    private readonly SingleFileStorageProvider _storageProvider;
    
    // PageBasedEngine for page management logic
    private readonly PageBasedEngine _engine;
    
    // Block name prefix for SCDB storage
    private const string BLOCK_PREFIX = "page:";
    
    // Methods:
    // - Insert(tableName, data) → long
    // - InsertBatch(tableName, dataBlocks) → long[]
    // - Read(tableName, storageRef) → byte[]?
    // - Update(tableName, storageRef, data) → bool
    // - Delete(tableName, storageRef) → bool
    // - Flush() → persist all pages to SCDB
}
```

### Key Integration Points

1. **Page Storage**: Each page stored as block `page:{tableId}:{pageId}`
2. **Metadata**: Page metadata in `pagemeta:{tableId}`
3. **Free List**: Free page list in `pagefree:{tableId}`
4. **WAL Integration**: All writes go through SCDB WAL

### Block Naming Convention

```
page:{tableId}:{pageId}     → Page data (8KB)
pagemeta:{tableId}          → Page metadata (header, count)
pagefree:{tableId}          → Free page list
pageindex:{tableId}:{name}  → Index data
```

---

## 📦 Component 2: ColumnarAdapter

**Purpose**: Enable ColumnStore to persist to SCDB format

### Class Design

```csharp
/// <summary>
/// Adapter for columnar storage integration with SCDB.
/// Stores columns as separate blocks for efficient analytics.
/// C# 14: Uses modern async patterns.
/// </summary>
public sealed class ColumnarAdapter<T> : IDisposable where T : class
{
    // Underlying column store
    private readonly ColumnStore<T> _columnStore;
    
    // SCDB provider for persistence
    private readonly SingleFileStorageProvider _storageProvider;
    
    // Table name for block naming
    private readonly string _tableName;
    
    // Methods:
    // - TransposeAndPersist(rows) → persist columns to SCDB
    // - LoadColumns() → restore from SCDB
    // - Sum/Avg/Min/Max → delegate to ColumnStore
}
```

### Block Naming Convention

```
column:{tableName}:{columnName}    → Column data (compressed)
colmeta:{tableName}                → Column metadata
colstats:{tableName}:{columnName}  → Column statistics (min/max/nulls)
```

### Serialization Format

```
Column Block:
┌─────────────────────────────┐
│ Header (16 bytes)           │
│ - Magic: "COL1"             │
│ - Type: byte (int/long/etc) │
│ - RowCount: int             │
│ - Compressed: bool          │
├─────────────────────────────┤
│ Data (variable)             │
│ - Raw or Brotli compressed  │
└─────────────────────────────┘
```

---

## 📦 Component 3: ScdbMigrator

**Purpose**: Migrate databases from Directory format to SCDB SingleFile format

### Class Design

```csharp
/// <summary>
/// Migrates databases from Directory storage to SCDB SingleFile format.
/// Supports streaming migration for large databases.
/// C# 14: Async streaming with IAsyncEnumerable.
/// </summary>
public sealed class ScdbMigrator : IDisposable
{
    // Source directory provider
    private readonly DirectoryStorageProvider _source;
    
    // Target SCDB provider
    private readonly SingleFileStorageProvider _target;
    
    // Methods:
    // - MigrateAsync(options) → MigrationResult
    // - ValidateAsync() → ValidationResult
    // - RollbackAsync() → restore from backup
}
```

### Migration Process

```
1. Validate source database
   └── Check all blocks readable
   └── Verify checksums
   └── Estimate target size

2. Create backup (optional)
   └── Copy source directory

3. Initialize target SCDB file
   └── Create with estimated size
   └── Initialize headers

4. Stream migration
   └── For each block in source:
       └── Read block data
       └── Write to SCDB
       └── Update progress
       └── Verify written data

5. Finalize
   └── Flush SCDB
   └── Checkpoint WAL
   └── Validate target

6. Report results
   └── Blocks migrated
   └── Time taken
   └── Size comparison
```

### MigrationOptions

```csharp
public sealed record MigrationOptions
{
    public bool CreateBackup { get; init; } = true;
    public bool ValidateAfterMigration { get; init; } = true;
    public bool DeleteSourceAfterSuccess { get; init; } = false;
    public int BatchSize { get; init; } = 100;
    public IProgress<MigrationProgress>? Progress { get; init; }
}
```

### MigrationResult

```csharp
public sealed record MigrationResult
{
    public bool Success { get; init; }
    public int BlocksMigrated { get; init; }
    public long BytesMigrated { get; init; }
    public TimeSpan Duration { get; init; }
    public double CompressionRatio { get; init; }
    public string? ErrorMessage { get; init; }
}
```

---

## 📦 Component 4: StorageFormatSwitcher

**Purpose**: Enable runtime format switching (advanced feature)

### Use Cases

1. **Development**: Use Directory for debugging, SCDB for production
2. **Migration**: Hot-switch during rolling upgrade
3. **Testing**: Compare performance between formats

### Implementation

```csharp
/// <summary>
/// Enables runtime switching between storage formats.
/// Maintains data consistency during switch.
/// </summary>
public sealed class StorageFormatSwitcher
{
    private IStorageProvider _currentProvider;
    private readonly Lock _switchLock = new();
    
    // Methods:
    // - SwitchToScdb(scdbPath) → migrate and switch
    // - SwitchToDirectory(dirPath) → export and switch
    // - GetCurrentFormat() → StorageMode
}
```

---

## 🧪 Test Strategy

### Integration Tests

```csharp
// PageBasedAdapterTests.cs
[Fact] InsertAndRead_WithScdb_DataPersists()
[Fact] BatchInsert_WithScdb_AllRecordsStored()
[Fact] Update_WithScdb_ModifiesData()
[Fact] Delete_WithScdb_RemovesRecord()
[Fact] Flush_WithScdb_DataDurable()

// ColumnarAdapterTests.cs
[Fact] TransposeAndPersist_WithScdb_ColumnsStored()
[Fact] LoadColumns_FromScdb_RestoresData()
[Fact] SumAggregate_WithScdb_CorrectResult()

// ScdbMigratorTests.cs
[Fact] Migrate_SmallDatabase_Success()
[Fact] Migrate_LargeDatabase_StreamingWorks()
[Fact] Migrate_WithValidation_VerifiesData()
[Fact] Migrate_WithProgress_ReportsAccurately()
[Fact] Rollback_AfterFailure_RestoresSource()
```

### Performance Benchmarks

```csharp
// MigrationBenchmarks.cs
[Fact] Migrate_10MB_UnderOneSecond()
[Fact] Migrate_100MB_LinearScaling()
[Fact] Migrate_1GB_ReasonableTime()
```

---

## 📁 File Structure

```
src/SharpCoreDB/Storage/Scdb/
├── PageBasedAdapter.cs         (NEW - 300 LOC)
├── ColumnarAdapter.cs          (NEW - 250 LOC)
├── StorageFormatSwitcher.cs    (NEW - 150 LOC)
└── (existing files...)

tools/SharpCoreDB.Migration/
├── ScdbMigrator.cs             (NEW - 400 LOC)
├── MigrationOptions.cs         (NEW - 50 LOC)
├── MigrationResult.cs          (NEW - 50 LOC)
└── Program.cs                  (NEW - 100 LOC) - CLI tool

tests/SharpCoreDB.Tests/Storage/
├── PageBasedAdapterTests.cs    (NEW - 200 LOC)
├── ColumnarAdapterTests.cs     (NEW - 200 LOC)
├── ScdbMigratorTests.cs        (NEW - 300 LOC)
└── MigrationBenchmarks.cs      (NEW - 150 LOC)
```

**Total Estimated:** ~2,150 LOC

---

## ⚡ Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| PageBasedAdapter.Insert | <5ms | Via SCDB WAL |
| PageBasedAdapter.Read | <1ms | Memory-mapped |
| ColumnarAdapter.Persist | <10ms/1000 rows | Batch write |
| Migration (small) | <1s/10MB | Streaming |
| Migration (large) | <10s/100MB | Batched |
| Format Switch | <5s | With validation |

---

## 🔐 Safety Guarantees

1. **Atomic Migration**: All-or-nothing via transaction
2. **Backup First**: Optional but recommended
3. **Validation**: Verify data after migration
4. **Rollback**: Restore from backup on failure
5. **Progress**: Report progress for large migrations

---

## 📋 Implementation Order

1. **PageBasedAdapter** (~2 hours)
   - Core adapter implementation
   - Block naming convention
   - Basic tests

2. **ColumnarAdapter** (~2 hours)
   - Column serialization
   - Compression support
   - Analytics integration

3. **ScdbMigrator** (~3 hours)
   - Core migration logic
   - Progress reporting
   - Validation

4. **Tests & Benchmarks** (~2 hours)
   - Integration tests
   - Performance validation
   - Edge cases

5. **Documentation** (~1 hour)
   - Update status docs
   - Create PHASE4_COMPLETE.md

**Total Estimated:** ~10 hours (much faster than 2 weeks estimated!)

---

## ✅ Acceptance Criteria

- [ ] PageBasedAdapter stores pages in SCDB
- [ ] ColumnarAdapter persists columns to SCDB
- [ ] ScdbMigrator migrates Directory → SCDB
- [ ] Migration <1s per 10MB
- [ ] All tests passing
- [ ] Zero data loss during migration
- [ ] Documentation complete

---

**Ready for Implementation!** 🚀
