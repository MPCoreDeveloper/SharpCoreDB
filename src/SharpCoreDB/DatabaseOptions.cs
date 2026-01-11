// <copyright file="DatabaseOptions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

/// <summary>
/// Storage mode for database files.
/// </summary>
public enum StorageMode
{
    /// <summary>
    /// Traditional directory-based storage with multiple files (default).
    /// Format: mydb/table1.dat, mydb/table2.pages, etc.
    /// Pros: Backward compatible, proven stable
    /// Cons: Many file handles, slower startup
    /// </summary>
    Directory = 0,

    /// <summary>
    /// Single-file storage with .scdb extension.
    /// Format: mydb.scdb (all data in one file)
    /// Pros: Fast startup, fewer file handles, atomic backups
    /// Cons: Requires migration for existing databases
    /// </summary>
    SingleFile = 1
}

/// <summary>
/// VACUUM operation mode for defragmentation.
/// </summary>
public enum VacuumMode
{
    /// <summary>
    /// Quick cleanup: Truncate WAL, update FSM statistics.
    /// Duration: ~10ms, No blocking
    /// </summary>
    Quick = 0,

    /// <summary>
    /// Incremental cleanup: Compact dirty blocks only.
    /// Duration: ~100ms, Minimal blocking
    /// </summary>
    Incremental = 1,

    /// <summary>
    /// Full cleanup: Rewrite entire file compactly.
    /// Duration: ~10s for 1GB, Exclusive lock required
    /// </summary>
    Full = 2
}

/// <summary>
/// Configuration options for database creation and opening.
/// C# 14: Uses field keyword and primary constructor patterns.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>
    /// Gets or sets the storage mode (Directory or SingleFile).
    /// Default: Directory (backward compatible).
    /// </summary>
    public StorageMode StorageMode { get; set; } = StorageMode.Directory;

    /// <summary>
    /// Gets or sets the page size for single-file storage.
    /// Default: 4096 (4KB) for SSD optimization.
    /// Valid values: 512, 1024, 2048, 4096, 8192, 16384, 32768.
    /// </summary>
    public int PageSize { get; set; } = 4096;

    /// <summary>
    /// Gets or sets whether to enable AES-256-GCM encryption for single-file storage.
    /// Default: false (no encryption).
    /// </summary>
    public bool EnableEncryption { get; set; } = false;

    /// <summary>
    /// Gets or sets the encryption key (32 bytes for AES-256).
    /// Required when EnableEncryption = true.
    /// </summary>
    public byte[]? EncryptionKey { get; set; }

    /// <summary>
    /// Gets or sets whether to enable memory-mapped I/O for reads.
    /// Default: true (enables zero-copy reads).
    /// Disable for: Very small databases (less than 1MB) or high write workloads.
    /// </summary>
    public bool EnableMemoryMapping { get; set; } = true;

    /// <summary>
    /// Gets or sets the WAL (Write-Ahead Log) buffer size in pages.
    /// Default: 1024 pages (4MB with 4KB pages).
    /// Higher values = Better crash recovery granularity, More memory usage.
    /// </summary>
    public int WalBufferSizePages { get; set; } = 1024;

    /// <summary>
    /// Gets or sets whether to auto-vacuum on close.
    /// Default: false (manual VACUUM only).
    /// </summary>
    public bool AutoVacuum { get; set; } = false;

    /// <summary>
    /// Gets or sets the auto-vacuum mode.
    /// Default: VacuumMode.Quick.
    /// </summary>
    public VacuumMode AutoVacuumMode { get; set; } = VacuumMode.Quick;

    /// <summary>
    /// Gets or sets the fragmentation threshold (0-100%) for auto-vacuum trigger.
    /// Default: 25% (trigger VACUUM when 25% of file is wasted space).
    /// </summary>
    public int FragmentationThreshold { get; set; } = 25;

    /// <summary>
    /// Gets or sets whether to create the file immediately on database creation.
    /// Default: true (eager creation).
    /// Set false for: Lazy file creation (useful for temporary databases).
    /// </summary>
    public bool CreateImmediately { get; set; } = true;

    /// <summary>
    /// Gets or sets the file share mode for single-file databases.
    /// Default: FileShare.None (exclusive access).
    /// </summary>
    public FileShare FileShareMode { get; set; } = FileShare.None;

    /// <summary>
    /// Gets or sets whether to use unbuffered I/O (O_DIRECT on Linux).
    /// Default: false (use OS buffering).
    /// Enable for: Direct SSD access, bypassing OS cache.
    /// </summary>
    public bool UseUnbufferedIO { get; set; } = false;

    /// <summary>
    /// Gets or sets whether the database is opened in read-only mode.
    /// Default: false (read-write mode).
    /// </summary>
    public bool IsReadOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets the database configuration (inherited from existing DatabaseConfig).
    /// Used for workload hints, storage engine selection, etc.
    /// </summary>
    public DatabaseConfig? DatabaseConfig { get; set; }

    /// <summary>
    /// Validates the options and throws if invalid.
    /// </summary>
    public void Validate()
    {
        // Validate page size (must be power of 2 between 512 and 32768)
        if (PageSize < 512 || PageSize > 32768 || (PageSize & (PageSize - 1)) != 0)
        {
            throw new ArgumentException(
                $"PageSize must be a power of 2 between 512 and 32768. Got: {PageSize}");
        }

        // Validate encryption key if encryption enabled
        if (EnableEncryption && (EncryptionKey == null || EncryptionKey.Length != 32))
        {
            throw new ArgumentException(
                "EncryptionKey must be exactly 32 bytes (256 bits) when EnableEncryption is true");
        }

        // Validate WAL buffer size
        if (WalBufferSizePages < 64 || WalBufferSizePages > 65536)
        {
            throw new ArgumentException(
                $"WalBufferSizePages must be between 64 and 65536. Got: {WalBufferSizePages}");
        }

        // Validate fragmentation threshold
        if (FragmentationThreshold < 0 || FragmentationThreshold > 100)
        {
            throw new ArgumentException(
                $"FragmentationThreshold must be between 0 and 100. Got: {FragmentationThreshold}");
        }
    }

    /// <summary>
    /// Creates default options for directory-based storage.
    /// </summary>
    /// <returns>Default directory options</returns>
    public static DatabaseOptions CreateDirectoryDefault()
    {
        return new DatabaseOptions
        {
            StorageMode = StorageMode.Directory
        };
    }

    /// <summary>
    /// Creates default options for single-file storage.
    /// </summary>
    /// <param name="enableEncryption">Whether to enable encryption</param>
    /// <param name="encryptionKey">Encryption key (32 bytes)</param>
    /// <returns>Default single-file options</returns>
    public static DatabaseOptions CreateSingleFileDefault(bool enableEncryption = false, byte[]? encryptionKey = null)
    {
        return new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = enableEncryption,
            EncryptionKey = encryptionKey,
            EnableMemoryMapping = true,
            AutoVacuum = true,
            AutoVacuumMode = VacuumMode.Quick
        };
    }

    /// <summary>
    /// Creates options optimized for high-performance workloads.
    /// </summary>
    /// <returns>Performance-optimized options</returns>
    public static DatabaseOptions CreatePerformanceOptimized()
    {
        return new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 8192, // Larger pages for sequential access
            EnableMemoryMapping = true,
            WalBufferSizePages = 2048, // 16MB WAL
            UseUnbufferedIO = true, // Direct I/O for SSDs
            AutoVacuum = true,
            AutoVacuumMode = VacuumMode.Incremental
        };
    }

    /// <summary>
    /// Creates options optimized for embedded/mobile scenarios.
    /// </summary>
    /// <returns>Embedded-optimized options</returns>
    public static DatabaseOptions CreateEmbeddedOptimized()
    {
        return new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 2048, // Smaller pages for memory efficiency
            EnableMemoryMapping = false, // Avoid mmap on mobile
            WalBufferSizePages = 256, // 512KB WAL
            AutoVacuum = true,
            AutoVacuumMode = VacuumMode.Incremental,
            FragmentationThreshold = 50 // More tolerant of fragmentation
        };
    }
}
