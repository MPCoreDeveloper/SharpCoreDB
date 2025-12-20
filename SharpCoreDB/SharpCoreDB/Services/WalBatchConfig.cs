// <copyright file="WalBatchConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// Configuration for batch WAL operations.
/// CRITICAL PERFORMANCE: Fine-tunes WAL batching behavior for optimal throughput.
/// 
/// Design:
/// - BatchFlushThreshold: Flush WAL when buffer reaches threshold (default: 1MB)
/// - MaxBatchSize: Maximum entries per batch (default: 10,000)
/// - AutoFlushIntervalMs: Auto-flush interval if threshold not reached (default: 100ms)
/// - EnableAdaptiveBatching: Tune batch size based on queue depth (default: true)
/// 
/// Performance Tuning:
/// - Small threshold (100KB): Frequent flushes, low latency
/// - Large threshold (10MB): Fewer flushes, better throughput
/// - Mixed workload: Set to 1MB (default), adjust based on profiling
/// </summary>
public sealed class WalBatchConfig
{
    /// <summary>
    /// Flush WAL buffer when it reaches this size in bytes.
    /// Default: 1MB. Larger = fewer flushes but more memory.
    /// Range: 64KB - 100MB recommended.
    /// </summary>
    public int BatchFlushThreshold { get; set; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Maximum number of WAL entries per batch.
    /// Default: 10,000. Ensures bounded memory growth.
    /// Useful for very large individual operations.
    /// Range: 100 - 1,000,000 recommended.
    /// </summary>
    public int MaxBatchSize { get; set; } = 10000;

    /// <summary>
    /// Auto-flush interval in milliseconds.
    /// If threshold not reached, flush anyway after this interval.
    /// Default: 100ms. Prevents unbounded latency.
    /// Range: 10ms - 5000ms recommended.
    /// </summary>
    public int AutoFlushIntervalMs { get; set; } = 100;

    /// <summary>
    /// Enable adaptive batch sizing based on queue depth.
    /// Default: true. Improves throughput in concurrent scenarios.
    /// Set false for predictable behavior.
    /// </summary>
    public bool EnableAdaptiveBatching { get; set; } = true;

    /// <summary>
    /// Minimum batch size for adaptive batching.
    /// Default: 10. Prevents batch size from dropping too low.
    /// </summary>
    public int MinAdaptiveBatchSize { get; set; } = 10;

    /// <summary>
    /// Maximum batch size for adaptive batching.
    /// Default: 10,000. Prevents unbounded memory growth.
    /// </summary>
    public int MaxAdaptiveBatchSize { get; set; } = 10000;

    /// <summary>
    /// Whether to compress WAL entries before writing.
    /// Default: false. Set true for better compression if CPU permits.
    /// Note: Requires System.IO.Compression namespace.
    /// </summary>
    public bool EnableWalCompression { get; set; } = false;

    /// <summary>
    /// Buffer pool size for staging area.
    /// Default: 10 buffers. More buffers = lower contention.
    /// </summary>
    public int BufferPoolSize { get; set; } = 10;

    /// <summary>
    /// Enable detailed WAL batch metrics.
    /// Default: false. Set true for profiling (small overhead).
    /// </summary>
    public bool EnableMetrics { get; set; } = false;

    /// <summary>
    /// Timeout for batch completion in milliseconds.
    /// Default: 5000ms. Prevents stuck batches.
    /// </summary>
    public int BatchTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Whether to use memory pool for WAL buffers.
    /// Default: true. Reduces GC pressure.
    /// </summary>
    public bool UseMemoryPool { get; set; } = true;

    /// <summary>
    /// Pre-allocated buffer size for WAL entries.
    /// Default: 64KB. Reduces allocations for typical batches.
    /// </summary>
    public int PreallocatedBufferSize { get; set; } = 64 * 1024;

    /// <summary>
    /// Validates configuration and corrects invalid values.
    /// Called automatically on initialization.
    /// </summary>
    public void Validate()
    {
        // Validate BatchFlushThreshold
        if (BatchFlushThreshold < 64 * 1024)
            BatchFlushThreshold = 64 * 1024; // Minimum 64KB

        if (BatchFlushThreshold > 100 * 1024 * 1024)
            BatchFlushThreshold = 100 * 1024 * 1024; // Maximum 100MB

        // Validate MaxBatchSize
        if (MaxBatchSize < 100)
            MaxBatchSize = 100; // Minimum 100

        if (MaxBatchSize > 1000000)
            MaxBatchSize = 1000000; // Maximum 1M

        // Validate AutoFlushIntervalMs
        if (AutoFlushIntervalMs < 10)
            AutoFlushIntervalMs = 10; // Minimum 10ms

        if (AutoFlushIntervalMs > 5000)
            AutoFlushIntervalMs = 5000; // Maximum 5s

        // Validate adaptive batch sizes
        if (MinAdaptiveBatchSize < 1)
            MinAdaptiveBatchSize = 1;

        if (MaxAdaptiveBatchSize > MaxBatchSize)
            MaxAdaptiveBatchSize = MaxBatchSize;

        if (MinAdaptiveBatchSize > MaxAdaptiveBatchSize)
            MinAdaptiveBatchSize = MaxAdaptiveBatchSize / 2;

        // Validate BufferPoolSize
        if (BufferPoolSize < 1)
            BufferPoolSize = 1;

        if (BufferPoolSize > 100)
            BufferPoolSize = 100;

        // Validate BatchTimeoutMs
        if (BatchTimeoutMs < 100)
            BatchTimeoutMs = 100; // Minimum 100ms

        if (BatchTimeoutMs > 60000)
            BatchTimeoutMs = 60000; // Maximum 60s

        // Validate PreallocatedBufferSize
        if (PreallocatedBufferSize < 16 * 1024)
            PreallocatedBufferSize = 16 * 1024; // Minimum 16KB

        if (PreallocatedBufferSize > 10 * 1024 * 1024)
            PreallocatedBufferSize = 10 * 1024 * 1024; // Maximum 10MB
    }

    /// <summary>
    /// Creates a configuration optimized for update-heavy workloads.
    /// Recommended for batch UPDATE operations.
    /// </summary>
    public static WalBatchConfig CreateForUpdateHeavy()
    {
        return new WalBatchConfig
        {
            BatchFlushThreshold = 2 * 1024 * 1024, // 2MB (larger for updates)
            MaxBatchSize = 50000,                   // 50K updates
            AutoFlushIntervalMs = 200,              // 200ms (less frequent)
            EnableAdaptiveBatching = true,
            MaxAdaptiveBatchSize = 50000
        };
    }

    /// <summary>
    /// Creates a configuration optimized for read-heavy workloads.
    /// Smaller batches, more frequent flushes.
    /// </summary>
    public static WalBatchConfig CreateForReadHeavy()
    {
        return new WalBatchConfig
        {
            BatchFlushThreshold = 256 * 1024,       // 256KB (smaller for reads)
            MaxBatchSize = 1000,                    // 1K operations
            AutoFlushIntervalMs = 50,               // 50ms (more frequent)
            EnableAdaptiveBatching = true,
            MaxAdaptiveBatchSize = 5000
        };
    }

    /// <summary>
    /// Creates a configuration optimized for low-latency operations.
    /// Very frequent flushes, minimal batching.
    /// </summary>
    public static WalBatchConfig CreateForLowLatency()
    {
        return new WalBatchConfig
        {
            BatchFlushThreshold = 64 * 1024,        // 64KB (minimal)
            MaxBatchSize = 100,                     // 100 operations
            AutoFlushIntervalMs = 10,               // 10ms (very frequent)
            EnableAdaptiveBatching = false,
            MaxAdaptiveBatchSize = 1000
        };
    }

    /// <summary>
    /// Creates a configuration optimized for maximum throughput.
    /// Large batches, infrequent flushes.
    /// </summary>
    public static WalBatchConfig CreateForMaxThroughput()
    {
        return new WalBatchConfig
        {
            BatchFlushThreshold = 10 * 1024 * 1024, // 10MB (maximum)
            MaxBatchSize = 500000,                  // 500K operations
            AutoFlushIntervalMs = 1000,             // 1s (infrequent)
            EnableAdaptiveBatching = true,
            MaxAdaptiveBatchSize = 500000
        };
    }

    /// <summary>
    /// Returns a summary string of the configuration.
    /// </summary>
    public override string ToString()
    {
        return $"WalBatchConfig(Threshold={BatchFlushThreshold / 1024}KB, " +
               $"MaxSize={MaxBatchSize}, Interval={AutoFlushIntervalMs}ms, " +
               $"Adaptive={EnableAdaptiveBatching})";
    }
}
