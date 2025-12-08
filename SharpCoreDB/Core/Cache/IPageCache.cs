// <copyright file="IPageCache.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.Cache;

using System;
using System.Threading;

/// <summary>
/// Interface for a high-performance page cache with thread-safe operations.
/// </summary>
public interface IPageCache : IDisposable
{
    /// <summary>
    /// Gets the maximum number of pages in the cache.
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// Gets the current number of pages in the cache.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the page size in bytes.
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    PageCacheStatistics Statistics { get; }

    /// <summary>
    /// Gets a page from the cache, loading it from disk if necessary.
    /// The page is automatically pinned and must be unpinned after use.
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    /// <param name="loadFunc">Function to load page data if not in cache.</param>
    /// <returns>The page frame.</returns>
    PageFrame GetPage(int pageId, Func<int, ReadOnlySpan<byte>>? loadFunc = null);

    /// <summary>
    /// Pins a page that's already in the cache.
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    /// <returns>True if page was found and pinned, false otherwise.</returns>
    bool PinPage(int pageId);

    /// <summary>
    /// Unpins a page, allowing it to be evicted.
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    void UnpinPage(int pageId);

    /// <summary>
    /// Marks a page as dirty (modified).
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    void MarkDirty(int pageId);

    /// <summary>
    /// Flushes a specific dirty page to disk.
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    /// <param name="flushFunc">Function to write page data to disk.</param>
    void FlushPage(int pageId, Action<int, ReadOnlySpan<byte>> flushFunc);

    /// <summary>
    /// Flushes all dirty pages to disk.
    /// </summary>
    /// <param name="flushFunc">Function to write page data to disk.</param>
    void FlushAll(Action<int, ReadOnlySpan<byte>> flushFunc);

    /// <summary>
    /// Evicts a page from the cache (must not be pinned).
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    /// <param name="flushFunc">Optional function to flush if dirty.</param>
    /// <returns>True if page was evicted, false if not found or pinned.</returns>
    bool EvictPage(int pageId, Action<int, ReadOnlySpan<byte>>? flushFunc = null);

    /// <summary>
    /// Clears the entire cache, flushing dirty pages if requested.
    /// </summary>
    /// <param name="flushDirty">Whether to flush dirty pages before clearing.</param>
    /// <param name="flushFunc">Function to write dirty pages to disk.</param>
    void Clear(bool flushDirty = false, Action<int, ReadOnlySpan<byte>>? flushFunc = null);
}

/// <summary>
/// Cache statistics for monitoring and diagnostics.
/// </summary>
public class PageCacheStatistics
{
    private long hits;
    private long misses;
    private long evictions;
    private long flushes;
    private long latchFailures;

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    public long Hits => Volatile.Read(ref this.hits);

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    public long Misses => Volatile.Read(ref this.misses);

    /// <summary>
    /// Gets the total number of page evictions.
    /// </summary>
    public long Evictions => Volatile.Read(ref this.evictions);

    /// <summary>
    /// Gets the total number of dirty page flushes.
    /// </summary>
    public long Flushes => Volatile.Read(ref this.flushes);

    /// <summary>
    /// Gets the number of failed latch attempts.
    /// </summary>
    public long LatchFailures => Volatile.Read(ref this.latchFailures);

    /// <summary>
    /// Gets the cache hit rate (0-1).
    /// </summary>
    public double HitRate
    {
        get
        {
            long h = this.Hits;
            long m = this.Misses;
            return h + m > 0 ? (double)h / (h + m) : 0;
        }
    }

    /// <summary>
    /// Increments the hit counter.
    /// </summary>
    internal void IncrementHits() => Interlocked.Increment(ref this.hits);

    /// <summary>
    /// Increments the miss counter.
    /// </summary>
    internal void IncrementMisses() => Interlocked.Increment(ref this.misses);

    /// <summary>
    /// Increments the eviction counter.
    /// </summary>
    internal void IncrementEvictions() => Interlocked.Increment(ref this.evictions);

    /// <summary>
    /// Increments the flush counter.
    /// </summary>
    internal void IncrementFlushes() => Interlocked.Increment(ref this.flushes);

    /// <summary>
    /// Increments the latch failure counter.
    /// </summary>
    internal void IncrementLatchFailures() => Interlocked.Increment(ref this.latchFailures);

    /// <summary>
    /// Resets all statistics.
    /// </summary>
    public void Reset()
    {
        Volatile.Write(ref this.hits, 0);
        Volatile.Write(ref this.misses, 0);
        Volatile.Write(ref this.evictions, 0);
        Volatile.Write(ref this.flushes, 0);
        Volatile.Write(ref this.latchFailures, 0);
    }

    /// <summary>
    /// Gets a string representation of the statistics.
    /// </summary>
    /// <returns>Statistics as a formatted string.</returns>
    public override string ToString()
    {
        return $"PageCacheStats[Hits={this.Hits}, Misses={this.Misses}, " +
               $"HitRate={this.HitRate:P2}, Evictions={this.Evictions}, " +
               $"Flushes={this.Flushes}, LatchFailures={this.LatchFailures}]";
    }
}
