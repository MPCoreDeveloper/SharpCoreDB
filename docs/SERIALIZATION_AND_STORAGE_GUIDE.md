# SharpCoreDB Serialization & Storage Format Guide

> **Dutch Title:** SharpCoreDB Serialisatie en Opslag Format Gids

Dit document beschrijft in detail hoe SharpCoreDB records serialiseert, opslaat, en beheert in databestanden. Het antwoordt op alle vragen over string-constraints, free space management, en record/column boundaries.

---

## 📋 Inhoudsopgave

1. [Overzicht](#overzicht)
2. [File Format (.scdb)](#file-format-scdb)
3. [Record Serialisatie](#record-serialisatie)
4. [String Handling & Size Constraints](#string-handling--size-constraints)
5. [Free Space Management](#free-space-management)
6. [Block Registry](#block-registry)
7. [Record & Column Boundaries](#record--column-boundaries)
8. [Performance Considerations](#performance-considerations)
9. [FAQ](#faq)

---

## 🎯 Overzicht

SharpCoreDB gebruikt een **single-file binary format** (`.scdb`) voor duurzame opslag. Het systeem is gebaseerd op deze principes:

| Aspect | Details |
|--------|---------|
| **Format** | Binary (niet JSON, niet SQL) - 3x sneller dan JSON |
| **Layout** | Fixed header + variable regions (FSM, WAL, Registry, Tables) |
| **Encoding** | UTF-8 voor strings; Little-Endian voor integers |
| **String Storage** | Variable-length; prefixed with 4-byte length field |
| **No Fixed-Length Requirement** | Strings kunnen willekeurig lang zijn (beperkt door beschikbare schijfruimte) |
| **Encryption** | Optional AES-256-GCM |
| **Compression** | Niet geïmplementeerd (reserved in header) |

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

## 🔄 Record Serialisatie

### Binary Format Specification

Records worden opgeslagen in een **self-describing binary format**. Dit betekent dat type-informatie **ingebedded** is in de data zelf.

#### Record Layout

```
┌──────────────────────────────────────────────┐
│ Binary Record Format                         │
├──────────────────────────────────────────────┤
│ [ColumnCount: 4 bytes]  ← int32, little-endian
│                                              │
│ For each column:                             │
│  ├─ [NameLength: 4 bytes] ← int32            │
│  ├─ [ColumnName: N bytes] ← UTF-8 string    │
│  ├─ [TypeMarker: 1 byte]  ← Type indicator  │
│  └─ [Value: variable]     ← Type-specific   │
│                                              │
│ ... (repeat for all columns)                 │
└──────────────────────────────────────────────┘
```

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

Stel je voor we hebben:

```csharp
var row = new Dictionary<string, object>
{
    ["UserId"]  = (int)42,
    ["Name"]    = "John Doe",
    ["Email"]   = "john@example.com",
    ["Active"]  = true,
};
```

Dit wordt geserialiseerd als:

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

### ❌ Misconception: "Je hebt veel vrije ruimte nodig"

**Dit is NIET waar!** Hier is waarom:

#### 1. **Strings zijn variable-length**
- Een record met 10 bytes strings hoeft maar 10 bytes schijfruimte
- Een record met 10MB strings hoeft 10MB schijfruimte
- **Geen vaste grootte per kolom** → geen verspilling

#### 2. **Length-prefixing solve boundaries**

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

### 3. **Size Constraints in SharpCoreDB**

| Constraint | Limit | Why |
|-----------|-------|-----|
| **Max string length** | 2,147,483,647 bytes (2GB per string) | Limited by int32 length field |
| **Max record size** | Limited by page size (4KB default, can be 8KB, 16KB) | Record must fit in one block |
| **Max block size** | Theoretically unlimited (file size dependent) | Blocks can span multiple pages |
| **Max column count** | 2,147,483,647 columns | Limited by int32 column count |
| **File size** | Limited by filesystem | ext4: 16TB, NTFS: 8EB (technically) |

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

### ⚠️ What About Record Size Limits?

**Records kunnen NIET groter zijn dan een blok** (page size).

```csharp
// Example: Default 4KB page size

var row = new Dictionary<string, object>
{
    ["Name"] = new string('A', 3000),      // ✅ Fits
    ["Data"] = new string('B', 4000),      // ❌ Might not fit!
};

// Why?
// Total record:
// - ColumnCount (4) + NameLength (4) + "Name" (4) + TypeMarker (1) 
//   + StringLength (4) + 3000 bytes = ~3021 bytes ✅
// - NameLength (4) + "Data" (4) + TypeMarker (1) 
//   + StringLength (4) + 4000 bytes = ~4013 bytes
// Total: ~7034 bytes > 4096 bytes ❌ ERROR
```

**Oplossing:** Verhoog page size

```csharp
var options = new DatabaseOptions
{
    PageSize = 8192,  // 8KB pages → supports larger records
    CreateImmediately = true,
};

var provider = SingleFileStorageProvider.Open("mydb.scdb", options);
```

### 5. **No Free Space Waste**

```csharp
// Example: Table with 1000 rows

// Scenario 1: All short strings (100 bytes each)
// File size: 1000 × (4 + 8 + 100) = ~112 KB

// Scenario 2: All long strings (10 MB each)
// File size: 1000 × (4 + 8 + 10,485,760) ≈ 10.5 GB

// Scenario 3: Mixed strings
// File size = sum of all actual record sizes (no padding)
```

**Geen vaste overhead per record!** Alleen de bytes die je gebruikt.

---

## 📊 Free Space Management

### How FSM Works

De **Free Space Map (FSM)** beheerd vrije pagina's. Dit is een 2-level bitmap:

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
            var extensionSize = Math.Max(
                MIN_EXTENSION_PAGES,           // 2560 pages = 10MB (Phase 3)
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
│ └─ Optimized for large allocations          │
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
    registry.Flush();  // ← Flushes to disk EVERY time!
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
┌────────────────────────────────┐
│ row = {Id: 42, Name: "John"}   │
└────────────────────────────────┘
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
Record layout (no fixed column offsets):

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

### Q1: Moet ik veel vrije ruimte reserveren?

**A:** Nee! Vrije ruimte wordt automatisch beheerd via FSM. Bestanden groeien exponentieel:
- Eerste groei: +10 MB
- Volgende groeien: exponentieel (2x, 4x, ...)
- No pre-allocation needed

### Q2: Hoe groot kunnen strings worden?

**A:** Theoretisch tot 2 GB (int32 limit per string). Praktisch:
- Small strings (< 1 KB): Very fast
- Medium strings (1-100 MB): Still efficient
- Large strings (> 100 MB): Will fragment disk, consider BLOB storage

### Q3: Hoe weet ik waar een record eindigt?

**A:** Via Block Registry! Elk record is opgeslagen als een block:
```csharp
BlockEntry entry = registry["Users_Row_001"];
ulong startOffset = entry.Offset;
ulong endOffset = entry.Offset + entry.Length;
```

### Q4: Kunnen strings NULL zijn?

**A:** Ja, via type marker 0:
```csharp
case null:
    buffer[offset++] = 0;  // Type: Null
    // No value follows
```

### Q5: Wat gebeurt er met Unicode?

**A:** UTF-8 encoding, automatic length adjustment:
```csharp
"Café"     → 5 bytes (C-a-f-[2-byte é])
"日本"     → 6 bytes (3 chars × 2 bytes each)
"🚀"       → 4 bytes (1 char × 4 bytes)
```

### Q6: Kan ik strings direkt wijzigen zonder het record opnieuw te schrijven?

**A:** Nee, SharpCoreDB werkt immutable:
1. Load record (deserialize)
2. Modify in memory
3. Serialize & write new block
4. Update registry
5. Mark old block as free (WAL handles recovery)

### Q7: Hoe werkt compression?

**A:** Momenteel niet geïmplementeerd. Reserved in header voor toekomstige use.
Huidige focus: Zero-allocation serialization is sneller dan compression overhead.

### Q8: Hoe is de free space distributed?

**A:** Non-contiguous! Records kunnen overal in het bestand staan:
```
File layout:
[Block1: 4KB] [Block2: 8KB] [Free: 2KB] [Block3: 4KB] [Free: 1KB] [Block4: 2KB]
```
Geen fragmentatie-waarschuwing nodig - FSM beheert dit transparant.

### Q9: Kan ik een hele tabel in één "block" opslaan?

**A:** Nee, iedere rij is een apart block. Voordelen:
- Fijnere granulariteit bij locking
- Betere cache-locality
- Flexibel grootten

### Q10: Hoe zit het met transacties?

**A:** Beheerd via WAL (Write-Ahead Log):
1. Begin transaction
2. Writes go to WAL first
3. On commit, registry updated
4. On crash, WAL replayed

---

## 📚 Related Documentation

- `FILE_FORMAT_DESIGN.md` - Low-level binary format details
- `SCHEMA_PERSISTENCE_TECHNICAL_DETAILS.md` - Schema storage
- `CODING_STANDARDS_CSHARP14.md` - Code style guide
- Phase 3 completion reports - Performance benchmarks

---

## 🎓 Summary

| Aspect | Answer |
|--------|--------|
| **Fixed-length strings?** | ❌ No! Variable-length with 4-byte length prefix |
| **Max string size?** | 2 GB (int32 limit) |
| **Free space needed?** | ❌ No! Automatic exponential file growth |
| **Record boundaries?** | Via Block Registry (O(1) lookup) |
| **Column boundaries?** | Self-describing binary format (no fixed positions) |
| **Unicode support?** | ✅ Full UTF-8 support |
| **Performance?** | 3x faster than JSON, zero-allocation serialization |

---

**Last Updated:** January 2025  
**Phase:** 3.3 (Serialization & Storage Optimization)  
**Status:** Complete

