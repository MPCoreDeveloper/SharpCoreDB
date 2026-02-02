# SCDB Phase 6: Unlimited Row Storage with FILESTREAM

**Version:** 1.0  
**Date:** 2026-01-28  
**Status:** 📐 Design Complete

---

## 🎯 Phase 6 Goals

**Objective:** Support rows of ANY size with optimal storage strategy

**Key Features:**
- ✅ No arbitrary size limits (only filesystem limits)
- ✅ 3-tier storage: Inline → Overflow → FILESTREAM
- ✅ Auto-selection based on row size
- ✅ Orphan detection & cleanup
- ✅ Missing file recovery
- ✅ Configurable thresholds
- ✅ Background maintenance

---

## 📐 Architecture Overview

### **3-Tier Storage Strategy**

```
┌──────────────────────────────────────────────────────┐
│              Row Size Decision Tree                   │
├──────────────────────────────────────────────────────┤
│  0 - 4KB       │ INLINE      │ Store in data page   │
│  4KB - 256KB   │ OVERFLOW    │ Page chain           │
│  256KB+        │ FILESTREAM  │ External file        │
└──────────────────────────────────────────────────────┘
```

### **Why This Approach?**

| Size | Strategy | Read Time | Reason |
|------|----------|-----------|--------|
| <4KB | Inline | 0.1ms | Single page read |
| 4-256KB | Overflow | 1-25ms | Multiple pages acceptable |
| >256KB | FILESTREAM | 3-50ms | OS optimized, no page overhead |

---

## 🗂️ Component 1: FilePointer (FILESTREAM)

### **Structure**

```csharp
public sealed record FilePointer
{
    // Identification
    public Guid FileId { get; init; }
    public string RelativePath { get; init; }
    
    // Metadata
    public long FileSize { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastAccessedAt { get; init; }
    public string ContentType { get; init; }
    public byte[] Checksum { get; init; }  // SHA-256
    
    // Reference tracking (for orphan detection)
    public long RowId { get; init; }
    public string TableName { get; init; }
    public string ColumnName { get; init; }
}
```

### **File Layout**

```
database/
├── data.scdb              (Main database)
├── wal/                   (Write-Ahead Log)
└── blobs/                 (FILESTREAM directory)
    ├── 00/                (First 2 hex chars of GUID)
    │   ├── 01/            (Next 2 hex chars)
    │   │   ├── 0001a2b3...bin   (Data file)
    │   │   └── 0001a2b3...meta  (Metadata JSON)
```

**Why subdirectories?**
- Prevents "too many files" OS issue
- 256×256 = 65,536 buckets
- ~1000 files per bucket = 65M files supported

---

## 🔗 Component 2: OverflowPageManager

### **Overflow Chain Structure**

```
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│  Data Page  │      │  Overflow   │      │  Overflow   │
│             │      │   Page 1    │      │   Page 2    │
│ Row Header: │──┐   │             │      │             │
│  - RowID    │  │   │ Data Part 1 │──┐   │ Data Part 2 │
│  - Type:    │  │   │ (4064 bytes)│  │   │ (remaining) │
│    Overflow │  │   │ Next: P2 ──┼──┘   │ Next: NULL  │
└─────────────┘  │   └─────────────┘      └─────────────┘
                 └────────────────────────────────────────┘
```

### **Overflow Page Header**

```csharp
public struct OverflowPageHeader
{
    public uint Magic;           // 0x4F564552 ("OVER")
    public ushort Version;       // 1
    public ulong PageId;         // This page ID
    public ulong RowId;          // Parent row ID
    public uint SequenceNum;     // 0, 1, 2... (position in chain)
    public ulong NextPage;       // Next page ID (or 0)
    public uint DataLength;      // Bytes in this page
    public uint Checksum;        // CRC32 of data
}
```

---

## 🎯 Component 3: StorageStrategy

### **Auto-Selection Logic**

```csharp
public enum StorageMode
{
    Inline,        // 0-4KB
    Overflow,      // 4KB-256KB
    FileStream,    // 256KB+
}

public static class StorageStrategy
{
    public static StorageMode DetermineMode(
        int dataSize,
        StorageOptions options)
    {
        if (dataSize <= options.InlineThreshold)
            return StorageMode.Inline;
        
        if (dataSize <= options.OverflowThreshold)
            return StorageMode.Overflow;
        
        return StorageMode.FileStream;
    }
}
```

---

## 🔍 Component 4: OrphanDetector

### **Detection Scenarios**

**Scenario 1: Orphaned Files**
```
Database: Row deleted ❌
Filesystem: File exists ✅ → ORPHAN
```

**Scenario 2: Missing Files**
```
Database: FilePointer exists ✅
Filesystem: File missing ❌ → MISSING
```

### **Detection Algorithm**

```
1. Scan all files in blobs/ directory
2. Load all FilePointers from database
3. Compare:
   - Files without DB entry = Orphans
   - DB entries without files = Missing
4. Check retention period for orphans
5. Generate report
```

---

## 🧹 Component 5: OrphanCleaner

### **Cleanup Policies**

```csharp
public enum MissingFilePolicy
{
    AlertOnly,     // Log warning, no action
    SetNull,       // Set column to NULL
    DeleteRow,     // Delete entire row
}

public class CleanupOptions
{
    public bool DryRun { get; set; } = true;
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    public MissingFilePolicy MissingPolicy { get; set; } = MissingFilePolicy.AlertOnly;
}
```

### **Recovery Options**

```csharp
public class RecoveryOptions
{
    public string BackupPath { get; set; }
    public bool VerifyChecksums { get; set; } = true;
    public bool SkipExisting { get; set; } = true;
}
```

---

## ⚙️ Configuration

### **DatabaseOptions Extension**

```csharp
public class DatabaseOptions
{
    // Existing options...
    
    // ✅ Phase 6: Storage thresholds
    public int InlineThreshold { get; set; } = 4096;
    public int OverflowThreshold { get; set; } = 262144;  // 256KB
    
    // ✅ FILESTREAM
    public bool EnableFileStream { get; set; } = true;
    public string FileStreamPath { get; set; } = "blobs";
    
    // ✅ Orphan protection
    public bool EnableOrphanDetection { get; set; } = true;
    public TimeSpan OrphanRetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    public int OrphanScanIntervalHours { get; set; } = 24;
    
    // ✅ Missing file handling
    public MissingFilePolicy MissingFilePolicy { get; set; } = MissingFilePolicy.AlertOnly;
    
    // ✅ Backup integration
    public string? BackupPath { get; set; }
    public bool AutoRecoverFromBackup { get; set; } = false;
}
```

---

## 🔄 Write Flow

### **Complete Write Transaction**

```csharp
public async Task WriteRowAsync(long rowId, byte[] data)
{
    // 1. Determine storage mode
    var mode = StorageStrategy.DetermineMode(data.Length, _options);
    
    switch (mode)
    {
        case StorageMode.Inline:
            // Simple: Write to data page
            WriteInlinePage(rowId, data);
            break;
            
        case StorageMode.Overflow:
            // Medium: Create page chain
            var pages = CreateOverflowChain(rowId, data);
            WriteOverflowHeader(rowId, pages[0].PageId, data.Length);
            break;
            
        case StorageMode.FileStream:
            // Complex: Write external file + pointer
            var pointer = await WriteFileStreamAsync(rowId, data);
            WriteInlinePage(rowId, SerializePointer(pointer));
            break;
    }
}
```

---

## 📊 Performance Targets

| Operation | Inline | Overflow | FILESTREAM |
|-----------|--------|----------|------------|
| **Write 4KB** | 0.08ms | 0.10ms | 0.15ms |
| **Write 64KB** | N/A | 1.28ms | 0.95ms ✅ |
| **Write 10MB** | N/A | 256ms ❌ | 12ms ✅ |
| **Read 4KB** | 0.05ms | 0.07ms | 0.12ms |
| **Read 64KB** | N/A | 0.96ms | 0.72ms ✅ |
| **Read 10MB** | N/A | 25.6s ❌ | 50ms ✅ |

**Key Insight:** FILESTREAM is 10-500x faster for large data!

---

## 🛡️ Safety Guarantees

### **Transactional Writes**

```
1. Write to temp file
2. Compute checksum
3. Write metadata
4. Atomic move (OS operation)
5. Update database
```

If any step fails → Full rollback

### **Orphan Protection**

```
1. Files retention period (7 days default)
2. Regular scans (24 hours default)
3. Alert on missing files
4. Auto-recovery from backup
```

---

## 🧪 Test Strategy

### **Unit Tests**

```csharp
// FilePointerTests
- Serialization_RoundTrip_Preserves Data
- PathGeneration_CreatesValidSubdirectories

// FileStreamManagerTests
- Write_LargeFile_CreatesCorrectly
- Read_ExistingFile_ReturnsData
- Delete_RemovesFileAndMetadata

// OverflowPageManagerTests
- CreateChain_50KB_Creates13Pages
- ReadChain_ReassemblesData

// OrphanDetectorTests
- Detect_OrphanedFile_Found
- Detect_MissingFile_Found

// OrphanCleanerTests
- Cleanup_OldOrphans_Deleted
- Recovery_FromBackup_Succeeds
```

### **Integration Tests**

```csharp
- WriteRead_InlineData_4KB
- WriteRead_OverflowData_64KB
- WriteRead_FileStreamData_10MB
- Delete_FileStreamRow_CleansUpFile
- OrphanScan_AfterCrash_DetectsOrphans
```

---

## 📁 File Structure

```
src/SharpCoreDB/Storage/Overflow/
├── FilePointer.cs                (150 LOC)
├── FileStreamManager.cs          (400 LOC)
├── OverflowPageManager.cs        (500 LOC)
├── StorageStrategy.cs            (100 LOC)
├── OrphanDetector.cs             (350 LOC)
├── OrphanCleaner.cs              (300 LOC)
└── MaintenanceScheduler.cs       (200 LOC)

tests/SharpCoreDB.Tests/Storage/
├── FilePointerTests.cs           (150 LOC)
├── FileStreamManagerTests.cs     (250 LOC)
├── OverflowPageManagerTests.cs   (200 LOC)
├── OrphanDetectorTests.cs        (200 LOC)
└── OrphanCleanerTests.cs         (200 LOC)
```

**Total:** ~3,000 LOC

---

## ✅ Acceptance Criteria

- [ ] Inline storage works for <4KB rows
- [ ] Overflow storage works for 4KB-256KB rows
- [ ] FILESTREAM storage works for >256KB rows
- [ ] Orphan detection finds orphaned files
- [ ] Orphan cleanup removes old orphans
- [ ] Missing file detection works
- [ ] Recovery from backup works
- [ ] All tests passing
- [ ] Build successful
- [ ] Documentation complete

---

## 🚀 Implementation Order

1. **FilePointer + Enums** (~30 min)
2. **FileStreamManager** (~2 hours)
3. **OverflowPageManager** (~2 hours)
4. **StorageStrategy** (~30 min)
5. **OrphanDetector** (~1 hour)
6. **OrphanCleaner** (~1 hour)
7. **Tests** (~2 hours)
8. **Documentation** (~30 min)

**Total Estimated:** ~9-10 hours

---

**Ready for Implementation!** 🚀
