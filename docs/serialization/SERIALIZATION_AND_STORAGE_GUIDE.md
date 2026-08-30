# SharpCoreDB Serialization & Storage Format Guide

This document describes in detail how SharpCoreDB serializes, stores, and manages records in data files. It answers all questions about string constraints, free space management, and record/column boundaries.

---

## 📋 Table of Contents

1. [Overview](#overview)
2. [File Format (.scdb)](#file-format-scdb)
3. [Record Serialization](#record-serialization)
4. [String Handling & Size Constraints](#string-handling--size-constraints)
5. [Free Space Management](#free-space-management)
6. [Block Registry](#block-registry)
7. [Record & Column Boundaries](#record--column-boundaries)
8. [Record Sizing & Page Boundaries](#record-sizing--page-boundaries)
9. [Performance Considerations](#performance-considerations)
10. [FAQ](#faq)

---

## 🎯 Overview

SharpCoreDB uses a **single-file binary format** (`.scdb`) for persistent storage. The system is based on these principles:

| Aspect | Details |
|--------|---------|
| **Format** | Binary (not JSON, not SQL) - 3x faster than JSON |
| **Layout** | Fixed header + variable regions (FSM, WAL, Registry, Tables) |
| **Encoding** | UTF-8 for strings; Little-Endian for integers |
| **String Storage** | Variable-length; prefixed with 4-byte length field |
| **No Fixed-Length Requirement** | Strings can be arbitrarily long (limited by available disk space) |
| **Encryption** | Optional AES-256-GCM |
| **Compression** | Not implemented (reserved in header) |

---

## 📁 File Format (.scdb)

### Overall Structure

```
┌─────────────────────────────────────────────────────┐
│  SCDB File Layout (Single File for All Data)        │
├─────────────────────────────────────────────────────┤
│ [Header: 512 bytes]                                 │
│ ├─ Magic: 0x4243445310000000 ("SCDB\x10")          │
│ ├─ Format Version: 1                                │
│ ├─ Page Size: 4096 bytes (default)                 │
│ ├─ Offsets to all regions                          │
│ └─ Transaction state, checksums                    │
├─────────────────────────────────────────────────────┤
│ [Block Registry: Variable]                          │
│ ├─ Maps block names → file offsets/sizes           │
│ └─ Enables O(1) lookups                            │
├─────────────────────────────────────────────────────┤
│ [Free Space Map (FSM): Variable]                    │
│ ├─ 2-level bitmap for page allocation              │
│ ├─ L1: 1 bit per page (allocated=1, free=0)       │
│ └─ L2: Extent map for large allocations            │
├─────────────────────────────────────────────────────┤
│ [Write-Ahead Log (WAL): Variable]                   │
│ ├─ Transaction log entries                         │
│ └─ Recovery mechanism                              │
├─────────────────────────────────────────────────────┤
│ [Table Directory: Variable]                         │
│ ├─ Table schemas and metadata                      │
│ └─ Column definitions                              │
├─────────────────────────────────────────────────────┤
│ [Data Pages: Variable]                              │
│ ├─ Actual table data (rows)                        │
│ ├─ Pages allocated from FSM                        │
│ └─ Can be scattered (fragmentation is normal)      │
└─────────────────────────────────────────────────────┘
```

### File Header (512 bytes, fixed)

```csharp
// C# 14 struct layout (sequential, packed)
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ScdbFileHeader
{
    // === Core Identification (16 bytes) ===
    public ulong Magic;           // 0x0000: Magic number + version
    public ushort FormatVersion;  // 0x0008: Version 1
    public ushort PageSize;       // 0x000A: Default 4096 bytes
    public uint HeaderSize;       // 0x000C: Always 512

    // === Encryption (16 bytes) ===
    public byte EncryptionMode;   // 0x0010: 0=None, 1=AES-256-GCM
    public byte CompressionMode;  // 0x0011: Reserved (always 0)
    public ushort EncryptionKeyId;// 0x0012: Key derivation ID
    public fixed byte Nonce[12];  // 0x0014: AES-GCM nonce

    // === Region Offsets (64 bytes) ===
    public ulong BlockRegistryOffset;  // 0x0020: Where registry starts
    public ulong BlockRegistryLength;  // 0x0028: Size in bytes
    public ulong FsmOffset;            // 0x0030: Free Space Map
    public ulong FsmLength;            // 0x0038: FSM size
    public ulong WalOffset;            // 0x0040: Write-Ahead Log
    public ulong WalLength;            // 0x0048: WAL size
    public ulong TableDirOffset;       // 0x0050: Table schemas
    public ulong TableDirLength;       // 0x0058: Table dir size

    // === Transaction State (32 bytes) ===
    public ulong LastTransactionId;    // 0x0060: Last commit
    public ulong LastCheckpointLsn;    // 0x0068: Log Sequence Number
    public ulong FileSize;             // 0x0070: Total file size
    public ulong AllocatedPages;       // 0x0078: Page count

    // === Integrity (32 bytes) ===
    public fixed byte FileChecksum[32];// 0x0080: SHA-256 of entire file

    // === Statistics (32 bytes) ===
    public ulong TotalRecords;         // 0x00A0: Record count
    public ulong TotalDeletes;         // 0x00A8: Deleted records
    public ulong LastVacuumTime;       // 0x00B0: VACUUM timestamp
    public ulong FragmentationPercent; // 0x00B8: % fragmentation

    // === Reserved (240 bytes) ===
    public fixed byte Reserved[240];   // For future extensions
}
// Total: 512 bytes
```

---

## 🔄 Record Serialization

### Binary Format Specification

Records are stored in a **self-describing binary format**. This means type information is **embedded** in the data itself.

#### Record Layout

```
┌──────────────────────────────────────────────────┐
│ Binary Record Format                             │
├──────────────────────────────────────────────────┤
│ [ColumnCount: 4 bytes]  ← int32, little-endian
│                                                  │
│ For each column:                                 │
│  ├─ [NameLength: 4 bytes] ← int32                │
│  ├─ [ColumnName: N bytes] ← UTF-8 string        │
│  ├─ [TypeMarker: 1 byte]  ← Type indicator      │
│  └─ [Value: variable]     ← Type-specific       │
│                                                  │
│ ... (repeat for all columns)                     │
└──────────────────────────────────────────────────┘

#### Type Markers

```csharp
// Binary type indicators
public enum BinaryTypeMarker : byte
{
    Null       = 0,  // NULL value
    Int32      = 1,  // 4 bytes
    Int64      = 2,  // 8 bytes
    Double     = 3,  // 8 bytes (IEEE 754)
    Boolean    = 4,  // 1 byte
    DateTime   = 5,  // 8 bytes (binary format)
    String     = 6,  // [Length:4][UTF8 bytes]
    Bytes      = 7,  // [Length:4][Raw bytes]
    Decimal    = 8,  // 16 bytes (decimal128)
}
```

#### Concrete Example

Suppose we have:

```csharp
var row = new Dictionary<string, object>
{
    ["UserId"]  = (int)42,
    ["Name"]    = "John Doe",
    ["Email"]   = "john@example.com",
    ["Active"]  = true,
};
```

This is serialized as:

```
Offset  Size  Value                  Explanation
------  ----  -----                  -----------
0       4     04 00 00 00            ColumnCount = 4 (little-endian int32)

4       4     06 00 00 00            NameLength = 6 (length of "UserId")
8       6     55 73 65 72 49 64      "UserId" (UTF-8)
14      1     01                     TypeMarker = 1 (Int32)
15      4     2A 00 00 00            Value = 42 (little-endian int32)

19      4     04 00 00 00            NameLength = 4 (length of "Name")
23      4     4E 61 6D 65            "Name" (UTF-8)
27      1     06                     TypeMarker = 6 (String)
28      4     09 00 00 00            StringLength = 9 (length of "John Doe")
32      9     4A 6F 68 6E 20 44 6F 65  "John Doe" (UTF-8)

41      4     05 00 00 00            NameLength = 5 (length of "Email")
45      5     45 6D 61 69 6C         "Email" (UTF-8)
50      1     06                     TypeMarker = 6 (String)
51      4     10 00 00 00            StringLength = 16
55      16    6A 6F 68 6E 40 65 78...  "john@example.com" (UTF-8)

71      4     06 00 00 00            NameLength = 6 (length of "Active")
75      6     41 63 74 69 76 65      "Active" (UTF-8)
81      1     04                     TypeMarker = 4 (Boolean)
82      1     01                     Value = 1 (true)

Total: 83 bytes
```

### Serialization Code (C# 14)

```csharp
public static class BinaryRowSerializer
{
    // Phase 3 optimization: Zero-allocation serialization using ArrayPool
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static byte[] Serialize(Dictionary<string, object> row)
    {
        // 1. Calculate total size (no allocations yet)
        int totalSize = sizeof(int); // Column count
        foreach (var (key, value) in row)
        {
            totalSize += sizeof(int);                    // Name length
            totalSize += Encoding.UTF8.GetByteCount(key); // Name
            totalSize += sizeof(byte);                    // Type marker
            totalSize += GetValueSize(value);             // Value size
        }

        // 2. Rent buffer from ArrayPool (zero allocation from heap)
        byte[]? pooledBuffer = null;
        try
        {
            pooledBuffer = BufferPool.Rent(totalSize);
            var buffer = pooledBuffer.AsSpan(0, totalSize);
            int offset = 0;

            // 3. Write column count
            BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], row.Count);
            offset += sizeof(int);

            // 4. Write each column
            foreach (var (key, value) in row)
            {
                // Write column name
                var nameBytes = Encoding.UTF8.GetBytes(key);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], nameBytes.Length);
                offset += sizeof(int);
                nameBytes.CopyTo(buffer[offset..]);
                offset += nameBytes.Length;

                // Write type and value
                offset += WriteValue(buffer[offset..], value);
            }

            // 5. Copy to final array (only allocation here)
            return buffer.ToArray();
        }
        finally
        {
            // 6. Return buffer to pool for reuse
            if (pooledBuffer is not null)
            {
                BufferPool.Return(pooledBuffer, clearArray: true);
            }
        }
    }

    private static int WriteValue(Span<byte> buffer, object? value)
    {
        int offset = 0;

        switch (value)
        {
            case null:
                buffer[offset++] = 0; // Type: Null
                break;

            case int i:
                buffer[offset++] = 1; // Type: Int32
                BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], i);
                offset += sizeof(int);
                break;

            case long l:
                buffer[offset++] = 2; // Type: Int64
                BinaryPrimitives.WriteInt64LittleEndian(buffer[offset..], l);
                offset += sizeof(long);
                break;

            case double d:
                buffer[offset++] = 3; // Type: Double
                BinaryPrimitives.WriteInt64LittleEndian(buffer[offset..], 
                    BitConverter.DoubleToInt64Bits(d));
                offset += sizeof(double);
                break;

            case bool b:
                buffer[offset++] = 4; // Type: Boolean
                buffer[offset++] = b ? (byte)1 : (byte)0;
                break;

            case DateTime dt:
                buffer[offset++] = 5; // Type: DateTime
                BinaryPrimitives.WriteInt64LittleEndian(buffer[offset..], dt.Ticks);
                offset += sizeof(long);
                break;

            case string s:
                buffer[offset++] = 6; // Type: String
                var strBytes = Encoding.UTF8.GetBytes(s);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], strBytes.Length);
                offset += sizeof(int);
                strBytes.CopyTo(buffer[offset..]);
                offset += strBytes.Length;
                break;

            case byte[] b:
                buffer[offset++] = 7; // Type: Bytes
                BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], b.Length);
                offset += sizeof(int);
                b.CopyTo(buffer[offset..]);
                offset += b.Length;
                break;
        }

        return offset;
    }
}
```

---

## 🔤 String Handling & Size Constraints

### ❌ Misconception: "You need lots of free space"

**This is NOT true!** Here's why:

#### 1. **Strings are variable-length**
- A record with 10-byte strings needs only 10 bytes of disk space
- A record with 10MB strings needs 10MB of disk space
- **No fixed size per column** → no wasted space

#### 2. **Length-prefixing solves boundaries**

```
String Layout:
┌─────────────────┬──────────────────────────────┐
│ Length (4 bytes)│ UTF-8 data (variable)        │
└─────────────────┴──────────────────────────────┘

Example: "John Doe" (8 characters = 8 bytes UTF-8)
┌──────────────────┬────────────────────────────────────────────────────┐
│ 08 00 00 00      │ 4A 6F 68 6E 20 44 6F 65                           │
│ (length = 8)     │ "John Doe"                                         │
└──────────────────┴────────────────────────────────────────────────────┘

Example: "日本" (2 characters = 6 bytes UTF-8)
┌──────────────────┬────────────────────────────────────────────────────┐
│ 06 00 00 00      │ E6 97 A5 E6 9C AC                                 │
│ (length = 6)     │ "日本"                                              │
└──────────────────┴────────────────────────────────────────────────────┘
```

### ⚠️ CRITICAL: Actual Size Constraints in SharpCoreDB

**CORRECTION:** The actual constraint is NOT "2GB per string" but rather **"record must fit in one page"**.

| Constraint | Limit | Why |
|-----------|-------|-----|
| **Max record size** | ~4056 bytes (default 4KB page) | Record must fit in one page (4096 - 40 header bytes) |
| **Max page size** | Configurable 4KB-64KB | Can be increased at database creation |
| **Max column count** | 2,147,483,647 | Limited by int32 column count in serialization |
| **Max file size** | Limited by filesystem | ext4: 16TB, NTFS: 8EB (technically) |
| **Single string in record** | ~4000-8000 bytes practical | Dependent on page size and other columns |

**WARNING:** If you have a record (including all columns) that exceeds the page size, you'll get an error:
```csharp
// This will fail if total serialized size > PageSize:
if (recordData.Length > MAX_RECORD_SIZE)  // MAX_RECORD_SIZE ≈ 4056 bytes
    return Error("Record too large for page");
```

### 4. **Unicode Support**

```csharp
// UTF-8 encoding handles all Unicode correctly
var testStrings = new[]
{
    "Hello",           // ASCII (5 bytes)
    "Café",            // Latin extended (5 bytes: C-a-f-[2-byte é])
    "日本国",          // Japanese (9 bytes: 3 chars × 3 bytes each)
    "🚀🎉",            // Emoji (8 bytes: 2 chars × 4 bytes each)
    "مرحبا",           // Arabic (10 bytes)
    "Ελληνικά",        // Greek (14 bytes)
};

// All stored correctly with length-prefix
foreach (var str in testStrings)
{
    int byteCount = Encoding.UTF8.GetByteCount(str);
    byte[] bytes = Encoding.UTF8.GetBytes(str);
    // Stored as: [byteCount:4][bytes:byteCount]
}
```

### ⚠️ What About Large Strings?

**You CANNOT store arbitrarily large strings in a single record.**

```csharp
// Example: 4KB page size (DEFAULT_PAGE_SIZE = 4096)

var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
    ["Biography"] = new string('X', 4000),  // 4000 bytes!
};

// Serialization:
// - ColumnCount (4 bytes)
// - Column 1: NameLen(4) + "UserId"(6) + Type(1) + Value(4) = 15 bytes
// - Column 2: NameLen(4) + "Name"(4) + Type(1) + StrLen(4) + "John Doe"(8) = 21 bytes  
// - Column 3: NameLen(4) + "Biography"(9) + Type(1) + StrLen(4) + 4000 bytes = 4018 bytes
// TOTAL: 4 + 15 + 21 + 4018 = 4058 bytes
//
// Result: 4058 > 4056 (MAX_PAGE_DATA_SIZE)
// ❌ ERROR! Record too large for page!
```

**What are your options?**

#### Option 1: Increase Page Size
```csharp
// Create database with larger pages
var options = new DatabaseOptions
{
    PageSize = 8192,  // 8 KB pages (8192 - 40 = 8152 bytes data)
    CreateImmediately = true,
};

var provider = SingleFileStorageProvider.Open("mydb.scdb", options);

// Now record of 4058 bytes fits in 8KB page ✅
```

#### Option 2: Store Externally

```csharp
// Don't store huge strings as columns
// Instead, use a reference/ID

var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
    ["BioFileId"] = "bio_12345",  // Reference to external file
};

// Then separately store large file:
var largeFile = File.ReadAllBytes("large_biography.txt");  // 10 MB
// Use your own file management (filesystem, cloud storage, etc.)
```

#### Option 3: Normalize Your Schema
```csharp
// Split into multiple records instead of one large record

// INSTEAD OF:
var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
    ["Biography"] = new string('X', 10000),  // ❌ Too large!
};

// DO THIS:
var userRecord = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
};

var bioRecord = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["BioContent"] = "Lorem ipsum...",  // Smaller chunks
};

// Store in separate table or with separate keys
```

---

## 📊 Free Space Management

### How FSM Works

The **Free Space Map (FSM)** manages free pages. This is a 2-level bitmap:

```csharp
internal sealed class FreeSpaceManager
{
    // ✅ Level 1: 1 bit per page (allocated=1, free=0)
    private readonly BitArray _l1Bitmap;  // 1M pages = 4GB @ 4KB
    
    // ✅ Level 2: Large contiguous extents
    private readonly List<FreeExtent> _l2Extents;
    
    // ✅ Format on disk:
    // [FsmHeader(64B)] [L1 Bitmap(variable)] [L2 Extents(variable)]
}
```

#### Allocation Algorithm

```csharp
public ulong AllocatePages(int count)
{
    lock (_allocationLock)
    {
        // 1. Try to find contiguous free pages
        var startPage = FindContiguousFreePages(count);
        
        if (startPage == ulong.MaxValue)
        {
            // 2. No space found? Extend file exponentially
            // Minimum extension is byte-based (issue #345): ~10 MB regardless of PageSize.
            var minExtensionPages = Math.Max(1, (int)(MIN_EXTENSION_BYTES / _pageSize));
            var extensionSize = Math.Max(
                minExtensionPages,
                Math.Max(count, currentSize / EXTENSION_GROWTH_FACTOR)
            );
            
            ExtendFile((int)extensionSize);  // Allocate more space
            _preallocatedPages = extensionSize - count;
        }

        // 3. Mark pages as allocated
        for (var i = 0; i < count; i++)
        {
            _l1Bitmap.Set((int)(startPage + i), true);
        }

        _freePages -= (ulong)count;
        return startPage * (ulong)_pageSize;  // Return byte offset
    }
}
```

#### File Growth Strategy

```
File Growth Pattern (Exponential):

Initial file:  100 pages free
After fill:    ├─ Request 50 pages → Extend by 50 (growth factor 1x)
               └─ New size: 150 pages
               
After fill:    ├─ Request 100 pages → Extend by 150 (growth factor 1.5x)
               └─ New size: 300 pages
               
After fill:    ├─ Request 200 pages → Extend by 300 (growth factor 2x)
               └─ New size: 600 pages

Result: File grows exponentially, reducing I/O for allocation
```

### FSM Data Structure

```
Free Space Map Layout:
┌─────────────────────────────────────────────┐
│ FSM Header (64 bytes)                       │
│ ├─ TotalPages: 8 bytes                      │
│ ├─ FreePages: 8 bytes                       │
│ └─ ... metadata ...                         │
├─────────────────────────────────────────────┤
│ L1 Bitmap (Variable)                        │
│ ├─ 1 bit per page                           │
│ ├─ 1 = allocated, 0 = free                  │
│ └─ Example: 1M pages = 128 KB bitmap        │
├─────────────────────────────────────────────┤
│ L2 Extent Map (Variable)                    │
│ ├─ Each extent: [StartPage: 8B][Count: 8B] │
│ └─ Optimized for large allocations         │
└─────────────────────────────────────────────┘
```

### Performance: O(1) Allocation

```csharp
// Why FSM is efficient:

// ❌ Slow (linear scan):
for (int i = 0; i < totalPages; i++)
    if (pageStatus[i] == Free) { /* found page */ }
// O(n) time complexity

// ✅ Fast (bitmap search):
int pageIndex = _l1Bitmap.FindFirstSet();  // Built-in CPU instruction
// O(1) amortized time
```

---

## 📑 Block Registry

### Purpose

The **Block Registry** maps logical block names to physical file locations:

```csharp
// Block Registry Entry
public struct BlockEntry
{
    public string BlockName;      // e.g., "Users_Table_001"
    public ulong Offset;          // Byte offset in file
    public ulong Length;          // Block size in bytes
    public byte[] Checksum;       // SHA-256 for integrity
    public ulong CreatedAt;       // Timestamp
    public ulong LastModified;    // Timestamp
}
```

### Registry Layout

```
Block Registry Format:
┌─────────────────────────────────────────────┐
│ Registry Header (64 bytes)                  │
│ ├─ EntryCount: 8 bytes                      │
│ ├─ IndexVersion: 8 bytes                    │
│ └─ ... metadata ...                         │
├─────────────────────────────────────────────┤
│ Entry 1 (Variable size)                     │
│ ├─ [NameLength: 4][Name: N][Offset: 8]    │
│ ├─ [Length: 8][Checksum: 32][Timestamps]   │
│ └─ Total: ~60-100 bytes per entry           │
├─────────────────────────────────────────────┤
│ Entry 2 ... Entry N                         │
└─────────────────────────────────────────────┘
```

### O(1) Lookups

```csharp
internal sealed class BlockRegistry
{
    // ✅ ConcurrentDictionary = O(1) average lookup
    private readonly ConcurrentDictionary<string, BlockEntry> _blocks;

    public bool TryGetBlock(string name, out BlockEntry entry)
    {
        return _blocks.TryGetValue(name, out entry);  // O(1)
    }

    // ✅ Batched writes reduce I/O
    private const int BATCH_THRESHOLD = 200;     // Flush every 200 blocks
    private const int FLUSH_INTERVAL_MS = 500;   // Or every 500ms

    // Performance: Batching reduces I/O from 500/sec to ~10/sec
}
```

### Phase 3 Optimization: Batching

```csharp
// OLD (Phase 1):
for (int i = 0; i < 1000; i++)
{
    registry.SetBlock(names[i], entries[i]);
    registry.Flush();  // Flushes to disk EVERY time!
}
// Result: 1000 disk writes

// NEW (Phase 3):
for (int i = 0; i < 1000; i++)
{
    registry.SetBlock(names[i], entries[i]);  // In-memory only
}
registry.FlushAsync();  // ← Single batched flush!
// Result: 1-10 disk writes (depends on batch size)
```

---

## 🎯 Record & Column Boundaries

### How Do We Know Where Records End?

**Answer: Records are stored in complete blocks, and we use the Block Registry.**

#### Record Storage Flow

```
Step 1: User writes a row
┌────────────────────────────────────┐
│ row = {Id: 42, Name: "John"}       │
└────────────────────────────────────┘
                ↓
Step 2: Serialize to binary
┌────────────────────────────────────────────────────┐
│ byte[] binary = [04][06]..."Id"...[42][06]...      │
│ Size: 50 bytes                                     │
└────────────────────────────────────────────────────┘
                ↓
Step 3: Allocate space from FSM
┌────────────────────────────────────────────────────┐
│ pages = FSM.AllocatePages(1)  // 4KB page          │
│ offset = pages * 4096 = 1,048,576 (byte position) │
└────────────────────────────────────────────────────┘
                ↓
Step 4: Write to disk
┌────────────────────────────────────────────────────┐
│ Write 50 bytes at offset 1,048,576                 │
│ (Data can be < page size; no padding required)    │
└────────────────────────────────────────────────────┘
                ↓
Step 5: Register in Block Registry
┌──────────────────────────────────────────────────────┐
│ registry["Users_Row_001"] = BlockEntry {            │
│     Offset: 1,048,576,                              │
│     Length: 50,                                     │
│     Checksum: SHA256(binary)                        │
│ }                                                    │
└──────────────────────────────────────────────────────┘
```

### Column Boundaries Within a Record

**Columns don't have fixed boundaries!** They are self-describing:

```
Record in memory:
┌──────────────────────────────────────┐
│ [ColumnCount: 4]                     │ ← Always at offset 0
│ [NameLen: 4][Name: N][Type: 1][Val]  │ ← Column 1
│ [NameLen: 4][Name: N][Type: 1][Val]  │ ← Column 2
│ [NameLen: 4][Name: N][Type: 1][Val]  │ ← Column 3
└──────────────────────────────────────┘

To deserialize:
1. Read ColumnCount from offset 0 → found 3 columns
2. Sequentially parse columns:
   - Read NameLen, Name, Type, Value (advance offset)
   - Repeat for column 2
   - Repeat for column 3
```

### Concrete Deserialization Example

```csharp
public static Dictionary<string, object> Deserialize(ReadOnlySpan<byte> data)
{
    int offset = 0;

    // Step 1: Read column count
    int columnCount = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    offset += sizeof(int);  // offset = 4

    var result = new Dictionary<string, object>(columnCount);

    // Step 2: Read each column sequentially
    for (int i = 0; i < columnCount; i++)
    {
        // Read column name
        int nameLength = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        offset += sizeof(int);  // offset = 8, 12, 16, ...
        
        var name = Encoding.UTF8.GetString(data.Slice(offset, nameLength));
        offset += nameLength;  // offset advances by name length
        
        // Read type and value
        var (value, bytesRead) = ReadValue(data[offset..]);
        offset += bytesRead;  // offset advances by value size

        result[name] = value;
    }

    return result;
}

// Key insight: offset advances based on ACTUAL data sizes
// No fixed column positions needed!
```

---

## 📄 Record Sizing & Page Boundaries

### Critical Constraint: Records Must Fit in a Single Page

**Important:** A record CANNOT be split across multiple pages.

#### Why?

```csharp
// Records are atomic units stored in blocks
BlockEntry entry = new BlockEntry
{
    BlockName = "Users_Row_001",
    Offset = 1048576,        // Start of page 256
    Length = 3950,           // Entire record size (< 4096)
    Checksum = [...],
    // ...
};

// The Block Registry stores:
// - Start offset (byte position)
// - Total length (entire record size)
// - This makes lookups O(1) and atomic
```

#### What Happens If a Record Would Exceed Page Size?

```csharp
// Example: 4KB page size (default)

var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Biography"] = new string('X', 4100),  // 4100 bytes!
};

// Serialization:
// ColumnCount (4) + UserId metadata (15) + Name metadata (21) 
// + Biography metadata (4030) + 4100 bytes (string data)
// ≈ 4 + 15 + 21 + 4030 + 4100 = 8206 bytes
//
// Result: 8206 > 4096 (page size)
// ❌ ERROR! Record too large for page!
```

#### Solution 1: Increase Page Size

```csharp
// Create database with larger pages
var options = new DatabaseOptions
{
    PageSize = 8192,  // 8 KB pages → 8152 bytes available
    CreateImmediately = true,
};

var provider = SingleFileStorageProvider.Open("mydb.scdb", options);
```

#### Solution 2: Store Externally

```csharp
// Don't store huge strings as columns
// Instead, use a reference/ID

var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
    ["BioFileId"] = "bio_12345",  // Reference to external file
};

// Then separately store large file:
var largeFile = File.ReadAllBytes("large_biography.txt");  // 10 MB
// Use your own file management (filesystem, cloud storage, etc.)
```

### How Pages Are Allocated

SharpCoreDB allocates pages as **complete units**. You cannot split data across page boundaries:

```
File Layout (4KB page size):

Page 0 (0-4095):         [Header: 512 bytes][unused: 3584 bytes]
Page 1 (4096-8191):      [Block Registry data: 2000 bytes][unused: 2096]
Page 2 (8192-12287):     [FSM data: 1500 bytes][unused: 2596]
Page 3 (12288-16383):    [Users_Row_001: 50 bytes][unused: 4046] ← Wasted space!
Page 4 (16384-20479):    [Users_Row_002: 100 bytes][unused: 3996] ← Wasted space!
...

Even though Row_001 is only 50 bytes, it occupies an entire 4096-byte page.
```

**Why?** Because the Block Registry tracks:
```csharp
// Block boundaries are PAGE-aligned
public ulong Offset;  // Always a multiple of PageSize (4096)
public ulong Length;  // Actual data size (can be < PageSize)

// Example:
// Offset = 12288 (Page 3 start, multiple of 4096)
// Length = 50 (actual record bytes)
```

### String Splitting: The Reality

If you have a long string that would exceed the page:

```csharp
// BEFORE serialization - THIS DOESN'T HAPPEN
// The entire record (including all strings) is serialized to binary
byte[] binary = Serialize(row);  // ← Complete binary in memory
int recordSize = binary.Length;

// Check if record fits in a page
if (recordSize > PageSize)
{
    throw new InvalidOperationException(
        $"Record too large ({recordSize} bytes) for page size ({PageSize} bytes)");
}

// If it fits, allocate ONE page and write entire record
ulong pageOffset = FSM.AllocatePages(1);  // ← Allocates 1 full page
provider.WriteBytes(pageOffset, binary);  // ← Write entire record at once
```

### Example: Long String at End of Page

**Scenario:** You have a string that's close to the page boundary

```
Page Layout (4KB = 4096 bytes):

Offset 0-3:              [ColumnCount: 4]
Offset 4-20:             [Column 1 metadata + value]
Offset 21-60:            [Column 2 metadata + value]
Offset 61-3200:          [Column 3 metadata + value (large string)]
Offset 3201-4090:        [Column 4: Short string]
Offset 4091-4095:        [unused: 5 bytes]
                         ↑ NO SPLITTING NEEDED
                         Record fits entirely (4091 bytes < 4096)
```

**What if record was 4097 bytes?**
```
❌ ERROR! Record doesn't fit in page.
   Must increase PageSize or reduce record size.
```

### The Key Insight: No Padding, No Splitting

```csharp
// 1. Records are serialized completely in memory
byte[] recordBinary = Serialize(row);
// recordBinary could be 50 bytes or 3000 bytes

// 2. FSM allocates ONE page (regardless of record size)
ulong pageStart = FSM.AllocatePages(1);
// pageStart = multiple of PageSize (e.g., 4096, 8192, 12288, ...)

// 3. Write record to that page
provider.WriteBytes(pageStart, recordBinary);
// Writes 50 bytes OR 3000 bytes
// NO PADDING to reach 4096 bytes
// NO SPLITTING across pages

// 4. Block Registry tracks exact length
registry[recordName] = new BlockEntry
{
    Offset = pageStart,
    Length = recordBinary.Length,  // ← EXACT size, not padded
};
```

### Performance Implication

```csharp
// With variable-length records:
Page 1: 50-byte record → 4046 bytes wasted space per page
Page 2: 100-byte record → 3996 bytes wasted space per page
Page 3: 3000-byte record → 1096 bytes wasted space per page
Page 4: 30-byte record → 4066 bytes wasted space per page
```

**This is normal and acceptable because:**
1. ✅ FSM tracks free space (can reuse partially-filled pages for small records)
2. ✅ Compression not needed (data is already binary, not JSON overhead)
3. ✅ Simpler architecture (no split-record complexity)
4. ✅ Atomic writes (record written once, completely)

### How FSM Reuses Wasted Space

```csharp
// FSM doesn't care about wasted space within a page
// It tracks FREE PAGES, not free bytes

FSM State:
├─ Page 0: Allocated (Header)
├─ Page 1: Allocated (Registry)
├─ Page 2: Allocated (FSM)
├─ Page 3: Allocated (50-byte record) ← Still counts as ALLOCATED
├─ Page 4: Allocated (100-byte record) ← Still counts as ALLOCATED
├─ Page 5: FREE ← Can reuse this
└─ ...

// When inserting a small record (30 bytes):
// Option 1: Reuse Page 3 (already allocated, has room)
// Option 2: Allocate new Page 5

// SharpCoreDB behavior:
// - Phase 1: Always allocate new pages (simpler)
// - Phase 3: Could implement "sub-page allocation" (future optimization)
```

### Summary: Page Boundaries & Strings

| Situation | What Happens | Result |
|-----------|--------------|--------|
| Small record (< page size) | Allocates 1 page, writes record, registers block | ✅ Works |
| Large record (> page size) | Throws error during serialization | ❌ Error |
| String at page end | String included in serialized record (no split) | ✅ Stays together |
| Multiple pages needed | Not supported; use larger page size | ⚠️ Design limit |

---

## ⚡ Performance Considerations

### Zero Allocation Principles

SharpCoreDB uses C# 14 modern features for zero-allocation:

```csharp
// ✅ Span<T> - zero-copy slicing
public static byte[] Serialize(Dictionary<string, object> row)
{
    byte[]? pooledBuffer = null;
    try
    {
        pooledBuffer = BufferPool.Rent(totalSize);
        var buffer = pooledBuffer.AsSpan(0, totalSize);  // ← No allocation
        
        // Write data directly to span
        // ... serialization ...
        
        return buffer.ToArray();  // Only allocation here
    }
    finally
    {
        if (pooledBuffer is not null)
        {
            BufferPool.Return(pooledBuffer, clearArray: true);
        }
    }
}

// ✅ ArrayPool<T> - reuse buffers
// Instead of allocating new byte[] each time, we rent from pool
// This reduces GC pressure significantly

// ✅ Inline arrays - fixed-size buffers on stack
[InlineArray(32)]
file struct ChecksumBuffer
{
    private byte _element0;
}
// 32 bytes on stack, NO heap allocation
```

### Write Batching (Phase 3)

```csharp
// Before:
for (int i = 0; i < 1000; i++)
{
    database.ExecuteSQL($"INSERT INTO users VALUES ({i}, 'User{i}')");
    database.Flush();  // ← 1000 disk writes
}

// After (Phase 3):
var statements = new List<string>();
for (int i = 0; i < 1000; i++)
{
    statements.Add($"INSERT INTO users VALUES ({i}, 'User{i}')");
}
database.ExecuteBatchSQL(statements);  // ← 1-10 disk writes
database.Flush();
```

### Metrics

```csharp
// Monitor performance
var metrics = blockRegistry.GetMetrics();
// (TotalFlushes, BatchedFlushes, BlocksWritten, DirtyCount)
// Example output: (10, 10, 1000, 0)
// Interpretation: 1000 blocks written in 10 batched flushes = 100 blocks/flush
```

---

## ❓ FAQ

### Q1: Do I need to reserve lots of free space?

**A:** No! Free space is managed automatically via FSM. Files grow exponentially:
- First growth: +10 MB
- Subsequent growth: exponential (2x, 4x, ...)
- No pre-allocation needed

### Q2: How big can strings be?

**A:** Limited by the **page size**, not theoretically unlimited:

**Default (4KB page):**
- Page capacity: 4096 bytes total
- Page header overhead: 40 bytes
- **Available for data: 4056 bytes**
- Minus serialization overhead for column metadata
- **Practical limit: ~4000-4050 bytes per complete record** (all columns combined!)

**Example breakdown:**
```csharp
// 4KB page (4096 bytes)
Page structure:
├─ Header: 40 bytes
└─ Data: 4056 bytes

Record with multiple columns:
├─ ColumnCount (4 bytes)
├─ Column 1 metadata + value
├─ Column 2 metadata + value
├─ Column 3 metadata + value (large string)
└─ Must ALL fit in 4056 bytes!

If total > 4056 bytes → ERROR!
```

**For larger strings:**
- ✅ Increase page size: Use 8KB, 16KB, or 32KB pages
- ✅ Store externally: Use filesystem or cloud storage references
- ✅ Normalize schema: Split into multiple records

**What Happens If You Try to Store Too Much?**

```csharp
// Example: Trying to store record > page size

var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["LargeText"] = new string('X', 4100),  // 4100 bytes!
};

try
{
    db.InsertRecord(row);
}
catch (InvalidOperationException ex)
{
    // Exception message:
    // "Record too large (4158 bytes) for page size (4096 bytes)"
    // Serialized size is 4158, but max is 4056!
    
    Console.WriteLine(ex.Message);
    // FIX: Increase page size BEFORE inserting large records
}

// Code that causes this:
// if (recordData.Length > MAX_RECORD_SIZE)  // MAX_RECORD_SIZE ≈ 4056
//     return Error("Record too large for page");

// ⚠️ IMPORTANT: Page size is FIXED at database creation time
// You CANNOT change it dynamically after creation.
// All existing pages, FSM, and Block Registry depend on it.
// Changing page size requires complete database migration.
```

**Best Practice: Pre-calculate and Plan Page Size**

```csharp
// BEFORE creating database, estimate your largest record:

var largestExpectedRow = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
    ["Description"] = "Some description...",
    ["LargeText"] = new string('X', 5000),  // Large column
};

// Estimate serialized size:
// ColumnCount(4) + UserId metadata(15) + Name metadata(21) 
// + Description metadata(30) + LargeText metadata(4030) ≈ 4100 bytes

// Since 4100 > 4056 (max for 4KB page), choose larger page:
var options = new DatabaseOptions
{
    PageSize = 8192,  // 8KB page → 8152 bytes available ✅
    CreateImmediately = true,
};

var db = new SharpCoreDB(options);

// Now large records will work!
db.InsertRecord(largestExpectedRow);  // ✅ Success
```

**Why Dynamic Page Size Isn't Practical**

SharpCoreDB's architecture makes dynamic page resizing impossible without complete database migration:

1. **File Header**: Page size is stored once, read at startup
2. **FSM (Free Space Map)**: Bitmap assumes fixed page size
3. **Block Registry**: Offsets are multiples of current page size
4. **Existing Records**: All stored with current page boundaries

Changing page size would require:
- ❌ Reading every page from disk
- ❌ Recalculating all offsets
- ❌ Rebuilding entire FSM
- ❌ Rewriting entire file

**Solution: Design Your Schema Appropriately**

```csharp
// ❌ DON'T: Store entire biography in one record
var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Biography"] = new string('X', 100000),  // 100KB!
};

// ✅ DO: Split into manageable pieces
var userRecord = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
};

var bioChunk1 = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["BioPart"] = 1,
    ["Content"] = "First part...",  // ~2KB
};

var bioChunk2 = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["BioPart"] = 2,
    ["Content"] = "Second part...",  // ~2KB
};

// OR: Store reference + external file
var userWithRef = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["BioFileRef"] = "user_1_bio.txt",  // Small reference
};














````````
This is the description of what the code block changes:
Add comprehensive LOB (Large Object) storage proposal as a future enhancement, explaining how it would work and why it's needed

This is the code block that represents the suggested code change:

````````markdown
---

## 🚀 Future Enhancement: LOB (Large Object) Storage

### The Vision: Automatic Overflow Handling

Instead of throwing an error, SharpCoreDB could automatically redirect large columns to external storage:

```csharp
// FUTURE FEATURE (not yet implemented)
var row = new Dictionary<string, object>
{
    ["UserId"] = 1,
    ["Name"] = "John Doe",
    ["Biography"] = new string('X', 1_000_000),  // 1MB - would overflow!
};

// What COULD happen:
// 1. Serialize record: Biography > threshold (e.g., 2KB)
// 2. Automatically create LOB reference: "LOB_12345.dat"
// 3. Store huge string in external file
// 4. Store pointer in record: ["Biography"] = "LOB_12345.dat"
// 5. On read: Automatically dereference pointer, fetch from disk

// Result: ✅ Works! No error, transparent to developer
```

### How It Would Work (Architecture)

```
Current (v1):
┌──────────────────────────────────────┐
│ Record (all data in page)            │
├──────────────────────────────────────┤
│ [UserId: 4][Name: 20][Biography: ???]│ ← Doesn't fit!
└──────────────────────────────────────┘

Future (LOB Overflow):
┌──────────────────────────────────────┐
│ Record (in page)                     │
├──────────────────────────────────────┤
│ [UserId: 4][Name: 20][BioRef: 32]   │ ← Pointer to LOB
└──────────────────────────────────────┘
           ↓
    [External Storage]
    ┌─────────────────────────┐
    │ LOB_12345.dat (1MB)     │
    │ [Biography data: full]  │
    └─────────────────────────┘
```

### Implementation Requirements

This would require:

1. **LOB Storage Layer**
   - Separate file or directory for large objects
   - Naming scheme: `LOB_<hash>.dat`
   - Reference counting (cleanup when record deleted)

2. **Automatic Threshold Detection**
   ```csharp
   // Configuration:
   DatabaseConfig.LobThresholdBytes = 2048;  // Columns > 2KB → LOB
   ```

3. **Transparent Dereferencing**
   - On read: Automatically fetch LOB data
   - On write: Check if value exceeds threshold
   - On delete: Clean up orphaned LOBs

4. **Garbage Collection**
   - Track which LOBs are referenced
   - Periodically clean up orphaned files
   - Similar to VACUUM in PostgreSQL

5. **Serialization Changes**
   ```csharp
   // Type markers would need:
   LobReference = 9,  // [LOB_ID: string pointer]
   
   // On serialization:
   if (strBytes.Length > LOB_THRESHOLD)
   {
       // Create LOB file
       var lobId = CreateLobFile(strBytes);
       // Store reference instead
       WriteValue(buffer, lobId, BinaryTypeMarker.LobReference);
   }
   ```

### Comparison with Current Workarounds

| Approach | Pros | Cons |
|----------|------|------|
| **Error (current)** | Simple architecture | User must handle large data |
| **Increase page size** | No code changes needed | Wastes space for small records |
| **External file refs** | Works today | Manual management |
| **LOB Overflow (future)** | ✅ Transparent, automatic | Complex implementation |

### Why This Isn't Trivial

```csharp
// Challenges:

// 1. Reference counting
//    - Track which LOBs are in use
//    - Handle cascade deletes
//    - Update when records are modified

// 2. Transaction consistency
//    - LOB creation happens AFTER record write
//    - Need to handle crashes between two writes
//    - Requires WAL entries for LOB operations

// 3. Performance implications
//    - Reading a record now requires potential I/O for LOB
//    - Cache LOB data? How much memory?
//    - Compression? Encryption?

// 4. Backward compatibility
//    - Old records have no LOBs
//    - New records might have LOBs
//    - Format version must change
```

### Proposal: Phase 5 Feature

This would be perfect for **Phase 5** after current performance work is complete:

**Goals:**
- ✅ Support arbitrary-sized strings transparently
- ✅ Maintain page size constraints
- ✅ Zero API changes for users
- ✅ Automatic compression of LOB data

**Tasks:**
1. Design LOB file format
2. Implement LOB storage layer
3. Update BinaryRowSerializer with threshold logic
4. Add reference tracking to Block Registry
5. Implement garbage collection
6. Add tests for edge cases (crashes during LOB creation, etc.)

### Temporary Workaround (Today)

Until LOB support is added, use this pattern:

```csharp
// Create a "LOB Reference" table
var lobTable = db.CreateTable("LOBData");

// Store large data separately
var largeContent = new string('X', 1_000_000);
var lobEntry = new Dictionary<string, object>
{
    ["LobId"] = Guid.NewGuid().ToString(),
    ["Owner"] = "Users",
    ["OwnerKey"] = 42,
    ["Data"] = largeContent,
};
lobTable.Insert(lobEntry);

// Store reference in main record
var userRecord = new Dictionary<string, object>
{
    ["UserId"] = 42,
    ["Name"] = "John Doe",
    ["BiographyLobId"] = lobEntry["LobId"],  // Reference only
};
usersTable.Insert(userRecord);

// On read:
var user = usersTable.FindById(42);
var lobId = user["BiographyLobId"];
var biography = lobTable.FindByLobId(lobId)["Data"];
```

---














````````

This is the description of what the code block changes:
Add practical string size calculator with formulas, examples, and API design for table creation

This is the code block that represents the suggested code change:

````````markdown
---

## 📏 String Size Calculator & Table Design Guide

### The Reality: Calculate Your Maximum String Size

When creating a table, you need to know: **Given all my columns, how large can a single string column be?**

#### Formula

```
MaxStringSize = (PageSize - HeaderSize - OtherColumnsSize - SerializationOverhead)
```

**Breaking it down:**

```csharp
// Step 1: Fixed overhead per record
int columnCount = 4;
int baseOverhead = sizeof(int);  // ColumnCount: 4 bytes

// Step 2: Per-column overhead (for NON-string columns)
int userIdOverhead = sizeof(int) + 1;  // NameLen(4) + Name(6) + Type(1) + Value(4) = 15
int emailOverhead = sizeof(int) + 1;   // NameLen(4) + Name(5) + Type(1) = 10

// Step 3: String column breakdown
// For a string, the formula is:
// NameLen(4) + ColumnName(N) + Type(1) + StringLen(4) + StringData(X)
int bioColumnNameLen = "Biography".Length;  // 9 bytes
int bioOverhead = 4 + bioColumnNameLen + 1 + 4;  // = 18 bytes
// Remaining space for string data:
int availableForBioData = MAX_PAGE_DATA_SIZE - baseOverhead - userIdOverhead - emailOverhead - bioOverhead;

// Example with 4KB page (4056 bytes available):
// 4056 - 4 - 15 - 10 - 18 = 4009 bytes available for Biography string!
```

### Practical Examples

#### Example 1: Small Records (4KB page)

```csharp
// Table schema:
// ┌─────────────────┬──────────┬────────┐
// │ Column          │ Type     │ Size   │
// ├─────────────────┼──────────┼────────┤
// │ UserId          │ Int32    │ 4 bytes│
// │ Email           │ String   │ 50 max │
// │ Name            │ String   │ 100 max│
// │ Bio             │ String   │ ??? max│
// └─────────────────┴──────────┴────────┘

var schema = new Dictionary<string, (string Type, int? MaxBytes)>
{
    ["UserId"] = ("Int32", 4),
    ["Email"] = ("String", 50),     // Fixed max of 50 bytes
    ["Name"] = ("String", 100),     // Fixed max of 100 bytes
    ["Bio"] = ("String", null),     // Variable - calculate below
};

// Calculation:
int pageDataSize = 4056;  // 4KB page - 40 byte header
int overhead = 0;

// Base: ColumnCount
overhead += 4;

// Column 1: UserId (Int32)
overhead += 4;  // NameLen("UserId" = 6)
overhead += 6;
overhead += 1;  // Type marker
overhead += 4;  // Value

// Column 2: Email (String, max 50 bytes)
overhead += 4;  // NameLen("Email" = 5)
overhead += 5;
overhead += 1;  // Type marker
overhead += 4;  // StringLen
overhead += 50; // Max string data

// Column 3: Name (String, max 100 bytes)
overhead += 4;  // NameLen("Name" = 4)
overhead += 4;
overhead += 1;  // Type marker
overhead += 4;  // StringLen
overhead += 100;// Max string data

// Column 4: Bio (String, remaining)
overhead += 4;  // NameLen("Bio" = 3)
overhead += 3;
overhead += 1;  // Type marker
overhead += 4;  // StringLen

// Available for Bio string:
int maxBioSize = pageDataSize - overhead;  // = 4056 - 192 = 3864 bytes!

Console.WriteLine($"Max Bio string: {maxBioSize} bytes");
// Result: Bio can be up to 3864 bytes (3.8KB)
```

#### Example 2: Larger Records (8KB page)

```csharp
// Same schema, but with 8KB page (8152 bytes available):
int pageDataSize8KB = 8152;
int maxBioSize8KB = pageDataSize8KB - 192;  // = 7960 bytes!

Console.WriteLine($"Max Bio string (8KB page): {maxBioSize8KB} bytes");
// Result: Bio can be up to 7960 bytes (7.96KB)
```

#### Example 3: Complex Schema

```csharp
var complexSchema = new Dictionary<string, (string Type, int? MaxBytes)>
{
    ["Id"] = ("ULID", 26),          // ULID as string: "01ARZ3NDEKTSV4RRFFQ69G5FAV" = 26 bytes
    ["CreatedAt"] = ("DateTime", 8),
    ["UpdatedAt"] = ("DateTime", 8),
    ["Status"] = ("String", 20),    // enum: "ACTIVE", "INACTIVE", etc.
    ["JSON"] = ("String", null),    // Variable - calculate!
};

// Calculation:
int baseOverhead = 4 + (4+2+1+26) + (4+9+1+8) + (4+9+1+8) + (4+6+1+4+20) + (4+4+1+4);
// = 4 + 33 + 22 + 22 + 39 + 13
// = 133 bytes

int maxJsonSize = 4056 - 133;  // = 3923 bytes for JSON!
```

### Implementation: Add to Table Creation API

```csharp
// PROPOSAL: TableSchema with size validation

public class TableSchema
{
    public int PageSize { get; set; }
    public List<ColumnDefinition> Columns { get; set; }
    
    /// <summary>
    /// Validates that all records will fit within page size.
    /// Returns: (maxStringSize for each string column, warnings)
    /// </summary>
    public TableSizeAnalysis AnalyzeSize()
    {
        int maxDataSize = PageSize - 40;  // Header overhead
        int fixedOverhead = CalculateFixedOverhead();
        
        if (fixedOverhead >= maxDataSize)
        {
            throw new InvalidOperationException(
                $"Table schema too large! Fixed overhead ({fixedOverhead}) " +
                $"exceeds page data size ({maxDataSize})");
        }
        
        return new TableSizeAnalysis
        {
            PageSize = PageSize,
            FixedOverhead = fixedOverhead,
            AvailableForStrings = maxDataSize - fixedOverhead,
            StringColumnLimits = CalculateStringLimits(),
        };
    }
}

public class TableSizeAnalysis
{
    public int PageSize { get; set; }
    public int FixedOverhead { get; set; }
    public int AvailableForStrings { get; set; }
    public Dictionary<string, int> StringColumnLimits { get; set; }  // Column name → max bytes
}

// USAGE:
var schema = new TableSchema
{
    PageSize = 4096,
    Columns = new List<ColumnDefinition>
    {
        new("UserId", "Int32"),
        new("Email", "String", maxLength: 50),
        new("Name", "String", maxLength: 100),
        new("Bio", "String"),  // No max - will be calculated
    }
};

var analysis = schema.AnalyzeSize();
Console.WriteLine($"Page size: {analysis.PageSize} bytes");
Console.WriteLine($"Fixed overhead: {analysis.FixedOverhead} bytes");
Console.WriteLine($"Available for strings: {analysis.AvailableForStrings} bytes");
Console.WriteLine();
foreach (var col in analysis.StringColumnLimits)
{
    Console.WriteLine($"{col.Key}: max {col.Value} bytes");
}

// Output:
// Page size: 4096 bytes
// Fixed overhead: 192 bytes
// Available for strings: 3864 bytes
// 
// Email: max 50 bytes
// Name: max 100 bytes
// Bio: max 3714 bytes (remaining)
```

### Practical Decision Tree

When designing your table:

```
Do you have large strings?
│
├─ NO (all < 1KB)
│  └─ Use 4KB page (default) ✅
│
├─ YES, 1-5KB strings
│  └─ Use 8KB page
│
├─ YES, 5-50KB strings  
│  └─ Use 16KB page OR split into multiple records
│
└─ YES, > 50KB strings
   └─ Use external storage (Phase 5 LOB feature)
      OR split into multiple records
```

### Best Practices

**1. Always Calculate BEFORE Creating Table**

```csharp
// BAD: Create table, then discover strings don't fit
var db = new SharpCoreDB();
var usersTable = db.CreateTable("Users");

// GOOD: Calculate first, then create
var analysis = new TableSchema { ... }.AnalyzeSize();
if (analysis.AvailableForStrings < expectedMaxStringSize)
{
    // Use larger page size
}
```

**2. Document Your Schema**

```csharp
// Document the size constraints
public class UserRecord
{
    public int UserId { get; set; }
    
    /// <summary>
    /// Email address. Max 50 bytes (typically 40-50 bytes for realistic emails).
    /// </summary>
    public string Email { get; set; }
    
    /// <summary>
    /// Full name. Max 100 bytes (typically 30-80 bytes for realistic names).
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// Biography text. Max 3714 bytes (based on 4KB page with other columns).
    /// If you need larger biographies, use external storage or increase page size to 8KB (7960 bytes).
    /// </summary>
    public string Bio { get; set; }
}
```

**3. Add Validation**

```csharp
// Validate before insert
public class User
{
    private const int MaxBioBytes = 3714;
    
    public void ValidateForInsert()
    {
        int bioBytes = Encoding.UTF8.GetByteCount(Bio ?? "");
        if (bioBytes > MaxBioBytes)
        {
            throw new ArgumentException(
                $"Bio exceeds max size: {bioBytes} > {MaxBioBytes} bytes");
        }
    }
}
```

**4. Test Edge Cases**

```csharp
[Fact]
public void InsertRecord_WithMaxSizeString_Should_Succeed()
{
    var row = new Dictionary<string, object>
    {
        ["UserId"] = 1,
        ["Email"] = "test@example.com",
        ["Name"] = "John Doe",
        ["Bio"] = new string('X', 3714),  // Max size
    };
    
    // Should succeed
    usersTable.Insert(row);
}

[Fact]
public void InsertRecord_WithOversizeString_Should_Throw()
{
    var row = new Dictionary<string, object>
    {
        ["UserId"] = 1,
        ["Email"] = "test@example.com",
        ["Name"] = "John Doe",
        ["Bio"] = new string('X', 3715),  // One byte over!
    };
    
    // Should throw InvalidOperationException
    Assert.Throws<InvalidOperationException>(() => usersTable.Insert(row));
}
```

---













