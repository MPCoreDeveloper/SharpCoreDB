// tests/SharpCoreDB.Tests/Storage/MetadataSizingTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Threading.Tasks;
using SharpCoreDB;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// REGRESSION TESTS: SingleFile metadata region sizing (issue #345).
/// Verifies the FSM / Block Registry / Table Directory regions are no longer
/// hard-coded to 4 pages and that the minimum file extension is byte-based
/// (stays ~10 MB regardless of PageSize).
/// </summary>
public sealed class MetadataSizingTests : IDisposable
{
    private readonly string _testDbPath;

    public MetadataSizingTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"metadata_sizing_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            if (File.Exists(_testDbPath + ".wal")) File.Delete(_testDbPath + ".wal");
            if (File.Exists(_testDbPath + ".vacuum.tmp")) File.Delete(_testDbPath + ".vacuum.tmp");
            if (File.Exists(_testDbPath + ".backup")) File.Delete(_testDbPath + ".backup");
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void ConfiguredRegionSizes_AreWrittenToHeader()
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 4096;
        options.WalBufferSizePages = 64;
        options.EnableMemoryMapping = false;
        options.BlockRegistrySizePages = 8;
        options.FsmSizePages = 16;
        options.TableDirectorySizePages = 12;

        using (SingleFileStorageProvider.Open(_testDbPath, options))
        {
            // Provider writes the header immediately on create; dispose closes the file.
        }

        using var fs = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        fs.Position = 0x28; // BlockRegistryLength
        var registryLength = reader.ReadUInt64();
        fs.Position = 0x38; // FsmLength
        var fsmLength = reader.ReadUInt64();
        fs.Position = 0x58; // TableDirLength
        var tableDirLength = reader.ReadUInt64();

        Assert.Equal((ulong)(4096 * 8), registryLength);
        Assert.Equal((ulong)(4096 * 16), fsmLength);
        Assert.Equal((ulong)(4096 * 12), tableDirLength);
    }

    [Fact]
    public async Task ManyBlocks_WithLargerRegistry_ShouldRoundtrip()
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 4096;
        options.WalBufferSizePages = 64;
        options.EnableMemoryMapping = false;
        options.BlockRegistrySizePages = 16; // 64 KB registry ≈ 680 entries

        const int blockCount = 300; // exceeds the default 4-page (~170) capacity
        var payload = new byte[64];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            for (var i = 0; i < blockCount; i++)
            {
                await provider.WriteBlockAsync($"blk_{i}", payload);
            }

            // ForceFlushAsync = registry force flush + WAL checkpoint (full durability),
            // avoiding a race between the periodic registry flusher and the reopen below.
            await provider.ForceFlushAsync();
        }

        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        for (var i = 0; i < blockCount; i++)
        {
            var data = await reopened.ReadBlockAsync($"blk_{i}");
            Assert.NotNull(data);
            Assert.Equal(payload, data);
        }
    }

    [Fact]
    public async Task MinExtension_IsByteBased_NotPageBased()
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 16384; // 16 KB pages
        options.WalBufferSizePages = 64; // keep the WAL small for the assertion
        options.EnableMemoryMapping = false;

        long fileSize;
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            await provider.WriteBlockAsync("blob", new byte[8192]);
            await provider.FlushAsync();
            fileSize = new FileInfo(_testDbPath).Length;
        }

        // Old behavior: MIN_EXTENSION_PAGES=2560 * 16 KB = 40 MB -> file ~41 MB.
        // Byte-based behavior: ~10 MB extension -> file ~11-12 MB.
        Assert.True(
            fileSize < 20L * 1024 * 1024,
            $"File grew to {fileSize} bytes; expected a ~10 MB byte-based extension, not a page-count-based one.");
    }

    [Fact]
    public void InvalidRegionSizes_ShouldThrowOnValidate()
    {
        var fsmZero = DatabaseOptions.CreateSingleFileDefault();
        fsmZero.FsmSizePages = 0;
        Assert.Throws<ArgumentException>(() => fsmZero.Validate());

        var registryNegative = DatabaseOptions.CreateSingleFileDefault();
        registryNegative.BlockRegistrySizePages = -1;
        Assert.Throws<ArgumentException>(() => registryNegative.Validate());

        var tableDirTooBig = DatabaseOptions.CreateSingleFileDefault();
        tableDirTooBig.TableDirectorySizePages = 70000;
        Assert.Throws<ArgumentException>(() => tableDirTooBig.Validate());
    }
}
