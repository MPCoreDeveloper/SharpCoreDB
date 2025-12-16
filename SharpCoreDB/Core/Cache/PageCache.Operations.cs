// <copyright file="PageCache.Operations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.Cache;

using System;
using System.Threading;

/// <summary>
/// High-performance page cache - Operations partial class.
/// Contains cache operation methods: Get, Pin, Unpin, Mark, Flush, Evict, Clear.
/// </summary>
public sealed partial class PageCache
{
    /// <inheritdoc/>
    public PageFrame GetPage(int pageId, Func<int, ReadOnlySpan<byte>>? loadFunc = null)
    {
        // Fast path: page is already in cache
        if (pageTable.TryGetValue(pageId, out var frame))
        {
            statistics.IncrementHits();
            frame.Pin();
            frame.UpdateLastAccessTime();
            frame.ClockBit = 1; // Mark as recently accessed
            return frame;
        }

        // Slow path: need to load page
        statistics.IncrementMisses();
        return LoadPage(pageId, loadFunc);
    }

    /// <inheritdoc/>
    public bool PinPage(int pageId)
    {
        if (pageTable.TryGetValue(pageId, out var frame))
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
        if (pageTable.TryGetValue(pageId, out var frame))
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
        if (pageTable.TryGetValue(pageId, out var frame))
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
        ArgumentNullException.ThrowIfNull(flushFunc); // ✅ C# 14: ArgumentNullException.ThrowIfNull

        if (pageTable.TryGetValue(pageId, out var frame) && frame.IsDirty && frame.TryLatch())
        {
            try
            {
                flushFunc(pageId, frame.Buffer);
                frame.ClearDirty();
                statistics.IncrementFlushes();
            }
            finally
            {
                frame.Unlatch();
            }
        }
    }

    /// <inheritdoc/>
    public void FlushAll(Action<int, ReadOnlySpan<byte>> flushFunc)
    {
        ArgumentNullException.ThrowIfNull(flushFunc); // ✅ C# 14: ArgumentNullException.ThrowIfNull

        foreach (var (_, frame) in pageTable) // ✅ C# 14: Tuple deconstruction
        {
            if (frame.IsDirty && frame.TryLatch())
            {
                try
                {
                    flushFunc(frame.PageId, frame.Buffer);
                    frame.ClearDirty();
                    statistics.IncrementFlushes();
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
        if (!pageTable.TryRemove(pageId, out var frame))
            return false;

        if (!frame.TryLatch())
        {
            // Failed to latch, put it back
            pageTable.TryAdd(pageId, frame);
            return false;
        }

        try
        {
            if (frame.PinCount > 0)
            {
                // Page is pinned, put it back
                pageTable.TryAdd(pageId, frame);
                return false;
            }

            // Flush if dirty
            if (frame.IsDirty && flushFunc is not null) // ✅ C# 14: is not pattern
            {
                flushFunc(pageId, frame.Buffer);
                statistics.IncrementFlushes();
            }

            Interlocked.Decrement(ref currentSize);
            statistics.IncrementEvictions();
            return true;
        }
        finally
        {
            frame.Unlatch();
        }
    }

    /// <inheritdoc/>
    public void Clear(bool flushDirty = false, Action<int, ReadOnlySpan<byte>>? flushFunc = null)
    {
        if (flushDirty && flushFunc is not null) // ✅ C# 14: is not pattern
        {
            FlushAll(flushFunc);
        }

        foreach (var (key, frame) in pageTable) // ✅ C# 14: Tuple deconstruction
        {
            if (frame.TryLatch())
            {
                try
                {
                    if (frame.PinCount is 0) // ✅ C# 14: is pattern with constant
                    {
                        pageTable.TryRemove(key, out _);
                        Interlocked.Decrement(ref currentSize);
                    }
                }
                finally
                {
                    frame.Unlatch();
                }
            }
        }
    }
}
