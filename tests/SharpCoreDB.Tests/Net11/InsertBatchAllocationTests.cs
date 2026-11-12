// <copyright file="InsertBatchAllocationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

#if NET11_0_OR_GREATER
namespace SharpCoreDB.Tests.Net11;

using SharpCoreDB;
using Xunit;

/// <summary>
/// Allocation canary for the batch INSERT path — the one write-path metric whose gate window (100.000 rows) is long
/// enough to measure stably, which makes it the first place a .NET 11-era optimisation can be proven rather than
/// hoped for. Measured the way <c>BlockCompressor_Zstd_Compress_AllocatesOnlyTheReturnedFrame</c> measures its own
/// path: warm the call shape, then read <see cref="GC.GetAllocatedBytesForCurrentThread"/> around one call.
/// </summary>
public sealed class InsertBatchAllocationTests : IDisposable
{
    private static readonly string[] Columns = ["name", "email", "age", "score", "data"];

    private readonly string testDbPath;
    private readonly Database db;

    public InsertBatchAllocationTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"net11_insertalloc_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        db = new Database(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSharpCoreDB()),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);

        db.ExecuteSQL("CREATE TABLE docs (name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)");
    }

    public void Dispose()
    {
        try { db.Dispose(); } catch { }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch { }
    }

    /// <summary>
    /// Per-row allocation of <c>InsertBatch(table, object[] rows, columns)</c>. The ceiling is a canary, not a target:
    /// it records what the path costs today so a regression shows up as a red test rather than as a slow commit.
    /// </summary>
    [Fact]
    public void InsertBatch_ArrayRows_AllocatesAtMostPerRow()
    {
        const int Rows = 2_000;
        // Measured 2026-11-12 (GA runtime): 1.838.776 bytes for 2.000 rows = 919 B/row through the dictionary-free
        // object[] + column-order overload. The presence mask hoist (the statement's columns are a statement
        // property, not a row property) took that to 1.787.608 bytes = 894 B/row, a measured -25 B/row with
        // identical behaviour. The ceiling sits just above the current figure so it is a regression canary — a
        // change that allocates more turns this red, and the next cut lowers it on purpose.
        const int CeilingBytesPerRow = 900;

        // Warm the call shape first: a tiered-JIT promotion inside the measured call would be charged to it.
        db.InsertBatch("docs", BuildRows(0, 500), Columns);

        var rows = BuildRows(1_000, Rows);
        long before = GC.GetAllocatedBytesForCurrentThread();
        db.InsertBatch("docs", rows, Columns);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        double perRow = allocated / (double)Rows;
        Assert.True(
            allocated <= Rows * (long)CeilingBytesPerRow,
            $"InsertBatch allocated {allocated:N0} bytes for {Rows:N0} rows ({perRow:F0} B/row); at most {CeilingBytesPerRow} B/row was expected.");
    }

    private static List<object[]> BuildRows(int start, int count)
    {
        var rows = new List<object[]>(count);
        for (int i = start; i < start + count; i++)
        {
            rows.Add([$"User{i}", $"user{i}@test.com", 20 + (i % 60), i * 0.1, $"payload-{i}"]);
        }

        return rows;
    }
}
#endif
