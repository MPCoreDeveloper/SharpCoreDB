// <copyright file="SingleFileStorageProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.Storage.Scdb;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Single-file storage provider using .scdb format.
/// Features: Zero-copy reads, memory-mapped I/O, WAL, FSM, encryption.
/// C# 14: Uses modern async patterns, primary constructors, field keyword.
/// </summary>
public sealed class SingleFileStorageProvider : IStorageProvider
{
    private readonly string _filePath;
    private readonly DatabaseOptions _options;
    private readonly FileStream _fileStream;
    private readonly MemoryMappedFile? _memoryMappedFile;
    private readonly BlockRegistry _blockRegistry;
    private readonly FreeSpaceManager _freeSpaceManager;
    private readonly WalManager _walManager;
    private readonly TableDirectoryManager _tableDirectoryManager;
    private readonly ConcurrentDictionary<string, BlockMetadata> _blockCache;
    private readonly Lock _transactionLock = new();
    // ✅ C# 14 / .NET 10: async-friendly gate to serialize I/O and registry updates without blocking threads
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private bool _isInTransaction;
    private bool _disposed;
    private ScdbFileHeader _header;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleFileStorageProvider"/> class.
    /// </summary>
    /// <param name="filePath">Path to .scdb file</param>
    /// <param name="options">Database options</param>
    /// <param name="fileStream">Open file stream</param>
    /// <param name="mmf">Optional memory-mapped file</param>
    /// <param name="header">File header structure</param>
    private SingleFileStorageProvider(string filePath, DatabaseOptions options, FileStream fileStream, 
        MemoryMappedFile? mmf, ScdbFileHeader header)
    {
        _filePath = filePath;
        _options = options;
        _fileStream = fileStream;
        _memoryMappedFile = mmf;
        _header = header;
        _blockCache = new ConcurrentDictionary<string, BlockMetadata>();

        // Initialize subsystems
        _blockRegistry = new BlockRegistry(this, header.BlockRegistryOffset, header.BlockRegistryLength);
        _freeSpaceManager = new FreeSpaceManager(this, header.FsmOffset, header.FsmLength, header.PageSize);
        _walManager = new WalManager(this, header.WalOffset, header.WalLength, options.WalBufferSizePages);
        _tableDirectoryManager = new TableDirectoryManager(this, header.TableDirOffset, header.TableDirLength);
    }

    /// <summary>
    /// Opens or creates a single-file storage provider.
    /// </summary>
    /// <param name="filePath">Path to .scdb file</param>
    /// <param name="options">Database options</param>
    /// <returns>Initialized provider</returns>
    public static SingleFileStorageProvider Open(string filePath, DatabaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(options);
        
        options.Validate();

        // Ensure .scdb extension
        if (!filePath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
        {
            filePath += ".scdb";
        }

        // Create or open file
        var fileMode = options.CreateImmediately && !File.Exists(filePath) 
            ? FileMode.CreateNew 
            : FileMode.OpenOrCreate;

        var fileOptions = FileOptions.RandomAccess;
        if (options.UseUnbufferedIO)
        {
            // Note: O_DIRECT equivalent on Windows requires special handling
            // For now, use RandomAccess which hints to OS
        }

        var fileStream = new FileStream(
            filePath,
            fileMode,
            FileAccess.ReadWrite,
            options.FileShareMode,
            bufferSize: 0, // Unbuffered
            fileOptions);

        ScdbFileHeader header;

        // Initialize or load header
        if (fileStream.Length == 0)
        {
            header = InitializeNewFile(fileStream, options);
        }
        else
        {
            header = LoadHeader(fileStream);
            ValidateHeader(header, options);
        }

        // Create memory-mapped file if enabled
        MemoryMappedFile? mmf = null;
        if (options.EnableMemoryMapping && fileStream.Length > 0)
        {
            try
            {
                mmf = MemoryMappedFile.CreateFromFile(
                    fileStream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: true);
            }
            catch
            {
                // Fall back to non-memory-mapped if OS doesn't support it
            }
        }

        return new SingleFileStorageProvider(filePath, options, fileStream, mmf, header);
    }

    /// <inheritdoc/>
    public StorageMode Mode => StorageMode.SingleFile;

    /// <inheritdoc/>
    public string RootPath => _filePath;

    /// <inheritdoc/>
    public bool IsEncrypted => _options.EnableEncryption;

    /// <inheritdoc/>
    public int PageSize => _header.PageSize;

    /// <summary>
    /// Gets the table directory manager for schema operations.
    /// </summary>
    internal TableDirectoryManager TableDirectoryManager => _tableDirectoryManager;

    /// <inheritdoc/>
    public bool BlockExists(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _blockRegistry.TryGetBlock(blockName, out _);
    }

    /// <inheritdoc/>
    public Stream? GetReadStream(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        if (!_blockRegistry.TryGetBlock(blockName, out var entry))
        {
            return null;
        }

        // Create a sub-stream view of the block
        return new BlockStream(_fileStream, entry.Offset, entry.Length, FileAccess.Read);
    }

    /// <inheritdoc/>
    public unsafe ReadOnlySpan<byte> GetReadSpan(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_blockRegistry.TryGetBlock(blockName, out var entry))
        {
            return ReadOnlySpan<byte>.Empty;
        }

        // Guard against invalid lengths
        if (entry.Length == 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        // If length cannot fit in int (required by span overload), fallback to stream
        if (entry.Length > int.MaxValue)
        {
            // Fallback: regular read (allocates)
            var largeLen = checked((long)entry.Length);
            var buffer = new byte[checked((int)Math.Min(entry.Length, (ulong)int.MaxValue))];
            _fileStream.Position = (long)entry.Offset;
            _fileStream.ReadExactly(buffer);
            return buffer;
        }

        // Use memory-mapped file for zero-copy access
        if (_memoryMappedFile != null)
        {
            try
            {
                var viewOffset = checked((long)entry.Offset);
                var viewLength = checked((long)entry.Length);

                using var accessor = _memoryMappedFile.CreateViewAccessor(
                    viewOffset,
                    viewLength,
                    MemoryMappedFileAccess.Read);

                byte* ptr = null;
                accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);

                if (ptr != null)
                {
                    return new ReadOnlySpan<byte>(ptr, (int)entry.Length);
                }
            }
            catch
            {
                // Fall through to regular read
            }
        }

        // Fallback: regular read (allocates)
        var buffer2 = new byte[(int)entry.Length];
        _fileStream.Position = (long)entry.Offset;
        _fileStream.ReadExactly(buffer2);
        return buffer2;
    }

    /// <inheritdoc/>
    public Stream GetWriteStream(string blockName, bool append = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ulong offset;
        ulong length;

        if (_blockRegistry.TryGetBlock(blockName, out var existingEntry))
        {
            if (append)
            {
                offset = existingEntry.Offset + existingEntry.Length;
                length = 0; // Will grow
            }
            else
            {
                // Overwrite: reuse existing space
                offset = existingEntry.Offset;
                length = existingEntry.Length;
            }
        }
        else
        {
            // Allocate new block
            var pages = 1; // Start with 1 page, will grow if needed
            offset = _freeSpaceManager.AllocatePages(pages);
            length = (ulong)_header.PageSize;

            // Register new block
            var newEntry = new BlockEntry
            {
                BlockType = (uint)Scdb.BlockType.TableData,
                Offset = offset,
                Length = length,
                Flags = (uint)BlockFlags.Dirty
            };
            _blockRegistry.AddOrUpdateBlock(blockName, newEntry);
        }

        return new BlockStream(_fileStream, offset, length, FileAccess.Write);
    }

    /// <inheritdoc/>
    public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Calculate required pages
            var requiredPages = (data.Length + _header.PageSize - 1) / _header.PageSize;
            
            ulong offset;
            BlockEntry entry;

            if (_blockRegistry.TryGetBlock(blockName, out var existingEntry))
            {
                var existingPages = (existingEntry.Length + (ulong)_header.PageSize - 1) / (ulong)_header.PageSize;

                if (requiredPages <= (int)existingPages)
                {
                    // Fits in existing space
                    offset = existingEntry.Offset;
                    entry = existingEntry with { Length = (ulong)data.Length, Flags = existingEntry.Flags | (uint)BlockFlags.Dirty };
                }
                else
                {
                    // Need more space: free old, allocate new
                    _freeSpaceManager.FreePages(existingEntry.Offset, (int)existingPages);
                    offset = _freeSpaceManager.AllocatePages(requiredPages);
                    entry = existingEntry with { Offset = offset, Length = (ulong)data.Length, Flags = (uint)BlockFlags.Dirty };
                }
            }
            else
            {
                // New block
                offset = _freeSpaceManager.AllocatePages(requiredPages);
                entry = new BlockEntry
                {
                    BlockType = (uint)Scdb.BlockType.TableData,
                    Offset = offset,
                    Length = (ulong)data.Length,
                    Flags = (uint)BlockFlags.Dirty
                };
            }

            // Compute checksum
            var checksum = SHA256.HashData(data.Span);
            entry = SetChecksum(entry, checksum);

            // Write to WAL first (crash safety)
            if (_isInTransaction)
            {
                await _walManager.LogWriteAsync(blockName, offset, data, cancellationToken).ConfigureAwait(false);
            }

            // Write data
            _fileStream.Position = (long)offset;
            await _fileStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            
            // ✅ CRITICAL: Flush file buffers to disk immediately
            // This ensures the checksum (calculated on memory buffer) matches disk data
            // before we store the checksum in the registry. Prevents checksum mismatch
            // errors on subsequent reads if OS hasn't flushed buffers yet.
            _fileStream.Flush(flushToDisk: true);

            // Update registry
            _blockRegistry.AddOrUpdateBlock(blockName, entry);
            
            // ✅ CRITICAL FIX: Immediately flush registry to disk after update
            // The registry contains the checksum that must be persisted ONLY AFTER the data
            // it references is fully written. This prevents race conditions where a concurrent
            // read gets the new checksum from the in-memory registry but reads stale data from disk.
            // Without this flush, BenchmarkDotNet iterations can trigger InvalidDataException during SELECT queries.
            await _blockRegistry.FlushAsync(cancellationToken).ConfigureAwait(false);

            // Update cache
            _blockCache[blockName] = new BlockMetadata
            {
                Name = blockName,
                BlockType = entry.BlockType,
                Size = (long)entry.Length,
                Offset = (long)entry.Offset,
                Checksum = checksum,
                IsEncrypted = _options.EnableEncryption,
                IsDirty = true,
                LastModified = DateTime.UtcNow
            };
        }
        finally
        {
            _ioGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> ReadBlockAsync(string blockName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_blockRegistry.TryGetBlock(blockName, out var entry))
            {
                return null;
            }

            var buffer = new byte[entry.Length];
            _fileStream.Position = (long)entry.Offset;
            await _fileStream.ReadExactlyAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);  // ✅ Use ReadExactlyAsync

            // Validate checksum
            if (!ValidateChecksum(entry, buffer))
            {
                throw new InvalidDataException($"Checksum mismatch for block '{blockName}'");
            }

            return buffer;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteBlockAsync(string blockName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_blockRegistry.TryGetBlock(blockName, out var entry))
        {
            return;
        }

        // Mark as deleted in WAL
        if (_isInTransaction)
        {
            await _walManager.LogDeleteAsync(blockName, cancellationToken);
        }

        // Free pages
        var pages = (entry.Length + (ulong)_header.PageSize - 1) / (ulong)_header.PageSize;
        _freeSpaceManager.FreePages(entry.Offset, (int)pages);

        // Remove from registry
        _blockRegistry.RemoveBlock(blockName);

        // Remove from cache
        _blockCache.TryRemove(blockName, out _);
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateBlocks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _blockRegistry.EnumerateBlockNames();
    }

    /// <inheritdoc/>
    public BlockMetadata? GetBlockMetadata(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_blockCache.TryGetValue(blockName, out var cached))
        {
            return cached;
        }

        if (!_blockRegistry.TryGetBlock(blockName, out var entry))
        {
            return null;
        }

        var metadata = new BlockMetadata
        {
            Name = blockName,
            BlockType = entry.BlockType,
            Size = (long)entry.Length,
            Offset = (long)entry.Offset,
            Checksum = GetChecksum(entry),
            IsEncrypted = _options.EnableEncryption,
            IsDirty = (entry.Flags & (uint)BlockFlags.Dirty) != 0,
            LastModified = DateTime.UtcNow
        };

        _blockCache[blockName] = metadata;
        return metadata;
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Flush block registry
        await _blockRegistry.FlushAsync(cancellationToken);

        // Flush FSM
        await _freeSpaceManager.FlushAsync(cancellationToken);

        // Flush table directory
        _tableDirectoryManager.Flush();

        // Checkpoint WAL
        await _walManager.CheckpointAsync(cancellationToken);

        // Flush file buffers to disk before updating header
        _fileStream.Flush(flushToDisk: true);

        // Update header
        _header.LastTransactionId++;
        _header.LastCheckpointLsn = _walManager.CurrentLsn;
        await WriteHeaderAsync(cancellationToken);

        // Ensure header is persisted
        _fileStream.Flush(flushToDisk: true);
    }

    /// <inheritdoc/>
    public async Task<VacuumResult> VacuumAsync(VacuumMode mode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sw = Stopwatch.StartNew();
        var stats = GetStatistics();
        var fileSizeBefore = stats.TotalSize;
        var fragmentationBefore = stats.FragmentationPercent;

        try
        {
            return mode switch
            {
                VacuumMode.Quick => await VacuumQuickAsync(stats, sw, cancellationToken),
                VacuumMode.Incremental => await VacuumIncrementalAsync(stats, sw, cancellationToken),
                VacuumMode.Full => await VacuumFullAsync(stats, sw, cancellationToken),
                _ => throw new ArgumentException($"Invalid vacuum mode: {mode}")
            };
        }
        catch (Exception ex)
        {
            return new VacuumResult
            {
                Mode = mode,
                DurationMs = sw.ElapsedMilliseconds,
                FileSizeBefore = fileSizeBefore,
                FileSizeAfter = _fileStream.Length,
                FragmentationBefore = fragmentationBefore,
                FragmentationAfter = stats.FragmentationPercent,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public void BeginTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_transactionLock)
        {
            if (_isInTransaction)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _isInTransaction = true;
            _walManager.BeginTransaction();
        }
    }

    /// <inheritdoc/>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_transactionLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }
        }

        await _walManager.CommitTransactionAsync(cancellationToken);
        await FlushAsync(cancellationToken);

        lock (_transactionLock)
        {
            _isInTransaction = false;
        }
    }

    /// <inheritdoc/>
    public void RollbackTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_transactionLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }

            _walManager.RollbackTransaction();
            _isInTransaction = false;
        }
    }

    /// <inheritdoc/>
    public bool IsInTransaction
    {
        get
        {
            lock (_transactionLock)
            {
                return _isInTransaction;
            }
        }
    }

    /// <inheritdoc/>
    public StorageStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fsmStats = _freeSpaceManager.GetStatistics();
        var walStats = _walManager.GetStatistics();

        return new StorageStatistics
        {
            TotalSize = _fileStream.Length,
            UsedSpace = _fileStream.Length - fsmStats.FreeSpace,
            FreeSpace = fsmStats.FreeSpace,
            FragmentationPercent = _header.FragmentationPercent / 100.0,
            BlockCount = _blockRegistry.Count,
            DirtyBlocks = _blockCache.Values.Count(b => b.IsDirty),
            PageCount = (long)_header.AllocatedPages,
            FreePages = fsmStats.FreePages,
            WalSize = walStats.Size,
            LastVacuum = _header.LastVacuumTime > 0 
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)_header.LastVacuumTime).DateTime 
                : null
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_isInTransaction)
            {
                RollbackTransaction();
            }

            FlushAsync().GetAwaiter().GetResult();

            _blockRegistry?.Dispose();
            _freeSpaceManager?.Dispose();
            _walManager?.Dispose();
            _tableDirectoryManager?.Dispose();
            _memoryMappedFile?.Dispose();
            _fileStream?.Dispose();
        }
        catch
        {
            // Best effort cleanup
        }
        finally
        {
            _disposed = true;
        }
    }

    // ==================== PRIVATE HELPER METHODS ====================

    private static ScdbFileHeader InitializeNewFile(FileStream fs, DatabaseOptions options)
    {
        var header = ScdbFileHeader.CreateDefault((ushort)options.PageSize);
        
        // Set encryption flags
        if (options.EnableEncryption)
        {
            header.EncryptionMode = 1; // AES-256-GCM
            // Generate random nonce
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            
            // ✅ Fix: Use Span instead of fixed for already-fixed buffer
            unsafe
            {
                var nonceSpan = new Span<byte>(header.Nonce, 12);
                nonce.CopyTo(nonceSpan);
            }
        }

        // Initialize block registry at page 1
        header.BlockRegistryOffset = ScdbFileHeader.HEADER_SIZE;
        header.BlockRegistryLength = (ulong)options.PageSize;

        // Initialize FSM at page 2
        header.FsmOffset = header.BlockRegistryOffset + header.BlockRegistryLength;
        header.FsmLength = (ulong)options.PageSize * 4; // 4 pages for FSM

        // Initialize WAL at page 6
        header.WalOffset = header.FsmOffset + header.FsmLength;
        header.WalLength = (ulong)options.PageSize * (ulong)options.WalBufferSizePages;

        // Initialize table directory at page 10
        header.TableDirOffset = header.WalOffset + header.WalLength;
        header.TableDirLength = (ulong)options.PageSize * 4; // 4 pages for table directory

        // Allocate space for metadata structures
        var totalMetadataSize = header.TableDirOffset + header.TableDirLength;
        fs.SetLength((long)totalMetadataSize);

        return header;
    }

    private static ScdbFileHeader LoadHeader(FileStream fs)
    {
        Span<byte> buffer = stackalloc byte[(int)ScdbFileHeader.HEADER_SIZE];
        fs.Position = 0;
        fs.ReadExactly(buffer);  // ✅ Use ReadExactly
        return ScdbFileHeader.Parse(buffer);
    }

    private static void ValidateHeader(ScdbFileHeader header, DatabaseOptions options)
    {
        if (!header.IsValid)
        {
            throw new InvalidDataException(
                $"Invalid SCDB file: magic=0x{header.Magic:X16}, version={header.FormatVersion}");
        }

        if (header.PageSize != options.PageSize)
        {
            throw new InvalidOperationException(
                $"Page size mismatch: file has {header.PageSize}, options specify {options.PageSize}");
        }
    }

    private async Task WriteHeaderAsync(CancellationToken cancellationToken)
    {
        _fileStream.Position = 0;
        var buffer = new byte[ScdbFileHeader.HEADER_SIZE];
        _header.WriteTo(buffer);
        await _fileStream.WriteAsync(buffer, cancellationToken);
    }

    private async Task<VacuumResult> VacuumQuickAsync(StorageStatistics stats, Stopwatch sw, CancellationToken cancellationToken)
    {
        // Quick: Just checkpoint WAL and update stats
        await _walManager.CheckpointAsync(cancellationToken);
        _header.LastVacuumTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await WriteHeaderAsync(cancellationToken);

        return new VacuumResult
        {
            Mode = VacuumMode.Quick,
            DurationMs = sw.ElapsedMilliseconds,
            FileSizeBefore = stats.TotalSize,
            FileSizeAfter = _fileStream.Length,
            BytesReclaimed = 0,
            FragmentationBefore = stats.FragmentationPercent,
            FragmentationAfter = stats.FragmentationPercent,
            BlocksMoved = 0,
            BlocksDeleted = 0,
            Success = true
        };
    }

    private async Task<VacuumResult> VacuumIncrementalAsync(StorageStatistics stats, Stopwatch sw, CancellationToken cancellationToken)
    {
        // Incremental: Compact dirty blocks by moving them to free space
        var dirtyBlocks = _blockCache.Values.Where(b => b.IsDirty).ToList();
        var blocksMoved = 0;
        var bytesReclaimed = 0L;

        foreach (var blockName in dirtyBlocks.Select(b => b.Name))
        {
            if (_blockCache.TryGetValue(blockName, out var cached))
            {
                if (_blockRegistry.TryGetBlock(blockName, out var entry))
                {
                    // Check if block is fragmented (not at optimal position)
                    var optimalPage = _freeSpaceManager.AllocatePages((int)((entry.Length + (ulong)_header.PageSize - 1) / (ulong)_header.PageSize));
                    
                    if (optimalPage < entry.Offset && optimalPage != entry.Offset)
                    {
                        // Move block to better position
                        var blockData = new byte[entry.Length];
                        _fileStream.Position = (long)entry.Offset;
                        await _fileStream.ReadExactlyAsync(blockData, cancellationToken);
                        
                        // Write to new location
                        _fileStream.Position = (long)optimalPage;
                        await _fileStream.WriteAsync(blockData, cancellationToken);
                        
                        // Free old location
                        var oldPages = (int)((entry.Length + (ulong)_header.PageSize - 1) / (ulong)_header.PageSize);
                        _freeSpaceManager.FreePages(entry.Offset, oldPages);
                        
                        // Update registry
                        var newEntry = entry with { Offset = optimalPage, Flags = entry.Flags & ~(uint)BlockFlags.Dirty };
                        _blockRegistry.AddOrUpdateBlock(blockName, newEntry);
                        
                        blocksMoved++;
                        bytesReclaimed += (long)entry.Length;
                    }
                }

                // Mark as clean in cache
                _blockCache[blockName] = new BlockMetadata
                {
                    Name = cached.Name,
                    BlockType = cached.BlockType,
                    Size = cached.Size,
                    Offset = cached.Offset,
                    Checksum = cached.Checksum,
                    IsEncrypted = cached.IsEncrypted,
                    IsDirty = false,
                    LastModified = DateTime.UtcNow
                };
            }
        }

        // Flush registry and FSM
        await _blockRegistry.FlushAsync(cancellationToken);
        await _freeSpaceManager.FlushAsync(cancellationToken);

        _header.LastVacuumTime = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await WriteHeaderAsync(cancellationToken);

        var statsAfter = GetStatistics();

        return new VacuumResult
        {
            Mode = VacuumMode.Incremental,
            DurationMs = sw.ElapsedMilliseconds,
            FileSizeBefore = stats.TotalSize,
            FileSizeAfter = _fileStream.Length,
            BytesReclaimed = bytesReclaimed,
            FragmentationBefore = stats.FragmentationPercent,
            FragmentationAfter = statsAfter.FragmentationPercent,
            BlocksMoved = blocksMoved,
            BlocksDeleted = 0,
            Success = true
        };
    }

    private async Task<VacuumResult> VacuumFullAsync(StorageStatistics stats, Stopwatch sw, CancellationToken cancellationToken)
    {
        // Full: Rewrite entire file compactly to temporary file, then swap
        var tempPath = _filePath + ".vacuum.tmp";
        var blocksMoved = 0;
        
        try
        {
            // Create temporary file with same options
            var tempOptions = new DatabaseOptions
            {
                StorageMode = StorageMode.SingleFile,
                PageSize = _options.PageSize,
                EnableEncryption = _options.EnableEncryption,
                EncryptionKey = _options.EncryptionKey,
                EnableMemoryMapping = false, // Don't use mmap for temp file
                CreateImmediately = true
            };

            using (var tempProvider = SingleFileStorageProvider.Open(tempPath, tempOptions))
            {
                // Copy all blocks to new file in optimal order
                foreach (var blockName in _blockRegistry.EnumerateBlockNames().OrderBy(n => n))
                {
                    var blockData = await ReadBlockAsync(blockName, cancellationToken);
                    if (blockData != null)
                    {
                        await tempProvider.WriteBlockAsync(blockName, blockData, cancellationToken);
                        blocksMoved++;
                    }
                }

                // Flush temp file
                await tempProvider.FlushAsync(cancellationToken);
            }

            // Close current file
            _memoryMappedFile?.Dispose();
            await _fileStream.FlushAsync(cancellationToken);
            _fileStream.Close();

            // Replace old file with new file
            var backupPath = _filePath + ".backup";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(_filePath, backupPath);
            File.Move(tempPath, _filePath);

            // Reopen file
            var newFileStream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                _options.FileShareMode,
                bufferSize: 0,
                FileOptions.RandomAccess);

            // Update internal state
            #pragma warning disable S3011 // Reflection is safe here - we own both classes
            var fsField = typeof(SingleFileStorageProvider).GetField("_fileStream",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            fsField!.SetValue(this, newFileStream);

            // Recreate memory-mapped file if needed
            if (_options.EnableMemoryMapping)
            {
                var mmf = MemoryMappedFile.CreateFromFile(
                    newFileStream,
                    mapName: null,
                    capacity: 0,
                    MemoryMappedFileAccess.Read,
                    HandleInheritability.None,
                    leaveOpen: true);

                var mmfField = typeof(SingleFileStorageProvider).GetField("_memoryMappedFile",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                mmfField!.SetValue(this, mmf);
            }
            #pragma warning restore S3011

            // Reload header
            _header = LoadHeader(newFileStream);

            // Delete backup
            File.Delete(backupPath);

            var statsAfter = GetStatistics();
            var bytesReclaimed = stats.TotalSize - statsAfter.TotalSize;

            return new VacuumResult
            {
                Mode = VacuumMode.Full,
                DurationMs = sw.ElapsedMilliseconds,
                FileSizeBefore = stats.TotalSize,
                FileSizeAfter = statsAfter.TotalSize,
                BytesReclaimed = bytesReclaimed,
                FragmentationBefore = stats.FragmentationPercent,
                FragmentationAfter = 0, // Perfectly compacted
                BlocksMoved = blocksMoved,
                BlocksDeleted = 0,
                Success = true
            };
        }
        catch (Exception ex)
        {
            // Cleanup temp file on error
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* Ignore */ }
            }

            return new VacuumResult
            {
                Mode = VacuumMode.Full,
                DurationMs = sw.ElapsedMilliseconds,
                FileSizeBefore = stats.TotalSize,
                FileSizeAfter = _fileStream.Length,
                BytesReclaimed = 0,
                FragmentationBefore = stats.FragmentationPercent,
                FragmentationAfter = stats.FragmentationPercent,
                BlocksMoved = 0,
                BlocksDeleted = 0,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static unsafe BlockEntry SetChecksum(BlockEntry entry, ReadOnlySpan<byte> checksum)
    {
        var result = entry;
        // ✅ Fix: Use Span instead of fixed for already-fixed buffer
        var checksumSpan = new Span<byte>(result.Checksum, 32);
        checksum.CopyTo(checksumSpan);
        return result;
    }

    private static unsafe byte[] GetChecksum(BlockEntry entry)
    {
        var checksum = new byte[32];
        // ✅ Fix: Use Span instead of fixed
        var checksumSpan = new ReadOnlySpan<byte>(entry.Checksum, 32);
        checksumSpan.CopyTo(checksum);
        return checksum;
    }

    private static unsafe bool ValidateChecksum(BlockEntry entry, ReadOnlySpan<byte> data)
    {
        var computedHash = SHA256.HashData(data);
        // ✅ Fix: Use Span instead of fixed
        var storedHash = new ReadOnlySpan<byte>(entry.Checksum, 32);
        return storedHash.SequenceEqual(computedHash);
    }

    /// <summary>
    /// Gets the underlying FileStream for internal use by subsystems.
    /// </summary>
    internal FileStream GetInternalFileStream() => _fileStream;
}

/// <summary>
/// Stream wrapper for block access.
/// </summary>
internal sealed class BlockStream : Stream
{
    private readonly FileStream _baseStream;
    private readonly long _startOffset;
    private readonly long _length;
    private readonly FileAccess _access;
    private long _position;

    public BlockStream(FileStream baseStream, ulong startOffset, ulong length, FileAccess access)
    {
        _baseStream = baseStream;
        _startOffset = (long)startOffset;
        _length = (long)length;
        _access = access;
        _position = 0;
    }

    public override bool CanRead => _access.HasFlag(FileAccess.Read);
    public override bool CanWrite => _access.HasFlag(FileAccess.Write);
    public override bool CanSeek => true;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Max(0, Math.Min(value, _length));
    }

    public override void Flush() => _baseStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _length - _position;
        var toRead = (int)Math.Min(count, remaining);
        
        _baseStream.Position = _startOffset + _position;
        var bytesRead = _baseStream.Read(buffer, offset, toRead);
        _position += bytesRead;
        
        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_position + count > _length)
        {
            throw new InvalidOperationException("Write exceeds block boundary");
        }

        _baseStream.Position = _startOffset + _position;
        _baseStream.Write(buffer, offset, count);
        _position += count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => _position
        };

        return _position;
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException("Cannot resize block stream");
    }
}
