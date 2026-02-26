// <copyright file="ScdbFile.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Buffers;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

/// <summary>
/// SCDB single-file storage format implementation.
/// Provides zero-copy, SSD-optimized, memory-mappable storage.
/// C# 14: Uses primary constructors, field keyword, and modern patterns.
/// </summary>
public sealed class ScdbFile : IDisposable
{
    private readonly string _filePath;
    private readonly FileStream _fileStream;
    private readonly MemoryMappedFile? _memoryMappedFile;
    private ScdbFileHeader _header;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScdbFile"/> class.
    /// </summary>
    /// <param name="filePath">Path to the .scdb file</param>
    /// <param name="fileStream">Open file stream</param>
    /// <param name="mmf">Optional memory-mapped file</param>
    /// <param name="header">File header</param>
    private ScdbFile(string filePath, FileStream fileStream, MemoryMappedFile? mmf, ScdbFileHeader header)
    {
        _filePath = filePath;
        _fileStream = fileStream;
        _memoryMappedFile = mmf;
        _header = header;
    }

    /// <summary>
    /// Opens or creates an SCDB file.
    /// C# 14: Static factory method pattern with modern switch expression.
    /// </summary>
    /// <param name="path">Path to the .scdb file</param>
    /// <param name="mode">Open mode flags</param>
    /// <returns>Configured ScdbFile instance</returns>
    public static ScdbFile Open(string path, ScdbOpenMode mode = ScdbOpenMode.ReadWrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Ensure .scdb extension
        if (!path.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
        {
            path += ".scdb";
        }

        // Create new file if requested
        if (mode.HasFlag(ScdbOpenMode.Create) && !File.Exists(path))
        {
            InitializeNewFile(path);
        }

        // Open file stream with appropriate access
        var fileAccess = mode switch
        {
            var m when m.HasFlag(ScdbOpenMode.ReadWrite) => FileAccess.ReadWrite,
            var m when m.HasFlag(ScdbOpenMode.Write) => FileAccess.Write,
            _ => FileAccess.Read
        };

        // ✅ SSD-OPTIMIZED: Use FileOptions.RandomAccess + unbuffered I/O
        var fileStream = new FileStream(
            path,
            FileMode.Open,
            fileAccess,
            FileShare.None,
            bufferSize: 0, // Unbuffered for O_DIRECT-like behavior
            FileOptions.RandomAccess);

        // ✅ ZERO-COPY: Memory-map file for reads if requested
        MemoryMappedFile? mmf = null;
        if (mode.HasFlag(ScdbOpenMode.MemoryMapped))
        {
            mmf = MemoryMappedFile.CreateFromFile(
                fileStream,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: true);
        }

        // Read and validate header
        var header = ReadAndValidateHeader(fileStream);

        return new ScdbFile(path, fileStream, mmf, header);
    }

    /// <summary>
    /// Gets the file header (read-only copy).
    /// </summary>
    public ScdbFileHeader Header => _header;

    /// <summary>
    /// Gets the current file size in bytes.
    /// </summary>
    public long FileSize => _fileStream.Length;

    /// <summary>
    /// Reads a block by name with zero-copy via memory mapping.
    /// Uses pattern matching and ReadOnlySpan for efficiency.
    /// </summary>
    /// <param name="blockName">Block name (e.g., "table:users:data")</param>
    /// <returns>Read-only span of block data, or empty if not found</returns>
    public unsafe ReadOnlySpan<byte> ReadBlock(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);

        // NOTE: Implement block registry lookup
        // For now, return empty span
        return ReadOnlySpan<byte>.Empty;
    }

    /// <summary>
    /// Writes a block to the file with automatic page alignment.
    /// </summary>
    /// <param name="blockName">Block name (e.g., "table:users:data")</param>
    /// <param name="blockType">Block type (see BlockType enum)</param>
    /// <param name="data">Block data to write</param>
    public void WriteBlock(string blockName, BlockType blockType, ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);

        // 1. Allocate pages (page-aligned)
        var pageCount = (data.Length + _header.PageSize - 1) / _header.PageSize;
        var pageOffset = AllocatePages(pageCount);

        // 2. Write data at page-aligned offset
        var offset = pageOffset * _header.PageSize;
        _fileStream.Position = (long)offset;
        _fileStream.Write(data);

        // 3. Compute checksum (stored but not used yet)
        _ = SHA256.HashData(data);

        // NOTE: Register block in block registry
        // NOTE: Log to WAL for crash safety
    }

    /// <summary>
    /// Allocates contiguous pages from the Free Space Map.
    /// Returns the starting page number.
    /// </summary>
    /// <param name="pageCount">Number of pages to allocate</param>
    /// <returns>Starting page number</returns>
    private ulong AllocatePages(int pageCount)
    {
        // NOTE: Implement FSM-based allocation
        // For now, just append to end of file
        var currentPages = (ulong)(_fileStream.Length / _header.PageSize);
        
        // Extend file to allocate new pages
        var newLength = (currentPages + (ulong)pageCount) * _header.PageSize;
        _fileStream.SetLength((long)newLength);

        return currentPages;
    }

    /// <summary>
    /// Aligns an offset to the next page boundary.
    /// </summary>
    /// <param name="offset">Offset to align</param>
    /// <param name="pageSize">Page size</param>
    /// <returns>Aligned offset</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static ulong AlignToPage(ulong offset, ushort pageSize)
    {
        return (offset + pageSize - 1) / pageSize * pageSize;
    }

    /// <summary>
    /// Flushes all dirty data to disk and updates file header.
    /// </summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // NOTE: Flush dirty blocks
        // NOTE: Update header statistics
        // NOTE: Checkpoint WAL

        _fileStream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Performs incremental VACUUM to defragment the file.
    /// </summary>
    /// <param name="maxPagesToMove">Maximum pages to move in this operation</param>
    public void VacuumIncremental(int maxPagesToMove = 1000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // NOTE: Implement incremental VACUUM
        // 1. Scan FSM for fragmentation hotspots
        // 2. Move up to maxPagesToMove to consolidate
        // 3. Update FSM and checkpoint
        _ = maxPagesToMove; // Suppress unused parameter warning
    }

    /// <summary>
    /// Performs full VACUUM to reclaim all free space.
    /// </summary>
    public void VacuumFull()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // NOTE: Implement full VACUUM
        // 1. Create temporary file
        // 2. Copy all live data to new file
        // 3. Replace old file with new file
    }

    /// <summary>
    /// Gets the file path.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Disposes resources (file handles, memory mappings).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            Flush();
        }
        catch
        {
            // Best effort flush
        }
        finally
        {
            _memoryMappedFile?.Dispose();
            _fileStream.Dispose();
            _disposed = true;
        }
    }

    // ==================== PRIVATE HELPER METHODS ====================

    /// <summary>
    /// Initializes a new SCDB file with default structures.
    /// Creates: File header, block registry, FSM, WAL, table directory.
    /// </summary>
    /// <param name="path">Path where file will be created</param>
    private static void InitializeNewFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        // Create default header
        var header = ScdbFileHeader.CreateDefault();
        
        // ✅ Phase 3.3: Enable delta-update feature flag
        header.FeatureFlags |= ScdbFileHeader.FEATURE_DELTA_UPDATES;
        
        // Write header
        Span<byte> buffer = stackalloc byte[(int)ScdbFileHeader.HEADER_SIZE];
        header.WriteTo(buffer);
        fs.Write(buffer);

        // Initialize block registry (empty)
        var registryHeader = new BlockRegistryHeader
        {
            Magic = BlockRegistryHeader.MAGIC,
            Version = BlockRegistryHeader.CURRENT_VERSION,
            BlockCount = 0,
            TotalSize = (ulong)BlockRegistryHeader.SIZE,
            LastModified = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        Span<byte> registryBuffer = stackalloc byte[BlockRegistryHeader.SIZE];
        MemoryMarshal.Write(registryBuffer, in registryHeader);
        fs.Write(registryBuffer);

        // Initialize FSM (empty)
        var fsmHeader = new FreeSpaceMapHeader
        {
            Magic = FreeSpaceMapHeader.MAGIC,
            Version = FreeSpaceMapHeader.CURRENT_VERSION,
            TotalPages = 1, // Just header page
            FreePages = 0,
            LargestExtent = 0,
            BitmapOffset = (uint)FreeSpaceMapHeader.SIZE,
            ExtentMapOffset = (uint)(FreeSpaceMapHeader.SIZE + 128) // 128 bytes for L1 bitmap
        };

        Span<byte> fsmBuffer = stackalloc byte[FreeSpaceMapHeader.SIZE];
        MemoryMarshal.Write(fsmBuffer, in fsmHeader);
        fs.Write(fsmBuffer);

        // Initialize WAL (empty)
        var walHeader = new WalHeader
        {
            Magic = WalHeader.MAGIC,
            Version = WalHeader.CURRENT_VERSION,
            CurrentLsn = 0,
            LastCheckpoint = 0,
            EntrySize = WalHeader.DEFAULT_ENTRY_SIZE,
            MaxEntries = 1024, // 4MB WAL buffer
            HeadOffset = 0,
            TailOffset = 0
        };

        Span<byte> walBuffer = stackalloc byte[WalHeader.SIZE];
        MemoryMarshal.Write(walBuffer, in walHeader);
        fs.Write(walBuffer);

        // Flush to disk
        fs.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Reads and validates the file header.
    /// </summary>
    /// <param name="fs">File stream to read from</param>
    /// <returns>Validated header</returns>
    private static ScdbFileHeader ReadAndValidateHeader(FileStream fs)
    {
        Span<byte> buffer = stackalloc byte[(int)ScdbFileHeader.HEADER_SIZE];
        fs.Position = 0;
        
        var bytesRead = fs.Read(buffer);
        if (bytesRead < ScdbFileHeader.HEADER_SIZE)
        {
            throw new InvalidDataException($"File too small: {bytesRead} bytes");
        }

        var header = ScdbFileHeader.Parse(buffer);

        if (!header.IsValid)
        {
            throw new InvalidDataException(
                $"Invalid SCDB file: magic=0x{header.Magic:X16}, version={header.FormatVersion}");
        }

        return header;
    }
}

/// <summary>
/// Open mode flags for ScdbFile.
/// Modern [Flags] enum with explicit underlying type.
/// </summary>
[Flags]
public enum ScdbOpenMode
{
    /// <summary>Open for reading only</summary>
    Read = 1,
    
    /// <summary>Open for writing only</summary>
    Write = 2,
    
    /// <summary>Open for reading and writing</summary>
    ReadWrite = Read | Write,
    
    /// <summary>Create file if it doesn't exist</summary>
    Create = 4,
    
    /// <summary>Use memory-mapped I/O for zero-copy reads</summary>
    MemoryMapped = 8
}
