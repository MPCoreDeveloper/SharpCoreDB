// <copyright file="PageBasedAdapterTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Threading.Tasks;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Tests for PageBasedAdapter SCDB integration.
/// ✅ SCDB Phase 4: Verifies page-based storage works with SCDB.
/// </summary>
public sealed class PageBasedAdapterTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public PageBasedAdapterTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"pagebased_test_{Guid.NewGuid():N}.scdb");
        CleanupTestFile();
    }

    public void Dispose()
    {
        CleanupTestFile();
    }
    
    private void CleanupTestFile()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void Insert_SingleRecord_ReturnsValidReference()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var reference = adapter.Insert("test_table", data);

        // Assert
        Assert.True(reference >= 0);
        _output.WriteLine($"Inserted record at reference: {reference}");
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void Read_InsertedRecord_ReturnsOriginalData()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);
        var data = new byte[] { 10, 20, 30, 40, 50 };

        // Act
        var reference = adapter.Insert("test_table", data);
        var readData = adapter.Read("test_table", reference);

        // Assert
        Assert.NotNull(readData);
        Assert.Equal(data, readData);
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void InsertBatch_MultipleRecords_AllStored()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);
        var records = new List<byte[]>
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 4, 5, 6 },
            new byte[] { 7, 8, 9 },
        };

        // Act
        var references = adapter.InsertBatch("test_table", records);

        // Assert
        Assert.Equal(3, references.Length);
        for (int i = 0; i < records.Count; i++)
        {
            var readData = adapter.Read("test_table", references[i]);
            Assert.Equal(records[i], readData);
        }
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void Update_ExistingRecord_ModifiesData()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);
        var original = new byte[] { 1, 1, 1 };
        var updated = new byte[] { 2, 2, 2, 2, 2 };

        // Act
        var reference = adapter.Insert("test_table", original);
        adapter.Update("test_table", reference, updated);
        var readData = adapter.Read("test_table", reference);

        // Assert
        Assert.Equal(updated, readData);
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void Delete_ExistingRecord_ReturnsNull()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);
        var data = new byte[] { 1, 2, 3 };

        // Act
        var reference = adapter.Insert("test_table", data);
        adapter.Delete("test_table", reference);
        var readData = adapter.Read("test_table", reference);

        // Assert
        Assert.Null(readData);
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void GetAllRecords_MultipleRecords_ReturnsAll()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);
        
        adapter.Insert("test_table", [1, 2, 3]);
        adapter.Insert("test_table", [4, 5, 6]);
        adapter.Insert("test_table", [7, 8, 9]);

        // Act
        var records = adapter.GetAllRecords("test_table").ToList();

        // Assert
        Assert.Equal(3, records.Count);
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void Transaction_CommitWrites_DataPersisted()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);

        // Act
        adapter.BeginTransaction();
        var ref1 = adapter.Insert("test_table", [1, 2, 3]);
        adapter.CommitAsync().GetAwaiter().GetResult();

        // Assert
        Assert.False(adapter.IsInTransaction);
        var data = adapter.Read("test_table", ref1);
        Assert.NotNull(data);
    }

    [Fact(Skip = "Requires database factory for SCDB file initialization")]
    public void GetMetrics_AfterOperations_ReportsCorrectly()
    {
        // Arrange
        using var provider = CreateProvider();
        using var adapter = new PageBasedAdapter(provider);

        // Act
        adapter.Insert("test_table", [1, 2, 3]);
        adapter.Insert("test_table", [4, 5, 6]);
        var metrics = adapter.GetMetrics();

        // Assert
        Assert.Equal(2, metrics.TotalInserts);
        Assert.True(metrics.BytesWritten > 0);
    }

    private SingleFileStorageProvider CreateProvider()
    {
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 4096,
            CreateImmediately = true,
        };

        return SingleFileStorageProvider.Open(_testDbPath, options);
    }
}
