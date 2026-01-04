// <copyright file="ScdbStructures.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

/// <summary>
/// SCDB file header structure (512 bytes, fixed size).
/// Uses C# 14 struct for zero-allocation parsing.
/// All offsets are 64-bit for files greater than 4GB.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ScdbFileHeader
{
    // === Core Identification (16 bytes) ===
    
    /// <summary>Magic number for SCDB format validation: "SCDB\x10\x00\x00\x00"</summary>
    public ulong Magic;           // 0x0000: Magic + version embedded
    
    /// <summary>Format version number (1 for this version)</summary>
    public ushort FormatVersion;  // 0x0008: Format version
    
    /// <summary>Page size in bytes (default: 4096)</summary>
    public ushort PageSize;       // 0x000A: Page size
    
    /// <summary>Header size in bytes (always 512)</summary>
    public uint HeaderSize;       // 0x000C: Header size

    // === Encryption & Compression (16 bytes) ===
    
    /// <summary>Encryption mode: 0=None, 1=AES-256-GCM</summary>
    public byte EncryptionMode;   // 0x0010: Encryption mode
    
    /// <summary>Compression mode: 0=None (always 0 for .scdb)</summary>
    public byte CompressionMode;  // 0x0011: Compression (unused)
    
    /// <summary>Key derivation ID for encryption</summary>
    public ushort EncryptionKeyId;// 0x0012: Key derivation ID
    
    /// <summary>AES-GCM nonce (12 bytes) if encrypted</summary>
    public unsafe fixed byte Nonce[12];  // 0x0014: Nonce

    // === Block Offsets (64 bytes) ===
    
    /// <summary>Offset to block registry</summary>
    public ulong BlockRegistryOffset;  // 0x0020: Block registry offset
    
    /// <summary>Size of block registry in bytes</summary>
    public ulong BlockRegistryLength;  // 0x0028: Block registry size
    
    /// <summary>Offset to Free Space Map (FSM)</summary>
    public ulong FsmOffset;            // 0x0030: FSM offset
    
    /// <summary>Size of FSM in bytes</summary>
    public ulong FsmLength;            // 0x0038: FSM size
    
    /// <summary>Offset to Write-Ahead Log (WAL)</summary>
    public ulong WalOffset;            // 0x0040: WAL offset
    
    /// <summary>Size of WAL in bytes</summary>
    public ulong WalLength;            // 0x0048: WAL size
    
    /// <summary>Offset to table directory</summary>
    public ulong TableDirOffset;       // 0x0050: Table directory offset
    
    /// <summary>Size of table directory in bytes</summary>
    public ulong TableDirLength;       // 0x0058: Table directory size

    // === Transaction State (32 bytes) ===
    
    /// <summary>Last committed transaction ID</summary>
    public ulong LastTransactionId;    // 0x0060: Last committed txn ID
    
    /// <summary>Last checkpoint Log Sequence Number</summary>
    public ulong LastCheckpointLsn;    // 0x0068: Last checkpoint LSN
    
    /// <summary>Total file size in bytes</summary>
    public ulong FileSize;             // 0x0070: Total file size
    
    /// <summary>Number of allocated pages</summary>
    public ulong AllocatedPages;       // 0x0078: Allocated page count

    // === Integrity (32 bytes) ===
    
    /// <summary>SHA-256 checksum of entire file (excluding this field)</summary>
    public unsafe fixed byte FileChecksum[32];// 0x0080: File checksum

    // === Statistics (32 bytes) ===
    
    /// <summary>Total number of records across all tables</summary>
    public ulong TotalRecords;         // 0x00A0: Total record count
    
    /// <summary>Total number of deleted records (candidates for VACUUM)</summary>
    public ulong TotalDeletes;         // 0x00A8: Deleted record count
    
    /// <summary>Unix timestamp (microseconds) of last VACUUM operation</summary>
    public ulong LastVacuumTime;       // 0x00B0: Last VACUUM timestamp
    
    /// <summary>Fragmentation percentage (0-10000 = 0.00% - 100.00%)</summary>
    public ulong FragmentationPercent; // 0x00B8: Fragmentation %

    // === Reserved (240 bytes) ===
    
    /// <summary>Reserved for future extensions</summary>
    public unsafe fixed byte Reserved[240];   // 0x00C0: Reserved space

    // Total: 512 bytes (0x200)

    /// <summary>Magic number constant: "SCDB\x10\x00\x00\x00"</summary>
    public const ulong MAGIC = 0x0000_0010_4244_4353;
    
    /// <summary>Current format version</summary>
    public const ushort CURRENT_VERSION = 1;
    
    /// <summary>Header size in bytes</summary>
    public const uint HEADER_SIZE = 512;
    
    /// <summary>Default page size (4KB)</summary>
    public const ushort DEFAULT_PAGE_SIZE = 4096;

    /// <summary>
    /// Validates the header magic and version.
    /// </summary>
    public readonly bool IsValid => Magic == MAGIC && FormatVersion == CURRENT_VERSION;

    /// <summary>
    /// Zero-allocation parser from ReadOnlySpan using MemoryMarshal.
    /// </summary>
    /// <param name="data">Input data span</param>
    /// <returns>Parsed header structure</returns>
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

    /// <summary>
    /// Creates a new default header for file initialization.
    /// </summary>
    /// <param name="pageSize">Page size in bytes</param>
    /// <returns>Initialized header</returns>
    public static ScdbFileHeader CreateDefault(ushort pageSize = DEFAULT_PAGE_SIZE)
    {
        return new ScdbFileHeader
        {
            Magic = MAGIC,
            FormatVersion = CURRENT_VERSION,
            PageSize = pageSize,
            HeaderSize = HEADER_SIZE,
            EncryptionMode = 0,
            CompressionMode = 0,
            EncryptionKeyId = 0,
            BlockRegistryOffset = HEADER_SIZE,
            BlockRegistryLength = 0,
            FsmOffset = 0,
            FsmLength = 0,
            WalOffset = 0,
            WalLength = 0,
            TableDirOffset = 0,
            TableDirLength = 0,
            LastTransactionId = 0,
            LastCheckpointLsn = 0,
            FileSize = HEADER_SIZE,
            AllocatedPages = 1,
            TotalRecords = 0,
            TotalDeletes = 0,
            LastVacuumTime = 0,
            FragmentationPercent = 0
        };
    }

    /// <summary>
    /// Serializes header to byte buffer for writing.
    /// </summary>
    /// <param name="destination">Destination buffer</param>
    public readonly unsafe void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HEADER_SIZE)
            throw new ArgumentException($"Buffer too small: {destination.Length} < {HEADER_SIZE}");

        fixed (byte* destPtr = destination)
        fixed (ScdbFileHeader* srcPtr = &this)
        {
            Buffer.MemoryCopy(srcPtr, destPtr, HEADER_SIZE, HEADER_SIZE);
        }
    }
}

/// <summary>
/// Block registry header (64 bytes).
/// Describes the structure of the block index.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlockRegistryHeader
{
    /// <summary>Magic number: "BREG" (0x47455242)</summary>
    public uint Magic;            // 0x00: Magic
    
    /// <summary>Registry version number</summary>
    public uint Version;          // 0x04: Version
    
    /// <summary>Number of block entries</summary>
    public ulong BlockCount;      // 0x08: Block count
    
    /// <summary>Total size of registry including header</summary>
    public ulong TotalSize;       // 0x10: Total size
    
    /// <summary>Last modification time (Unix microseconds)</summary>
    public ulong LastModified;    // 0x18: Last modified timestamp
    
    /// <summary>Reserved for future use</summary>
    public unsafe fixed byte Reserved[32]; // 0x20: Reserved

    /// <summary>Magic number constant "BREG"</summary>
    public const uint MAGIC = 0x4745_5242;
    
    /// <summary>Current registry version</summary>
    public const uint CURRENT_VERSION = 1;
    
    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 64;

    /// <summary>
    /// Validates the registry header.
    /// </summary>
    public readonly bool IsValid => Magic == MAGIC && Version == CURRENT_VERSION;

    /// <summary>
    /// Parses registry header from byte span.
    /// </summary>
    /// <param name="data">Input data span</param>
    /// <returns>Parsed registry header</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BlockRegistryHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < SIZE)
            throw new InvalidDataException($"Registry header too small: {data.Length} < {SIZE}");

        return MemoryMarshal.Read<BlockRegistryHeader>(data);
    }
}

/// <summary>
/// Block entry (64 bytes per entry).
/// Describes a single data block in the file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlockEntry
{
    // === Identification (36 bytes) ===
    
    /// <summary>
    /// Block name (UTF-8, null-terminated, max 31 chars + null).
    /// Format: "namespace:identifier:subtype"
    /// Examples: "table:users:data", "table:users:index:pk"
    /// </summary>
    public unsafe fixed byte Name[32];   // 0x00: Block name
    
    /// <summary>Block type enum (see BlockType)</summary>
    public uint BlockType;        // 0x20: Block type

    // === Location (16 bytes) ===
    
    /// <summary>Byte offset in file (page-aligned)</summary>
    public ulong Offset;          // 0x24: File offset
    
    /// <summary>Length in bytes (multiple of page size)</summary>
    public ulong Length;          // 0x2C: Block length

    // === Integrity (36 bytes) ===
    
    /// <summary>SHA-256 checksum of block data</summary>
    public unsafe fixed byte Checksum[32]; // 0x34: Checksum
    
    /// <summary>Block flags (see BlockFlags enum)</summary>
    public uint Flags;            // 0x54: Flags

    // === Reserved (8 bytes) ===
    
    /// <summary>Reserved for future use</summary>
    public ulong Reserved;        // 0x58: Reserved

    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 64;
    
    /// <summary>Maximum name length (excluding null terminator)</summary>
    public const int MAX_NAME_LENGTH = 31;

    /// <summary>
    /// Gets the block name as a string using zero-allocation UTF-8 decoding.
    /// </summary>
    /// <returns>Block name string</returns>
    public unsafe string GetName()
    {
        fixed (byte* ptr = Name)
        {
            var span = new ReadOnlySpan<byte>(ptr, 32);
            var nullIndex = span.IndexOf((byte)0);
            if (nullIndex >= 0)
                span = span[..nullIndex];
            
            return System.Text.Encoding.UTF8.GetString(span);
        }
    }

    /// <summary>
    /// Creates a new BlockEntry with the specified name using zero-allocation UTF-8 encoding.
    /// </summary>
    /// <param name="name">Block name to set</param>
    /// <param name="template">Template entry to copy from</param>
    /// <returns>New entry with name set</returns>
    public static unsafe BlockEntry WithName(string name, BlockEntry template)
    {
        if (name.Length > MAX_NAME_LENGTH)
            throw new ArgumentException($"Block name too long: {name.Length} > {MAX_NAME_LENGTH}");

        var result = template;
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        
        var nameSpan = new Span<byte>(result.Name, 32);
        nameSpan.Clear();
        nameBytes.CopyTo(nameSpan);

        return result;
    }

    /// <summary>
    /// Validates the block checksum using SHA256.
    /// </summary>
    /// <param name="blockData">Block data to validate</param>
    /// <returns>True if checksum matches</returns>
    public unsafe bool ValidateChecksum(ReadOnlySpan<byte> blockData)
    {
        var computedHash = SHA256.HashData(blockData);
        
        fixed (byte* checksumPtr = Checksum)
        {
            var storedHash = new ReadOnlySpan<byte>(checksumPtr, 32);
            return storedHash.SequenceEqual(computedHash);
        }
    }

    /// <summary>
    /// Parses block entry from byte span.
    /// </summary>
    /// <param name="data">Input data span</param>
    /// <returns>Parsed block entry</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BlockEntry Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < SIZE)
            throw new InvalidDataException($"Block entry too small: {data.Length} < {SIZE}");

        return MemoryMarshal.Read<BlockEntry>(data);
    }
}

/// <summary>
/// Block types for namespacing.
/// </summary>
public enum BlockType : uint
{
    /// <summary>Unknown block type</summary>
    Unknown = 0,
    
    /// <summary>Table data block (table:*:data)</summary>
    TableData = 1,
    
    /// <summary>Index data block (table:*:index:*)</summary>
    IndexData = 2,
    
    /// <summary>Table metadata block (table:*:meta)</summary>
    TableMeta = 3,
    
    /// <summary>Free Space Map (sys:fsm)</summary>
    FreeSpaceMap = 4,
    
    /// <summary>Write-Ahead Log (sys:wal)</summary>
    WriteAheadLog = 5,
    
    /// <summary>Table directory (sys:tabledir)</summary>
    TableDirectory = 6,
    
    /// <summary>BLOB data (blob:*)</summary>
    BlobData = 7,
    
    /// <summary>Temporary data (temp:*)</summary>
    TemporaryData = 8
}

/// <summary>
/// Block state flags.
/// Note: Named BlockState to avoid S2344 warning about Flags suffix.
/// </summary>
[Flags]
#pragma warning disable S2344 // Enumeration type names should not have "Flags" or "Enum" suffixes - intentional for [Flags] enum
public enum BlockFlags : uint
#pragma warning restore S2344
{
    /// <summary>No flags set</summary>
    None = 0,
    
    /// <summary>Block has uncommitted changes</summary>
    Dirty = 1 << 0,
    
    /// <summary>Block is compressed (reserved, always 0 for .scdb)</summary>
    Compressed = 1 << 1,
    
    /// <summary>Block is encrypted with AES-256-GCM</summary>
    Encrypted = 1 << 2,
    
    /// <summary>Block marked for deletion (VACUUM target)</summary>
    Deleted = 1 << 3,
    
    /// <summary>Block is read-only (system blocks)</summary>
    Immutable = 1 << 4,
    
    /// <summary>Block has sparse allocation (holes)</summary>
    Sparse = 1 << 5
}

/// <summary>
/// Free Space Map header (64 bytes).
/// Provides O(1) page allocation via two-level bitmap.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FreeSpaceMapHeader
{
    /// <summary>Magic number: "FSM\0" (0x004D5346)</summary>
    public uint Magic;            // 0x00: Magic
    
    /// <summary>FSM version</summary>
    public uint Version;          // 0x04: Version
    
    /// <summary>Total pages in file</summary>
    public ulong TotalPages;      // 0x08: Total pages
    
    /// <summary>Number of free pages</summary>
    public ulong FreePages;       // 0x10: Free pages
    
    /// <summary>Largest contiguous free extent (pages)</summary>
    public ulong LargestExtent;   // 0x18: Largest extent
    
    /// <summary>Offset to L1 bitmap (relative to FSM start)</summary>
    public uint BitmapOffset;     // 0x20: L1 bitmap offset
    
    /// <summary>Offset to L2 extent map (relative to FSM start)</summary>
    public uint ExtentMapOffset;  // 0x24: L2 extent map offset
    
    /// <summary>Reserved for future use</summary>
    public unsafe fixed byte Reserved[24]; // 0x28: Reserved

    /// <summary>Magic number constant "FSM\0"</summary>
    public const uint MAGIC = 0x004D_5346;
    
    /// <summary>Current FSM version</summary>
    public const uint CURRENT_VERSION = 1;
    
    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 64;

    /// <summary>
    /// Validates the FSM header.
    /// </summary>
    public readonly bool IsValid => Magic == MAGIC && Version == CURRENT_VERSION;
}

/// <summary>
/// Free extent descriptor (16 bytes).
/// Used in L2 extent map for large allocations.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct FreeExtent
{
    /// <summary>Starting page number</summary>
    public readonly ulong StartPage;       // 0x00: Start page
    
    /// <summary>Length in pages</summary>
    public readonly ulong Length;          // 0x08: Extent length

    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 16;

    /// <summary>
    /// Initializes a new instance of FreeExtent.
    /// </summary>
    /// <param name="startPage">Starting page number</param>
    /// <param name="length">Length in pages</param>
    public FreeExtent(ulong startPage, ulong length)
    {
        StartPage = startPage;
        Length = length;
    }

    /// <summary>
    /// Checks if this extent can fit the requested number of pages.
    /// </summary>
    /// <param name="requestedPages">Number of pages requested</param>
    /// <returns>True if extent is large enough</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanFit(ulong requestedPages) => Length >= requestedPages;
}

/// <summary>
/// Write-Ahead Log header (64 bytes).
/// Manages circular buffer of WAL entries for crash recovery.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WalHeader
{
    /// <summary>Magic number: "WAL\0" (0x004C4157)</summary>
    public uint Magic;            // 0x00: Magic
    
    /// <summary>WAL version</summary>
    public uint Version;          // 0x04: Version
    
    /// <summary>Current Log Sequence Number</summary>
    public ulong CurrentLsn;      // 0x08: Current LSN
    
    /// <summary>LSN of last checkpoint</summary>
    public ulong LastCheckpoint;  // 0x10: Last checkpoint
    
    /// <summary>Size of each WAL entry (default: 4096)</summary>
    public uint EntrySize;        // 0x18: Entry size
    
    /// <summary>Maximum entries in circular buffer</summary>
    public uint MaxEntries;       // 0x1C: Max entries
    
    /// <summary>Offset to oldest entry</summary>
    public ulong HeadOffset;      // 0x20: Head offset
    
    /// <summary>Offset to newest entry</summary>
    public ulong TailOffset;      // 0x28: Tail offset
    
    /// <summary>Reserved for future use</summary>
    public unsafe fixed byte Reserved[16]; // 0x30: Reserved

    /// <summary>Magic number constant "WAL\0"</summary>
    public const uint MAGIC = 0x004C_4157;
    
    /// <summary>Current WAL version</summary>
    public const uint CURRENT_VERSION = 1;
    
    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 64;
    
    /// <summary>Default entry size (one page)</summary>
    public const uint DEFAULT_ENTRY_SIZE = 4096;

    /// <summary>
    /// Validates the WAL header.
    /// </summary>
    public readonly bool IsValid => Magic == MAGIC && Version == CURRENT_VERSION;
}

/// <summary>
/// Write-Ahead Log entry (4096 bytes = 1 page).
/// Contains transaction operation for crash recovery.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WalEntry
{
    /// <summary>Log sequence number</summary>
    public ulong Lsn;             // 0x00: LSN
    
    /// <summary>Transaction ID</summary>
    public ulong TransactionId;   // 0x08: Transaction ID
    
    /// <summary>Timestamp (Unix microseconds)</summary>
    public ulong Timestamp;       // 0x10: Timestamp
    
    /// <summary>Operation type (see WalOperation)</summary>
    public ushort Operation;      // 0x18: Operation
    
    /// <summary>Block registry index</summary>
    public ushort BlockIndex;     // 0x1A: Block index
    
    /// <summary>Page ID within block</summary>
    public ulong PageId;          // 0x1C: Page ID
    
    /// <summary>Length of data payload</summary>
    public ushort DataLength;     // 0x24: Data length
    
    /// <summary>SHA-256 checksum of entry</summary>
    public unsafe fixed byte Checksum[32]; // 0x26: Checksum
    
    /// <summary>Payload data (before/after images)</summary>
    public unsafe fixed byte Data[4000]; // 0x46: Payload data

    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 4096;
    
    /// <summary>Maximum payload data length</summary>
    public const int MAX_DATA_LENGTH = 4000;

    /// <summary>
    /// Validates entry checksum using SHA256.
    /// </summary>
    /// <returns>True if checksum matches</returns>
    public unsafe bool ValidateChecksum()
    {
        const int headerSize = 38; // Up to checksum field
        var dataSize = DataLength;
        
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        
        // Hash header fields up to checksum
        fixed (WalEntry* entryPtr = &this)
        {
            sha256.AppendData(new ReadOnlySpan<byte>((byte*)entryPtr, headerSize));
        }
        
        // Hash data payload
        fixed (byte* dataPtr = Data)
        {
            sha256.AppendData(new ReadOnlySpan<byte>(dataPtr, dataSize));
        }
        
        var computedHash = sha256.GetHashAndReset();
        
        fixed (byte* checksumPtr = Checksum)
        {
            var storedHash = new ReadOnlySpan<byte>(checksumPtr, 32);
            return storedHash.SequenceEqual(computedHash);
        }
    }
}

/// <summary>
/// WAL operation types.
/// </summary>
public enum WalOperation : ushort
{
    /// <summary>Insert operation</summary>
    Insert = 1,
    
    /// <summary>Update operation</summary>
    Update = 2,
    
    /// <summary>Delete operation</summary>
    Delete = 3,
    
    /// <summary>Checkpoint marker</summary>
    Checkpoint = 4,
    
    /// <summary>Transaction begin</summary>
    TransactionBegin = 5,
    
    /// <summary>Transaction commit</summary>
    TransactionCommit = 6,
    
    /// <summary>Transaction abort</summary>
    TransactionAbort = 7,
    
    /// <summary>Page allocation</summary>
    PageAllocate = 8,
    
    /// <summary>Page free</summary>
    PageFree = 9
}

/// <summary>
/// Table directory header (64 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TableDirectoryHeader
{
    /// <summary>Magic number: "TDIR" (0x52494454)</summary>
    public uint Magic;            // 0x00: Magic
    
    /// <summary>Directory version</summary>
    public uint Version;          // 0x04: Version
    
    /// <summary>Number of tables</summary>
    public uint TableCount;       // 0x08: Table count
    
    /// <summary>Reserved for future use</summary>
    public uint Reserved;         // 0x0C: Reserved

    /// <summary>Magic number constant "TDIR"</summary>
    public const uint MAGIC = 0x5249_4454;
    
    /// <summary>Current directory version</summary>
    public const uint CURRENT_VERSION = 1;
    
    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 16;

    /// <summary>
    /// Validates the header magic and version.
    /// </summary>
    public readonly bool IsValid => Magic == MAGIC && Version == CURRENT_VERSION;
}

/// <summary>
/// Table metadata entry (256 bytes).
/// Describes a single table in the database.
/// Extended to include full schema information.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TableMetadataEntry
{
    /// <summary>Table name (UTF-8, null-terminated, max 63 chars)</summary>
    public unsafe fixed byte TableName[64]; // 0x00: Table name
    
    /// <summary>Table ID (hash of table name)</summary>
    public uint TableId;             // 0x40: Table ID
    
    /// <summary>Offset to data block</summary>
    public ulong DataBlockOffset;    // 0x44: Data block offset
    
    /// <summary>Offset to primary key index block</summary>
    public ulong PrimaryKeyIndexOffset; // 0x4C: PK index block offset
    
    /// <summary>Total record count</summary>
    public ulong RecordCount;        // 0x54: Record count
    
    /// <summary>Number of columns</summary>
    public uint ColumnCount;         // 0x5C: Column count
    
    /// <summary>Primary key column index (-1 if no PK)</summary>
    public int PrimaryKeyIndex;      // 0x60: Primary key column index
    
    /// <summary>Storage mode (0=Columnar, 1=PageBased)</summary>
    public byte StorageMode;         // 0x64: Storage mode
    
    /// <summary>Table flags</summary>
    public byte Flags;               // 0x65: Flags
    
    /// <summary>Number of hash indexes</summary>
    public uint HashIndexCount;      // 0x66: Hash index count
    
    /// <summary>Number of B-tree indexes</summary>
    public uint BTreeIndexCount;     // 0x6A: B-tree index count
    
    /// <summary>Offset to column definitions block</summary>
    public ulong ColumnDefsOffset;   // 0x6E: Column definitions offset
    
    /// <summary>Offset to index definitions block</summary>
    public ulong IndexDefsOffset;    // 0x76: Index definitions offset
    
    /// <summary>Creation timestamp (Unix microseconds)</summary>
    public ulong CreatedTime;        // 0x7E: Creation timestamp
    
    /// <summary>Last modification timestamp</summary>
    public ulong ModifiedTime;       // 0x86: Last modified timestamp
    
    /// <summary>Reserved for future use</summary>
    public unsafe fixed byte Reserved[26];  // 0x8E: Reserved

    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 256;
    
    /// <summary>Maximum table name length</summary>
    public const int MAX_TABLE_NAME_LENGTH = 63;
}

/// <summary>
/// Column definition entry (64 bytes).
/// Describes a single column in a table.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ColumnDefinitionEntry
{
    /// <summary>Column name (UTF-8, null-terminated, max 31 chars)</summary>
    public unsafe fixed byte ColumnName[32]; // 0x00: Column name
    
    /// <summary>Data type (see DataType enum)</summary>
    public uint DataType;           // 0x20: Data type
    
    /// <summary>Column flags (auto, not null, etc.)</summary>
    public uint Flags;              // 0x24: Column flags
    
    /// <summary>Default value length (0 if no default)</summary>
    public uint DefaultValueLength; // 0x28: Default value length
    
    /// <summary>Check constraint length (0 if no check)</summary>
    public uint CheckLength;        // 0x2C: Check constraint length

    /// <summary>Structure size in bytes (fixed part)</summary>
    public const int FIXED_SIZE = 48;
    
    /// <summary>Maximum column name length</summary>
    public const int MAX_COLUMN_NAME_LENGTH = 31;
}

/// <summary>
/// Index definition entry (128 bytes).
/// Describes a single index on a table.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IndexDefinitionEntry
{
    /// <summary>Index name (UTF-8, null-terminated, max 63 chars)</summary>
    public unsafe fixed byte IndexName[64]; // 0x00: Index name
    
    /// <summary>Index type (0=Hash, 1=BTree)</summary>
    public uint IndexType;          // 0x40: Index type
    
    /// <summary>Number of columns in index</summary>
    public uint ColumnCount;        // 0x44: Column count
    
    /// <summary>Index flags (unique, etc.)</summary>
    public uint Flags;              // 0x48: Index flags
    
    /// <summary>Offset to index data block</summary>
    public ulong IndexDataOffset;   // 0x4C: Index data offset
    
    /// <summary>Column indexes (up to 16 columns)</summary>
    public unsafe fixed int ColumnIndexes[16]; // 0x54: Column indexes
    
    /// <summary>Reserved for future use</summary>
    public unsafe fixed byte Reserved[16]; // 0x94: Reserved

    /// <summary>Structure size in bytes</summary>
    public const int SIZE = 128;
    
    /// <summary>Maximum index name length</summary>
    public const int MAX_INDEX_NAME_LENGTH = 63;
    
    /// <summary>Maximum columns per index</summary>
    public const int MAX_COLUMNS = 16;
}

/// <summary>
/// Table flags enumeration.
/// </summary>
[Flags]
public enum TableFlags : byte
{
    /// <summary>No flags</summary>
    None = 0,
    
    /// <summary>Table has been modified</summary>
    Dirty = 1 << 0,
    
    /// <summary>Table is temporary</summary>
    Temporary = 1 << 1,
    
    /// <summary>Table is read-only</summary>
    ReadOnly = 1 << 2
}

/// <summary>
/// Column flags enumeration.
/// </summary>
[Flags]
public enum ColumnFlags : uint
{
    /// <summary>No flags</summary>
    None = 0,
    
    /// <summary>Column is auto-increment</summary>
    AutoIncrement = 1 << 0,
    
    /// <summary>Column is NOT NULL</summary>
    NotNull = 1 << 1,
    
    /// <summary>Column is UNIQUE</summary>
    Unique = 1 << 2,
    
    /// <summary>Column is PRIMARY KEY</summary>
    PrimaryKey = 1 << 3,
    
    /// <summary>Column has a default value</summary>
    HasDefault = 1 << 4,
    
    /// <summary>Column has a check constraint</summary>
    HasCheck = 1 << 5
}

/// <summary>
/// Index flags enumeration.
/// </summary>
[Flags]
public enum IndexFlags : uint
{
    /// <summary>No flags</summary>
    None = 0,
    
    /// <summary>Index enforces uniqueness</summary>
    Unique = 1 << 0,
    
    /// <summary>Index is clustered</summary>
    Clustered = 1 << 1,
    
    /// <summary>Index is disabled</summary>
    Disabled = 1 << 2
}
