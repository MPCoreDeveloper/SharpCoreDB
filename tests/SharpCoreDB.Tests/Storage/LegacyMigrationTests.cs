// tests/SharpCoreDB.Tests/Storage/LegacyMigrationTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using Xunit;

/// <summary>
/// REGRESSION TESTS: format-v1 → v2 migration (issue #345 Phase 2).
/// A legacy file with fixed-offset metadata regions (BlockRegistryOffset/FsmOffset in the
/// header) must be migrated to the dynamic-metadata layout on open, with data preserved.
/// </summary>
public sealed class LegacyMigrationTests : IDisposable
{
    private readonly string _testDbPath;

    private const int PageSize = 4096;

    public LegacyMigrationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"legacy_migration_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            if (File.Exists(_testDbPath + ".backup")) File.Delete(_testDbPath + ".backup");
            if (File.Exists(_testDbPath + ".migrate.tmp")) File.Delete(_testDbPath + ".migrate.tmp");
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task Open_LegacyV1File_MigratesToV2_AndDataSurvives()
    {
        // Arrange: craft a minimal format-v1 file.
        var payload = new byte[PageSize];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i % 251);
        }

        WriteLegacyV1File(payload);

        // Act: opening the file triggers the v1 → v2 migration.
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.CreateImmediately = false;
        options.EnableMemoryMapping = false;

        byte[]? readBack = null;
        using (var provider = SingleFileStorageProvider.Open(_testDbPath, options))
        {
            readBack = await provider.ReadBlockAsync("table:test:data", System.Threading.CancellationToken.None);
            Assert.NotNull(readBack);
        }

        // Assert: data survived.
        Assert.Equal(payload, readBack);

        // The header must now be format v2 (dynamic metadata).
        using (var fs = new FileStream(_testDbPath, FileMode.Open, FileAccess.Read))
        {
            fs.Position = 0x08; // FormatVersion
            Span<byte> buf = stackalloc byte[2];
            fs.ReadExactly(buf);
            Assert.Equal((ushort)2, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(buf));
        }

        // The original file must be preserved as a backup.
        Assert.True(File.Exists(_testDbPath + ".backup"), "Backup of the legacy file must exist");
    }
    /// <summary>
    /// Writes a minimal legacy format-v1 SCDB file:
    /// [Header][Registry(BREG, 4 pages)][FSM(4 pages)][WAL(64 pages)][TableDir(4 pages)][Data(1 page)]
    /// </summary>
    private void WriteLegacyV1File(byte[] payload)
    {
        const ulong registryOffset = 4096;
        const ulong registryLength = 4096 * 4;
        const ulong fsmOffset = registryOffset + registryLength;
        const ulong fsmLength = 4096 * 4;
        const ulong walOffset = fsmOffset + fsmLength;
        const ulong walLength = 4096 * 64;
        const ulong tableDirOffset = walOffset + walLength;
        const ulong tableDirLength = 4096 * 4;
        const ulong dataOffset = tableDirOffset + tableDirLength;
        var fileSize = dataOffset + (ulong)payload.Length;

        using var fs = new FileStream(_testDbPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        // ── Header (v1 layout, 512 bytes) ────────────────────────────────────────────────
        var header = new ScdbFileHeader
        {
            Magic = ScdbFileHeader.MAGIC,
            FormatVersion = 1,
            PageSize = PageSize,
            HeaderSize = ScdbFileHeader.HEADER_SIZE,
            EncryptionMode = 0,
            CompressionMode = 0,
            RegistryRootOffset = registryOffset,   // byte position 0x20 = v1 BlockRegistryOffset
            RegistryRootLength = registryLength,   // byte position 0x28 = v1 BlockRegistryLength
            ReservedRegion0 = fsmOffset,           // byte position 0x30 = v1 FsmOffset
            ReservedRegion1 = fsmLength,           // byte position 0x38 = v1 FsmLength
            WalOffset = walOffset,
            WalLength = walLength,
            TableDirOffset = tableDirOffset,
            TableDirLength = tableDirLength,
            FileSize = fileSize,
            AllocatedPages = fileSize / PageSize,
            FeatureFlags = ScdbFileHeader.FEATURE_ULID_SPEC
        };
        var headerBytes = new byte[ScdbFileHeader.HEADER_SIZE];
        header.WriteTo(headerBytes);
        fs.Write(headerBytes);
        fs.SetLength((long)fileSize);

        // ── Block registry (v1 BREG format) ───────────────────────────────────────────────
        fs.Position = (long)registryOffset;
        var regHeader = new BlockRegistryHeader
        {
            Magic = BlockRegistryHeader.MAGIC,
            Version = BlockRegistryHeader.CURRENT_VERSION,
            BlockCount = 1,
            TotalSize = BlockRegistryHeader.SIZE + BlockEntry.SIZE,
            LastModified = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        Span<byte> regHeaderSpan = stackalloc byte[BlockRegistryHeader.SIZE];
        System.Runtime.InteropServices.MemoryMarshal.Write(regHeaderSpan, in regHeader);
        fs.Write(regHeaderSpan);

        Span<byte> entrySpan = stackalloc byte[BlockEntry.SIZE];
        entrySpan.Clear();
        var checksum = SHA256.HashData(payload);
        var entry = new BlockEntry
        {
            BlockType = (uint)BlockType.TableData,
            Offset = dataOffset,
            Length = (ulong)payload.Length,
            Flags = 0
        };
        var namedEntry = BlockEntry.WithName("table:test:data", entry);
        System.Runtime.InteropServices.MemoryMarshal.Write(entrySpan, in namedEntry);
        var entryBytes = entrySpan.ToArray();
        for (var i = 0; i < 32; i++)
        {
            entryBytes[0x34 + i] = checksum[i];
        }

        fs.Write(entryBytes);



        // ── FSM (v1): mark all pages allocated ────────────────────────────────────────────
        fs.Position = (long)fsmOffset;
        var totalPages = (ulong)(fileSize / PageSize);
        var bitmapSizeBytes = (int)((totalPages + 7) / 8);
        var fsmRegion = new byte[fsmLength];
        var fsmHeader = new FreeSpaceMapHeader
        {
            Magic = FreeSpaceMapHeader.MAGIC,
            Version = FreeSpaceMapHeader.CURRENT_VERSION,
            TotalPages = totalPages,
            FreePages = 0,
            LargestExtent = 0,
            BitmapOffset = (uint)FreeSpaceMapHeader.SIZE,
            ExtentMapOffset = (uint)(FreeSpaceMapHeader.SIZE + bitmapSizeBytes + sizeof(int))
        };
        System.Runtime.InteropServices.MemoryMarshal.Write(fsmRegion.AsSpan(0, FreeSpaceMapHeader.SIZE), in fsmHeader);
        for (var i = 0; i < bitmapSizeBytes - 1; i++)
        {
            fsmRegion[FreeSpaceMapHeader.SIZE + i] = 0xFF;
        }

        fsmRegion[FreeSpaceMapHeader.SIZE + bitmapSizeBytes - 1] = (byte)(0xFF >> ((bitmapSizeBytes * 8) - (int)totalPages));
        fs.Write(fsmRegion);

        // ── WAL + TableDir: left zeroed (handled as empty) ───────────────────────────────

        // ── Data block ────────────────────────────────────────────────────────────────────
        fs.Position = (long)dataOffset;
        fs.Write(payload);

        fs.Flush(flushToDisk: true);
    }
}
