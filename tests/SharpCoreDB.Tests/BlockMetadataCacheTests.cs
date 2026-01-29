// <copyright file="BlockMetadataCacheTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using System;
using Xunit;

/// <summary>
/// Tests for BlockMetadataCache - Phase 3.2 optimization.
/// Verifies LRU eviction, cache hit/miss tracking, and thread safety.
/// </summary>
public sealed class BlockMetadataCacheTests
{
    [Fact]
    public void BlockMetadataCache_AddAndGet_Succeeds()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };

        // Act
        cache.Add("test_block", entry);
        var found = cache.TryGet("test_block", out var retrieved);

        // Assert
        Assert.True(found);
        Assert.Equal(entry.Offset, retrieved.Offset);
        Assert.Equal(entry.Length, retrieved.Length);
    }

    [Fact]
    public void BlockMetadataCache_Miss_ReturnsF​alse()
    {
        // Arrange
        var cache = new BlockMetadataCache();

        // Act
        var found = cache.TryGet("nonexistent", out _);

        // Assert
        Assert.False(found);
    }

    [Fact]
    public void BlockMetadataCache_LRU_EvictsOldest()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 0,
            Length = 512,
            Flags = 0
        };

        // Act - Add 1001 entries (max is 1000)
        for (int i = 0; i < 1001; i++)
        {
            cache.Add($"block_{i}", entry with { Offset = (ulong)i });
        }

        // Assert - First block should be evicted
        Assert.False(cache.TryGet("block_0", out _), "First block should be evicted");
        Assert.True(cache.TryGet("block_1000", out _), "Last block should still be cached");
        
        var stats = cache.GetStatistics();
        Assert.Equal(1000, stats.Size);
    }

    [Fact]
    public void BlockMetadataCache_Update_RefreshesEntry()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry1 = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };
        var entry2 = entry1 with { Offset = 2000 };

        // Act
        cache.Add("test_block", entry1);
        cache.Add("test_block", entry2); // Update
        var found = cache.TryGet("test_block", out var retrieved);

        // Assert
        Assert.True(found);
        Assert.Equal(2000ul, retrieved.Offset);
    }

    [Fact]
    public void BlockMetadataCache_Remove_DeletesEntry()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };

        // Act
        cache.Add("test_block", entry);
        var removed = cache.Remove("test_block");
        var found = cache.TryGet("test_block", out _);

        // Assert
        Assert.True(removed);
        Assert.False(found);
    }

    [Fact]
    public void BlockMetadataCache_Clear_RemovesAll()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };

        // Act
        for (int i = 0; i < 100; i++)
        {
            cache.Add($"block_{i}", entry);
        }
        cache.Clear();

        // Assert
        var stats = cache.GetStatistics();
        Assert.Equal(0, stats.Size);
        Assert.False(cache.TryGet("block_0", out _));
    }

    [Fact]
    public void BlockMetadataCache_Statistics_TrackHitRate()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };

        cache.Add("block_1", entry);
        cache.Add("block_2", entry);

        // Act
        cache.TryGet("block_1", out _); // Hit
        cache.TryGet("block_1", out _); // Hit
        cache.TryGet("block_2", out _); // Hit
        cache.TryGet("block_3", out _); // Miss
        cache.TryGet("block_4", out _); // Miss

        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(3, stats.Hits);
        Assert.Equal(2, stats.Misses);
        Assert.Equal(0.6, stats.HitRate, precision: 2); // 3/(3+2) = 0.6
    }

    [Fact]
    public void BlockMetadataCache_ResetStatistics_ClearsCounters()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };

        cache.Add("block_1", entry);
        cache.TryGet("block_1", out _); // Hit
        cache.TryGet("block_2", out _); // Miss

        // Act
        cache.ResetStatistics();
        var stats = cache.GetStatistics();

        // Assert
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
        Assert.Equal(0.0, stats.HitRate);
    }

    [Fact]
    public void BlockMetadataCache_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var cache = new BlockMetadataCache();
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = 1000,
            Length = 512,
            Flags = 0
        };

        // Act - Concurrent adds and gets
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                cache.Add($"block_{index}", entry with { Offset = (ulong)index });
                cache.TryGet($"block_{index}", out _);
            }));
        }

        Task.WaitAll([.. tasks]);

        // Assert - No exceptions, cache is consistent
        var stats = cache.GetStatistics();
        Assert.True(stats.Size > 0);
        Assert.True(stats.Size <= 100);
    }
}
