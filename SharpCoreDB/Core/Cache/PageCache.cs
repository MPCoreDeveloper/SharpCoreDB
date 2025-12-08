// <copyright file="PageCache.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Core.Cache;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// High-performance page cache with CLOCK eviction algorithm.
/// Thread-safe and lock-free for most operations.
/// </summary>
public sealed class PageCache : IPageCache
{
    private readonly int pageSize;
    private readonly int capacity;
    private readonly MemoryPool<byte> memoryPool;
    private readonly ConcurrentDictionary<int, PageFrame> pageTable;
    private readonly PageFrame[] frames;
    private readonly PageCacheStatistics statistics;
    private int clockHand;
    private int currentSize;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageCache"/> class.
    /// </summary>
    /// <param name="capacity">Maximum number of pages to cache.</param>
    /// <param name="pageSize">Size of each page in bytes (default 4096).</param>
    public PageCache(int capacity, int pageSize = 4096)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("Capacity must be positive", nameof(capacity));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentException("Page size must be positive", nameof(pageSize));
        }

        this.capacity = capacity;
        this.pageSize = pageSize;
        this.memoryPool = MemoryPool<byte>.Shared;
        this.pageTable = new ConcurrentDictionary<int, PageFrame>();
        this.frames = new PageFrame[capacity];
        this.statistics = new PageCacheStatistics();
        this.clockHand = 0;
        this.currentSize = 0;
        this.disposed = false;
    }

    /// <inheritdoc/>
    public int Capacity => this.capacity;

    /// <inheritdoc/>
    public int Count => Volatile.Read(ref this.currentSize);

    /// <inheritdoc/>
    public int PageSize => this.pageSize;

    /// <inheritdoc/>
    public PageCacheStatistics Statistics => this.statistics;

    /// <inheritdoc/>
    public PageFrame GetPage(int pageId, Func<int, ReadOnlySpan<byte>>? loadFunc = null)
    {
        // Fast path: page is already in cache
        if (this.pageTable.TryGetValue(pageId, out var frame))
        {
            this.statistics.IncrementHits();
            frame.Pin();
            frame.UpdateLastAccessTime();
            frame.ClockBit = 1; // Mark as recently accessed
            return frame;
        }

        // Slow path: need to load page
        this.statistics.IncrementMisses();
        return this.LoadPage(pageId, loadFunc);
    }

    /// <inheritdoc/>
    public bool PinPage(int pageId)
    {
        if (this.pageTable.TryGetValue(pageId, out var frame))
        {
            frame.Pin();
            frame.UpdateLastAccessTime();
            frame.ClockBit = 1;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void UnpinPage(int pageId)
    {
        if (this.pageTable.TryGetValue(pageId, out var frame))
        {
            frame.Unpin();
        }
        else
        {
            throw new InvalidOperationException($"Page {pageId} not found in cache");
        }
    }

    /// <inheritdoc/>
    public void MarkDirty(int pageId)
    {
        if (this.pageTable.TryGetValue(pageId, out var frame))
        {
            frame.MarkDirty();
        }
        else
        {
            throw new InvalidOperationException($"Page {pageId} not found in cache");
        }
    }

    /// <inheritdoc/>
    public void FlushPage(int pageId, Action<int, ReadOnlySpan<byte>> flushFunc)
    {
        if (flushFunc == null)
        {
            throw new ArgumentNullException(nameof(flushFunc));
        }

        if (this.pageTable.TryGetValue(pageId, out var frame))
        {
            if (frame.IsDirty && frame.TryLatch())
            {
                try
                {
                    flushFunc(pageId, frame.Buffer);
                    frame.ClearDirty();
                    this.statistics.IncrementFlushes();
                }
                finally
                {
                    frame.Unlatch();
                }
            }
        }
    }

    /// <inheritdoc/>
    public void FlushAll(Action<int, ReadOnlySpan<byte>> flushFunc)
    {
        if (flushFunc == null)
        {
            throw new ArgumentNullException(nameof(flushFunc));
        }

        foreach (var kvp in this.pageTable)
        {
            var frame = kvp.Value;
            if (frame.IsDirty && frame.TryLatch())
            {
                try
                {
                    flushFunc(frame.PageId, frame.Buffer);
                    frame.ClearDirty();
                    this.statistics.IncrementFlushes();
                }
                finally
                {
                    frame.Unlatch();
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool EvictPage(int pageId, Action<int, ReadOnlySpan<byte>>? flushFunc = null)
    {
        if (this.pageTable.TryRemove(pageId, out var frame))
        {
            if (!frame.TryLatch())
            {
                // Failed to latch, put it back
                this.pageTable.TryAdd(pageId, frame);
                return false;
            }

            try
            {
                if (frame.PinCount > 0)
                {
                    // Page is pinned, put it back
                    this.pageTable.TryAdd(pageId, frame);
                    return false;
                }

                // Flush if dirty
                if (frame.IsDirty && flushFunc != null)
                {
                    flushFunc(pageId, frame.Buffer);
                    this.statistics.IncrementFlushes();
                }

                Interlocked.Decrement(ref this.currentSize);
                this.statistics.IncrementEvictions();
                return true;
            }
            finally
            {
                frame.Unlatch();
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void Clear(bool flushDirty = false, Action<int, ReadOnlySpan<byte>>? flushFunc = null)
    {
        if (flushDirty && flushFunc != null)
        {
            this.FlushAll(flushFunc);
        }

        foreach (var kvp in this.pageTable)
        {
            var frame = kvp.Value;
            if (frame.TryLatch())
            {
                try
                {
                    if (frame.PinCount == 0)
                    {
                        this.pageTable.TryRemove(kvp.Key, out _);
                        Interlocked.Decrement(ref this.currentSize);
                    }
                }
                finally
                {
                    frame.Unlatch();
                }
            }
        }
    }

    /// <summary>
    /// Loads a page into the cache, evicting if necessary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private PageFrame LoadPage(int pageId, Func<int, ReadOnlySpan<byte>>? loadFunc)
    {
        // Check if we need to evict
        if (this.Count >= this.capacity)
        {
            if (!this.EvictPageUsingClock(loadFunc))
            {
                // All pages are pinned, throw exception
                throw new InvalidOperationException("Cache is full and all pages are pinned");
            }
        }

        // Create new frame
        var newFrame = new PageFrame(pageId, this.pageSize, this.memoryPool);
        
        // Latch immediately
        if (!newFrame.TryLatch())
        {
            throw new InvalidOperationException("Failed to latch newly created frame");
        }

        try
        {
            // Load data if function provided
            if (loadFunc != null)
            {
                var data = loadFunc(pageId);
                data.CopyTo(newFrame.Buffer);
            }

            // Pin the page before adding to cache
            newFrame.Pin();

            // Try to add to cache
            if (this.pageTable.TryAdd(pageId, newFrame))
            {
                Interlocked.Increment(ref this.currentSize);
                
                // Store in frames array for CLOCK algorithm
                int index = this.FindEmptyFrameSlot();
                if (index >= 0)
                {
                    Volatile.Write(ref this.frames[index], newFrame);
                }

                return newFrame;
            }
            else
            {
                // Another thread added it, use theirs
                newFrame.Unpin();
                newFrame.Dispose();
                
                if (this.pageTable.TryGetValue(pageId, out var existingFrame))
                {
                    existingFrame.Pin();
                    return existingFrame;
                }

                throw new InvalidOperationException($"Failed to add page {pageId} to cache");
            }
        }
        finally
        {
            newFrame.Unlatch();
        }
    }

    /// <summary>
    /// Evicts a page using the CLOCK algorithm.
    /// Only evicts pages with pinCount == 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool EvictPageUsingClock(Func<int, ReadOnlySpan<byte>>? flushFunc)
    {
        int startHand = Volatile.Read(ref this.clockHand);
        int attempts = 0;
        int maxAttempts = this.capacity * 2; // Two full sweeps

        while (attempts < maxAttempts)
        {
            int currentHand = Volatile.Read(ref this.clockHand);
            var frame = Volatile.Read(ref this.frames[currentHand]);

            // Move clock hand
            int nextHand = (currentHand + 1) % this.capacity;
            Interlocked.CompareExchange(ref this.clockHand, nextHand, currentHand);

            if (frame == null)
            {
                attempts++;
                continue;
            }

            // Check if page can be evicted
            if (!frame.CanEvict())
            {
                attempts++;
                continue;
            }

            // Try to latch the frame
            if (!frame.TryLatch(50))
            {
                this.statistics.IncrementLatchFailures();
                attempts++;
                continue;
            }

            try
            {
                // Double-check pin count after latching
                if (frame.PinCount > 0)
                {
                    attempts++;
                    continue;
                }

                // CLOCK algorithm: check and update clock bit
                if (frame.ClockBit == 1)
                {
                    // Recently accessed, give second chance
                    frame.ClockBit = 0;
                    attempts++;
                    continue;
                }

                // Evict this page
                int pageId = frame.PageId;
                
                // Flush if dirty
                if (frame.IsDirty && flushFunc != null)
                {
                    var data = flushFunc(pageId);
                    data.CopyTo(frame.Buffer);
                    this.statistics.IncrementFlushes();
                }

                // Remove from page table
                if (this.pageTable.TryRemove(pageId, out _))
                {
                    // Clear frame slot
                    Volatile.Write(ref this.frames[currentHand], null!);
                    Interlocked.Decrement(ref this.currentSize);
                    this.statistics.IncrementEvictions();
                    
                    frame.Dispose();
                    return true;
                }
            }
            finally
            {
                frame.Unlatch();
            }

            attempts++;
        }

        return false;
    }

    /// <summary>
    /// Finds an empty slot in the frames array.
    /// </summary>
    private int FindEmptyFrameSlot()
    {
        for (int i = 0; i < this.capacity; i++)
        {
            if (Volatile.Read(ref this.frames[i]) == null)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets diagnostic information about the cache state.
    /// </summary>
    public string GetDiagnostics()
    {
        int pinnedCount = 0;
        int dirtyCount = 0;

        foreach (var kvp in this.pageTable)
        {
            if (kvp.Value.PinCount > 0) pinnedCount++;
            if (kvp.Value.IsDirty) dirtyCount++;
        }

        return $"PageCache[Capacity={this.Capacity}, Size={this.Count}, " +
               $"Pinned={pinnedCount}, Dirty={dirtyCount}, " +
               $"ClockHand={this.clockHand}, {this.Statistics}]";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!this.disposed)
        {
            // Flush and dispose all frames
            foreach (var kvp in this.pageTable)
            {
                kvp.Value.Dispose();
            }

            this.pageTable.Clear();
            this.disposed = true;
        }
    }
}
