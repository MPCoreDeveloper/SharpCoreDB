// <copyright file="BufferConstants.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Constants;

/// <summary>
/// Constants for buffer pool and memory management.
/// Extracted from magic numbers to improve maintainability and clarity.
/// </summary>
public static class BufferConstants
{
    /// <summary>
    /// Default WAL buffer size in bytes (4 MB for high throughput).
    /// Sufficient for batching multiple transactions before flush.
    /// </summary>
    public const int DEFAULT_WAL_BUFFER_SIZE = 4 * 1024 * 1024; // 4 MB

    /// <summary>
    /// Default page size in bytes (4 KB - standard database page size).
    /// Aligns with OS page size for optimal I/O performance.
    /// </summary>
    public const int DEFAULT_PAGE_SIZE = 4096; // 4 KB

    /// <summary>
    /// Page header size in bytes (40 bytes).
    /// Includes magic number, version, type, flags, counts, checksum, transaction ID, and links.
    /// </summary>
    public const int PAGE_HEADER_SIZE = 40;

    /// <summary>
    /// Maximum data size per page in bytes (4056 bytes).
    /// Calculated as: PAGE_SIZE - PAGE_HEADER_SIZE = 4096 - 40 = 4056.
    /// </summary>
    public const int MAX_PAGE_DATA_SIZE = DEFAULT_PAGE_SIZE - PAGE_HEADER_SIZE;

    /// <summary>
    /// Stack allocation threshold in bytes (256 bytes).
    /// Values under this size use stackalloc, larger values use ArrayPool.
    /// Balance between stack overflow risk and allocation performance.
    /// </summary>
    public const int STACK_ALLOC_THRESHOLD = 256;

    /// <summary>
    /// Default buffer pool size for general purpose buffers (32 MB).
    /// Used for temporary allocations during query processing and serialization.
    /// </summary>
    public const int DEFAULT_BUFFER_POOL_SIZE = 32 * 1024 * 1024; // 32 MB

    /// <summary>
    /// Low memory buffer pool size (8 MB for mobile/constrained environments).
    /// </summary>
    public const int LOW_MEMORY_BUFFER_POOL_SIZE = 8 * 1024 * 1024; // 8 MB

    /// <summary>
    /// High performance buffer pool size (64 MB for server environments).
    /// </summary>
    public const int HIGH_PERFORMANCE_BUFFER_POOL_SIZE = 64 * 1024 * 1024; // 64 MB

    /// <summary>
    /// Memory-mapped file size threshold in bytes (10 MB).
    /// Files larger than this use memory mapping for improved read performance.
    /// </summary>
    public const int MEMORY_MAPPING_THRESHOLD = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Default query cache size (1024 entries).
    /// Balances memory usage with cache hit rate for common queries.
    /// </summary>
    public const int DEFAULT_QUERY_CACHE_SIZE = 1024;

    /// <summary>
    /// Default page cache capacity (1000 pages).
    /// At 4KB per page = 4 MB of cached pages in memory.
    /// </summary>
    public const int DEFAULT_PAGE_CACHE_CAPACITY = 1000;

    /// <summary>
    /// High performance page cache capacity (10000 pages = 40 MB).
    /// </summary>
    public const int HIGH_PERFORMANCE_PAGE_CACHE_CAPACITY = 10000;

    /// <summary>
    /// Low memory page cache capacity (100 pages = 400 KB).
    /// </summary>
    public const int LOW_MEMORY_PAGE_CACHE_CAPACITY = 100;

    /// <summary>
    /// Parallel SIMD threshold for columnar aggregates (50,000 rows).
    /// Below this threshold: Use single-threaded SIMD
    /// Above this threshold: Use parallel SIMD with partitioning
    /// Rationale: Parallel overhead (thread creation, synchronization) is only worth it for larger datasets.
    /// For 10k rows, single-threaded SIMD is faster due to lower overhead.
    /// </summary>
    public const int PARALLEL_SIMD_THRESHOLD = 50_000;

    /// <summary>
    /// Minimum partition size for parallel SIMD operations (1,000 rows).
    /// Each thread should process at least this many rows to justify parallelization overhead.
    /// </summary>
    public const int MIN_PARALLEL_PARTITION_SIZE = 1_000;

    /// <summary>
    /// Maximum number of parallel partitions (defaults to ProcessorCount).
    /// Can be overridden for testing or custom hardware configurations.
    /// </summary>
    public static int MAX_PARALLEL_PARTITIONS => Environment.ProcessorCount;

    /// <summary>
    /// Initial WAL batch size multiplier based on ProcessorCount.
    /// Default batch size = ProcessorCount * 128 (e.g., 8 cores * 128 = 1024 operations).
    /// This ensures batch size scales with hardware capabilities.
    /// </summary>
    public const int WAL_BATCH_SIZE_MULTIPLIER = 128;

    /// <summary>
    /// Minimum WAL batch size regardless of ProcessorCount.
    /// Ensures batching efficiency even on single-core systems.
    /// </summary>
    public const int MIN_WAL_BATCH_SIZE = 100;

    /// <summary>
    /// Maximum WAL batch size to prevent excessive memory usage.
    /// Large batches (>10k) can cause GC pressure and latency spikes.
    /// </summary>
    public const int MAX_WAL_BATCH_SIZE = 10_000;

    /// <summary>
    /// Queue depth threshold for batch size scale-up.
    /// When queue.Count > currentBatchSize * 4, double the batch size.
    /// This adapts to high-concurrency scenarios (32+ threads).
    /// Expected gain: +15-25% throughput at 32+ threads.
    /// </summary>
    public const int WAL_SCALE_UP_THRESHOLD_MULTIPLIER = 4;

    /// <summary>
    /// Queue depth threshold for batch size scale-down.
    /// When queue.Count &lt; currentBatchSize / 4, halve the batch size.
    /// This reduces latency during low-concurrency periods.
    /// </summary>
    public const int WAL_SCALE_DOWN_THRESHOLD_DIVISOR = 4;

    /// <summary>
    /// Minimum operations between batch size adjustments.
    /// Prevents thrashing when load fluctuates rapidly.
    /// </summary>
    public const int MIN_OPERATIONS_BETWEEN_ADJUSTMENTS = 1000;

    /// <summary>
    /// Gets the recommended initial WAL batch size based on ProcessorCount.
    /// Scales from 100 (1 core) to 1024 (8 cores) to 2048 (16 cores).
    /// </summary>
    public static int GetRecommendedWalBatchSize()
    {
        int recommended = Environment.ProcessorCount * WAL_BATCH_SIZE_MULTIPLIER;
        return Math.Clamp(recommended, MIN_WAL_BATCH_SIZE, MAX_WAL_BATCH_SIZE);
    }

    /// <summary>
    /// Gets the recommended WAL batch size for bulk operations based on total row count.
    /// Dynamically scales batch size to match data volume.
    /// </summary>
    /// <param name="totalRows">Total number of rows to process.</param>
    /// <returns>Recommended batch size for this operation.</returns>
    public static int GetBulkOperationBatchSize(int totalRows)
    {
        return totalRows switch
        {
            < 100 => 10,                      // Small: 10 rows per batch
            < 1_000 => 100,                   // Medium: 100 rows per batch
            < 10_000 => 1_000,                // Large: 1K rows per batch
            < 100_000 => 5_000,               // Very large: 5K rows per batch
            _ => 10_000                       // Extreme: 10K rows per batch (max)
        };
    }

    /// <summary>
    /// Gets the recommended WAL buffer size for bulk operations.
    /// Dynamically scales buffer size based on estimated data volume.
    /// </summary>
    /// <param name="totalRows">Total number of rows to process.</param>
    /// <param name="avgRowSizeBytes">Average row size in bytes (default 1KB).</param>
    /// <returns>Recommended WAL buffer size in bytes.</returns>
    public static int GetBulkOperationWalBufferSize(int totalRows, int avgRowSizeBytes = 1024)
    {
        long estimatedDataSize = (long)totalRows * avgRowSizeBytes;
        
        return estimatedDataSize switch
        {
            < 1 * 1024 * 1024 => 1 * 1024 * 1024,      // < 1MB: 1MB buffer
            < 10 * 1024 * 1024 => 4 * 1024 * 1024,     // < 10MB: 4MB buffer
            < 100 * 1024 * 1024 => 16 * 1024 * 1024,   // < 100MB: 16MB buffer
            _ => 64 * 1024 * 1024                       // ≥ 100MB: 64MB buffer (max)
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // SIMD THRESHOLDS AND OPTIMIZATION GUIDELINES
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// SIMD threshold for buffer copy operations (256 bytes).
    /// Below this size, use Span.CopyTo() which is optimized by the runtime.
    /// Above this size, use SimdHelper.CopyBuffer() for AVX2/SSE2 acceleration.
    /// Rationale: SIMD setup overhead is ~50 cycles; crossover point is ~256 bytes.
    /// Expected gain: 2-3x speedup for buffers >= 1KB.
    /// </summary>
    public const int SIMD_BUFFER_COPY_THRESHOLD = 256;

    /// <summary>
    /// SIMD threshold for buffer fill operations (64 bytes).
    /// Below this size, use Span.Fill() for simplicity.
    /// Above this size, use SimdHelper.FillBuffer() for vectorized filling.
    /// Expected gain: 4-5x speedup for buffers >= 512 bytes.
    /// </summary>
    public const int SIMD_BUFFER_FILL_THRESHOLD = 64;

    /// <summary>
    /// SIMD threshold for arithmetic operations on arrays (128 elements).
    /// Below this size, scalar operations are faster due to setup overhead.
    /// Above this size, use SimdHelper.AddInt32, MultiplyDouble, etc.
    /// Rationale: Vectorization overhead includes:
    ///   - Function call overhead (~10-20 cycles)
    ///   - Loop unrolling setup (~30 cycles)
    ///   - Data alignment checks (~20 cycles)
    /// Total overhead: ~60-70 cycles, crossover at ~128 int32s (512 bytes).
    /// Expected gain: 4-8x speedup for arrays >= 1024 elements.
    /// </summary>
    public const int SIMD_ARITHMETIC_THRESHOLD = 128;

    /// <summary>
    /// SIMD threshold for counting operations (256 elements).
    /// Below this size, scalar counting is sufficient.
    /// Above this size, use SimdHelper.CountNonZero() for acceleration.
    /// Expected gain: 8-16x speedup for arrays >= 4096 elements.
    /// </summary>
    public const int SIMD_COUNT_THRESHOLD = 256;

    /// <summary>
    /// SIMD threshold for hash code computation (32 bytes).
    /// Below this size, use scalar FNV-1a hashing.
    /// Above this size, use SimdHelper.ComputeHashCode() for AVX2 acceleration.
    /// Rationale: Hash code overhead is minimal; even small buffers benefit.
    /// Expected gain: 2-4x speedup for buffers >= 128 bytes.
    /// </summary>
    public const int SIMD_HASH_THRESHOLD = 32;

    /// <summary>
    /// SIMD threshold for sequence equality checks (32 bytes).
    /// Below this size, use Span.SequenceEqual().
    /// Above this size, use SimdHelper.SequenceEqual() for parallel comparison.
    /// Expected gain: 4-8x speedup for buffers >= 256 bytes.
    /// </summary>
    public const int SIMD_SEQUENCE_EQUAL_THRESHOLD = 32;

    /// <summary>
    /// SIMD threshold for pattern search operations (32 bytes).
    /// Below this size, use Span.IndexOf().
    /// Above this size, use SimdHelper.IndexOf() for vectorized search.
    /// Expected gain: 8-16x speedup for buffers >= 1024 bytes.
    /// </summary>
    public const int SIMD_INDEX_OF_THRESHOLD = 32;

    /// <summary>
    /// Gets a summary of SIMD thresholds for documentation and logging.
    /// </summary>
    /// <returns>Human-readable summary of SIMD optimization thresholds.</returns>
    public static string GetSimdThresholdSummary()
    {
        return $"""
            SIMD Optimization Thresholds (.NET 10):
            
            Buffer Operations:
              - Copy:          >= {SIMD_BUFFER_COPY_THRESHOLD} bytes  (2-3x speedup)
              - Fill:          >= {SIMD_BUFFER_FILL_THRESHOLD} bytes  (4-5x speedup)
              - Zero:          >= 32 bytes  (4-6x speedup)
            
            Comparison Operations:
              - SequenceEqual: >= {SIMD_SEQUENCE_EQUAL_THRESHOLD} bytes  (4-8x speedup)
              - IndexOf:       >= {SIMD_INDEX_OF_THRESHOLD} bytes  (8-16x speedup)
              - HashCode:      >= {SIMD_HASH_THRESHOLD} bytes  (2-4x speedup)
            
            Arithmetic Operations:
              - Add/Multiply:  >= {SIMD_ARITHMETIC_THRESHOLD} elements  (4-8x speedup)
              - Min/Max:       >= {SIMD_ARITHMETIC_THRESHOLD} elements  (4-8x speedup)
              - Count:         >= {SIMD_COUNT_THRESHOLD} elements  (8-16x speedup)
            
            Hardware Support:
              - x86/x64: AVX2 (256-bit), SSE2 (128-bit)
              - ARM:     NEON (128-bit)
              - Fallback: Scalar (automatic)
            
            Note: Actual performance depends on CPU architecture, cache, and data alignment.
            """;
    }
}
