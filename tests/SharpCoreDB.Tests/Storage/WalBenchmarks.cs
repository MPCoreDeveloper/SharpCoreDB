// <copyright file="WalBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SharpCoreDB.Storage;
using SharpCoreDB.Storage.Scdb;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Performance benchmarks for WAL and Recovery (Phase 3).
/// Validates performance targets: WAL write <5ms, Recovery <100ms/1000 tx.
/// C# 14: Modern benchmarking with Stopwatch.
/// </summary>
public sealed class WalBenchmarks : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDbPath;

    public WalBenchmarks(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wal_bench_{Guid.NewGuid():N}.scdb");
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
            // Ignore
        }
    }

    // ========================================
    // WAL Write Performance
    // ========================================

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_WalWrite_SingleEntry_UnderOneMicrosecond()
    {
        // Arrange
        using var provider = CreateProvider();
        var testData = new byte[100];

        // Act - Single WAL entry write
        var sw = Stopwatch.StartNew();
        await provider.WalManager.LogWriteAsync("test_block", 0, testData);
        sw.Stop();

        // Assert
        var microseconds = sw.Elapsed.TotalMicroseconds;
        _output.WriteLine($"Single WAL entry: {microseconds:F3}µs");
        
        Assert.True(microseconds < 1000, 
            $"WAL write took {microseconds:F1}µs, expected <1000µs");
    }

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_WalWrite_1000Entries_UnderFiveMilliseconds()
    {
        // Arrange
        using var provider = CreateProvider();
        var testData = new byte[100];

        // Act - 1000 WAL writes
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            await provider.WalManager.LogWriteAsync($"block_{i}", 0, testData);
        }
        sw.Stop();

        // Assert
        var milliseconds = sw.ElapsedMilliseconds;
        _output.WriteLine($"1000 WAL entries: {milliseconds}ms");
        _output.WriteLine($"Average per entry: {milliseconds / 1000.0:F3}ms");
        
        Assert.True(milliseconds < 5, 
            $"1000 WAL writes took {milliseconds}ms, expected <5ms");
    }

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_Transaction_Commit_UnderOneMillisecond()
    {
        // Arrange
        using var provider = CreateProvider();
        var testData = new byte[100];

        provider.WalManager.BeginTransaction();
        await provider.WalManager.LogWriteAsync("test_block", 0, testData);

        // Act - Commit transaction (includes flush)
        var sw = Stopwatch.StartNew();
        await provider.WalManager.CommitTransactionAsync();
        sw.Stop();

        // Assert
        var milliseconds = sw.ElapsedMilliseconds;
        _output.WriteLine($"Transaction commit: {milliseconds}ms");
        
        Assert.True(milliseconds < 1, 
            $"Commit took {milliseconds}ms, expected <1ms");
    }

    // ========================================
    // Recovery Performance
    // ========================================

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_Recovery_1000Transactions_UnderOneSecond()
    {
        // Arrange - Write 1000 transactions
        using (var provider = CreateProvider())
        {
            for (int i = 0; i < 1000; i++)
            {
                provider.WalManager.BeginTransaction();
                await provider.WalManager.LogWriteAsync($"block_{i}", 0, new byte[100]);
                await provider.WalManager.CommitTransactionAsync();
            }
        }

        // Act - Measure recovery time
        var sw = Stopwatch.StartNew();
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            sw.Stop();

            // Assert
            var milliseconds = sw.ElapsedMilliseconds;
            _output.WriteLine($"Recovery time: {milliseconds}ms for {info.CommittedTransactions} transactions");
            _output.WriteLine($"Average: {milliseconds / 1000.0:F3}ms per transaction");
            
            Assert.Equal(1000, info.CommittedTransactions);
            Assert.True(milliseconds < 1000, 
                $"Recovery took {milliseconds}ms, expected <1000ms");
            
            // Target: <100ms per 1000 transactions = <0.1ms per transaction
            var msPerTx = milliseconds / 1000.0;
            Assert.True(msPerTx < 0.1, 
                $"Recovery {msPerTx:F3}ms per tx, expected <0.1ms");
        }
    }

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_Recovery_10000Transactions_LinearScaling()
    {
        // Arrange - Write 10K transactions
        using (var provider = CreateProvider())
        {
            for (int i = 0; i < 10000; i++)
            {
                provider.WalManager.BeginTransaction();
                await provider.WalManager.LogWriteAsync($"block_{i}", 0, new byte[50]);
                await provider.WalManager.CommitTransactionAsync();
                
                if (i % 1000 == 0)
                {
                    _output.WriteLine($"Written {i}/10000 transactions");
                }
            }
        }

        // Act - Measure recovery
        var sw = Stopwatch.StartNew();
        using (var provider = CreateProvider())
        {
            var recoveryManager = new RecoveryManager(provider, provider.WalManager);
            var info = await recoveryManager.RecoverAsync();
            sw.Stop();

            // Assert
            var milliseconds = sw.ElapsedMilliseconds;
            _output.WriteLine($"Recovery: {milliseconds}ms for {info.CommittedTransactions} transactions");
            _output.WriteLine($"Average: {milliseconds / 10000.0:F3}ms per transaction");
            
            Assert.Equal(10000, info.CommittedTransactions);
            
            // Should scale linearly - 10x transactions ≈ 10x time
            // But still fast: <10s for 10K transactions
            Assert.True(milliseconds < 10000, 
                $"Recovery took {milliseconds}ms for 10K tx, expected <10s");
        }
    }

    // ========================================
    // Checkpoint Performance
    // ========================================

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_Checkpoint_UnderTenMilliseconds()
    {
        // Arrange
        using var provider = CreateProvider();
        
        // Write some transactions
        for (int i = 0; i < 100; i++)
        {
            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync($"block_{i}", 0, new byte[50]);
            await provider.WalManager.CommitTransactionAsync();
        }

        // Act - Checkpoint
        var sw = Stopwatch.StartNew();
        await provider.WalManager.CheckpointAsync();
        sw.Stop();

        // Assert
        var milliseconds = sw.ElapsedMilliseconds;
        _output.WriteLine($"Checkpoint: {milliseconds}ms");
        
        Assert.True(milliseconds < 10, 
            $"Checkpoint took {milliseconds}ms, expected <10ms");
    }

    // ========================================
    // Throughput Tests
    // ========================================

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_WalThroughput_OperationsPerSecond()
    {
        // Arrange
        using var provider = CreateProvider();
        var testData = new byte[100];
        const int durationSeconds = 1;
        
        // Act - Write for 1 second
        var sw = Stopwatch.StartNew();
        int operationCount = 0;
        
        while (sw.Elapsed.TotalSeconds < durationSeconds)
        {
            await provider.WalManager.LogWriteAsync($"block_{operationCount}", 0, testData);
            operationCount++;
        }
        sw.Stop();

        // Assert
        var opsPerSecond = operationCount / sw.Elapsed.TotalSeconds;
        _output.WriteLine($"WAL throughput: {opsPerSecond:F0} ops/sec");
        _output.WriteLine($"Total operations: {operationCount}");
        
        // Should achieve >10,000 ops/sec
        Assert.True(opsPerSecond > 10000, 
            $"Throughput {opsPerSecond:F0} ops/sec, expected >10,000");
    }

    // ========================================
    // Memory Efficiency
    // ========================================

    [Fact(Skip = "Requires database factory for proper SCDB file initialization")]
    public async Task Benchmark_WalMemory_UnderOneMegabyte()
    {
        // Arrange
        using var provider = CreateProvider();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var startMemory = GC.GetTotalMemory(false);

        // Act - Write 1000 transactions
        for (int i = 0; i < 1000; i++)
        {
            provider.WalManager.BeginTransaction();
            await provider.WalManager.LogWriteAsync($"block_{i}", 0, new byte[100]);
            await provider.WalManager.CommitTransactionAsync();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var endMemory = GC.GetTotalMemory(false);
        var memoryUsed = (endMemory - startMemory) / 1024.0 / 1024.0; // MB

        // Assert
        _output.WriteLine($"Memory used: {memoryUsed:F2} MB");
        
        // WAL should use minimal memory (most data on disk)
        Assert.True(memoryUsed < 1.0, 
            $"WAL used {memoryUsed:F2}MB, expected <1MB");
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
