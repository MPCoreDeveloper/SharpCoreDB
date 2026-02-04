// <copyright file="DatabaseStorageProviderTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// Tests for Database class integration with IStorageProvider (SCDB Phase 1).
/// Uses a simple mock provider to verify Database correctly delegates to IStorageProvider.
/// </summary>
public sealed class DatabaseStorageProviderTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _services;

    public DatabaseStorageProviderTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDbPath);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ICryptoService, CryptoService>();
        _services = serviceCollection.BuildServiceProvider();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void Database_WithStorageProvider_SavesMetadataToProvider()
    {
        // Arrange
        using var mockProvider = new MockStorageProvider();

        // Act - Create database with storage provider
        using var db = new Database(_services, _testDbPath, "test123", storageProvider: mockProvider);

        // Create a test table
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");

        // Force save metadata
        db.ForceSave();

        // Assert - Metadata block should be written to provider
        Assert.True(mockProvider.WrittenBlocks.ContainsKey("sys:metadata"));
        
        var metadataBytes = mockProvider.WrittenBlocks["sys:metadata"];
        var metadataJson = System.Text.Encoding.UTF8.GetString(metadataBytes);
        
        Assert.Contains("\"tables\"", metadataJson);
        Assert.Contains("\"users\"", metadataJson);
    }

    [Fact]
    public void Database_WithStorageProvider_LoadsMetadataFromProvider()
    {
        // Arrange - Create database with a table and save metadata
        var mockProvider = new MockStorageProvider();
        
        using (var db = new Database(_services, _testDbPath, "test123", storageProvider: mockProvider))
        {
            db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT)");
            db.ForceSave();
        }

        // Act - Create new database instance with same provider (simulates reload)
        using (var db2 = new Database(_services, _testDbPath, "test123", storageProvider: mockProvider))
        {
            // The metadata should have been loaded from provider
            // We can't easily verify table existence without ExecuteQuery, 
            // but we can verify metadata was read
            Assert.True(mockProvider.ReadBlocks.Contains("sys:metadata"));
        }
    }

    [Fact]
    public void Database_WithStorageProvider_FlushCallsProviderFlush()
    {
        // Arrange
        using var mockProvider = new MockStorageProvider();
        using var db = new Database(_services, _testDbPath, "test123", storageProvider: mockProvider);

        db.ExecuteSQL("CREATE TABLE data (id INTEGER PRIMARY KEY)");

        // Act
        db.Flush();

        // Assert - FlushAsync should have been called
        Assert.True(mockProvider.FlushCalled);
        Assert.True(mockProvider.WrittenBlocks.ContainsKey("sys:metadata"));
    }

    [Fact]
    public void Database_WithoutStorageProvider_UsesLegacyStorage()
    {
        // Arrange - Create database WITHOUT storage provider (legacy mode)
        using var db = new Database(_services, _testDbPath, "test123");

        // Act
        db.ExecuteSQL("CREATE TABLE legacy (id INTEGER PRIMARY KEY, name TEXT)");
        db.ForceSave();

        // Assert - Metadata should be saved to file (not storage provider)
        var metadataPath = Path.Combine(_testDbPath, "meta.dat");  // ← Fixed: Use meta.dat not metadata.json
        Assert.True(File.Exists(metadataPath), $"Expected metadata file at {metadataPath} but it doesn't exist");
    }

    /// <summary>
    /// Simple mock storage provider for testing Database integration.
    /// Stores blocks in memory dictionary.
    /// </summary>
    private sealed class MockStorageProvider : IStorageProvider
    {
        public Dictionary<string, byte[]> WrittenBlocks { get; } = new();
        public HashSet<string> ReadBlocks { get; } = new();
        public bool FlushCalled { get; private set; }

        public StorageMode Mode => StorageMode.SingleFile;
        public string RootPath => "mock://storage";
        public bool IsEncrypted => false;
        public int PageSize => 4096;
        public bool IsInTransaction => false;

        public bool BlockExists(string blockName)
        {
            return WrittenBlocks.ContainsKey(blockName);
        }

        public Stream? GetReadStream(string blockName) => null;

        public ReadOnlySpan<byte> GetReadSpan(string blockName)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        public Stream GetWriteStream(string blockName, bool append = false)
        {
            return new MemoryStream();
        }

        public Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            WrittenBlocks[blockName] = data.ToArray();
            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadBlockAsync(string blockName, CancellationToken cancellationToken = default)
        {
            ReadBlocks.Add(blockName);
            return Task.FromResult(WrittenBlocks.TryGetValue(blockName, out var data) ? data : null);
        }

        public Task DeleteBlockAsync(string blockName, CancellationToken cancellationToken = default)
        {
            WrittenBlocks.Remove(blockName);
            return Task.CompletedTask;
        }

        public IEnumerable<string> EnumerateBlocks() => WrittenBlocks.Keys;

        public BlockMetadata? GetBlockMetadata(string blockName) => null;

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            FlushCalled = true;
            return Task.CompletedTask;
        }

        public Task<VacuumResult> VacuumAsync(VacuumMode mode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new VacuumResult
            {
                Mode = mode,
                Success = true,
                BytesReclaimed = 0,
                DurationMs = 0
            });
        }

        public void BeginTransaction() { }
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RollbackTransaction() { }

        public StorageStatistics GetStatistics() => new StorageStatistics
        {
            TotalSize = 0,
            BlockCount = WrittenBlocks.Count,
            FragmentationPercent = 0
        };

        public void Dispose() { }
    }
}
