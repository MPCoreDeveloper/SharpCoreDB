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
    /// Gets a value indicating whether new directory-mode tables use the fixed-width record layout
    /// with out-of-line overflow (the SQLite-model). Fixed-size columns live at constant record
    /// offsets; variable-length (TEXT/BLOB) values are stored in a per-table overflow arena, so the
    /// record length is constant per schema and every UPDATE is an in-place overwrite.
    /// ⚠️ OPT-IN FORMAT: only enable on databases whose columnar tables are created with the same
    /// flag, and never share such databases with tooling built before this option. Existing tables
    /// are unaffected.
    /// </summary>
    public bool FixedWidthRecordLayout { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether NEW columnar tables that declare a PRIMARY KEY default to
    /// the fixed-width record layout with out-of-line overflow (see
    /// <see cref="FixedWidthRecordLayout"/>), so UPDATE/DELETE by key are in-place overwrites.
    /// This only affects tables created after the setting is in effect — existing tables keep
    /// their persisted record format until an explicit opt-in (<see cref="FixedWidthRecordLayout"/>)
    /// or <c>MigrateTableToFixedWidth</c> converts them. Set to <see langword="false"/> to keep
    /// creating every new table with the legacy variable-length records.
    /// </summary>
    public bool AutoFixedWidthRecords { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether SQLite integer type affinity is used for DDL type mapping.
    /// When <see langword="true"/> (opt-in), <c>INTEGER</c> maps to <see cref="DataType.Long"/> (Int64),
    /// matching SQLite semantics so values like <c>DateTime.UtcNow.Ticks</c> (~6.4e17) fit.
    /// When <see langword="false"/> (default), <c>INTEGER</c> maps to <see cref="DataType.Integer"/> (Int32)
    /// for full backward compatibility with existing databases and consumer code.
    /// ⚠️ OPT-IN BREAKING: enabling changes the persisted type for newly created/added INTEGER columns;
    /// existing columns are unaffected (their type is stored in table metadata).
    /// </summary>
    public bool UseSqliteIntegerAffinity { get; init; } = false;

    /// <summary>
    /// Gets a value indicating whether table payload records are encrypted at rest with per-record
    /// AES-256-GCM. <b>Enabled by default.</b>
    /// <para>
    /// With this on (the default) a table's data file carries an 8-byte magic header followed by
    /// per-record ciphertext, and the overflow arena holds ciphertext as well — so table data, arena,
    /// metadata and transaction files are all protected. <see cref="NoEncryptMode"/> being
    /// <see langword="true"/> is the single, documented raw-speed opt-out and leaves every file
    /// plaintext.
    /// </para>
    /// <para>
    /// Measured cost of the default versus that opt-out: ≈1.11× CREATE/INSERT, no measurable UPDATE
    /// penalty on the contiguous paths, roughly double the file size for the per-record GCM framing,
    /// and one whole-file decrypt per full-scan-shaped query (the scan reads the file once). Numbers in
    /// <c>docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md</c> §3-1c and §3-1d.
    /// </para>
    /// <para>
    /// ⚠️ FORMAT: a file is encrypted only when it was CREATED with this option on. A pre-existing
    /// plaintext database stays byte-for-byte readable and is never mixed with encrypted records — but
    /// its tables are upgraded to the encrypted format when they are compacted, because compaction
    /// rewrites them through a brand-new file.
    /// </para>
    /// </summary>
    public bool EnableAtRestRecordEncryption { get; init; } = true;

    /// <summary>
    /// <para>
    /// Opt-in buffered append mode for the single-row INSERT path. When <c>true</c>, a row appended
    /// outside a transaction is buffered in memory with the rest of the pending appends and written
    /// with ONE open/write/close when a flush boundary is reached — instead of a
    /// <see cref="System.IO.FileStream"/> open, write-through and close per row.
    /// </para>
    /// <para>
    /// MEASURED: the per-row open dominates this path. A 64-byte record costs ~512 µs through the
    /// write-through open/close, of which only ~4.5 µs is the actual buffered write; the same records
    /// reach ~0.43 µs/row through the batched path. The whole gap is the handle, not the data.
    /// </para>
    /// <para>
    /// DURABILITY TRADE (this is the reason the feature is opt-in and off by default): the default
    /// behavior forces each record to the device before the call returns. Buffered mode hands the
    /// bytes to the operating system at the flush boundary instead — a process crash still cannot
    /// lose them, but a power loss can lose everything after the last flush. The window is bounded by
    /// <see cref="AppendBufferFlushThresholdBytes"/> and <see cref="AppendBufferFlushIntervalMs"/>, and
    /// every structural operation (commit, <c>Database.Flush()</c>, compaction, fixed-width migration,
    /// overflow-arena compaction, <c>DROP TABLE</c>, dispose) flushes first, so the data is never
    /// invisible to the engine — only the durability window changes.
    /// </para>
    /// <para>
    /// Reads stay correct while rows are buffered: point lookups and full scans overlay the buffer, so
    /// a row is visible to the same session the moment it is inserted.
    /// </para>
    /// <para>Default <c>false</c>: byte-for-byte identical behavior to the unbuffered engine.</para>
    /// </summary>
    public bool EnableBufferedAppends { get; init; } = false;

    /// <summary>
    /// Gets the number of payload bytes a variable-length column may store <b>inline</b> in its fixed-width record
    /// slot instead of writing them to the overflow arena. <b>Default 0 — the existing layout, byte for byte.</b>
    /// <para>
    /// Context (plan §4b): the fixed-width record already implements the stable-slot + overflow model — fixed-size
    /// columns inline, String/Blob as a 5-byte <c>[null-flag(1)][overflow offset(4)]</c> slot — so *every*
    /// variable-length value, however short, costs an arena write. Measured on the multi-row pass those arena
    /// stages are <b>arena-write 2.26 µs + arena-append 1.79 µs = ~4.05 µs/row, ~24 %</b>, and on that schema every
    /// TEXT value is short (<c>User1</c>, <c>user1@test.com</c>, <c>payload-1</c>).
    /// </para>
    /// <para>
    /// Above zero, a variable slot becomes <c>[null-flag(1)][offset(4)][inline length(2)][inline payload(N)]</c> —
    /// still a <b>constant size per schema</b>, so the position-stable record and every in-place update and delete
    /// fast path keep working — and a payload that fits is written in the slot with <c>null-flag = 2</c>. NULL (0)
    /// and overflow (1) keep their existing encodings and offsets, so the inline case is purely additive.
    /// </para>
    /// <para>
    /// ⚠️ <b>This changes the on-disk record layout.</b> The layout is computed from the schema rather than stored,
    /// so a file written with one value of this property must be opened with the same one — which is why the default
    /// preserves the current layout exactly, and why enabling it belongs with the record-version and migration work
    /// (plan §4b): rewrite the table, do not flip it under existing data. 16 is the value the benchmark schema
    /// needs, because it inlines all three of its TEXT columns.
    /// </para>
    /// </summary>
    public int FixedWidthInlineValueBytes { get; init; }

    /// <summary>
    /// Gets the pending-append threshold, in bytes, at which buffered appends are flushed
    /// (only used when <see cref="EnableBufferedAppends"/> is enabled). Bounds both the memory the
    /// buffer can hold and the amount of work a crash can lose. Default 1 MB.
    /// </summary>
    public int AppendBufferFlushThresholdBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Gets the maximum age, in milliseconds, of an unflushed buffered append before it is flushed by
    /// the next append on that database (only used when <see cref="EnableBufferedAppends"/> is enabled).
    /// Together with <see cref="AppendBufferFlushThresholdBytes"/> this bounds the durability window for
    /// slow, low-volume writers. Default 10 ms. Set to 0 to flush only on the byte threshold and on the
    /// explicit structural boundaries.
    /// </summary>
    public int AppendBufferFlushIntervalMs { get; init; } = 10;

    /// <summary>
    /// Gets a value indicating whether DELETE defers index maintenance. <b>Enabled by default.</b>
    /// <para>
    /// When on, a DELETE writes only the durable tombstone and skips the per-key PK B-tree removal and
    /// the per-key hash-index removal. Point lookups already treat a tombstoned position as absent (a
    /// null read) and insert-time uniqueness verifies the stored position is live before rejecting a
    /// re-INSERT, so a stale index entry is invisible to callers. The PK B-tree is rebuilt from the data
    /// file (dropping tombstoned entries) on reopen and when <see cref="DeferredDeleteIndexThreshold"/>
    /// is crossed; hash indexes rebuild on reopen. Measured on the random-key DELETE workload:
    /// <b>2.19x</b> (plaintext) / <b>1.19x</b> (encrypted default) throughput.
    /// </para>
    /// Set to <see langword="false"/> for the eager behaviour (every DELETE removes its index entries
    /// immediately), which keeps <c>GetHashIndexStatistics</c> exact between deletes.
    /// </summary>
    public bool EnableDeferredDeleteIndexes { get; init; } = true;

    /// <summary>
    /// Gets the number of deferred DELETE primary keys that may accumulate in the stale PK B-tree before
    /// it is rebuilt from the data file (dropping tombstoned entries). Only used when
    /// <see cref="EnableDeferredDeleteIndexes"/> is enabled.
    /// <para>
    /// ⚠️ The rebuild is a full **O(n)** pass over the data file (`RebuildPrimaryKeyIndexFromDisk`), so
    /// this is a **memory/latency safety valve, not a maintenance schedule**: each rebuild costs about
    /// as much as rebuilding the whole PK index, and it only pays off when it is rare relative to the
    /// deletes it covers. The default is therefore deliberately high (100,000). Measured: with a
    /// threshold of 10,000 a single 10,000-delete batch triggered the rebuild and the random-key DELETE
    /// workload fell from 222,752 to 70,248 ops/s — the deferral win is lost whenever the rebuild fires
    /// at the same frequency as the deletes. Setting it to a small value is only sensible on a table
    /// where the whole index is cheap to rebuild.
    /// </para>
    /// Staleness is also bounded by the table's own key count (a B-tree holds one entry per unique key),
    /// and a reopen rebuilds the index for free, so the practical bound is "until the next reopen".
    /// </summary>
    public int DeferredDeleteIndexThreshold { get; init; } = 100000;

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
    /// <para>
    /// <b>What this property does NOT do to the table append (reversed 2026-09-16).</b> For one day
    /// (2026-09-15, §5 item 2 / A1) <c>Async</c> also disabled write-through for table appends, buffering them
    /// exactly as <see cref="EnableBufferedAppends"/> does. That coupling was measured and removed: this
    /// property is a <i>durability</i> statement, and six presets set it for durability reasons across opposite
    /// append regimes — <c>BulkImport</c> and the write-once logging sink (append-only, where buffering wins:
    /// 9.4× on 20,000 standalone single-row <c>INSERT</c> statements, 1,127.61 → 119.79 µs/row), but also the
    /// mixed-OLTP, analytics, read-heavy and mobile configurations (which read between appends, where buffering
    /// costs). Measured on the tuned <c>--pk</c> arm, one variable, same build, medians of 3, isolated: with
    /// buffering engaged, <b>UPDATE 230,722 → 356,135 (+54 %)</b>, <b>DELETE 168,804 → 216,909 (+28 %)</b>,
    /// <b>INSERT 81,344 → 99,575 (+22 %)</b>, READ flat, with SQLite's reference within 5 %. The append path
    /// therefore writes through per record at either setting, and table-append buffering is requested only by
    /// <see cref="EnableBufferedAppends"/> — which the bulk-oriented presets now set explicitly.
    /// </para>
    /// <para>
    /// <b>Trade:</b> buffered rows are lost by a crash — process kill as well as power loss — until a
    /// boundary flushes them, so choose <c>Async</c> only where that window is acceptable.
    /// </para>
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
    /// When true, hash indexes use the native-memory <c>UnsafeEqualityIndex</c> backend
    /// (open-addressing, NativeMemory arenas, zero GC pressure on lookups) instead of
    /// <c>Dictionary&lt;object, List&lt;long&gt;&gt;</c>.
    /// Delivers ~2x faster point-lookups on high-cardinality string columns.
    /// Defaults to <see langword="false"/>; enable for read-heavy workloads where insert
    /// throughput is not the bottleneck. When enabled, inserts allocate a byte[] key per
    /// row via BuildUnsafeKey — avoid for bulk-insert workloads.
    /// </summary>
    public bool EnableUnsafeEqualityIndex { get; init; } = false;

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
        WalDurabilityMode = DurabilityMode.Async, // Fast async writes (WAL only — see that property's note)
        // Append buffering is the *other* half of the bulk trade, and since 2026-09-16 it is requested
        // explicitly instead of being inherited from `Async`: a bulk import appends without reading in between,
        // which is exactly the shape buffering is for (~512 µs -> ~4.5 µs per 64-byte row), whereas a reader in
        // the loop pays 22-54 % for it.
        EnableBufferedAppends = true,
        WalMaxBatchSize = 0,               // Adaptive (scales to 10k)
        WalMaxBatchDelayMs = 1,            // Minimal delay
        GroupCommitSize = 5000,            // ✅ LARGE: 5000 rows per commit
        
        // Disable query cache (bulk import doesn't repeat queries)
        EnableQueryCache = false,
        
        // Hash indexes are enabled. This comment previously claimed they "will be built AFTER import" — they are
        // not: nothing in the engine defers the build, so the per-row hash maintenance is paid on every INSERT
        // even in this preset. Measured on the multi-row batch path (2026-09-16), per row: `hash-index` 1.7 µs
        // (10 % of the 17.2 µs/row budget) and `index-maint` (the PK B-tree insert) 2.2 µs (13 %). Deferring both
        // to a single bulk build is priority 2 of the plan's re-derived order (§9) and the machinery for it exists
        // — RebuildHashIndex / RebuildAllIndexesFromFile, plus ValidateBatchPrimaryKeysUpfront for the uniqueness
        // the index would otherwise enforce — but it is NOT implemented, so this preset must not claim it.
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
    /// - WriteHeavy: Optimized for INSERT/UPDATE → currently APPEND_ONLY/COLUMNAR too (PageBased is
    ///   opt-in only via an explicit <see cref="StorageEngineType.PageBased"/>; see the note below)
    /// - General: Balanced for mixed workloads → APPEND_ONLY/COLUMNAR storage (the fast, hardened path)
    ///
    /// NOTE (production hardening): the PageBased engine is NOT yet OLTP-ready — measured UPDATE is
    /// ~26K ops/s vs ~245K ops/s on the fixed-width Columnar (AppendOnly) path. Auto selection
    /// therefore routes the default General workload (and the unknown-hint fallback) to
    /// AppendOnly/Columnar until PageBased reaches UPDATE/DELETE parity.
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

        // Auto-select based on workload hint. PageBased is deliberately not selected for General /
        // unknown hints (and should be treated as opt-in only) until its UPDATE/DELETE fast paths
        // reach the fixed-width Columnar engine's parity (see class-level note).
        return WorkloadHint switch
        {
            WorkloadHint.ReadHeavy => Interfaces.StorageEngineType.Columnar,
            WorkloadHint.Analytics => Interfaces.StorageEngineType.Columnar,
            WorkloadHint.General => Interfaces.StorageEngineType.AppendOnly,
            _ => Interfaces.StorageEngineType.AppendOnly
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
