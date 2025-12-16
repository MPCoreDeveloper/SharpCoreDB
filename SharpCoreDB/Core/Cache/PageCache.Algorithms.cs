// <copyright file="PageCache.Algorithms.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.Cache;

using System;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// High-performance page cache - Algorithms partial class.
/// Contains CLOCK eviction algorithm and internal helper methods.
/// </summary>
public sealed partial class PageCache
{
    /// <summary>
    /// Loads a page into the cache, evicting if necessary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private PageFrame LoadPage(int pageId, Func<int, ReadOnlySpan<byte>>? loadFunc)
    {
        // Check if we need to evict
        if (Count >= capacity && !EvictPageUsingClock(loadFunc))
        {
            // All pages are pinned, throw exception
            throw new InvalidOperationException("Cache is full and all pages are pinned");
        }

        // Create new frame
        PageFrame newFrame = new(pageId, pageSize, memoryPool); // ✅ C# 14: Target-typed new
        
        // Latch immediately
        if (!newFrame.TryLatch())
        {
            throw new InvalidOperationException("Failed to latch newly created frame");
        }

        try
        {
            // Load data if function provided
            if (loadFunc is not null) // ✅ C# 14: is not pattern
            {
                var data = loadFunc(pageId);
                data.CopyTo(newFrame.Buffer);
            }

            // Pin the page before adding to cache
            newFrame.Pin();

            // Try to add to cache
            if (pageTable.TryAdd(pageId, newFrame))
            {
                Interlocked.Increment(ref currentSize);
                
                // Store in frames array for CLOCK algorithm
                int index = FindEmptyFrameSlot();
                if (index >= 0)
                {
                    Volatile.Write(ref frames[index], newFrame);
                }

                return newFrame;
            }

            // Another thread added it, use theirs
            newFrame.Unpin();
            newFrame.Dispose();
            
            if (pageTable.TryGetValue(pageId, out var existingFrame))
            {
                existingFrame.Pin();
                return existingFrame;
            }

            throw new InvalidOperationException($"Failed to add page {pageId} to cache");
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
        int maxAttempts = capacity * 2; // Two full sweeps

        for (int attempts = 0; attempts < maxAttempts; attempts++)
        {
            int currentHand = Volatile.Read(ref clockHand);
            var frame = Volatile.Read(ref frames[currentHand]);

            // Move clock hand
            int nextHand = (currentHand + 1) % capacity;
            Interlocked.CompareExchange(ref clockHand, nextHand, currentHand);

            if (frame is null) // ✅ C# 14: is null pattern
                continue;

            // Check if page can be evicted
            if (!frame.CanEvict())
                continue;

            // Try to latch the frame
            if (!frame.TryLatch(50))
            {
                statistics.IncrementLatchFailures();
                continue;
            }

            try
            {
                // Double-check pin count after latching
                if (frame.PinCount > 0)
                    continue;

                // CLOCK algorithm: check and update clock bit
                if (frame.ClockBit is 1) // ✅ C# 14: is pattern with constant
                {
                    // Recently accessed, give second chance
                    frame.ClockBit = 0;
                    continue;
                }

                // Evict this page
                int pageId = frame.PageId;
                
                // Flush if dirty
                if (frame.IsDirty && flushFunc is not null) // ✅ C# 14: is not pattern
                {
                    var data = flushFunc(pageId);
                    data.CopyTo(frame.Buffer);
                    statistics.IncrementFlushes();
                }

                // Remove from page table
                if (pageTable.TryRemove(pageId, out _))
                {
                    // Clear frame slot
                    Volatile.Write(ref frames[currentHand], null!);
                    Interlocked.Decrement(ref currentSize);
                    statistics.IncrementEvictions();
                    
                    frame.Dispose();
                    return true;
                }
            }
            finally
            {
                frame.Unlatch();
            }
        }

        return false;
    }

    /// <summary>
    /// Finds an empty slot in the frames array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindEmptyFrameSlot()
    {
        for (int i = 0; i < capacity; i++)
        {
            if (Volatile.Read(ref frames[i]) is null) // ✅ C# 14: is null pattern
                return i;
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

        foreach (var (_, frame) in pageTable) // ✅ C# 14: Tuple deconstruction
        {
            if (frame.PinCount > 0) pinnedCount++;
            if (frame.IsDirty) dirtyCount++;
        }

        return $"PageCache[Capacity={Capacity}, Size={Count}, " +
               $"Pinned={pinnedCount}, Dirty={dirtyCount}, " +
               $"ClockHand={clockHand}, {Statistics}]";
    }
}
