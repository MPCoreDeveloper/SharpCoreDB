// <copyright file="IStorageProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Abstraction for storage providers (directory-based or single-file).
/// Provides unified interface for different storage backends.
/// C# 14: Uses modern async patterns and ReadOnlySpan for zero-copy.
/// </summary>
public interface IStorageProvider : IDisposable
{
    /// <summary>
    /// Gets the storage mode of this provider.
    /// </summary>
    StorageMode Mode { get; }

    /// <summary>
    /// Gets the root path (directory or .scdb file path).
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Gets whether the storage is encrypted.
    /// </summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// Gets the page size (for single-file mode, 0 for directory mode).
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Checks if a block exists.
    /// </summary>
    /// <param name="blockName">Block name (e.g., "table:users:data")</param>
    /// <returns>True if block exists</returns>
    bool BlockExists(string blockName);

    /// <summary>
    /// Gets a read-only stream for a block (zero-copy via memory mapping if available).
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <returns>Read-only stream, or null if block doesn't exist</returns>
    Stream? GetReadStream(string blockName);

    /// <summary>
    /// Gets a read-only span for a block (zero-copy, single-file only).
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <returns>Read-only span of block data, or empty if not found</returns>
    ReadOnlySpan<byte> GetReadSpan(string blockName);

    /// <summary>
    /// Gets a write stream for a block.
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <param name="append">Whether to append or overwrite</param>
    /// <returns>Writable stream</returns>
    Stream GetWriteStream(string blockName, bool append = false);

    /// <summary>
    /// Writes data to a block (optimized for small writes).
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <param name="data">Data to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads data from a block (optimized for small reads).
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Block data, or null if not found</returns>
    Task<byte[]?> ReadBlockAsync(string blockName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a block.
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteBlockAsync(string blockName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates all block names in the storage.
    /// </summary>
    /// <returns>Enumerable of block names</returns>
    IEnumerable<string> EnumerateBlocks();

    /// <summary>
    /// Gets block metadata (size, checksum, etc.).
    /// </summary>
    /// <param name="blockName">Block name</param>
    /// <returns>Block metadata, or null if not found</returns>
    BlockMetadata? GetBlockMetadata(string blockName);

    /// <summary>
    /// Flushes all pending writes to disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs VACUUM operation (single-file only).
    /// </summary>
    /// <param name="mode">Vacuum mode</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vacuum result with statistics</returns>
    Task<VacuumResult> VacuumAsync(VacuumMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a transaction (for atomic multi-block operations).
    /// </summary>
    void BeginTransaction();

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    void RollbackTransaction();

    /// <summary>
    /// Gets whether currently in a transaction.
    /// </summary>
    bool IsInTransaction { get; }

    /// <summary>
    /// Gets storage statistics (file size, block count, fragmentation, etc.).
    /// </summary>
    /// <returns>Storage statistics</returns>
    StorageStatistics GetStatistics();
}

/// <summary>
/// Metadata for a storage block.
/// </summary>
public sealed class BlockMetadata
{
    /// <summary>
    /// Gets or sets the block name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the block type (from BlockType enum).
    /// </summary>
    public uint BlockType { get; init; }

    /// <summary>
    /// Gets or sets the block size in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// Gets or sets the block offset in file (single-file only).
    /// </summary>
    public long Offset { get; init; }

    /// <summary>
    /// Gets or sets the SHA-256 checksum (if available).
    /// </summary>
    public byte[]? Checksum { get; init; }

    /// <summary>
    /// Gets or sets whether the block is encrypted.
    /// </summary>
    public bool IsEncrypted { get; init; }

    /// <summary>
    /// Gets or sets whether the block is dirty (has uncommitted changes).
    /// </summary>
    public bool IsDirty { get; init; }

    /// <summary>
    /// Gets or sets the last modified timestamp.
    /// </summary>
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Result of a VACUUM operation.
/// </summary>
public sealed class VacuumResult
{
    /// <summary>
    /// Gets or sets the vacuum mode used.
    /// </summary>
    public required VacuumMode Mode { get; init; }

    /// <summary>
    /// Gets or sets the duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Gets or sets the file size before VACUUM.
    /// </summary>
    public long FileSizeBefore { get; init; }

    /// <summary>
    /// Gets or sets the file size after VACUUM.
    /// </summary>
    public long FileSizeAfter { get; init; }

    /// <summary>
    /// Gets or sets the number of bytes reclaimed.
    /// </summary>
    public long BytesReclaimed { get; init; }

    /// <summary>
    /// Gets or sets the fragmentation percentage before VACUUM.
    /// </summary>
    public double FragmentationBefore { get; init; }

    /// <summary>
    /// Gets or sets the fragmentation percentage after VACUUM.
    /// </summary>
    public double FragmentationAfter { get; init; }

    /// <summary>
    /// Gets or sets the number of blocks moved.
    /// </summary>
    public int BlocksMoved { get; init; }

    /// <summary>
    /// Gets or sets the number of blocks deleted.
    /// </summary>
    public int BlocksDeleted { get; init; }

    /// <summary>
    /// Gets whether VACUUM was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets or sets the error message (if Success = false).
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Storage statistics for monitoring.
/// </summary>
public sealed class StorageStatistics
{
    /// <summary>
    /// Gets or sets the total file size in bytes.
    /// </summary>
    public long TotalSize { get; init; }

    /// <summary>
    /// Gets or sets the used space in bytes.
    /// </summary>
    public long UsedSpace { get; init; }

    /// <summary>
    /// Gets or sets the free space in bytes.
    /// </summary>
    public long FreeSpace { get; init; }

    /// <summary>
    /// Gets or sets the fragmentation percentage (0-100).
    /// </summary>
    public double FragmentationPercent { get; init; }

    /// <summary>
    /// Gets or sets the total number of blocks.
    /// </summary>
    public int BlockCount { get; init; }

    /// <summary>
    /// Gets or sets the number of dirty blocks.
    /// </summary>
    public int DirtyBlocks { get; init; }

    /// <summary>
    /// Gets or sets the number of pages (single-file only).
    /// </summary>
    public long PageCount { get; init; }

    /// <summary>
    /// Gets or sets the number of free pages (single-file only).
    /// </summary>
    public long FreePages { get; init; }

    /// <summary>
    /// Gets or sets the WAL size in bytes (single-file only).
    /// </summary>
    public long WalSize { get; init; }

    /// <summary>
    /// Gets or sets the last VACUUM timestamp.
    /// </summary>
    public DateTime? LastVacuum { get; init; }
}
