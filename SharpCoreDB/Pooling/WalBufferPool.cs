// <copyright file="WalBufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

/// <summary>
/// Specialized buffer pool for Write-Ahead Log (WAL) operations with zero-contention access.
/// 
/// <para>
/// <b>Architecture:</b>
/// This class is split into multiple partial files for better organization:
/// </para>
/// 
/// <list type="bullet">
/// <item>
/// <term>WalBufferPool.Core.cs</term>
/// <description>Fields, constructor, configuration, and disposal logic</description>
/// </item>
/// <item>
/// <term>WalBufferPool.Operations.cs</term>
/// <description>Rent/Return operations for buffer management</description>
/// </item>
/// <item>
/// <term>WalBufferPool.Diagnostics.cs</term>
/// <description>Nested types: BufferCache, RentedBuffer, WalBufferPoolStatistics</description>
/// </item>
/// </list>
/// 
/// <para>
/// <b>Key Features:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Performance</term>
/// <description>Uses ArrayPool with thread-local caching for optimal throughput (zero locks on cache hit)</description>
/// </item>
/// <item>
/// <term>Memory Efficiency</term>
/// <description>Reuses large buffers (default 4MB) to minimize GC pressure and allocations</description>
/// </item>
/// <item>
/// <term>Thread Safety</term>
/// <description>Completely lock-free when using thread-local cache, safe concurrent access</description>
/// </item>
/// <item>
/// <term>Security</term>
/// <description>Optional buffer clearing on return to prevent data leakage</description>
/// </item>
/// <item>
/// <term>RAII Pattern</term>
/// <description>RentedBuffer ref struct ensures automatic buffer return with 'using' statement</description>
/// </item>
/// <item>
/// <term>Diagnostics</term>
/// <description>Comprehensive statistics for monitoring cache hits, misses, and outstanding buffers</description>
/// </item>
/// </list>
/// 
/// <para>
/// <b>Thread-Local Caching:</b>
/// </para>
/// <para>
/// The pool uses thread-local caches to eliminate lock contention. Each thread maintains
/// a small cache (typically 2 buffers for WAL patterns: one for writing, one for flushing).
/// On cache miss, buffers are rented from the shared ArrayPool.
/// </para>
/// 
/// <para>
/// <b>Buffer Lifecycle:</b>
/// </para>
/// <code>
/// 1. Rent() -> Check thread-local cache -> Cache hit: return immediately (zero locks)
///           -> Cache miss: rent from ArrayPool -> Update statistics
/// 
/// 2. Use buffer -> Write WAL data
/// 
/// 3. Dispose() -> Return buffer -> Try thread-local cache -> Cache full: return to ArrayPool
///              -> Clear buffer if configured (security)
/// </code>
/// 
/// <para>
/// <b>C# 14 Features:</b>
/// </para>
/// <list type="bullet">
/// <item>ArgumentOutOfRangeException.ThrowIfNegativeOrZero - Parameter validation</item>
/// <item>ObjectDisposedException.ThrowIf - Disposal checks</item>
/// <item>Target-typed new expressions - Concise initialization</item>
/// <item>Enhanced pattern matching (is/is not null) - Cleaner null checks</item>
/// <item>Expression bodies - More concise methods and properties</item>
/// <item>Throw expressions - Inline throwing in expressions</item>
/// <item>ref struct improvements - RentedBuffer optimizations</item>
/// </list>
/// 
/// <para>
/// <b>Usage Example:</b>
/// </para>
/// <code>
/// // Create pool with 4MB default buffer size
/// using var pool = new WalBufferPool(defaultBufferSize: 4 * 1024 * 1024);
/// 
/// // Rent buffer using RAII pattern
/// using (var rentedBuffer = pool.Rent())
/// {
///     // Write WAL data
///     var span = rentedBuffer.AsSpan();
///     WriteWalData(span);
///     rentedBuffer.UsedSize = actualBytesWritten;
///     
///     // Buffer is automatically returned on disposal
/// }
/// 
/// // Check statistics
/// var stats = pool.GetStatistics();
/// Console.WriteLine($"Cache Hit Rate: {stats.CacheHitRate:P2}");
/// Console.WriteLine($"Outstanding: {stats.OutstandingBuffers}");
/// </code>
/// 
/// <para>
/// <b>Advanced Configuration:</b>
/// </para>
/// <code>
/// var config = new PoolConfiguration
/// {
///     UseThreadLocal = true,           // Enable thread-local caching
///     ThreadLocalCapacity = 3,         // Cache up to 3 buffers per thread
///     ClearBuffersOnReturn = true      // Clear buffers for security
/// };
/// 
/// using var pool = new WalBufferPool(
///     defaultBufferSize: 8 * 1024 * 1024,  // 8MB buffers
///     config: config
/// );
/// </code>
/// 
/// <para>
/// <b>Performance Characteristics:</b>
/// </para>
/// <list type="bullet">
/// <item>Cache hit: ~10-20 ns (zero locks, pure memory access)</item>
/// <item>Cache miss: ~100-200 ns (ArrayPool rent, one lock)</item>
/// <item>Buffer clearing: O(n) where n = used size (only if configured)</item>
/// <item>Thread-local disposal: O(capacity * bufferSize) on thread exit</item>
/// </list>
/// 
/// <para>
/// <b>Memory Overhead:</b>
/// </para>
/// <list type="bullet">
/// <item>Per pool: ~200 bytes + ThreadLocal overhead</item>
/// <item>Per thread: capacity * 16 bytes (BufferEntry array)</item>
/// <item>Buffers: managed by ArrayPool (shared across application)</item>
/// </list>
/// 
/// <para>
/// <b>Security Considerations:</b>
/// </para>
/// <list type="bullet">
/// <item>Enable ClearBuffersOnReturn for sensitive data (WAL may contain user data)</item>
/// <item>Only clears used portion (UsedSize) for performance</item>
/// <item>Automatic clearing on pool disposal</item>
/// <item>Thread-local caches are tracked and disposed properly</item>
/// </list>
/// 
/// <para>
/// <b>Best Practices:</b>
/// </para>
/// <list type="bullet">
/// <item>Always use 'using' statement with RentedBuffer to ensure return</item>
/// <item>Set UsedSize accurately for optimal clearing performance</item>
/// <item>Monitor cache hit rate - should be >90% for WAL patterns</item>
/// <item>Use default 4MB size for typical WAL operations</item>
/// <item>Enable ClearBuffersOnReturn if WAL contains sensitive data</item>
/// <item>Call ResetStatistics() periodically to avoid counter overflow</item>
/// </list>
/// </summary>
public partial class WalBufferPool
{
}
