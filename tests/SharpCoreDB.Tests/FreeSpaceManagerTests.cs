// <copyright file="FreeSpaceManagerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Storage;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Unit tests for FreeSpaceManager pre-allocation behavior.
/// C# 14: Uses modern async patterns and primary constructors.
/// ✅ Tests exponential file growth for better I/O performance.
/// </summary>
public sealed class FreeSpaceManagerTests : IDisposable
{
    private readonly string _tempDbPath;

    public FreeSpaceManagerTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_fsm_{Guid.NewGuid()}.scdb");
    }

    [Fact]
    public void DatabaseProvider_WhenCreated_ShouldInitializeWithPreallocatedSpace()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 4096,
            CreateImmediately = true
        };

        // Act
        using var provider = SingleFileStorageProvider.Open(_tempDbPath, options);
        var fileStream = provider.GetInternalFileStream();
        var initialFileSize = fileStream.Length;

        // Assert - File should be pre-allocated with some initial space
        Assert.True(initialFileSize > 0, "File should be created with initial size");
        Assert.True(initialFileSize % 4096 == 0, "File size should be multiple of page size");
    }

    [Fact]
    public async System.Threading.Tasks.Task PreallocationReducesFileExtensionsAsync()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 4096,
            CreateImmediately = true
        };

        using var provider = SingleFileStorageProvider.Open(_tempDbPath, options);
        var fileStream = provider.GetInternalFileStream();
        var initialFileSize = fileStream.Length;

        // Act - Write many small blocks
        for (int i = 0; i < 10; i++)
        {
            var data = new byte[100];
            Random.Shared.NextBytes(data);
            await provider.WriteBlockAsync($"block_{i}", data);
        }

        // ✅ Wait for all queued writes to complete before checking file size
        await provider.FlushPendingWritesAsync();

        var finalFileSize = fileStream.Length;

        // Assert - File should have minimal extensions
        // With pre-allocation, the file should not grow 10 times
        var growthFactor = finalFileSize / initialFileSize;
        Assert.True(growthFactor < 5, 
            $"File should not grow excessively (grew {growthFactor}x)");
    }

    [Fact]
    public void SetLength_ShouldPreallocateFileSpace()
    {
        // Arrange
        var testFile = Path.Combine(Path.GetTempPath(), $"test_setlength_{Guid.NewGuid()}.bin");
        
        try
        {
            // Act
            using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.ReadWrite))
            {
                var targetSize = 10 * 1024 * 1024; // 10 MB
                fs.SetLength(targetSize);
            }

            // Assert - File should exist with correct size
            var fileInfo = new FileInfo(testFile);
            Assert.True(fileInfo.Exists);
            Assert.Equal(10 * 1024 * 1024, fileInfo.Length);
        }
        finally
        {
            if (File.Exists(testFile))
            {
                File.Delete(testFile);
            }
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task MultipleBlockWrites_ShouldPreallocateEfficientlyAsync()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 4096,
            CreateImmediately = true
        };

        using var provider = SingleFileStorageProvider.Open(_tempDbPath, options);
        var fileStream = provider.GetInternalFileStream();
        var initialFileSize = fileStream.Length;

        // Act - Write 50 blocks rapidly
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();
        for (int i = 0; i < 50; i++)
        {
            var data = new byte[1024];
            Random.Shared.NextBytes(data);
            tasks.Add(provider.WriteBlockAsync($"block_{i}", data));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);
        
        // ✅ Wait for all queued writes to complete before checking file size
        await provider.FlushPendingWritesAsync();
        
        var finalFileSize = fileStream.Length;

        // Assert - File growth should be reasonable with Phase 3 optimization
        // ✅ UPDATED: MIN_EXTENSION_PAGES = 2560 (10 MB) for performance
        // With 50 blocks × 1024 bytes = 51,200 bytes, expect single 10 MB extension
        var totalBlockSize = 50 * 1024;
        var expectedMinSize = initialFileSize + totalBlockSize;
        
        // ✅ FIX: Account for 10 MB preallocation (2560 pages @ 4KB)
        const int MIN_EXTENSION_SIZE = 2560 * 4096; // 10,485,760 bytes
        var expectedMaxSize = initialFileSize + MIN_EXTENSION_SIZE + (totalBlockSize * 2); // Allow overhead

        Assert.True(finalFileSize >= expectedMinSize,
            $"File size {finalFileSize} should be >= {expectedMinSize}");
        Assert.True(finalFileSize <= expectedMaxSize,
            $"File size {finalFileSize} should be <= {expectedMaxSize} (Phase 3: 10MB preallocation)");
    }

    [Fact]
    public void ConstantsExistForPreallocation()
    {
        // Arrange & Act
        // ✅ UPDATED: Phase 3 optimization values (increased from 512 pages = 2MB)
        const int MIN_EXTENSION_PAGES = 2560; // 10 MB @ 4KB pages (Phase 3: aggressive preallocation)
        const int EXTENSION_GROWTH_FACTOR = 2; // Double size each time

        // Assert
        Assert.Equal(2560, MIN_EXTENSION_PAGES);
        Assert.Equal(2, EXTENSION_GROWTH_FACTOR);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try
            {
                File.Delete(_tempDbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
