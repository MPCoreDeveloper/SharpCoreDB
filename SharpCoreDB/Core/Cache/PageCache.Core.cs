// <copyright file="PageCache.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copipilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.Cache;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// High-performance page cache with CLOCK eviction algorithm - Core partial class.
/// Contains fields, constructor, properties, and initialization logic.
/// Thread-safe and lock-free for most operations.
/// </summary>
public sealed partial class PageCache : IPageCache
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity); // ✅ C# 14: ArgumentOutOfRangeException.ThrowIfNegativeOrZero
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        this.capacity = capacity;
        this.pageSize = pageSize;
        this.memoryPool = MemoryPool<byte>.Shared;
        this.pageTable = new(); // ✅ C# 14: Target-typed new
        this.frames = new PageFrame[capacity];
        this.statistics = new(); // ✅ C# 14: Target-typed new
        this.clockHand = 0;
        this.currentSize = 0;
    }

    /// <inheritdoc/>
    public int Capacity => capacity;

    /// <inheritdoc/>
    public int Count => Volatile.Read(ref currentSize);

    /// <inheritdoc/>
    public int PageSize => pageSize;

    /// <inheritdoc/>
    public PageCacheStatistics Statistics => statistics;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        // Flush and dispose all frames
        foreach (var (_, frame) in pageTable) // ✅ C# 14: Tuple deconstruction in foreach
        {
            frame.Dispose();
        }

        pageTable.Clear();
        disposed = true;
    }
}
