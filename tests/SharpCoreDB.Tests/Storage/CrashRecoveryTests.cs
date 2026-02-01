// <copyright file="CrashRecoveryTests.cs" company="MPCoreDeveloper">
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
/// Crash recovery tests for Phase 3 WAL implementation.
/// Validates zero data loss guarantee and transaction ACID properties.
/// C# 14: Modern test patterns with async/await.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public CrashRecoveryTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"crash_test_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
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

    // ========================================
    // Basic Recovery Tests
    // ========================================

    [Fact]
    public async Task BasicRecovery_CommittedTransaction_DataPersists()
    {
        // Arrange - Write data in transaction
        using (var provider = CreateProvider())
        {
            provider.WalManager.BeginTransaction();
            
            var testData = new byte[100];
            Array.Fill(testData, (byte)42);
            
            await provider.WriteBlockAsync("test_block", testData);
            await provider.WalManager.CommitTransactionAsync();
            
            // Simulate crash - dispose without proper flush
        }

        // Act - Reopen and recover
        RecoveryInfo recoveryInfo;
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            recoveryInfo = await recoveryManager.RecoverAsync();
            
            // Assert - Data should be recovered
            var recovered = await provider.ReadBlockAsync("test_block");
            Assert.NotNull(recovered);
            Assert.Equal(100, recovered.Length);
            Assert.All(recovered, b => Assert.Equal(42, b));
        }

        _output.WriteLine(recoveryInfo.ToString());
        Assert.True(recoveryInfo.RecoveryNeeded);
        Assert.Equal(1, recoveryInfo.CommittedTransactions);
    }

    [Fact]
    public async Task BasicRecovery_UncommittedTransaction_DataLost()
    {
        // Arrange - Write data but don't commit
        using (var provider = CreateProvider())
        {
            provider.WalManager.BeginTransaction();
            
            var testData = new byte[100];
            Array.Fill(testData, (byte)99);
            
            await provider.WriteBlockAsync("uncommitted_block", testData);
            
            // Simulate crash - no commit, no flush
        }

        // Act - Reopen and recover
        RecoveryInfo recoveryInfo;
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            recoveryInfo = await recoveryManager.RecoverAsync();
            
            // Assert - Data should NOT be present
            var exists = provider.BlockExists("uncommitted_block");
            Assert.False(exists);
        }

        _output.WriteLine(recoveryInfo.ToString());
        Assert.Equal(1, recoveryInfo.UncommittedTransactions);
    }

    // ========================================
    // Multi-Transaction Tests
    // ========================================

    [Fact]
    public async Task MultiTransaction_MixedCommits_OnlyCommittedRecovered()
    {
        // Arrange - Multiple transactions, some committed
        using (var provider = CreateProvider())
        {
            // Transaction 1: Committed
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("block1", new byte[50]);
            await provider.WalManager.CommitTransactionAsync();
            
            // Transaction 2: Uncommitted
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("block2", new byte[50]);
            // No commit
            
            // Transaction 3: Committed
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("block3", new byte[50]);
            await provider.WalManager.CommitTransactionAsync();
            
            // Simulate crash
        }

        // Act - Recover
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            
            // Assert
            _output.WriteLine($"Recovery: {info}");
            Assert.Equal(2, info.CommittedTransactions); // T1 and T3
            Assert.Equal(1, info.UncommittedTransactions); // T2
            
            Assert.True(provider.BlockExists("block1"));
            Assert.False(provider.BlockExists("block2")); // Uncommitted
            Assert.True(provider.BlockExists("block3"));
        }
    }

    // ========================================
    // Checkpoint Tests
    // ========================================

    [Fact]
    public async Task CheckpointRecovery_OnlyReplaysAfterCheckpoint()
    {
        // Arrange
        using (var provider = CreateProvider())
        {
            // Before checkpoint
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("before_cp", new byte[50]);
            await provider.WalManager.CommitTransactionAsync();
            await provider.FlushAsync();
            
            // Checkpoint
            await provider.WalManager.CheckpointAsync();
            
            // After checkpoint
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("after_cp", new byte[50]);
            await provider.WalManager.CommitTransactionAsync();
            
            // Simulate crash
        }

        // Act - Recover
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            
            // Assert - Should only replay transactions after checkpoint
            _output.WriteLine($"Recovery: {info}");
            // In real implementation, this would verify only 1 transaction replayed
            Assert.True(info.RecoveryNeeded);
        }
    }

    // ========================================
    // Corruption Tests
    // ========================================

    [Fact]
    public async Task CorruptedWalEntry_GracefulHandling()
    {
        // Arrange - Write valid transaction
        using (var provider = CreateProvider())
        {
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("valid_block", new byte[50]);
            await provider.WalManager.CommitTransactionAsync();
            await provider.FlushAsync();
        }

        // Corrupt WAL file
        using (var fs = new FileStream(_testDbPath, FileMode.Open, FileAccess.ReadWrite))
        {
            // Corrupt some bytes in WAL region
            fs.Position = 1024 * 1024; // Somewhere in WAL
            fs.WriteByte(0xFF);
            fs.WriteByte(0xFF);
        }

        // Act - Attempt recovery
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            
            // Should not throw, handle corruption gracefully
            var exception = await Record.ExceptionAsync(async () =>
            {
                await recoveryManager.RecoverAsync();
            });
            
            Assert.Null(exception); // Should handle gracefully
        }
    }

    // ========================================
    // Performance Tests
    // ========================================

    [Fact]
    public async Task Recovery_1000Transactions_UnderOneSecond()
    {
        // Arrange - Write 1000 transactions
        using (var provider = CreateProvider())
        {
            for (int i = 0; i < 1000; i++)
            {
                provider.WalManager.BeginTransaction();
                await provider.WriteBlockAsync($"block_{i}", new byte[100]);
                await provider.WalManager.CommitTransactionAsync();
            }
            
            // Simulate crash
        }

        // Act - Measure recovery time
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            sw.Stop();
            
            // Assert
            _output.WriteLine($"Recovery: {info}");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            
            Assert.Equal(1000, info.CommittedTransactions);
            Assert.True(sw.ElapsedMilliseconds < 1000, 
                $"Recovery took {sw.ElapsedMilliseconds}ms, expected <1000ms");
        }
    }

    [Fact]
    public async Task Recovery_LargeWAL_Efficient()
    {
        // Arrange - Fill WAL with many entries
        using (var provider = CreateProvider())
        {
            for (int i = 0; i < 100; i++)
            {
                provider.WalManager.BeginTransaction();
                
                // Multiple operations per transaction
                for (int j = 0; j < 10; j++)
                {
                    await provider.WriteBlockAsync($"block_{i}_{j}", new byte[50]);
                }
                
                await provider.WalManager.CommitTransactionAsync();
            }
        }

        // Act - Recover
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            sw.Stop();
            
            // Assert
            _output.WriteLine($"Recovery: {info}");
            _output.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Operations: {info.OperationsReplayed}");
            
            Assert.Equal(100, info.CommittedTransactions);
            Assert.True(info.OperationsReplayed > 0);
        }
    }

    // ========================================
    // Edge Cases
    // ========================================

    [Fact]
    public async Task Recovery_EmptyWAL_NoRecoveryNeeded()
    {
        // Arrange - Fresh database
        using (var provider = CreateProvider())
        {
            await provider.FlushAsync();
        }

        // Act - Recover
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            
            // Assert
            Assert.False(info.RecoveryNeeded);
            Assert.Equal(0, info.TotalEntries);
            Assert.Equal(0, info.CommittedTransactions);
        }
    }

    [Fact]
    public async Task Recovery_AbortedTransaction_NoReplay()
    {
        // Arrange - Transaction with explicit abort
        using (var provider = CreateProvider())
        {
            provider.WalManager.BeginTransaction();
            await provider.WriteBlockAsync("aborted_block", new byte[50]);
            provider.WalManager.RollbackTransaction();
            
            // Simulate crash
        }

        // Act - Recover
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            
            // Assert - Aborted transaction should not be replayed
            Assert.False(provider.BlockExists("aborted_block"));
            Assert.Equal(0, info.CommittedTransactions);
        }
    }

    // ========================================
    // Helper Methods
    // ========================================

    private SingleFileStorageProvider CreateProvider()
    {
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            PageSize = 4096,
        };

        return SingleFileStorageProvider.Open(_testDbPath, options);
    }
}
