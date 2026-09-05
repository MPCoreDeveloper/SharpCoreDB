// tests/SharpCoreDB.Tests/Storage/SingleFileWriteBehindDurabilityTests.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Tests.Storage;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// REGRESSION TESTS: Issue #400 - the single-file (.scdb) write-behind worker and the block
/// grow path (free old pages + allocate a new offset on every flush past the page threshold).
///
/// The physical block write is queued by WriteBlockAsync and drained by a background worker
/// (ProcessWriteQueueAsync) or by FlushPendingWritesAsync during Flush/ForceSave. If a flush
/// persisted the registry/FSM and fsynced while the worker still held dequeued-but-unwritten
/// operations, a reopen could observe the previous block version (the "500 vs 499 rows" flake on
/// macOS/Linux/container runners). These tests force the grow + flush + reopen cycle repeatedly
/// and byte-verify that the latest payload survives.
/// </summary>
public sealed class SingleFileWriteBehindDurabilityTests : IDisposable
{
    private readonly List<string> _filesToCleanup = [];

    public void Dispose()
    {
        foreach (var path in _filesToCleanup)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore cleanup errors */ }
        }
    }

    // ========================================
    // Provider-level: growing block + flush + reopen must be byte-exact
    // ========================================

    [Fact]
    public async Task WriteGrowingBlock_ForceFlushThenReopen_ReadsExactLatestPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scdb_wb_grow_{Guid.NewGuid():N}.scdb");
        _filesToCleanup.Add(path);

        byte[] lastPayload;
        using (var provider = SingleFileStorageProvider.Open(path, CreateOptions(BlockCompressionMode.None)))
        {
            // Repeatedly rewrite the same block while it grows well past several page boundaries.
            // Every size increase forces the free-old / allocate-new grow path inside
            // WriteBlockAsync and queues the write behind the worker.
            lastPayload = [];
            for (int i = 1; i <= 60; i++)
            {
                lastPayload = Encoding.UTF8.GetBytes($"payload-{i}:" + new string('x', i * 700));
                await provider.WriteBlockAsync("growing", lastPayload);
            }

            await provider.FlushPendingWritesAsync();
            await provider.ForceFlushAsync();
        }

        // Reopen and byte-compare: the reopen must observe the LAST payload, never an earlier
        // block version that a write-behind ordering race could leave on disk (Issue #400).
        using (var reopened = SingleFileStorageProvider.Open(path, CreateOptions(BlockCompressionMode.None)))
        {
            var data = await reopened.ReadBlockAsync("growing");
            Assert.NotNull(data);
            Assert.Equal(lastPayload, data);
        }
    }

    [Fact]
    public async Task WriteGrowingBlock_ConcurrentFlushThenReopen_ReadsExactLatestPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scdb_wb_race_{Guid.NewGuid():N}.scdb");
        _filesToCleanup.Add(path);

        using var provider = SingleFileStorageProvider.Open(path, CreateOptions(BlockCompressionMode.None));

        // A single writer keeps growing one block while a flusher repeatedly calls FlushAsync
        // (each of which drains the queue and fsyncs). This maximizes the chance of hitting the
        // worker-vs-flush ordering window that lost the newest block version on slow runners.
        byte[] lastPayload = [];
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(async () =>
        {
            for (int i = 1; i <= 100; i++)
            {
                lastPayload = Encoding.UTF8.GetBytes($"payload-{i}:" + new string('y', i * 400));
                await provider.WriteBlockAsync("growing", lastPayload);
                if ((i & 3) == 0)
                {
                    await Task.Yield(); // let the flusher interleave mid-burst
                }
            }
        });

        var flusher = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await provider.FlushAsync(CancellationToken.None);
                    await Task.Delay(1);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            catch
            {
                // Best effort: a failed flush must not mask the final ForceFlushAsync below.
            }
        });

        await writer;
        stop.Cancel();
        try { await flusher; } catch { /* best effort */ }

        // Final durable flush before reopen.
        await provider.ForceFlushAsync();
        provider.Dispose();

        using (var reopened = SingleFileStorageProvider.Open(path, CreateOptions(BlockCompressionMode.None)))
        {
            var data = await reopened.ReadBlockAsync("growing");
            Assert.NotNull(data);
            Assert.Equal(lastPayload, data);
        }
    }

    // ========================================
    // Database-level: growing compressed table survives repeated reopen rounds
    // ========================================

    [Fact]
    public void DatabaseFactory_Compression_GrowingTable_ReopenRepeatedRounds_AllRowsSurvive()
    {
        // Issue #400 full-stack scenario: single-file (.scdb) + Brotli compression, auto-flush that
        // repeatedly rewrites the row-cache block past the compression threshold, then Flush() +
        // ForceSave() + reopen + SELECT * ORDER BY id. Repeat the round to keep CI pressure on the
        // write-behind drain / registry / FSM ordering without slowing the suite.
        var factory = BuildFactory();
        var options = CreateOptions(BlockCompressionMode.Brotli);

        const int rounds = 3;
        const int rowsPerRound = 250;

        for (int round = 0; round < rounds; round++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"scdb_wb_db_{Guid.NewGuid():N}.scdb");
            _filesToCleanup.Add(path);

            var db = factory.CreateWithOptions(path, "unused", options);
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, payload TEXT)");
            for (int i = 0; i < rowsPerRound; i++)
            {
                db.ExecuteSQL($"INSERT INTO t VALUES ({i}, 'user{i}', '{new string('x', 32)}')");
            }
            db.Flush();
            db.ForceSave();
            DisposeDatabase(db);

            var db2 = factory.CreateWithOptions(path, "unused", options);
            var results = db2.ExecuteQuery("SELECT * FROM t ORDER BY id");
            var actualIds = results.Select(r => Convert.ToInt64(r["id"])).OrderBy(id => id).ToList();
            var expectedIds = Enumerable.Range(0, rowsPerRound).Select(i => (long)i).ToList();
            var missing = expectedIds.Except(actualIds).ToList();
            var extra = actualIds.Except(expectedIds).ToList();
            Assert.True(
                missing.Count == 0 && extra.Count == 0,
                $"Round {round}: row set mismatch after reopen: missing={missing.Count} [{string.Join(",", missing.Take(10))}], extra={extra.Count} [{string.Join(",", extra.Take(10))}].");
            DisposeDatabase(db2);
        }
    }

    // ========================================
    // Helpers
    // ========================================

    private static DatabaseFactory BuildFactory()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<DatabaseFactory>();
    }

    private static void DisposeDatabase(IDatabase database)
    {
        (database as IDisposable)?.Dispose();
    }

    private static DatabaseOptions CreateOptions(BlockCompressionMode mode)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.PageSize = 4096;
        options.WalBufferSizePages = 256;
        options.EnableMemoryMapping = false;
        options.BlockCompression = mode;
        options.CompressionThreshold = 64; // Low threshold for tests
        return options;
    }
}

