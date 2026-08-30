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
        options.BlockRegistrySizePages = 8; // legacy option: ignored in the dynamic layout
        options.FsmSizePages = 16;
        options.TableDirectorySizePages = 12;

        using (SingleFileStorageProvider.Open(_testDbPath, options))
        {
            // Provider writes the header immediately on create; dispose closes the file.
        }

        using var fs = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(fs);

        // Dynamic-metadata layout (issue #345): 0x20 = RegistryRootOffset, 0x28 = RegistryRootLength.
        fs.Position = 0x20;
        var registryRootOffset = reader.ReadUInt64();
        fs.Position = 0x28;
        var registryRootLength = reader.ReadUInt64();
        fs.Position = 0x58; // TableDirLength
        var tableDirLength = reader.ReadUInt64();

        // The registry is dynamic: the root chunk is always one page; growth relocates it.
        Assert.Equal((ulong)options.PageSize, registryRootLength);

        // The FSM is a named block (sys:fsm) whose entry lives in the root registry chunk.
        var fsmLength = ReadFsmEntryLength(fs, registryRootOffset);
        Assert.Equal((ulong)(options.PageSize * options.FsmSizePages), fsmLength);
        Assert.Equal((ulong)(options.PageSize * options.TableDirectorySizePages), tableDirLength);
    }

    /// <summary>
    /// Reads the length of the sys:fsm entry from the root registry chunk.
    /// Registry format v2: [RegistryChunkHeader(64)][BlockEntry(96)...] (plaintext file).
    /// BlockEntry layout: Name[32] @ 0x00, BlockType @ 0x20, Offset @ 0x24, Length @ 0x2C.
    /// </summary>
    private static ulong ReadFsmEntryLength(FileStream fs, ulong registryRootOffset)
    {
        for (var i = 0; i < 42; i++)
        {
            var entryStart = (long)registryRootOffset + 64 + (i * 96);
            fs.Position = entryStart;
            Span<byte> nameBuf = stackalloc byte[32];
            fs.ReadExactly(nameBuf);

            var nameEnd = nameBuf.IndexOf((byte)0);
            var name = System.Text.Encoding.UTF8.GetString(nameBuf[..nameEnd]);
            if (name == "sys:fsm")
            {
                fs.Position = entryStart + 0x2C; // Length field
                return ReadUInt64(fs);
            }
        }

        return 0;
    }

    private static ulong ReadUInt64(FileStream fs)
    {
        Span<byte> buf = stackalloc byte[8];
        fs.ReadExactly(buf);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buf);
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
    public async Task LargeData_GrowsFsmBlock_AndRoundtrips()
    {
        // Issue #345 Phase 2: with a 1-page FSM (4 KB bitmap ≈ 32 K pages) the FSM block must
        // relocate (grow) once the file exceeds ~16 MB. Data must round-trip across the growth.
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 512;   // small pages → small bitmap capacity → growth kicks in early
        options.FsmSizePages = 1;
        options.WalBufferSizePages = 64;
        options.EnableMemoryMapping = false;

        const int chunkSize = 2 * 1024 * 1024;   // 2 MB per block
        var payload = new byte[chunkSize];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        const int chunkCount = 10;               // 20 MB total → exceeds the 1-page FSM capacity
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            for (var i = 0; i < chunkCount; i++)
            {
                await provider.WriteBlockAsync($"chunk_{i}", payload);
            }

            await provider.ForceFlushAsync();
        }

        // The FSM must have grown: its serialized bitmap no longer fits in a single page.
        using (var fs = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read))
        {
            fs.Position = 0x20;
            var registryRootOffset = ReadUInt64(fs);
            var fsmLength = ReadFsmEntryLength(fs, registryRootOffset);
            Assert.True(fsmLength > (ulong)options.PageSize, $"FSM block should have grown past 1 page, got {fsmLength}");
        }

        using var reopened = SingleFileStorageProvider.Open(_testDbPath, options);
        for (var i = 0; i < chunkCount; i++)
        {
            var data = await reopened.ReadBlockAsync($"chunk_{i}");
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
