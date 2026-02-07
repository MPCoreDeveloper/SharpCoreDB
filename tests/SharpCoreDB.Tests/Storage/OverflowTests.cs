// <copyright file="OverflowTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Threading.Tasks;
using SharpCoreDB.Storage.Overflow;
using Xunit;

/// <summary>
/// Tests for Phase 6: Overflow storage components.
/// ✅ SCDB Phase 6: FileStreamManager, OverflowPageManager, StorageStrategy tests.
/// </summary>
public sealed class OverflowTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public OverflowTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"overflow_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    // ========================================
    // StorageStrategy Tests
    // ========================================

    [Theory]
    [InlineData(100, StorageMode.Inline)]
    [InlineData(4096, StorageMode.Inline)]
    [InlineData(4097, StorageMode.Overflow)]
    [InlineData(100000, StorageMode.Overflow)]
    [InlineData(262144, StorageMode.Overflow)]
    [InlineData(262145, StorageMode.FileStream)]
    [InlineData(1000000, StorageMode.FileStream)]
    public void DetermineMode_VariousSizes_ReturnsCorrectMode(int size, StorageMode expected)
    {
        // Act
        var actual = StorageStrategy.DetermineMode(size);

        // Assert
        Assert.Equal(expected, actual);
        _output.WriteLine($"{size} bytes -> {actual}");
    }

    [Fact]
    public void DetermineMode_CustomThresholds_Respected()
    {
        // Arrange
        var options = new StorageOptions
        {
            InlineThreshold = 1024,     // 1KB
            OverflowThreshold = 10240,  // 10KB
        };

        // Act & Assert
        Assert.Equal(StorageMode.Inline, StorageStrategy.DetermineMode(1024, options));
        Assert.Equal(StorageMode.Overflow, StorageStrategy.DetermineMode(1025, options));
        Assert.Equal(StorageMode.Overflow, StorageStrategy.DetermineMode(10240, options));
        Assert.Equal(StorageMode.FileStream, StorageStrategy.DetermineMode(10241, options));
    }

    [Theory]
    [InlineData(4000, 4096, 1)]      // 4000 < 4054 usable space
    [InlineData(4054, 4096, 1)]      // Exact usable space
    [InlineData(4055, 4096, 2)]      // Just over usable space
    [InlineData(8000, 4096, 2)]      // 2 pages
    [InlineData(16000, 4096, 4)]     // 4 pages
    [InlineData(100000, 4096, 25)]   // 25 pages
    public void CalculateOverflowPages_VariousSizes_ReturnsCorrectCount(int dataSize, int pageSize, int expected)
    {
        // Act
        var actual = StorageStrategy.CalculateOverflowPages(dataSize, pageSize);

        // Assert
        Assert.Equal(expected, actual);
        _output.WriteLine($"{dataSize} bytes with {pageSize} page size -> {actual} pages");
    }

    // ========================================
    // FileStreamManager Tests
    // ========================================

    [Fact]
    public async Task WriteAsync_ValidData_CreatesFileAndMetadata()
    {
        // Arrange
        using var manager = new FileStreamManager(_testDbPath);
        var data = new byte[1024];
        Random.Shared.NextBytes(data);

        // Act
        var pointer = await manager.WriteAsync(
            rowId: 123,
            tableName: "test_table",
            columnName: "data_column",
            data: data,
            contentType: "application/octet-stream");

        // Assert
        Assert.NotEqual(Guid.Empty, pointer.FileId);
        Assert.Equal(1024, pointer.FileSize);
        Assert.Equal("test_table", pointer.TableName);
        Assert.Equal("data_column", pointer.ColumnName);
        Assert.Equal(123, pointer.RowId);
        Assert.NotNull(pointer.Checksum);
        Assert.Equal(32, pointer.Checksum.Length);  // SHA-256

        _output.WriteLine($"Created file: {pointer.FileId}");
        _output.WriteLine($"Path: {pointer.RelativePath}");
    }

    [Fact]
    public async Task ReadAsync_ExistingFile_ReturnsData()
    {
        // Arrange
        using var manager = new FileStreamManager(_testDbPath);
        var originalData = new byte[2048];
        Random.Shared.NextBytes(originalData);

        var pointer = await manager.WriteAsync(
            rowId: 1,
            tableName: "test",
            columnName: "col",
            data: originalData);

        // Act
        var readData = await manager.ReadAsync(pointer);

        // Assert
        Assert.Equal(originalData, readData);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesFile()
    {
        // Arrange
        using var manager = new FileStreamManager(_testDbPath);
        var data = new byte[512];

        var pointer = await manager.WriteAsync(
            rowId: 1,
            tableName: "test",
            columnName: "col",
            data: data);

        Assert.True(manager.FileExists(pointer.FileId));

        // Act
        await manager.DeleteAsync(pointer);

        // Assert
        Assert.False(manager.FileExists(pointer.FileId));
    }

    [Fact]
    public async Task WriteAsync_LargeFile_HandlesCorrectly()
    {
        // Arrange
        using var manager = new FileStreamManager(_testDbPath);
        var data = new byte[10 * 1024 * 1024];  // 10MB
        Random.Shared.NextBytes(data);

        // Act
        var pointer = await manager.WriteAsync(
            rowId: 1,
            tableName: "large",
            columnName: "blob",
            data: data);

        var readData = await manager.ReadAsync(pointer);

        // Assert
        Assert.Equal(data.Length, pointer.FileSize);
        Assert.Equal(data, readData);

        _output.WriteLine($"Wrote and read 10MB file successfully");
    }

    // ========================================
    // OverflowPageManager Tests
    // ========================================

    [Fact]
    public async Task CreateChainAsync_SmallData_CreatesSinglePage()
    {
        // Arrange
        using var manager = new OverflowPageManager(_testDbPath);
        var data = new byte[1000];
        Random.Shared.NextBytes(data);

        // Act
        var firstPageId = await manager.CreateChainAsync(rowId: 1, data);

        // Assert
        Assert.True(firstPageId > 0);

        var readData = await manager.ReadChainAsync(firstPageId);
        Assert.Equal(data, readData);
    }

    [Fact]
    public async Task CreateChainAsync_LargeData_CreatesMultiplePages()
    {
        // Arrange
        using var manager = new OverflowPageManager(_testDbPath);
        var data = new byte[50000];  // ~12 pages needed
        Random.Shared.NextBytes(data);

        // Act
        var firstPageId = await manager.CreateChainAsync(rowId: 1, data);
        var readData = await manager.ReadChainAsync(firstPageId);

        // Assert
        Assert.Equal(data, readData);

        _output.WriteLine($"50KB data stored in chain starting at page {firstPageId}");
    }

    [Fact]
    public async Task DeleteChainAsync_ExistingChain_RemovesAllPages()
    {
        // Arrange
        using var manager = new OverflowPageManager(_testDbPath);
        var data = new byte[20000];
        Random.Shared.NextBytes(data);

        var firstPageId = await manager.CreateChainAsync(rowId: 1, data);

        // Act
        await manager.DeleteChainAsync(firstPageId);

        // Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => 
            manager.ReadChainAsync(firstPageId));
    }

    [Fact]
    public async Task ValidateChainAsync_ValidChain_ReturnsTrue()
    {
        // Arrange
        using var manager = new OverflowPageManager(_testDbPath);
        var data = new byte[30000];
        Random.Shared.NextBytes(data);

        var firstPageId = await manager.CreateChainAsync(rowId: 1, data);

        // Act
        var (isValid, error) = await manager.ValidateChainAsync(firstPageId);

        // Assert
        Assert.True(isValid);
        Assert.Null(error);
    }

    // ========================================
    // FilePointer Tests
    // ========================================

    [Fact]
    public void GenerateRelativePath_ValidGuid_ReturnsCorrectFormat()
    {
        // Arrange
        var fileId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");

        // Act
        var path = FilePointer.GenerateRelativePath(fileId);

        // Assert
        Assert.Equal("ab/cd/abcdef1234567890abcdef1234567890.bin", path);
        Assert.Contains("/", path);
        Assert.EndsWith(".bin", path);
    }

    // ========================================
    // StorageOptions Tests
    // ========================================

    [Fact]
    public void StorageOptions_Default_HasCorrectValues()
    {
        // Act
        var options = StorageOptions.Default;

        // Assert
        Assert.Equal(4096, options.InlineThreshold);
        Assert.Equal(262144, options.OverflowThreshold);
        Assert.True(options.EnableFileStream);
        Assert.Equal("blobs", options.FileStreamPath);
        Assert.Equal(TimeSpan.FromDays(7), options.OrphanRetentionPeriod);
        Assert.True(options.EnableOrphanDetection);
    }
}
