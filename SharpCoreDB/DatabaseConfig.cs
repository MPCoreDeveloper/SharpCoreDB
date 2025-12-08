// <copyright file="DatabaseConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

using SharpCoreDB.Services;

/// <summary>
/// Configuration options for database performance and behavior.
/// Uses C# 9.0+ init-only properties for immutable configuration.
/// </summary>
public class DatabaseConfig
{
    /// <summary>
    /// Gets a value indicating whether gets whether encryption should be disabled for maximum performance.
    /// WARNING: Disabling encryption removes AES-256-GCM protection. Use only for trusted environments.
    /// </summary>
    public bool NoEncryptMode { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether gets whether query caching is enabled.
    /// </summary>
    public bool EnableQueryCache { get; init; } = true;

    /// <summary>
    /// Gets the query cache size limit.
    /// </summary>
    public int QueryCacheSize { get; init; } = 1024;

    /// <summary>
    /// Gets the WAL buffer size in bytes.
    /// </summary>
    public int WalBufferSize { get; init; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Gets a value indicating whether gets whether hash indexes should be used.
    /// </summary>
    public bool EnableHashIndexes { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether gets whether to use buffered I/O for high-performance mode.
    /// </summary>
    public bool UseBufferedIO { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether gets whether to use memory-mapped files for improved read performance.
    /// When enabled, files larger than 10 MB will be accessed via memory-mapped I/O,
    /// reducing disk operations and improving SELECT query performance by 30-50%.
    /// </summary>
    public bool UseMemoryMapping { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether gets whether to perform GC.Collect after batch operations for memory cleanup.
    /// </summary>
    public bool CollectGCAfterBatches { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether the page cache is enabled.
    /// When enabled, frequently accessed database pages are cached in memory using a lock-free CLOCK eviction algorithm.
    /// This can improve read performance by 5-10x by avoiding disk I/O for hot pages.
    /// </summary>
    public bool EnablePageCache { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of pages to cache in memory.
    /// Each page is 4KB by default. Recommended: 1000-10000 for typical workloads.
    /// Memory usage = PageCacheCapacity * PageSize (e.g., 10000 * 4KB = 40MB).
    /// </summary>
    public int PageCacheCapacity { get; init; } = 1000;

    /// <summary>
    /// Gets the page size in bytes for the page cache.
    /// Standard page size is 4096 bytes (4KB). Larger pages (8KB, 16KB) can improve
    /// throughput for large records at the cost of memory efficiency.
    /// </summary>
    public int PageSize { get; init; } = 4096;

    /// <summary>
    /// Gets the WAL durability mode.
    /// FullSync uses FileStream.Flush(true) for full durability.
    /// Async relies on OS buffering for better performance.
    /// </summary>
    public DurabilityMode WalDurabilityMode { get; init; } = DurabilityMode.FullSync;

    /// <summary>
    /// Gets the maximum number of commits to batch in a single WAL group commit.
    /// Higher values improve throughput but increase latency.
    /// </summary>
    public int WalMaxBatchSize { get; init; } = 100;

    /// <summary>
    /// Gets the maximum delay in milliseconds before flushing a WAL batch.
    /// Lower values reduce latency but decrease batching efficiency.
    /// </summary>
    public int WalMaxBatchDelayMs { get; init; } = 10;

    /// <summary>
    /// Gets a value indicating whether to use the new group commit WAL implementation.
    /// When enabled, multiple commits are batched into a single fsync for improved throughput.
    /// </summary>
    public bool UseGroupCommitWal { get; init; } = false;

    /// <summary>
    /// Gets default configuration with encryption enabled.
    /// </summary>
    public static DatabaseConfig Default => new();

    /// <summary>
    /// Gets high-performance configuration with encryption disabled.
    /// </summary>
    public static DatabaseConfig HighPerformance => new()
    {
        NoEncryptMode = true,
        EnableQueryCache = true,
        EnableHashIndexes = true,
        WalBufferSize = 128 * 1024, // 128KB
        UseBufferedIO = true,
        UseMemoryMapping = true,
        CollectGCAfterBatches = true,
        EnablePageCache = true,
        PageCacheCapacity = 10000, // 40MB cache
        PageSize = 4096,
    };

    /// <summary>
    /// Gets low-memory configuration optimized for memory-constrained environments.
    /// </summary>
    public static DatabaseConfig LowMemory => new()
    {
        NoEncryptMode = false,
        EnableQueryCache = true,
        QueryCacheSize = 256,
        EnableHashIndexes = true,
        WalBufferSize = 64 * 1024, // 64KB
        UseMemoryMapping = false,
        EnablePageCache = true,
        PageCacheCapacity = 100, // Only 400KB cache
        PageSize = 4096,
    };
}
