// <copyright file="ExtentAllocator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Extent allocator for efficient contiguous page allocation.
/// Uses best-fit and first-fit strategies to minimize fragmentation.
/// C# 14: Primary constructor, collection expressions, modern patterns.
/// 
/// Purpose: Shared by FreeSpaceManager (Phase 2) and OverflowPageManager (Phase 6).
/// Performance: O(log n) allocation via sorted extent list.
/// </summary>
public sealed class ExtentAllocator : IDisposable
{
    private readonly List<FreeExtent> _freeExtents = [];  // ✅ C# 14: Collection expression
    private readonly Lock _allocationLock = new();        // ✅ C# 14: Lock type
    private bool _isDirty;
    private bool _disposed;

    // ✅ Allocation strategy configuration
    private AllocationStrategy _strategy = AllocationStrategy.BestFit;

    /// <summary>
    /// Gets or sets the allocation strategy.
    /// </summary>
    public AllocationStrategy Strategy
    {
        get => _strategy;
        set => _strategy = value;
    }

    /// <summary>
    /// Gets the number of free extents tracked.
    /// </summary>
    public int ExtentCount
    {
        get
        {
            lock (_allocationLock)
            {
                return _freeExtents.Count;
            }
        }
    }

    /// <summary>
    /// Gets whether there are pending changes.
    /// </summary>
    public bool IsDirty
    {
        get
        {
            lock (_allocationLock)
            {
                return _isDirty;
            }
        }
    }

    /// <summary>
    /// Allocates a contiguous extent using the configured strategy.
    /// </summary>
    /// <param name="pageCount">Number of pages to allocate.</param>
    /// <returns>Allocated extent, or null if no suitable extent found.</returns>
    public FreeExtent? Allocate(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);

        lock (_allocationLock)
        {
            return _strategy switch
            {
                AllocationStrategy.BestFit => AllocateBestFit(pageCount),
                AllocationStrategy.FirstFit => AllocateFirstFit(pageCount),
                AllocationStrategy.WorstFit => AllocateWorstFit(pageCount),
                _ => throw new InvalidOperationException($"Unknown allocation strategy: {_strategy}")
            };
        }
    }

    /// <summary>
    /// Frees an extent and adds it back to the free list.
    /// Automatically coalesces with adjacent extents if possible.
    /// </summary>
    /// <param name="extent">Extent to free.</param>
    public void Free(FreeExtent extent)
    {
        lock (_allocationLock)
        {
            // Insert extent and coalesce with neighbors
            InsertAndCoalesce(extent);
            _isDirty = true;
        }
    }

    /// <summary>
    /// Manually triggers coalescing of adjacent extents.
    /// Useful for defragmentation.
    /// </summary>
    /// <returns>Number of extents coalesced.</returns>
    public int Coalesce()
    {
        lock (_allocationLock)
        {
            var originalCount = _freeExtents.Count;
            CoalesceInternal();
            var coalescedCount = originalCount - _freeExtents.Count;
            
            if (coalescedCount > 0)
            {
                _isDirty = true;
            }

            return coalescedCount;
        }
    }

    /// <summary>
    /// Gets the largest available extent size.
    /// </summary>
    /// <returns>Size of largest extent in pages, or 0 if no extents available.</returns>
    public ulong GetLargestExtentSize()
    {
        lock (_allocationLock)
        {
            return _freeExtents.Count > 0 
                ? _freeExtents.Max(e => e.Length) 
                : 0;
        }
    }

    /// <summary>
    /// Gets all free extents (for persistence/debugging).
    /// </summary>
    /// <returns>Read-only copy of extent list.</returns>
    public IReadOnlyList<FreeExtent> GetExtents()
    {
        lock (_allocationLock)
        {
            return _freeExtents.ToList();  // Return copy for thread-safety
        }
    }

    /// <summary>
    /// Replaces all extents (for loading from disk).
    /// </summary>
    /// <param name="extents">Extents to load.</param>
    public void LoadExtents(IEnumerable<FreeExtent> extents)
    {
        ArgumentNullException.ThrowIfNull(extents);

        lock (_allocationLock)
        {
            _freeExtents.Clear();
            _freeExtents.AddRange(extents);
            SortExtents();
            _isDirty = false;
        }
    }

    /// <summary>
    /// Clears the dirty flag (called after flush).
    /// </summary>
    public void MarkClean()
    {
        lock (_allocationLock)
        {
            _isDirty = false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_allocationLock)
        {
            _freeExtents.Clear();
            _disposed = true;
        }
    }

    // ========================================
    // Private allocation strategies
    // ========================================

    /// <summary>
    /// Best-fit allocation: finds smallest extent that fits.
    /// Minimizes fragmentation, good for general use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FreeExtent? AllocateBestFit(int pageCount)
    {
        FreeExtent? bestFit = null;
        var bestIndex = -1;
        var minWaste = ulong.MaxValue;

        for (var i = 0; i < _freeExtents.Count; i++)
        {
            var extent = _freeExtents[i];
            
            if (extent.CanFit((ulong)pageCount))
            {
                var waste = extent.Length - (ulong)pageCount;
                
                if (waste < minWaste)
                {
                    minWaste = waste;
                    bestFit = extent;
                    bestIndex = i;
                }

                // Perfect fit found
                if (waste == 0)
                {
                    break;
                }
            }
        }

        if (bestFit.HasValue && bestIndex >= 0)
        {
            RemoveAndSplitExtent(bestIndex, pageCount);
            _isDirty = true;
        }

        return bestFit;
    }

    /// <summary>
    /// First-fit allocation: finds first extent that fits.
    /// Fastest allocation, may increase fragmentation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FreeExtent? AllocateFirstFit(int pageCount)
    {
        for (var i = 0; i < _freeExtents.Count; i++)
        {
            var extent = _freeExtents[i];
            
            if (extent.CanFit((ulong)pageCount))
            {
                RemoveAndSplitExtent(i, pageCount);
                _isDirty = true;
                return extent;
            }
        }

        return null;
    }

    /// <summary>
    /// Worst-fit allocation: finds largest extent.
    /// Useful for overflow chains (maximize remaining contiguous space).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FreeExtent? AllocateWorstFit(int pageCount)
    {
        var largestIndex = -1;
        var largestSize = 0UL;

        for (var i = 0; i < _freeExtents.Count; i++)
        {
            var extent = _freeExtents[i];
            
            if (extent.CanFit((ulong)pageCount) && extent.Length > largestSize)
            {
                largestSize = extent.Length;
                largestIndex = i;
            }
        }

        if (largestIndex >= 0)
        {
            var extent = _freeExtents[largestIndex];
            RemoveAndSplitExtent(largestIndex, pageCount);
            _isDirty = true;
            return extent;
        }

        return null;
    }

    /// <summary>
    /// Removes extent from list and splits if necessary.
    /// </summary>
    private void RemoveAndSplitExtent(int index, int allocatedPages)
    {
        var extent = _freeExtents[index];
        _freeExtents.RemoveAt(index);

        // If extent is larger than needed, create remainder
        if (extent.Length > (ulong)allocatedPages)
        {
            var remainder = new FreeExtent(
                extent.StartPage + (ulong)allocatedPages,
                extent.Length - (ulong)allocatedPages);
            
            _freeExtents.Add(remainder);
            SortExtents();
        }
    }

    /// <summary>
    /// Inserts extent and coalesces with adjacent extents.
    /// </summary>
    private void InsertAndCoalesce(FreeExtent extent)
    {
        _freeExtents.Add(extent);
        SortExtents();
        CoalesceInternal();
    }

    /// <summary>
    /// Coalesces adjacent extents to reduce fragmentation.
    /// </summary>
    private void CoalesceInternal()
    {
        if (_freeExtents.Count <= 1) return;

        var i = 0;
        while (i < _freeExtents.Count - 1)
        {
            var current = _freeExtents[i];
            var next = _freeExtents[i + 1];

            // Check if extents are adjacent
            if (current.StartPage + current.Length == next.StartPage)
            {
                // Merge extents
                var merged = new FreeExtent(current.StartPage, current.Length + next.Length);
                _freeExtents[i] = merged;
                _freeExtents.RemoveAt(i + 1);
                // Don't increment i, check if more merges possible
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// Sorts extents by start page (required for coalescing).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SortExtents()
    {
        _freeExtents.Sort((a, b) => a.StartPage.CompareTo(b.StartPage));
    }
}

/// <summary>
/// Allocation strategy for extent allocator.
/// C# 14: Enum for strategy selection.
/// </summary>
public enum AllocationStrategy : byte
{
    /// <summary>Best-fit: smallest extent that fits (default, minimal fragmentation).</summary>
    BestFit = 0,

    /// <summary>First-fit: first extent that fits (fastest allocation).</summary>
    FirstFit = 1,

    /// <summary>Worst-fit: largest extent (useful for overflow chains).</summary>
    WorstFit = 2,
}
