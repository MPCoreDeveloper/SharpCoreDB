# SharpCoreDB Binary Format - Visual Reference Guide

## 1. Overall .scdb File Structure

```
SCDB File (Single file for entire database)
┌─────────────────────────────────────────────────────────────┐
│ Byte 0 - 511: SCDB File Header (512 bytes, fixed)          │
│ ├─ Magic: 0x4243445310000000 ("SCDB\x10")                 │
│ ├─ Version, PageSize, Offsets to all regions             │
│ ├─ Encryption/Compression info                           │
│ ├─ Transaction state, checksums                          │
│ └─ Statistics (record count, fragmentation %)            │
├─────────────────────────────────────────────────────────────┤
│ Byte 512 - N: Block Registry                               │
│ ├─ Maps logical block names → physical offsets            │
│ └─ Enables O(1) record lookups                            │
├─────────────────────────────────────────────────────────────┤
│ Free Space Map (FSM)                                        │
│ ├─ 2-level bitmap for page allocation                     │
│ ├─ L1: 1 bit per page (allocated=1, free=0)             │
│ └─ L2: Extent map for large allocations                   │
├─────────────────────────────────────────────────────────────┤
│ Write-Ahead Log (WAL)                                       │
│ ├─ Transaction log entries                                │
│ ├─ Enables crash recovery & ACID guarantees              │
│ └─ Appended to, never random-access                       │
├─────────────────────────────────────────────────────────────┤
│ Table Directory                                             │
│ ├─ Table schemas                                          │
│ ├─ Column definitions                                     │
│ └─ Table metadata                                         │
├─────────────────────────────────────────────────────────────┤
│ Data Pages (Allocated from FSM)                            │
│ ├─ Record blocks scattered throughout file                │
│ ├─ Can be fragmented (normal, FSM handles it)            │
│ └─ No fixed positions                                     │
└─────────────────────────────────────────────────────────────┘

All region offsets defined in Header (bytes 0x20-0x5F)
```

---

## 2. File Header Structure (512 bytes)

```
SCDB File Header Layout:

Offset   Size   Field Name                Value
──────   ────   ────────────────────      ──────────────────────
0x0000   8      Magic                     0x4243445310000000
0x0008   2      FormatVersion             1
0x000A   2      PageSize                  4096 (default)
0x000C   4      HeaderSize                512 (always)

0x0010   1      EncryptionMode            0=None, 1=AES-256-GCM
0x0011   1      CompressionMode           0=None (reserved)
0x0012   2      EncryptionKeyId           Key derivation ID
0x0014   12     Nonce                     AES-GCM nonce

0x0020   8      RegistryRootOffset        Start of root registry block (format v2, issue #345)
0x0028   8      RegistryRootLength        Root registry block size (v2; initial = BlockRegistrySizePages pages, grows by relocation)
0x0030   8      Reserved0                 (format v1: FsmOffset)
0x0038   8      Reserved1                 (format v1: FsmLength)
0x0040   8      WalOffset                 Start of WAL
0x0048   8      WalLength                 WAL size
0x0050   8      TableDirOffset            Start of table dir
0x0058   8      TableDirLength            Table dir size

0x0060   8      LastTransactionId         Last committed txn
0x0068   8      LastCheckpointLsn         LSN at checkpoint
0x0070   8      FileSize                  Total file size
0x0078   8      AllocatedPages            Number of pages

0x0080   32     FileChecksum              SHA-256 of entire file

0x00A0   8      TotalRecords              Total record count
0x00A8   8      TotalDeletes              Deleted record count
0x00B0   8      LastVacuumTime            Unix timestamp
0x00B8   8      FragmentationPercent      0-10000 (0.00%-100.00%)

0x00C0   240    Reserved                  For future extensions

Total: 512 bytes (0x200)
```

---

## 3. Binary Record Format

### Simple Record Example

Input:
```csharp
var row = new Dictionary<string, object>
{
    ["UserId"] = 42,
    ["Name"] = "John Doe",
    ["Active"] = true,
};
```

Binary Output:
```
Offset  Bytes   Meaning
──────  ──────  ──────────────────────────────────────────
0-3     04 00 00 00     ColumnCount = 4 (little-endian int32)

4-7     06 00 00 00     NameLength("UserId") = 6
8-13    55 73 65 72 49 64  "UserId" (UTF-8)
14      01              TypeMarker = 1 (Int32)
15-18   2A 00 00 00     Value = 42 (little-endian int32)

19-22   04 00 00 00     NameLength("Name") = 4
23-26   4E 61 6D 65     "Name" (UTF-8)
27      06              TypeMarker = 6 (String)
28-31   09 00 00 00     StringLength = 9 (length of "John Doe")
32-40   4A 6F 68 6E 20 44 6F 65  "John Doe" (UTF-8)

41-44   06 00 00 00     NameLength("Active") = 6
45-50   41 63 74 69 76 65  "Active" (UTF-8)
51      04              TypeMarker = 4 (Boolean)
52      01              Value = 1 (true)

Total: 83 bytes
```

### Detailed Byte Layout

```
Record with Mixed Types:

┌────────────────────────────────────────────────────┐
│ ColumnCount (4 bytes)                              │
│ ┌──────────────────────────────────────────────┐   │
│ │ 04 00 00 00                                   │   │ Little-endian
│ │ = 4 columns                                   │   │
│ └──────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────┤
│ Column 1: Integer Field                            │
│ ┌──────────────────────────────────────────────┐   │
│ │ NameLen: 02 00 00 00 → 2 bytes                │   │
│ │ Name: 49 64 → "Id"                            │   │ UTF-8
│ │ Type: 01 → Int32                              │   │
│ │ Value: 2A 00 00 00 → 42                       │   │ Little-endian
│ └──────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────┤
│ Column 2: String Field                             │
│ ┌──────────────────────────────────────────────┐   │
│ │ NameLen: 04 00 00 00 → 4 bytes                │   │
│ │ Name: 4E 61 6D 65 → "Name"                    │   │ UTF-8
│ │ Type: 06 → String                             │   │
│ │ StrLen: 04 00 00 00 → 4 bytes                 │   │ Little-endian
│ │ Value: 4A 6F 68 6E → "John"                   │   │ UTF-8
│ └──────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────┤
│ Column 3: Float Field                              │
│ ┌──────────────────────────────────────────────┐   │
│ │ NameLen: 05 00 00 00 → 5 bytes                │   │
│ │ Name: 50 72 69 63 65 → "Price"                │   │ UTF-8
│ │ Type: 03 → Double                             │   │
│ │ Value: 9A 99 99 99 99 99 24 40 → 10.5        │   │ IEEE 754 LE
│ └──────────────────────────────────────────────┘   │
├────────────────────────────────────────────────────┤
│ Column 4: NULL Field                               │
│ ┌──────────────────────────────────────────────┐   │
│ │ NameLen: 07 00 00 00 → 7 bytes                │   │
│ │ Name: 4F 70 74 69 6F 6E 61 6C → "Optional"   │   │ UTF-8
│ │ Type: 00 → Null                               │   │
│ │ Value: (none - 0 bytes)                       │   │
│ └──────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────┘
```

---

## 4. Type Markers Reference

```
Type Marker (1 byte per field)

┌──────────┬──────────┬─────────────────────────┬──────────────┐
│ Marker   │ Type     │ Value Format            │ Total Bytes* │
├──────────┼──────────┼─────────────────────────┼──────────────┤
│ 0x00     │ Null     │ (no value)              │ 1            │
│ 0x01     │ Int32    │ Little-endian int       │ 1 + 4 = 5    │
│ 0x02     │ Int64    │ Little-endian long      │ 1 + 8 = 9    │
│ 0x03     │ Double   │ IEEE 754 double         │ 1 + 8 = 9    │
│ 0x04     │ Boolean  │ 0x00 (false) or 0x01   │ 1 + 1 = 2    │
│ 0x05     │ DateTime │ Ticks (int64)           │ 1 + 8 = 9    │
│ 0x06     │ String   │ [Len:4][UTF-8:N]       │ 1 + 4 + N    │
│ 0x07     │ Bytes    │ [Len:4][Raw:N]         │ 1 + 4 + N    │
│ 0x08     │ Decimal  │ 16-byte decimal128     │ 1 + 16 = 17  │
└──────────┴──────────┴─────────────────────────┴──────────────┘

* Total includes type marker byte
```

---

## 5. String Encoding Example

### Variable-Length String Storage

```
String: "Café" (4 visible characters, 5 UTF-8 bytes)

Breakdown:
  C    → 0x43 (1 byte)
  a    → 0x61 (1 byte)
  f    → 0x66 (1 byte)
  é    → 0xC3 0xA9 (2 bytes in UTF-8)
  Total: 5 bytes

Serialized (with type marker):
┌──────┬──────────────┬────────────────────────────┐
│ 06   │ 05 00 00 00  │ 43 61 66 C3 A9            │
├──────┼──────────────┼────────────────────────────┤
│ Type │ Length = 5   │ "Café" (UTF-8 encoded)    │
│String│ (int32 LE)   │                            │
└──────┴──────────────┴────────────────────────────┘

Note: NO PADDING. Exactly 5 bytes for the string data.
If string was "Cat" (3 chars = 3 bytes), output would be 3 bytes, not 5.
```

### Unicode Handling Examples

```
ASCII String "Hello" (5 chars = 5 bytes):
┌──────┬──────────────┬──────────────────────────┐
│ 06   │ 05 00 00 00  │ 48 65 6C 6C 6F          │
│ Type │ Length = 5   │ "Hello"                  │
└──────┴──────────────┴──────────────────────────┘

Japanese String "日本" (2 chars = 6 bytes in UTF-8):
┌──────┬──────────────┬──────────────────────────────┐
│ 06   │ 06 00 00 00  │ E6 97 A5 E6 9C AC        │
│ Type │ Length = 6   │ "日本"                     │
└──────┴──────────────┴──────────────────────────────┘

Emoji "🚀" (1 char = 4 bytes in UTF-8):
┌──────┬──────────────┬──────────────────────────┐
│ 06   │ 04 00 00 00  │ F0 9F 9A 80             │
│ Type │ Length = 4   │ "🚀"                     │
└──────┴──────────────┴──────────────────────────┘
```

---

## 6. Block Registry Layout

```
Block Registry Format (in-file):

┌─────────────────────────────────────────────────────┐
│ Registry Header (64 bytes)                          │
│ ├─ EntryCount: 8 bytes (int64 LE) = N entries     │
│ ├─ IndexVersion: 8 bytes (version)                │
│ └─ Metadata... (48 bytes reserved)                │
├─────────────────────────────────────────────────────┤
│ Entry 1 (Variable size)                            │
│ ├─ [NameLength: 4]  → 10                          │
│ ├─ [Name: 10]       → "Users_001"                │
│ ├─ [Offset: 8]      → 1,048,576 (byte position)  │
│ ├─ [Length: 8]      → 50 (block size)            │
│ ├─ [Checksum: 32]   → SHA-256 hash               │
│ ├─ [CreatedAt: 8]   → Unix timestamp             │
│ └─ [LastModified:8] → Unix timestamp             │
├─────────────────────────────────────────────────────┤
│ Entry 2, Entry 3, ... Entry N                      │
│ (Same format as Entry 1)                           │
└─────────────────────────────────────────────────────┘

In-Memory Hash Table (loaded from disk):
┌──────────────────────────────────────────┐
│ "Users_001" → BlockEntry {...}           │ O(1) lookup
│ "Users_002" → BlockEntry {...}           │
│ "Orders_001" → BlockEntry {...}          │
│ ...                                      │
└──────────────────────────────────────────┘
```

---

## 7. Free Space Map (FSM) Structure

```
FSM Layout:

┌──────────────────────────────────────────────────────┐
│ FSM Header (64 bytes)                               │
│ ├─ TotalPages: 8 bytes → 1,000,000 pages          │
│ ├─ FreePages: 8 bytes → 500,000 pages             │
│ ├─ LastAllocation: 8 bytes → page 512,345         │
│ └─ Metadata (40 bytes reserved)                    │
├──────────────────────────────────────────────────────┤
│ L1 Bitmap (Variable)                                │
│ ├─ 1 bit per page                                 │
│ ├─ 1 = allocated, 0 = free                        │
│ ├─ Example for 8 pages:                           │
│ │  Bits: 1 1 0 1 0 1 1 1                          │
│ │  Pages: 1 2 3 4 5 6 7 8                         │
│ │  Status: A A F A F A A A (A=allocated, F=free)  │
│ └─ For 1M pages: ~128 KB bitmap                   │
├──────────────────────────────────────────────────────┤
│ L2 Extent Map (Variable)                            │
│ ├─ Each extent: [StartPage:8] [Count:8]           │
│ ├─ Example:                                        │
│ │  [10, 5]       → Pages 10-14 are free           │
│ │  [100, 50]     → Pages 100-149 are free         │
│ │  [1000, 100]   → Pages 1000-1099 are free       │
│ └─ For allocation: Binary search, O(log n)        │
└──────────────────────────────────────────────────────┘

Allocation Algorithm:
1. Need N pages
2. Check L2 extents for contiguous block (O(log n))
3. If found: Mark as allocated, update bitmap
4. If not found: Extend file exponentially
5. Return allocated page number
```

---

## 8. Data Fragmentation Example

```
Real-world file layout (not contiguous):

Byte Position    Block              Size    Status
────────────────────────────────────────────────────
0                 [Header]           512    Fixed
512               [WAL]              1MB    Fixed
1,050,112         [Table Directory]  100KB  Fixed
1,150,112         [Registry block]   4KB    Grows (relocates, issue #345)
1,154,208         [FSM block]        10KB   Grows (relocates, issue #345)

1,310,016         Row_001            50B    Allocated
1,310,066         Row_002            100B   Allocated
1,310,166         [Free]             4000B  Free (FSM: free=0)
1,314,166         Row_003            75B    Allocated
1,314,241         [Free]             4000B  Free (FSM: free=0)
1,318,241         Row_004            200B   Allocated
...and so on

FSM tracks free pages:
┌────────┬──────────┐
│ Pages  │ Status   │
├────────┼──────────┤
│ 0      │ Allocated│ Header
│ 1-10   │ Allocated│ Registry
│ 11-14  │ Allocated│ FSM
│ 15-300 │ Allocated│ WAL + Table Dir
│ 301    │ Allocated│ Row_001 + Row_002
│ 302    │ Free     │ Available for allocation
│ 303    │ Allocated│ Row_003
│ 304    │ Free     │ Available for allocation
│ 305    │ Allocated│ Row_004 + more
└────────┴──────────┘

When allocating 10 bytes:
1. FSM looks for free page (finds page 302 or 304)
2. Allocates there
3. Updates bitmap and L2 extent map
4. Returns page offset to caller
```

---

## 9. Write-Ahead Log (WAL) Entry Format

```
WAL Entry Structure:

┌─────────────┬───────┬──────────┬────────────┬──────────┐
│ TxnId (8B)  │ Typ(1)│ BlockLen │ BlockData  │ CRC32(4) │
└─────────────┴───────┴──────────┴────────────┴──────────┘

Example: INSERT transaction

┌─────────────┬───┬──────────┬──────────────────┬──────────┐
│ 00000000001 │ I │ 00000050 │ [50 byte record] │ ABCD1234 │
│ TxnId=1     │ I │ Length=80│ Binary row data  │ Checksum │
│             │ n │          │                  │          │
│             │ s │          │                  │          │
│             │ e │          │                  │          │
│             │ r │          │                  │          │
│             │ t │          │                  │          │
└─────────────┴───┴──────────┴──────────────────┴──────────┘

Type markers:
  I = Insert
  U = Update (old block + new block)
  D = Delete
  C = Commit marker
  R = Rollback marker

On crash recovery:
1. Read WAL from last checkpoint
2. Replay all committed transactions (marked with C)
3. Ignore uncommitted (no C marker)
4. Restore database to last consistent state
```

---

## 10. Record Lookup Flow (O(1))

```
User query: SELECT * FROM Users WHERE Id = 42

Step 1: Hash lookup
┌──────────────────────────────────────┐
│ Look for block "Users_Row_42"        │
│ ConcurrentDictionary.TryGetValue()   │
│ Time: O(1) average                   │
└──────────────────────────────────────┘
     ↓
Step 2: BlockEntry retrieval
┌──────────────────────────────────────┐
│ BlockEntry entry = registry[...]     │
│ Offset: 1,048,576                    │
│ Length: 50                           │
│ Checksum: [SHA-256]                  │
└──────────────────────────────────────┘
     ↓
Step 3: Memory-mapped read
┌──────────────────────────────────────┐
│ byte[] data = provider.ReadBytes(    │
│   1,048,576,  // offset              │
│   50          // length              │
│ )                                    │
│ Time: O(1) + I/O                     │
└──────────────────────────────────────┘
     ↓
Step 4: Deserialize
┌──────────────────────────────────────┐
│ Dictionary<string,object> row =      │
│   BinaryRowSerializer.Deserialize()  │
│ Time: O(n) where n = column count    │
└──────────────────────────────────────┘
     ↓
Step 5: Return result
┌──────────────────────────────────────┐
│ {                                    │
│   ["Id"]: 42,                        │
│   ["Name"]: "John",                  │
│   ["Active"]: true                   │
│ }                                    │
└──────────────────────────────────────┘
```

---

## 11. String Size Comparison

```
"Name" field with 1,000 records:

Fixed-length approach (255 bytes per field):
┌─────────────────────────────────┐
│ Record 1: "John" + 251 zeros    │ 255 bytes
│ Record 2: "Jane" + 251 zeros    │ 255 bytes
│ Record 3: "Bob"  + 252 zeros    │ 255 bytes
│ ...                             │ × 1,000
│ Total: 255,000 bytes            │
└─────────────────────────────────┘

SharpCoreDB variable-length:
┌────────────────────────────────────────┐
│ Record 1: [04][John]                   │ 8 bytes
│ Record 2: [04][Jane]                   │ 8 bytes
│ Record 3: [03][Bob]                    │ 7 bytes
│ ...                                    │ × 1,000
│ Avg: 8 bytes per record                │
│ Total: ~8,000 bytes                    │
│                                        │
│ Savings: 255,000 - 8,000 = 247,000 B  │
│ Reduction: 96.9% ✅                    │
└────────────────────────────────────────┘
```

---

## 12. File Growth Pattern

```
Initial database: 100 pages (400 KB @ 4KB pages)

Insert 5,000 rows (avg 100 bytes each):

Timeline:
┌──────────────────────────────────────┐
│ Initial: 100 pages                   │ 400 KB
│ ├─ After 1000 rows: Extend +2560 p   │ 10.2 MB
│ ├─ After 2000 rows: Extend +2560 p   │ 20.4 MB
│ ├─ After 3000 rows: Extend +5120 p   │ 40.6 MB
│ ├─ After 4000 rows: Extend +10240 p  │ 81.0 MB
│ └─ After 5000 rows: (no extend)      │ 81.0 MB
└──────────────────────────────────────┘

Extension strategy (Phase 3):
  min_pages = 2560      // 10 MB minimum
  growth = exponential  // 2x, 4x, 8x...

Result:
  - Few allocations (4 extensions for 5000 rows)
  - Pre-allocated pages available for future writes
  - Minimal I/O overhead
  - Predictable growth
```

---

## Summary Cheat Sheet

```
┌─────────────────────────────────────────────────┐
│ SharpCoreDB Binary Format Cheat Sheet           │
├─────────────────────────────────────────────────┤
│                                                 │
│ File Header:           512 bytes (fixed)        │
│ Page size:             4096 bytes (default)     │
│ String encoding:       UTF-8                    │
│ Number encoding:       Little-endian            │
│ Record format:         Self-describing binary   │
│ Record lookup:         O(1) hash table          │
│ String overhead:       4 bytes (length prefix)  │
│ Type info:             1 byte per field         │
│ NULL handling:         Type marker 0x00         │
│ File growth:           Exponential (2x each)    │
│ Free space management: FSM (2-level bitmap)    │
│ Fragmentation:         Normal & handled         │
│ Transaction support:   WAL-based ACID           │
│ Max string size:       2 GB (int32 limit)       │
│ Max file size:         Limited by filesystem    │
│                                                 │
│ Performance:           3x faster than JSON      │
│ Zero allocation:       Using ArrayPool & Span  │
│                                                 │
└─────────────────────────────────────────────────┘
```

---

**Last Updated:** January 2025  
**Phase:** 3.3 Serialization & Storage  
**Status:** Complete visual reference
