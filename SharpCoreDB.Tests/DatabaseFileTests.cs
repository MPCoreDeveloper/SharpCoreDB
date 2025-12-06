// <copyright file="DatabaseFileTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.Core.File;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Unit tests for DatabaseFile page I/O operations.
/// </summary>
public class DatabaseFileTests : IDisposable
{
    private readonly string testFilePath;
    private readonly ICryptoService crypto;
    private readonly byte[] key;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseFileTests"/> class.
    /// </summary>
    public DatabaseFileTests()
    {
        this.testFilePath = Path.Combine(Path.GetTempPath(), $"DatabaseFileTest_{Guid.NewGuid()}.db");
        this.crypto = new CryptoService();
        this.key = this.crypto.DeriveKey("testpassword", "testsalt");
    }

    /// <summary>
    /// Cleans up test resources.
    /// </summary>
    public void Dispose()
    {
        if (File.Exists(this.testFilePath))
        {
            File.Delete(this.testFilePath);
        }
    }

    /// <summary>
    /// Tests reading and writing a page.
    /// </summary>
    [Fact]
    public void ReadWritePage_ShouldWorkCorrectly()
    {
        // Arrange
        using var dbFile = new DatabaseFile(this.testFilePath, this.crypto, this.key);
        var testData = new byte[4096];
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = (byte)(i % 256);
        }

        // Act
        dbFile.WritePage(0, testData);
        var readData = dbFile.ReadPage(0);

        // Assert
        Assert.Equal(testData, readData);
    }

    /// <summary>
    /// Tests zero-allocation read.
    /// </summary>
    [Fact]
    public void ReadPageZeroAlloc_ShouldReadIntoBuffer()
    {
        // Arrange
        using var dbFile = new DatabaseFile(this.testFilePath, this.crypto, this.key);
        var testData = new byte[4096];
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = (byte)(i % 256);
        }
        var buffer = new byte[4096];

        // Act
        dbFile.WritePage(0, testData);
        int bytesRead = dbFile.ReadPageZeroAlloc(0, buffer);

        // Assert
        Assert.Equal(4096, bytesRead);
        Assert.Equal(testData, buffer);
    }

    /// <summary>
    /// Tests PageBuffer ref struct.
    /// </summary>
    [Fact]
    public void PageBuffer_ShouldHandleUInt32Operations()
    {
        // Arrange
        var buffer = new DatabaseFile.PageBuffer(4096);
        uint testValue = 0x12345678;

        // Act
        buffer.WriteUInt32LittleEndian(0, testValue);
        uint readValue = buffer.ReadUInt32LittleEndian(0);

        // Assert
        Assert.Equal(testValue, readValue);
    }

    // TODO: Add more comprehensive tests for edge cases, multiple pages, encryption verification, etc.
}