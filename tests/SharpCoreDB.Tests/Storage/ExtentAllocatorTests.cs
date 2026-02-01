// <copyright file="ExtentAllocatorTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.Linq;
using SharpCoreDB.Storage.Scdb;
using Xunit;

/// <summary>
/// Comprehensive tests for ExtentAllocator (SCDB Phase 2).
/// Tests allocation strategies, coalescing, and edge cases.
/// C# 14: Modern test patterns with collection expressions.
/// </summary>
public sealed class ExtentAllocatorTests : IDisposable
{
    private readonly ExtentAllocator _allocator = new();

    public void Dispose()
    {
        _allocator?.Dispose();
    }

    // ========================================
    // Basic Allocation Tests
    // ========================================

    [Fact]
    public void Allocate_WithNoExtents_ReturnsNull()
    {
        // Act
        var result = _allocator.Allocate(10);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Allocate_WithExactFit_ReturnsExtent()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));

        // Act
        var result = _allocator.Allocate(10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100UL, result.Value.StartPage);
        Assert.Equal(10UL, result.Value.Length);
        Assert.Equal(0, _allocator.ExtentCount); // Consumed completely
    }

    [Fact]
    public void Allocate_WithLargerExtent_ReturnsAndSplits()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 50));

        // Act
        var result = _allocator.Allocate(10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100UL, result.Value.StartPage);
        Assert.Equal(50UL, result.Value.Length); // Returns original extent info
        Assert.Equal(1, _allocator.ExtentCount); // Remainder should exist
        
        var extents = _allocator.GetExtents();
        Assert.Equal(110UL, extents[0].StartPage); // Remainder starts at 110
        Assert.Equal(40UL, extents[0].Length);      // Remainder is 40 pages
    }

    // ========================================
    // Strategy Tests
    // ========================================

    [Fact]
    public void BestFit_SelectsSmallestSuitableExtent()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 20)); // 20 pages - best fit
        _allocator.Free(new FreeExtent(200, 50)); // 50 pages - too large
        _allocator.Free(new FreeExtent(300, 15)); // 15 pages - too small
        
        _allocator.Strategy = AllocationStrategy.BestFit;

        // Act
        var result = _allocator.Allocate(18);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100UL, result.Value.StartPage); // Should pick 20-page extent
    }

    [Fact]
    public void FirstFit_SelectsFirstSuitableExtent()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 15)); // Too small
        _allocator.Free(new FreeExtent(200, 30)); // First fit
        _allocator.Free(new FreeExtent(300, 25)); // Also fits but not first
        
        _allocator.Strategy = AllocationStrategy.FirstFit;

        // Act
        var result = _allocator.Allocate(20);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200UL, result.Value.StartPage); // Should pick first suitable
    }

    [Fact]
    public void WorstFit_SelectsLargestExtent()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 20));
        _allocator.Free(new FreeExtent(200, 100)); // Largest - worst fit
        _allocator.Free(new FreeExtent(400, 50));
        
        _allocator.Strategy = AllocationStrategy.WorstFit;

        // Act
        var result = _allocator.Allocate(10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(200UL, result.Value.StartPage); // Should pick largest
    }

    // ========================================
    // Coalescing Tests
    // ========================================

    [Fact]
    public void Coalesce_AdjacentExtents_Merges()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));
        _allocator.Free(new FreeExtent(110, 10)); // Adjacent
        _allocator.Free(new FreeExtent(120, 10)); // Adjacent
        
        Assert.Equal(3, _allocator.ExtentCount);

        // Act
        var coalescedCount = _allocator.Coalesce();

        // Assert
        Assert.Equal(2, coalescedCount); // 3 -> 1, so 2 merges
        Assert.Equal(1, _allocator.ExtentCount);
        
        var extent = _allocator.GetExtents()[0];
        Assert.Equal(100UL, extent.StartPage);
        Assert.Equal(30UL, extent.Length);
    }

    [Fact]
    public void Coalesce_NonAdjacentExtents_NoMerge()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));
        _allocator.Free(new FreeExtent(200, 10)); // Gap
        _allocator.Free(new FreeExtent(300, 10)); // Gap
        
        // Act
        var coalescedCount = _allocator.Coalesce();

        // Assert
        Assert.Equal(0, coalescedCount);
        Assert.Equal(3, _allocator.ExtentCount); // No merges
    }

    [Fact]
    public void Free_AutomaticallyCoalesces()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));
        _allocator.Free(new FreeExtent(120, 10)); // Gap

        // Act - Free adjacent extent
        _allocator.Free(new FreeExtent(110, 10)); // Fills gap

        // Assert - Should auto-coalesce
        var extents = _allocator.GetExtents();
        Assert.Equal(1, extents.Count);
        Assert.Equal(100UL, extents[0].StartPage);
        Assert.Equal(30UL, extents[0].Length);
    }

    // ========================================
    // Edge Cases
    // ========================================

    [Fact]
    public void Allocate_ZeroOrNegativePages_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _allocator.Allocate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _allocator.Allocate(-1));
    }

    [Fact]
    public void Allocate_LargerThanAnyExtent_ReturnsNull()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));
        _allocator.Free(new FreeExtent(200, 20));

        // Act
        var result = _allocator.Allocate(50);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetLargestExtentSize_WithNoExtents_ReturnsZero()
    {
        // Act
        var size = _allocator.GetLargestExtentSize();

        // Assert
        Assert.Equal(0UL, size);
    }

    [Fact]
    public void GetLargestExtentSize_WithMultipleExtents_ReturnsMax()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));
        _allocator.Free(new FreeExtent(200, 50)); // Largest
        _allocator.Free(new FreeExtent(300, 25));

        // Act
        var size = _allocator.GetLargestExtentSize();

        // Assert
        Assert.Equal(50UL, size);
    }

    // ========================================
    // Load/Save Tests
    // ========================================

    [Fact]
    public void LoadExtents_RestoresState()
    {
        // Arrange
        var extents = new[]
        {
            new FreeExtent(100, 10),
            new FreeExtent(200, 20),
            new FreeExtent(300, 30),
        };

        // Act
        _allocator.LoadExtents(extents);

        // Assert
        Assert.Equal(3, _allocator.ExtentCount);
        Assert.False(_allocator.IsDirty); // Should not be dirty after load
        
        var loaded = _allocator.GetExtents();
        Assert.Equal(extents.Length, loaded.Count);
    }

    [Fact]
    public void MarkClean_ClearsDirtyFlag()
    {
        // Arrange
        _allocator.Free(new FreeExtent(100, 10));
        Assert.True(_allocator.IsDirty);

        // Act
        _allocator.MarkClean();

        // Assert
        Assert.False(_allocator.IsDirty);
    }

    // ========================================
    // Stress Tests
    // ========================================

    [Fact]
    public void StressTest_ThousandAllocations_WorksCorrectly()
    {
        // Arrange
        for (int i = 0; i < 1000; i++)
        {
            _allocator.Free(new FreeExtent((ulong)(i * 100), (ulong)(10 + (i % 90))));
        }

        // Act & Assert - Allocate and free 1000 times
        for (int i = 0; i < 1000; i++)
        {
            var extent = _allocator.Allocate(10);
            Assert.NotNull(extent);
            _allocator.Free(extent.Value);
        }
    }

    [Fact]
    public void StressTest_Fragmentation_CoalescesCorrectly()
    {
        // Arrange - Create highly fragmented state
        for (ulong i = 0; i < 100; i++)
        {
            _allocator.Free(new FreeExtent(i * 20, 10)); // Gaps between extents
        }

        var countBefore = _allocator.ExtentCount;

        // Act - Fill gaps to enable coalescing
        for (ulong i = 0; i < 100; i++)
        {
            _allocator.Free(new FreeExtent(i * 20 + 10, 10)); // Fill gaps
        }

        // Assert - Should have coalesced significantly
        Assert.True(_allocator.ExtentCount < countBefore,
            "Coalescing should reduce extent count");
    }
}
