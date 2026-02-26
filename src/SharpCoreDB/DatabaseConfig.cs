// <copyright file="DatabaseConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

using SharpCoreDB.Services;
using SharpCoreDB.Interfaces;

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
    /// Gets a value indicating whether batch encryption is enabled during bulk operations.
    /// When true, rows are accumulated in plaintext and encrypted in 64KB batches.
    /// Expected gain: 6-10x faster than per-row encryption for bulk inserts.
    /// Only effective when UseOptimizedInsertPath = true.
    /// Target: 10k encrypted inserts from 666ms to less than 100ms.
    /// </summary>
    public bool EnableBatchEncryption { get; init; } = false;

    /// <summary>
    /// Gets the batch encryption buffer size in KB.
    /// Larger buffers reduce encryption operations but increase memory usage.
    /// Range: 16-128KB
    /// Default: 64KB (good balance for 1K-10K row batches)
    /// Recommended: 32-64KB for most workloads.
    /// </summary>
    public int BatchEncryptionSizeKB { get; init; } = 64;

    /// <summary>
    /// Gets a value indicating whether High-Speed Insert mode is enabled.
    /// When enabled: Buffers encryption operations, uses larger WAL batches (1000+ rows), and optimizes for bulk throughput.
    /// BENCHMARK RESULTS (10K records):
    /// - HighSpeed: 261ms, 15.98 MB (10% faster, 76% less memory vs baseline)
    /// - Standard:  252ms, 15.64 MB (13% faster, 77% less memory vs baseline) ⭐ RECOMMENDED
    /// </summary>
    public bool HighSpeedInsertMode { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether to use optimized insert path with:
    /// 1. Delayed columnar transpose (only on first SELECT)
    /// 2. Buffered AES encryption (batch encrypt instead of per-row)
    /// 3. Optional encryption toggle during bulk import
    /// 
    /// Expected improvement: 70-80% (252ms → 50-75ms for 10K inserts)
    /// Target: Within 20-30% of SQLite performance (42ms → 50-55ms achievable)
    /// 
    /// When enabled, use Database.BeginBulkImport() / CompleteBulkImport() for best results.
    /// </summary>
    public bool UseOptimizedInsertPath { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether to disable encryption during bulk import.
    /// When true, data is written UNENCRYPTED and re-encrypted after import completes.
    /// WARNING: Use ONLY in trusted environments (no network access, secure storage).
    /// 
    /// Expected gain: 40-50% faster (140ms → 70-85ms for 10K inserts)
    /// Only effective when UseOptimizedInsertPath = true.
    /// </summary>
    public bool ToggleEncryptionDuringBulk { get; init; } = false;

    /// <summary>
    /// Gets the buffer size (in KB) for buffered AES encryption.
    /// Larger buffers reduce encryption overhead but increase memory usage.
    /// Default: 32KB (good balance for 1K-10K row batches)
    /// Range: 16-128KB
    /// </summary>
    public int EncryptionBufferSizeKB { get; init; } = 32;

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
    /// Default increased to 4MB for better bulk insert performance.
    /// </summary>
    public int WalBufferSize { get; init; } = 4 * 1024 * 1024; // 4MB (increased from 1MB)

    /// <summary>
    /// Gets the buffer pool size in bytes for general purpose buffers.
    /// Platform-specific defaults: 8MB (mobile), 64MB (desktop).
    /// </summary>
    public int BufferPoolSize { get; init; } = 32 * 1024 * 1024; // 32MB default

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
    /// When HighSpeedInsertMode is enabled, this defaults to 1000 (vs 100 normal).
    /// </summary>
    public int WalMaxBatchSize { get; init; } = 100;

    /// <summary>
    /// Gets the group commit size for bulk operations.
    /// Used when HighSpeedInsertMode is enabled to batch larger chunks.
    /// Default: 1000 rows per commit.
    /// </summary>
    public int GroupCommitSize { get; init; } = 1000;

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
    /// Gets a value indicating whether to enable adaptive WAL batch tuning.
    /// When enabled, batch size automatically scales based on queue depth and concurrency.
    /// Expected gain: +15-25% throughput at 32+ threads.
    /// Recommended: true for production (handles variable workloads).
    /// </summary>
    public bool EnableAdaptiveWalBatching { get; init; } = true;

    /// <summary>
    /// Gets the WAL batch size multiplier for adaptive tuning.
    /// Initial batch size = ProcessorCount * Multiplier.
    /// Default: 128 (e.g., 8 cores * 128 = 1024 operations).
    /// Use higher values (256, 512) for extreme concurrency (64+ threads).
    /// </summary>
    public int WalBatchMultiplier { get; init; } = 128;

    /// <summary>
    /// Gets the SQL query validation mode.
    /// Strict mode (recommended for production) throws exceptions on unsafe queries.
    /// Lenient mode (development) shows warnings only.
    /// </summary>
    public SqlQueryValidator.ValidationMode SqlValidationMode { get; init; } = SqlQueryValidator.ValidationMode.Lenient;

    /// <summary>
    /// Gets a value indicating whether to strictly validate that named parameter keys (@param) 
    /// match the parameter dictionary keys. When true, warns about missing or unused parameters.
    /// Recommended for development to catch parameter mismatches early.
    /// </summary>
    public bool StrictParameterValidation { get; init; } = true;

    /// <summary>
    /// Enables caching of compiled query plans (normalized SQL + parameter shape).
    /// Zero-breaking changes: falls back to dynamic parsing if cache miss.
    /// </summary>
    public bool EnableCompiledPlanCache { get; init; } = true;

    /// <summary>
    /// Maximum entries for compiled plan cache (LRU eviction).
    /// </summary>
    public int CompiledPlanCacheCapacity { get; init; } = 2048;

    /// <summary>
    /// When true, normalizes SQL (whitespace, case, literals) for better cache hit rate.
    /// </summary>
    public bool NormalizeSqlForPlanCache { get; init; } = true;

    /// <summary>
    /// Enables SIMD filtering and projection pushdown in compiled plans.
    /// </summary>
    public bool EnableSimdAndProjectionPushdown { get; init; } = true;

    /// <summary>
    /// Enables B-tree index selection hints in query executor.
    /// </summary>
    public bool EnableBTreeSelection { get; init; } = true;

    /// <summary>
    /// Enables delta-update support (Phase 3.3).
    /// When enabled, UPDATE operations store only changed fields instead of full records.
    /// Improves performance for workloads with frequent small updates.
    /// </summary>
    public bool EnableDeltaUpdates { get; init; } = false;

    /// <summary>
    /// Gets default configuration with encryption enabled.
    /// </summary>
    public static DatabaseConfig Default => new();

    /// <summary>
    /// Gets platform-specific optimal configuration for the current OS.
    /// Uses PlatformHelper to detect mobile vs desktop and sets appropriate defaults.
    /// </summary>
    public static DatabaseConfig PlatformOptimized => PlatformHelper.GetPlatformDefaults();

    /// <summary>
    /// Gets high-performance configuration with encryption disabled.
    /// OPTIMAL for production workloads with concurrent writes.
    /// </summary>
    public static DatabaseConfig HighPerformance => new()
    {
        NoEncryptMode = true,
        HighSpeedInsertMode = false, // Disabled for mixed OLTP workloads
        
        // ✅ GroupCommitWAL with ADAPTIVE batching for production
        UseGroupCommitWal = true,
        EnableAdaptiveWalBatching = true,  // ✅ NEW: Auto-scales with load
        WalBatchMultiplier = 128,          // Default: ProcessorCount * 128
        WalDurabilityMode = DurabilityMode.Async,
        WalMaxBatchSize = 0,               // 0 = use adaptive (ProcessorCount * 128)
        WalMaxBatchDelayMs = 10,
        GroupCommitSize = 1000,            // Bulk commit size
        
        // Query cache for repeated queries
        EnableQueryCache = true,
        QueryCacheSize = 2000,
        
        // Hash indexes for O(1) lookups
        EnableHashIndexes = true,
        
        // Large WAL and buffer pool
        WalBufferSize = 4 * 1024 * 1024,  // 4MB (increased from 128KB)
        BufferPoolSize = 64 * 1024 * 1024,
        
        // I/O optimizations
        UseBufferedIO = true,
        UseMemoryMapping = true,
        CollectGCAfterBatches = true,
        
        // Large page cache for read performance
        EnablePageCache = true,
        PageCacheCapacity = 10000,
        PageSize = 4096,
    };

    /// <summary>
    /// Gets configuration optimized for bulk import scenarios.
    /// Maximizes throughput for large INSERT operations (10K-1M rows).
    /// Expected: 2-4x faster than HighPerformance for bulk inserts.
    /// </summary>
    public static DatabaseConfig BulkImport => new()
    {
        NoEncryptMode = true,
        HighSpeedInsertMode = true, // ✅ ENABLED for maximum bulk insert speed
        
        // ✅ AGGRESSIVE WAL batching for bulk imports
        UseGroupCommitWal = true,
        EnableAdaptiveWalBatching = true,
        WalBatchMultiplier = 512,          // ✅ EXTREME: ProcessorCount * 512
        WalDurabilityMode = DurabilityMode.Async, // Fast async writes
        WalMaxBatchSize = 0,               // Adaptive (scales to 10k)
        WalMaxBatchDelayMs = 1,            // Minimal delay
        GroupCommitSize = 5000,            // ✅ LARGE: 5000 rows per commit
        
        // Disable query cache (bulk import doesn't repeat queries)
        EnableQueryCache = false,
        
        // Hash indexes enabled but will be built AFTER import
        EnableHashIndexes = true,
        
        // ✅ VERY LARGE buffers for bulk import
        WalBufferSize = 16 * 1024 * 1024,  // 16MB WAL buffer
        BufferPoolSize = 256 * 1024 * 1024, // 256MB buffer pool
        
        // I/O optimizations
        UseBufferedIO = true,
        UseMemoryMapping = false,          // Bulk writes don't benefit from mmap
        CollectGCAfterBatches = true,      // Clean up after each batch
        
        // Minimal page cache (focus on writes, not reads)
        EnablePageCache = true,
        PageCacheCapacity = 1000,          // Small cache (bulk import is write-heavy)
        PageSize = 4096,
        
        SqlValidationMode = SqlQueryValidator.ValidationMode.Disabled,
        StrictParameterValidation = false,
        
        // ✅ Auto-select storage (typically PageBased for bulk import)
        StorageEngineType = Interfaces.StorageEngineType.Auto,
        WorkloadHint = WorkloadHint.WriteHeavy
    };

    /// <summary>
    /// Gets benchmark-optimized configuration with SQL validation disabled for maximum performance.
    /// ONLY use for trusted benchmark code - no security validation!
    /// Optimized for single-threaded sequential operations.
    /// </summary>
    public static DatabaseConfig Benchmark => new()
    {
        NoEncryptMode = true,
        HighSpeedInsertMode = true,
        
        UseGroupCommitWal = false,
        EnableAdaptiveWalBatching = false,
        GroupCommitSize = 1000,
        
        EnableQueryCache = true,
        QueryCacheSize = 2000,
        
        EnableHashIndexes = true,
        
        WalBufferSize = 8 * 1024 * 1024,
        BufferPoolSize = 128 * 1024 * 1024,
        
        UseBufferedIO = true,
        UseMemoryMapping = true,
        CollectGCAfterBatches = true,
        
        EnablePageCache = true,
        PageCacheCapacity = 10000,
        PageSize = 4096,
        
        SqlValidationMode = SqlQueryValidator.ValidationMode.Disabled,
        StrictParameterValidation = false,
        
        // ✅ Auto-select storage based on workload
        StorageEngineType = Interfaces.StorageEngineType.Auto,
        WorkloadHint = WorkloadHint.General
    };

    /// <summary>
    /// ✅ NEW: Configuration optimized for analytics workloads.
    /// Automatically selects COLUMNAR storage for fast aggregations and scans.
    /// Expected: 5-10x faster GROUP BY, SUM, AVG queries vs row-based storage.
    /// </summary>
    public static DatabaseConfig Analytics => new()
    {
        NoEncryptMode = true,
        HighSpeedInsertMode = false, // Analytics is read-heavy
        
        UseGroupCommitWal = true,
        EnableAdaptiveWalBatching = true,
        WalBatchMultiplier = 128,
        WalDurabilityMode = DurabilityMode.Async,
        
        // Large query cache for repeated analytical queries
        EnableQueryCache = true,
        QueryCacheSize = 5000,
        
        EnableHashIndexes = true,
        
        WalBufferSize = 4 * 1024 * 1024,
        BufferPoolSize = 256 * 1024 * 1024, // Large buffer for analytics
        
        UseBufferedIO = true,
        UseMemoryMapping = true, // Memory mapping helps with scans
        CollectGCAfterBatches = true,
        
        // Very large page cache for scan performance
        EnablePageCache = true,
        PageCacheCapacity = 20000, // 80MB cache (20K × 4KB)
        PageSize = 4096,
        
        SqlValidationMode = SqlQueryValidator.ValidationMode.Lenient,
        
        // ✅ AUTO-SELECT: COLUMNAR storage for analytics
        StorageEngineType = Interfaces.StorageEngineType.Auto,
        WorkloadHint = WorkloadHint.Analytics
    };

    /// <summary>
    /// ✅ NEW: Configuration optimized for OLTP workloads.
    /// Automatically selects PAGE_BASED storage for fast random updates.
    /// Expected: 3-5x faster UPDATE/DELETE vs append-only storage.
    /// </summary>
    public static DatabaseConfig OLTP => new()
    {
        NoEncryptMode = true,
        HighSpeedInsertMode = false,
        
        UseGroupCommitWal = true,
        EnableAdaptiveWalBatching = true,
        WalBatchMultiplier = 128,
        WalDurabilityMode = DurabilityMode.FullSync, // OLTP needs durability
        
        EnableQueryCache = true,
        QueryCacheSize = 2000,
        
        EnableHashIndexes = true,
        
        WalBufferSize = 4 * 1024 * 1024,
        BufferPoolSize = 64 * 1024 * 1024,
        
        UseBufferedIO = true,
        UseMemoryMapping = true,
        CollectGCAfterBatches = false, // OLTP is latency-sensitive
        
        // Medium page cache optimized for hot pages
        EnablePageCache = true,
        PageCacheCapacity = 10000,
        PageSize = 8192, // 8KB pages for OLTP (matches PageManager)
        
        SqlValidationMode = SqlQueryValidator.ValidationMode.Strict,
        StrictParameterValidation = true,
        
        // ✅ AUTO-SELECT: PAGE_BASED storage for OLTP
        StorageEngineType = Interfaces.StorageEngineType.Auto,
        WorkloadHint = WorkloadHint.WriteHeavy
    };

    /// <summary>
    /// ✅ NEW: Configuration optimized for read-heavy workloads.
    /// Automatically selects COLUMNAR storage for fast SELECT queries.
    /// Expected: 5-10x faster SELECT with column pruning vs row-based storage.
    /// </summary>
    public static DatabaseConfig ReadHeavy => new()
    {
        NoEncryptMode = true,
        HighSpeedInsertMode = false,
        
        UseGroupCommitWal = true,
        EnableAdaptiveWalBatching = false, // Read-heavy doesn't need write batching
        WalDurabilityMode = DurabilityMode.Async,
        
        // Very large query cache for read-heavy workloads
        EnableQueryCache = true,
        QueryCacheSize = 10000,
        
        EnableHashIndexes = true,
        
        WalBufferSize = 2 * 1024 * 1024, // Smaller WAL (less writes)
        BufferPoolSize = 128 * 1024 * 1024,
        
        UseBufferedIO = true,
        UseMemoryMapping = true, // Memory mapping perfect for reads
        CollectGCAfterBatches = false,
        
        // Very large page cache for read performance
        EnablePageCache = true,
        PageCacheCapacity = 25000, // 100MB cache (25K × 4KB)
        PageSize = 4096,
        
        SqlValidationMode = SqlQueryValidator.ValidationMode.Lenient,
        
        // ✅ AUTO-SELECT: COLUMNAR storage for read-heavy
        StorageEngineType = Interfaces.StorageEngineType.Auto,
        WorkloadHint = WorkloadHint.ReadHeavy
    };

    /// <summary>
    /// Gets the storage engine type to use for this database.
    /// - AppendOnly: Sequential writes, best for append-heavy workloads
    /// - PageBased: In-place updates, best for OLTP workloads with updates/deletes (✅ READY: >10K records)
    /// - Columnar: Column-oriented storage, best for analytics and aggregations
    /// - Auto: Intelligent selection based on WorkloadHint (RECOMMENDED)
    /// Default: Auto (selects based on WorkloadHint)
    /// </summary>
    public Interfaces.StorageEngineType StorageEngineType { get; init; } = Interfaces.StorageEngineType.Auto;

    /// <summary>
    /// Gets the workload hint to guide automatic storage engine selection.
    /// ✅ NEW: Smart storage selection based on workload characteristics!
    /// - ReadHeavy: Optimized for SELECT queries → COLUMNAR storage
    /// - Analytics: Optimized for aggregates/scans → COLUMNAR storage
    /// - WriteHeavy: Optimized for INSERT/UPDATE → PAGE_BASED storage
    /// - General: Balanced for mixed workloads → PAGE_BASED storage
    /// 
    /// When StorageEngineType = Auto, the engine is selected based on this hint.
    /// </summary>
    public WorkloadHint WorkloadHint { get; init; } = WorkloadHint.General;

    /// <summary>
    /// Gets the optimal storage engine type based on workload hint.
    /// This method is called when StorageEngineType = Auto.
    /// </summary>
    /// <returns>The recommended storage engine type for the workload.</returns>
    public Interfaces.StorageEngineType GetOptimalStorageEngine()
    {
        // If explicitly set, use that
        if (StorageEngineType != Interfaces.StorageEngineType.Auto)
        {
            return StorageEngineType;
        }

        // Auto-select based on workload hint
        return WorkloadHint switch
        {
            WorkloadHint.ReadHeavy => Interfaces.StorageEngineType.Columnar,
            WorkloadHint.Analytics => Interfaces.StorageEngineType.Columnar,
            WorkloadHint.WriteHeavy => Interfaces.StorageEngineType.PageBased,
            WorkloadHint.General => Interfaces.StorageEngineType.PageBased,
            _ => Interfaces.StorageEngineType.PageBased // Default to PAGE_BASED (safest choice)
        };
    }

    /// <summary>
    /// Threshold for automatic compaction in COLUMNAR storage.
    /// When the sum of UPDATEs and DELETEs since the last compaction reaches this threshold,
    /// a background compaction is triggered.
    /// Default: 1000. Set to 0 to disable auto-compaction.
    /// </summary>
    public long ColumnarAutoCompactionThreshold { get; init; } = 1000;

    /// <summary>
    /// Gets the WAL buffer size in pages for single-file storage providers.
    /// Default: 2048 pages (with 4KB page size = 8MB). Larger buffers reduce flush frequency
    /// by batching more writes, improving throughput for random writes.
    /// </summary>
    public int WalBufferSizePages { get; init; } = 2048; // 2048 pages × 4KB = 8MB WAL

    // I/O tuning defaults for benchmarks (20–50% I/O improvement expected)
    // Do NOT duplicate existing properties; PageCacheCapacity/EnablePageCache/UseMemoryMapping already exist above.

    // NOTE: FileShareMode belongs to DatabaseOptions, not DatabaseConfig.

}

/// <summary>
/// Workload hints for automatic storage engine selection.
/// Guides intelligent storage mode choice for different workloads.
/// </summary>
public enum WorkloadHint
{
    /// <summary>
    /// General-purpose workload with mixed operations. Recommendation: PAGE_BASED storage.
    /// </summary>
    General = 0,
    /// <summary>
    /// Read-heavy workload with frequent SELECT queries (80%+ reads). Recommendation: COLUMNAR storage.
    /// </summary>
    ReadHeavy = 1,
    /// <summary>
    /// Analytics workload with heavy aggregations and scans. Recommendation: COLUMNAR storage.
    /// </summary>
    Analytics = 2,
    /// <summary>
    /// Write-heavy workload with frequent INSERT/UPDATE/DELETE (50%+ writes). Recommendation: PAGE_BASED storage.
    /// </summary>
    WriteHeavy = 3
}
