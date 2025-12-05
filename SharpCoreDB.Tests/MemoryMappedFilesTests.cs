using System;
using System.IO;
using Xunit;
using SharpCoreDB.Core.File;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for memory-mapped file functionality.
/// </summary>
public class MemoryMappedFilesTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly byte[] _testData;

    public MemoryMappedFilesTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_mmf_{Guid.NewGuid():N}.dat");
        
        // Create test data (15 MB to exceed the 10MB threshold for memory mapping)
        _testData = new byte[15 * 1024 * 1024];
        new Random(42).NextBytes(_testData);
        
        File.WriteAllBytes(_testFilePath, _testData);
    }

    [Fact]
    public void MemoryMappedFileHandler_ShouldReadAllBytes()
    {
        // Arrange & Act
        using var handler = new MemoryMappedFileHandler(_testFilePath, useMemoryMapping: true);
        var result = handler.ReadAllBytes();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_testData.Length, result.Length);
        Assert.Equal(_testData, result);
    }

    [Fact]
    public void MemoryMappedFileHandler_ShouldUseMemoryMapping_ForLargeFiles()
    {
        // Arrange & Act
        using var handler = new MemoryMappedFileHandler(_testFilePath, useMemoryMapping: true);

        // Assert
        Assert.True(handler.IsMemoryMapped);
        Assert.Equal(_testData.Length, handler.FileSize);
    }

    [Fact]
    public void MemoryMappedFileHandler_ShouldFallbackToFileStream_WhenDisabled()
    {
        // Arrange & Act
        using var handler = new MemoryMappedFileHandler(_testFilePath, useMemoryMapping: false);
        var result = handler.ReadAllBytes();

        // Assert
        Assert.False(handler.IsMemoryMapped);
        Assert.NotNull(result);
        Assert.Equal(_testData.Length, result.Length);
    }

    [Fact]
    public void MemoryMappedFileHandler_ShouldReadPartialData()
    {
        // Arrange
        using var handler = new MemoryMappedFileHandler(_testFilePath, useMemoryMapping: true);
        var buffer = new byte[1024];
        const long offset = 1000;

        // Act
        var bytesRead = handler.ReadBytes(offset, buffer);

        // Assert
        Assert.Equal(buffer.Length, bytesRead);
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(_testData[offset + i], buffer[i]);
        }
    }

    [Fact]
    public void MemoryMappedFileHandler_TryCreate_ShouldReturnNull_ForNonExistentFile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_file.dat");

        // Act
        var handler = MemoryMappedFileHandler.TryCreate(nonExistentPath);

        // Assert
        Assert.Null(handler);
    }

    [Fact]
    public void MemoryMappedFileHandler_TryCreate_ShouldReturnHandler_ForExistingFile()
    {
        // Act
        using var handler = MemoryMappedFileHandler.TryCreate(_testFilePath);

        // Assert
        Assert.NotNull(handler);
        Assert.True(handler.IsMemoryMapped);
    }

    [Fact]
    public void MemoryMappedFileHandler_ShouldThrow_ForNonExistentFile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_file.dat");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => new MemoryMappedFileHandler(nonExistentPath));
    }

    [Fact]
    public void MemoryMappedFileHandler_ShouldNotUseMMF_ForSmallFiles()
    {
        // Arrange - Create small file (< 10MB)
        var smallFilePath = Path.Combine(Path.GetTempPath(), $"test_small_{Guid.NewGuid():N}.dat");
        var smallData = new byte[1024 * 1024]; // 1MB
        File.WriteAllBytes(smallFilePath, smallData);

        try
        {
            // Act
            using var handler = new MemoryMappedFileHandler(smallFilePath, useMemoryMapping: true);

            // Assert
            Assert.False(handler.IsMemoryMapped); // Should fallback due to size
            var result = handler.ReadAllBytes();
            Assert.Equal(smallData.Length, result.Length);
        }
        finally
        {
            if (File.Exists(smallFilePath))
                File.Delete(smallFilePath);
        }
    }

    [Fact]
    public void MemoryMappedFileHandler_Dispose_ShouldCleanupResources()
    {
        // Arrange
        var handler = new MemoryMappedFileHandler(_testFilePath, useMemoryMapping: true);
        
        // Act
        handler.Dispose();

        // Assert - Should not throw when calling Dispose multiple times
        handler.Dispose();
        
        // Should throw when trying to use disposed handler
        Assert.Throws<ObjectDisposedException>(() => handler.ReadAllBytes());
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            try
            {
                File.Delete(_testFilePath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
