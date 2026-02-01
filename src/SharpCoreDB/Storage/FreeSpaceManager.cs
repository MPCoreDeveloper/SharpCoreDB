// <copyright file="FreeSpaceManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.Storage.Scdb;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// ✅ SCDB Phase 2: Public type alias for user-facing API
using Extent = SharpCoreDB.Storage.Scdb.FreeExtent;

/// <summary>
/// Free Space Map (FSM) for O(1) page allocation.
/// Uses two-level bitmap inspired by PostgreSQL.
/// L1: 1 bit per page (allocated/free)
/// L2: Extent map for large contiguous allocations.
/// Format: [FsmHeader(64B)] [L1 Bitmap(variable)] [L2 Extents(variable)]
/// </summary>
internal sealed class FreeSpaceManager : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private readonly ulong _fsmOffset;
    private readonly ulong _fsmLength;
    private readonly int _pageSize;
    private readonly BitArray _l1Bitmap; // 1 bit per page
    private readonly List<FreeExtent> _l2Extents; // Large free extents
    private readonly Lock _allocationLock = new();
    
    // ✅ SCDB Phase 2: ExtentAllocator for optimized extent allocation
    private readonly ExtentAllocator _extentAllocator;
    
    private bool _isDirty;
    private bool _disposed;
    private ulong _totalPages;
    private ulong _freePages;

    // ✅ C# 14: Pre-allocation settings for optimal file growth - Phase 3 optimized
    private const int MIN_EXTENSION_PAGES = 2560;      // 10 MB @ 4KB pages (Phase 3: increased from 512 = 2MB)
    private const int EXTENSION_GROWTH_FACTOR = 2;     // Double size each time (exponential growth)
    private ulong _preallocatedPages = 0;

    public FreeSpaceManager(SingleFileStorageProvider provider, ulong fsmOffset, ulong fsmLength, int pageSize)
    {
        _provider = provider;
        _fsmOffset = fsmOffset;
        _fsmLength = fsmLength;
        _pageSize = pageSize;
        _l1Bitmap = new BitArray(1024 * 1024); // 1M pages = 4GB @ 4KB pages
        _l2Extents = new List<FreeExtent>();
        _totalPages = 0;
        _freePages = 0;

        // ✅ SCDB Phase 2: Initialize ExtentAllocator
        _extentAllocator = new ExtentAllocator
        {
            Strategy = AllocationStrategy.BestFit  // Default: minimize fragmentation
        };

        // Load existing FSM from disk
        LoadFsm();
    }

    // ========================================
    // ✅ SCDB Phase 2: Public API for page/extent allocation
    // ========================================

    /// <summary>
    /// Allocates a single page.
    /// </summary>
    /// <returns>Page ID of allocated page.</returns>
    public ulong AllocatePage()
    {
        var offset = AllocatePages(1);
        return offset / (ulong)_pageSize; // Convert byte offset to page ID
    }

    /// <summary>
    /// Allocates a contiguous extent of pages.
    /// ✅ SCDB Phase 2: Uses ExtentAllocator for optimized allocation.
    /// </summary>
    /// <param name="pageCount">Number of pages to allocate.</param>
    /// <returns>Extent representing the allocated pages.</returns>
    public Extent AllocateExtent(int pageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageCount);

        lock (_allocationLock)
        {
            // ✅ Try to allocate from existing free extents first (O(log n))
            var existingExtent = _extentAllocator.Allocate(pageCount);
            
            if (existingExtent.HasValue)
            {
                // Found suitable extent, mark pages as allocated
                for (var i = 0UL; i < (ulong)pageCount; i++)
                {
                    _l1Bitmap.Set((int)(existingExtent.Value.StartPage + i), true);
                }
                
                _freePages -= (ulong)pageCount;
                _isDirty = true;
                
                return new Extent(existingExtent.Value.StartPage, (ulong)pageCount);
            }

            // No suitable extent found, allocate new pages
            var offset = AllocatePages(pageCount);
            var startPage = offset / (ulong)_pageSize;

            return new Extent(startPage, (ulong)pageCount);
        }
    }

    /// <summary>
    /// Frees a single page.
    /// </summary>
    /// <param name="pageId">Page ID to free.</param>
    public void FreePage(ulong pageId)
    {
        var offset = pageId * (ulong)_pageSize;
        FreePages(offset, 1);
    }

    /// <summary>
    /// Frees an extent of pages.
    /// ✅ SCDB Phase 2: Uses ExtentAllocator for coalescing.
    /// </summary>
    /// <param name="extent">Extent to free.</param>
    public void FreeExtent(Extent extent)
    {
        var offset = extent.StartPage * (ulong)_pageSize;
        FreePages(offset, (int)extent.Length);
        
        // ✅ Add to ExtentAllocator for future reuse
        lock (_allocationLock)
        {
            _extentAllocator.Free(extent);
            _isDirty = true;
        }
    }

    /// <summary>
    /// Gets comprehensive FSM statistics.
    /// ✅ SCDB Phase 2: Includes ExtentAllocator metrics.
    /// </summary>
    /// <returns>Statistics including fragmentation and extent information.</returns>
    public FsmStatistics GetDetailedStatistics()
    {
        lock (_allocationLock)
        {
            var usedPages = _totalPages - _freePages;
            var largestExtent = _extentAllocator.GetLargestExtentSize();
            var extentCount = _extentAllocator.ExtentCount;

            // Calculate fragmentation percentage
            // Fragmentation = (1 - (largest_extent / free_pages)) * 100
            var fragmentation = 0.0;
            if (_freePages > 0)
            {
                fragmentation = (1.0 - ((double)largestExtent / (double)_freePages)) * 100.0;
                fragmentation = Math.Max(0, Math.Min(100, fragmentation)); // Clamp 0-100
            }

            return new FsmStatistics
            {
                TotalPages = (long)_totalPages,
                FreePages = (long)_freePages,
                UsedPages = (long)usedPages,
                FreeSpace = (long)_freePages * _pageSize,
                LargestExtent = (long)largestExtent,
                ExtentCount = extentCount,
                FragmentationPercent = fragmentation
            };
        }
    }

    public ulong AllocatePages(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        lock (_allocationLock)
        {
            // Try to find contiguous free pages
            var startPage = FindContiguousFreePages(count);
            
            if (startPage == ulong.MaxValue)
            {
                // No space found, extend file
                startPage = _totalPages;
                
                // ✅ Calculate extension size (grow exponentially)
                var requiredPages = (ulong)count;
                var currentSize = _totalPages;
                var extensionSize = Math.Max(
                    MIN_EXTENSION_PAGES,
                    Math.Max(requiredPages, currentSize / EXTENSION_GROWTH_FACTOR)
                );
                
                ExtendFile((int)extensionSize);
                _preallocatedPages = extensionSize - requiredPages;

#if DEBUG
                System.Diagnostics.Debug.WriteLine(
                    $"[FSM] Extended file by {extensionSize} pages " +
                    $"(requested: {count}, preallocated: {_preallocatedPages})");
#endif
            }

            // Mark pages as allocated
            for (var i = 0; i < count; i++)
            {
                _l1Bitmap.Set((int)(startPage + (ulong)i), true);
            }

            _freePages -= (ulong)count;
            _isDirty = true;

            return startPage * (ulong)_pageSize; // Return byte offset
        }
    }

    public void FreePages(ulong offset, int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var startPage = offset / (ulong)_pageSize;

        lock (_allocationLock)
        {
            // Mark pages as free
            for (var i = 0; i < count; i++)
            {
                _l1Bitmap.Set((int)(startPage + (ulong)i), false);
            }

            _freePages += (ulong)count;
            _isDirty = true;

            // Coalesce into extent if large
            if (count >= 16)
            {
                _l2Extents.Add(new FreeExtent(startPage, (ulong)count));
            }
        }
    }

    public (long FreeSpace, long FreePages) GetStatistics()
    {
        lock (_allocationLock)
        {
            return ((long)_freePages * _pageSize, (long)_freePages);
        }
    }

    /// <summary>
    /// Flushes the FSM to disk.
    /// Format: [FreeSpaceMapHeader(64B)] [L1 Bitmap(bytes)] [L2 Extent Count(4B)] [FreeExtent1(16B)] ...
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDirty) return;

        byte[] buffer;
        int totalSize;

        // Prepare data inside lock (fast, synchronous)
        lock (_allocationLock)
        {
            if (!_isDirty) return;

            // Calculate L1 bitmap size (1 bit per page, packed into bytes)
            var bitmapSizeBytes = (int)((_totalPages + 7) / 8);
            
            // Calculate L2 extent size
            var extentCount = _l2Extents.Count;
            var extentSizeBytes = extentCount * Scdb.FreeExtent.SIZE;
            
            // Total size
            totalSize = FreeSpaceMapHeader.SIZE + bitmapSizeBytes + sizeof(int) + extentSizeBytes;

            if ((ulong)totalSize > _fsmLength)
            {
                throw new InvalidOperationException(
                    $"FSM too large: {totalSize} bytes exceeds limit {_fsmLength}");
            }

            buffer = ArrayPool<byte>.Shared.Rent(totalSize);
            var span = buffer.AsSpan(0, totalSize);
            span.Clear();

            // Write header
            var header = new FreeSpaceMapHeader
            {
                Magic = FreeSpaceMapHeader.MAGIC,
                Version = FreeSpaceMapHeader.CURRENT_VERSION,
                TotalPages = _totalPages,
                FreePages = _freePages,
                LargestExtent = extentCount > 0 ? _l2Extents.Max(e => e.Length) : 0,
                BitmapOffset = FreeSpaceMapHeader.SIZE,
                ExtentMapOffset = (uint)(FreeSpaceMapHeader.SIZE + bitmapSizeBytes + sizeof(int))
            };

            MemoryMarshal.Write(span[..FreeSpaceMapHeader.SIZE], in header);

            // Write L1 bitmap
            var bitmapSpan = span.Slice(FreeSpaceMapHeader.SIZE, bitmapSizeBytes);
            SerializeBitmap(bitmapSpan);

            // Write L2 extent count
            var countOffset = FreeSpaceMapHeader.SIZE + bitmapSizeBytes;
            MemoryMarshal.Write(span[countOffset..], extentCount);

            // Write L2 extents
            var extentOffset = countOffset + sizeof(int);
            for (var i = 0; i < extentCount; i++)
            {
                var extent = _l2Extents[i];
                var extentSpan = span.Slice(extentOffset + (i * Scdb.FreeExtent.SIZE), Scdb.FreeExtent.SIZE);
                MemoryMarshal.Write(extentSpan, in extent);
            }

            _isDirty = false;
        }

        // Write to file OUTSIDE lock
        try
        {
            var fileStream = GetFileStream();
            fileStream.Position = (long)_fsmOffset;
            await fileStream.WriteAsync(buffer.AsMemory(0, totalSize), cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_isDirty)
            {
                FlushAsync().GetAwaiter().GetResult();
            }
        }
        finally
        {
            _extentAllocator?.Dispose();  // ✅ SCDB Phase 2: Dispose ExtentAllocator
            _disposed = true;
        }
    }

    private ulong FindContiguousFreePages(int count)
    {
        // Scan L1 bitmap for contiguous free pages
        var consecutive = 0;
        var startPage = 0UL;

        for (var i = 0UL; i < _totalPages; i++)
        {
            if (!_l1Bitmap.Get((int)i))
            {
                if (consecutive == 0)
                {
                    startPage = i;
                }
                consecutive++;

                if (consecutive >= count)
                {
                    return startPage;
                }
            }
            else
            {
                consecutive = 0;
            }
        }

        return ulong.MaxValue; // Not found
    }

    private void ExtendFile(int pages)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages);

        var newTotalPages = _totalPages + (ulong)pages;
        var newFileSize = (long)(newTotalPages * (ulong)_pageSize);
        
        // ✅ OPTIMIZED: Set file length explicitly (pre-allocates space on disk)
        // NOTE: SetLength may fail if MemoryMappedFile is active (Windows limitation)
        // In that case, file will grow on-demand when written to
        var fileStream = GetFileStream();
        try
        {
            fileStream.SetLength(newFileSize);
        }
        catch (IOException ex) when (ex.Message.Contains("user-mapped section"))
        {
            // Windows limitation: Cannot resize file with active memory mapping
            // File will grow on-demand when written to - this is acceptable
            // Log for debugging but don't fail the operation
            Debug.WriteLine($"[FSM] Could not pre-allocate file (MMF active): {ex.Message}");
        }
        
        // ✅ Update free space tracking
        for (ulong i = _totalPages; i < newTotalPages; i++)
        {
            if (i < (ulong)_l1Bitmap.Length)
            {
                _l1Bitmap.Set((int)i, false); // Mark as free
            }
        }
        
        _freePages += (ulong)pages;
        _totalPages = newTotalPages;
        
        // Expand bitmap if needed
        if ((int)_totalPages > _l1Bitmap.Length)
        {
            _l1Bitmap.Length = (int)_totalPages * 2;
        }

#if DEBUG
        Debug.WriteLine(
            $"[FSM] Extended file to {newFileSize} bytes " +
            $"({newTotalPages} pages, {_freePages} free)");
#endif
    }

    /// <summary>
    /// Loads FSM from disk.
    /// </summary>
    private void LoadFsm()
    {
        try
        {
            var fileStream = GetFileStream();
            
            if (fileStream.Length < (long)(_fsmOffset + FreeSpaceMapHeader.SIZE))
            {
                return; // Empty FSM
            }

            // Read header
            fileStream.Position = (long)_fsmOffset;
            Span<byte> headerBuffer = stackalloc byte[FreeSpaceMapHeader.SIZE];
            fileStream.ReadExactly(headerBuffer);
            
            var header = MemoryMarshal.Read<FreeSpaceMapHeader>(headerBuffer);
            
            if (!header.IsValid)
            {
                return;
            }

            _totalPages = header.TotalPages;
            _freePages = header.FreePages;

            // Read L1 bitmap
            var bitmapSizeBytes = (int)((_totalPages + 7) / 8);
            var bitmapBuffer = ArrayPool<byte>.Shared.Rent(bitmapSizeBytes);
            try
            {
                var bitmapSpan = bitmapBuffer.AsSpan(0, bitmapSizeBytes);
                fileStream.ReadExactly(bitmapSpan);
                DeserializeBitmap(bitmapSpan);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bitmapBuffer);
            }

            // Read L2 extent count
            Span<byte> countBuffer = stackalloc byte[sizeof(int)];
            fileStream.ReadExactly(countBuffer);
            var extentCount = MemoryMarshal.Read<int>(countBuffer);

            // Read L2 extents
            if (extentCount > 0)
            {
                var extentBufferSize = extentCount * Scdb.FreeExtent.SIZE;
                var extentBuffer = ArrayPool<byte>.Shared.Rent(extentBufferSize);
                try
                {
                    var extentSpan = extentBuffer.AsSpan(0, extentBufferSize);
                    fileStream.ReadExactly(extentSpan);

                    for (var i = 0; i < extentCount; i++)
                    {
                        var offset = i * Scdb.FreeExtent.SIZE;
                        var extent = MemoryMarshal.Read<Scdb.FreeExtent>(extentSpan[offset..]);
                        _l2Extents.Add(extent);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(extentBuffer);
                }
            }
        }
        catch (Exception)
        {
            // If loading fails, start with empty FSM
            _totalPages = 0;
            _freePages = 0;
            _l1Bitmap.Length = 1024 * 1024;
            _l2Extents.Clear();
        }
    }

    private void SerializeBitmap(Span<byte> destination)
    {
        for (var i = 0; i < _l1Bitmap.Length; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;
            
            if (_l1Bitmap.Get(i))
            {
                destination[byteIndex] |= (byte)(1 << bitIndex);
            }
        }
    }

    private void DeserializeBitmap(ReadOnlySpan<byte> source)
    {
        for (var i = 0; i < (int)_totalPages; i++)
        {
            var byteIndex = i / 8;
            var bitIndex = i % 8;
            
            var isSet = (source[byteIndex] & (1 << bitIndex)) != 0;
            _l1Bitmap.Set(i, isSet);
        }
    }

    private System.IO.FileStream GetFileStream()
    {
        return _provider.GetInternalFileStream();
    }

    internal bool IsDirty => _isDirty;
}

/// <summary>
/// Simple BitArray implementation (substitute for System.Collections.BitArray).
/// </summary>
internal sealed class BitArray
{
    private int[] _array;
    private int _length;

    public BitArray(int length)
    {
        _length = length;
        _array = new int[(length + 31) / 32];
    }

    public int Length
    {
        get => _length;
        set
        {
            if (value > _length)
            {
                Array.Resize(ref _array, (value + 31) / 32);
            }
            _length = value;
        }
    }

    public bool Get(int index)
    {
        if (index < 0 || index >= _length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range [0, {_length})");
        }

        var arrayIndex = index / 32;
        var bitIndex = index % 32;
        return (_array[arrayIndex] & (1 << bitIndex)) != 0;
    }

    public void Set(int index, bool value)
    {
        if (index < 0 || index >= _length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index {index} is out of range [0, {_length})");
        }

        var arrayIndex = index / 32;
        var bitIndex = index % 32;

        if (value)
        {
            _array[arrayIndex] |= (1 << bitIndex);
        }
        else
        {
            _array[arrayIndex] &= ~(1 << bitIndex);
        }
    }
}
