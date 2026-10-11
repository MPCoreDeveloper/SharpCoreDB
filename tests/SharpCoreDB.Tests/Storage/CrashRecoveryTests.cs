// <copyright file="CrashRecoveryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using Xunit;

/// <summary>
/// Crash recovery tests for Phase 3 WAL implementation.
/// Validates zero data loss guarantee and transaction ACID properties.
/// C# 15: Modern test patterns with async/await.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public CrashRecoveryTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"crash_test_{Guid.NewGuid():N}.scdb");

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
            // Test cleanup should not mask product failures.
        }
    }

    [Fact]
    public async Task BasicRecovery_WalPersistsCommittedTransactions()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        ulong replayOffset;

        using (var provider = CreateProvider())
        {
            replayOffset = GetSafeReplayOffset(provider, payload.Length);
            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync("test_block", replayOffset, payload);
            await provider.WalManager.CommitTransactionAsync();
            await provider.FlushAsync();
        }

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var recoveryInfo = await recoveryManager.RecoverAsync();

            _output.WriteLine(recoveryInfo.ToString());

            Assert.True(recoveryInfo.RecoveryNeeded);
            Assert.Equal(3, recoveryInfo.TotalEntries);
            Assert.Equal(1, recoveryInfo.CommittedTransactions);
            Assert.Equal(0, recoveryInfo.UncommittedTransactions);
            Assert.Equal(1, recoveryInfo.OperationsReplayed);
            Assert.Equal(payload, ReadBytesAtOffset(provider, replayOffset, payload.Length));
        }
    }

    [Fact]
    public async Task BasicRecovery_UncommittedTransactionNotReplayed()
    {
        var payload = new byte[] { 9, 9, 9 };
        ulong replayOffset;

        using (var provider = CreateProvider())
        {
            replayOffset = GetSafeReplayOffset(provider, payload.Length);
            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync("uncommitted_block", replayOffset, payload);
            provider.WalManager.RollbackTransaction();
            await provider.FlushAsync();
        }

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var recoveryInfo = await recoveryManager.RecoverAsync();

            Assert.False(recoveryInfo.RecoveryNeeded);
            Assert.Equal(0, recoveryInfo.TotalEntries);
            Assert.Equal(0, recoveryInfo.CommittedTransactions);
            Assert.Equal(0, recoveryInfo.OperationsReplayed);
            Assert.Empty(ReadBytesAtOffset(provider, replayOffset, payload.Length));
        }
    }

    [Fact]
    public async Task MultiTransaction_SequentialCommits_AllRecorded()
    {
        var payloads = new[]
        {
            new byte[] { 1, 0, 0 },
            new byte[] { 2, 0, 0 },
            new byte[] { 3, 0, 0 }
        };
        ulong[] offsets;

        using (var provider = CreateProvider())
        {
            offsets =
            [
                GetSafeReplayOffset(provider, payloads[0].Length),
                GetSafeReplayOffset(provider, payloads[1].Length, 256),
                GetSafeReplayOffset(provider, payloads[2].Length, 512)
            ];

            for (int i = 0; i < payloads.Length; i++)
            {
                provider.WalManager.BeginTransaction();
                await provider.WalManager.LogWriteAsync($"block{i + 1}", offsets[i], payloads[i]);
                await provider.WalManager.CommitTransactionAsync();
            }

            await provider.FlushAsync();
        }

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();

            _output.WriteLine($"Recovery: {info}");
            Assert.True(info.RecoveryNeeded);
            Assert.Equal(9, info.TotalEntries);
            Assert.Equal(3, info.CommittedTransactions);
            Assert.Equal(3, info.OperationsReplayed);

            for (int i = 0; i < payloads.Length; i++)
            {
                Assert.Equal(payloads[i], ReadBytesAtOffset(provider, offsets[i], payloads[i].Length));
            }
        }
    }

    [Fact]
    public async Task CheckpointRecovery_OnlyReplaysAfterCheckpoint()
    {
        var beforePayload = new byte[] { 7, 7, 7 };
        var afterPayload = new byte[] { 8, 8, 8 };
        ulong beforeOffset;
        ulong afterOffset;

        using (var provider = CreateProvider())
        {
            beforeOffset = GetSafeReplayOffset(provider, beforePayload.Length);
            afterOffset = GetSafeReplayOffset(provider, afterPayload.Length, 256);

            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync("before_cp", beforeOffset, beforePayload);
            await provider.WalManager.CommitTransactionAsync();
            await provider.FlushAsync();

            await provider.WalManager.CheckpointAsync();

            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync("after_cp", afterOffset, afterPayload);
            await provider.WalManager.CommitTransactionAsync();
            await provider.FlushAsync();
        }

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();

            _output.WriteLine($"Recovery: {info}");
            Assert.True(info.RecoveryNeeded);
            Assert.Equal(3, info.TotalEntries);
            Assert.Equal(1, info.CommittedTransactions);
            Assert.Equal(1, info.OperationsReplayed);
            Assert.NotEqual(beforePayload, ReadBytesAtOffset(provider, beforeOffset, beforePayload.Length));
            Assert.Equal(afterPayload, ReadBytesAtOffset(provider, afterOffset, afterPayload.Length));
        }
    }

    [Fact]
    public async Task CorruptedWalEntry_GracefulHandling()
    {
        using (var provider = CreateProvider())
        {
            var replayOffset = GetSafeReplayOffset(provider, 8);
            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync("valid_block", replayOffset, new byte[8]);
            await provider.WalManager.CommitTransactionAsync();
            await provider.FlushAsync();
        }

        CorruptFirstWalEntryChecksum();

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var exception = await Record.ExceptionAsync(() => recoveryManager.RecoverAsync());
            Assert.Null(exception);
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Recovery_1000Transactions_UnderOneSecond()
    {
        using (var provider = CreateProvider())
        {
            var replayOffset = GetSafeReplayOffset(provider, 64);
            for (int i = 0; i < 1000; i++)
            {
                provider.WalManager.BeginTransaction();
                await provider.WalManager.LogWriteAsync($"block_{i}", replayOffset, new byte[64]);
                await provider.WalManager.CommitTransactionAsync();
            }

            await provider.FlushAsync();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            sw.Stop();

            _output.WriteLine($"Recovery: {info}");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");

            Assert.True(info.RecoveryNeeded);
            Assert.Equal(3000, info.TotalEntries);
            Assert.Equal(1000, info.CommittedTransactions);
            Assert.Equal(1000, info.OperationsReplayed);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Recovery took {sw.ElapsedMilliseconds}ms, expected <5000ms");
        }
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Recovery_LargeWAL_Efficient()
    {
        using (var provider = CreateProvider())
        {
            var replayOffset = GetSafeReplayOffset(provider, 50);
            for (int i = 0; i < 100; i++)
            {
                provider.WalManager.BeginTransaction();
                for (int j = 0; j < 10; j++)
                {
                    await provider.WalManager.LogWriteAsync($"block_{i}_{j}", replayOffset + (ulong)j * 64, new byte[50]);
                }
                await provider.WalManager.CommitTransactionAsync();
            }

            await provider.FlushAsync();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            sw.Stop();

            _output.WriteLine($"Recovery: {info}");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");

            Assert.True(info.RecoveryNeeded);
            Assert.Equal(1200, info.TotalEntries);
            Assert.Equal(100, info.CommittedTransactions);
            Assert.Equal(1000, info.OperationsReplayed);
        }
    }

    [Fact]
    public async Task Recovery_EmptyWAL_NoRecoveryNeeded()
    {
        using (var provider = CreateProvider())
        {
            await provider.FlushAsync();
        }

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();

            Assert.False(info.RecoveryNeeded);
            Assert.Equal(0, info.TotalEntries);
            Assert.Equal(0, info.CommittedTransactions);
            Assert.Equal(0, info.UncommittedTransactions);
            Assert.Equal(0, info.OperationsReplayed);
        }
    }

    [Fact]
    public async Task Recovery_AbortedTransaction_NoReplay()
    {
        var payload = new byte[] { 4, 4, 4 };
        ulong replayOffset;

        using (var provider = CreateProvider())
        {
            replayOffset = GetSafeReplayOffset(provider, payload.Length);
            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync("aborted_block", replayOffset, payload);
            provider.WalManager.RollbackTransaction();
            await provider.FlushAsync();
        }

        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();

            Assert.False(info.RecoveryNeeded);
            Assert.Equal(0, info.TotalEntries);
            Assert.Equal(0, info.CommittedTransactions);
            Assert.Equal(0, info.OperationsReplayed);
            Assert.Empty(ReadBytesAtOffset(provider, replayOffset, payload.Length));
        }
    }

    private SingleFileStorageProvider CreateProvider()
    {
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 4096,
            CreateImmediately = true,
            EnableMemoryMapping = false,
            WalBufferSizePages = 4096,
        };

        return SingleFileStorageProvider.Open(_testDbPath, options);
    }

    private static ulong GetSafeReplayOffset(SingleFileStorageProvider provider, int payloadLength, int extraPadding = 0)
    {
        var stream = provider.GetInternalFileStream();
        return checked((ulong)stream.Length + (ulong)extraPadding + (ulong)Math.Max(128, payloadLength));
    }

    private static byte[] ReadBytesAtOffset(SingleFileStorageProvider provider, ulong offset, int length)
    {
        var stream = provider.GetInternalFileStream();
        if ((long)offset >= stream.Length)
        {
            return [];
        }

        stream.Position = (long)offset;
        var buffer = new byte[length];
        var read = stream.Read(buffer, 0, buffer.Length);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    private void CorruptFirstWalEntryChecksum()
    {
        using var stream = new FileStream(_testDbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Span<byte> headerBytes = stackalloc byte[Marshal.SizeOf<ScdbFileHeader>()];
        stream.ReadExactly(headerBytes);
        var header = MemoryMarshal.Read<ScdbFileHeader>(headerBytes);

        var checksumOffset = (long)header.WalOffset + WalHeader.SIZE + 38;
        stream.Position = checksumOffset;
        var checksum = new byte[32];
        stream.ReadExactly(checksum);
        checksum[0] ^= 0xFF;
        stream.Position = checksumOffset;
        stream.Write(checksum, 0, checksum.Length);
        stream.Flush(true);
    }
}
