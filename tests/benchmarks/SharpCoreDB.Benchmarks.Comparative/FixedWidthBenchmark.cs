// <copyright file="FixedWidthBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Benchmarks.Comparative;

/// <summary>
/// Before/after benchmark for the fixed-width record layout work (B1–B6):
/// the same workload runs against a legacy (variable-length records) database and a
/// fixed-width database, and the storage growth and elapsed time are compared.
/// Run with: dotnet run --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --fixedwidth
/// </summary>
internal static class FixedWidthBenchmark
{
    private const int SameLengthUpdates = 10_000;
    private const int VariableUpdates = 1_000;
    private const int SelectRows = 100_000;
    private const int SelectRounds = 30;

    public static void Run()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Fixed-Width vs Legacy (variable-length) — storage & speed   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine();

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<DatabaseFactory>();

        Console.WriteLine("── Workload A: 10,000 growing variable-column updates (storage growth) ──");
        RunGrowingUpdates(factory);
        Console.WriteLine();

        Console.WriteLine("── Workload B: 1,000 variable-length updates + arena compaction (storage) ──");
        RunVariableUpdates(factory);
        Console.WriteLine();

        Console.WriteLine($"── Workload C: {SelectRounds} full scans, WHERE on a non-indexed INTEGER column over {SelectRows:N0} rows ──");
        RunSelectWhere(factory);
        Console.WriteLine();

        Console.WriteLine("── Workload D: batch INSERT throughput ──");
        RunInsertThroughput(factory);
    }

    private static DatabaseConfig BuildConfig(bool fixedWidth) => new()
    {
        NoEncryptMode = true,
        UseGroupCommitWal = false,
        EnableAdaptiveWalBatching = false,
        HighSpeedInsertMode = true,
        GroupCommitSize = 1000,
        WalDurabilityMode = Services.DurabilityMode.Async,
        EnablePageCache = true,
        PageCacheCapacity = 10_000,
        UseMemoryMapping = true,
        UseBufferedIO = true,
        EnableHashIndexes = true,
        EnableQueryCache = false,
        EnableBTreeSelection = true,
        EnableSimdAndProjectionPushdown = true,
        SqlValidationMode = Services.SqlQueryValidator.ValidationMode.Disabled,
        StrictParameterValidation = false,
        FixedWidthRecordLayout = fixedWidth,
    };

    private static (Database db, string dir) CreateDatabase(DatabaseFactory factory, bool fixedWidth)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"scdb_fwbench_{fixedWidth}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var db = (Database)factory.Create(dir, "bench123", isReadOnly: false, config: BuildConfig(fixedWidth));
        return (db, dir);
    }

    private static long DataBytes(string dir)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".ovf", StringComparison.OrdinalIgnoreCase))
            {
                total += new FileInfo(file).Length;
            }
        }

        return total;
    }

    private static void RunGrowingUpdates(DatabaseFactory factory)
    {
        // Growing values force the legacy path to append a new record per update (its .dat grows
        // with every new record length). Fixed-width keeps the .dat constant and grows only the
        // overflow arena, which the auto-compaction (1000-update threshold) keeps bounded.
        foreach (var fixedWidth in new[] { true, false })
        {
            var label = fixedWidth ? "fixed-width" : "legacy     ";
            var (db, dir) = CreateDatabase(factory, fixedWidth);
            try
            {
                db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
                db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");

                // Warm-up (JIT + index structures).
                for (int i = 0; i < 100; i++)
                {
                    db.ExecuteSQL("UPDATE t SET name = 'warmup-value' WHERE id = 1");
                }

                long startBytes = DataBytes(dir);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < SameLengthUpdates; i++)
                {
                    var value = new string((char)('A' + (i % 26)), 1 + (i % 200));
                    db.ExecuteSQL($"UPDATE t SET name = '{value}' WHERE id = 1");
                }

                sw.Stop();
                long growth = DataBytes(dir) - startBytes;
                Console.WriteLine($"  {label}: {SameLengthUpdates:N0} updates in {sw.Elapsed.TotalSeconds:F2}s, storage growth {growth / 1024.0:F1} KB");
            }
            finally
            {
                (db as IDisposable)?.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    private static void RunVariableUpdates(DatabaseFactory factory)
    {
        foreach (var fixedWidth in new[] { true, false })
        {
            var label = fixedWidth ? "fixed-width" : "legacy     ";
            var (db, dir) = CreateDatabase(factory, fixedWidth);
            try
            {
                db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
                db.ExecuteSQL("INSERT INTO t VALUES (1, 'a')");
                for (int i = 0; i < 100; i++)
                {
                    db.ExecuteSQL("UPDATE t SET name = 'warmup-value' WHERE id = 1");
                }

                long startBytes = DataBytes(dir);

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < VariableUpdates; i++)
                {
                    var value = new string((char)('a' + (i % 26)), (i % 50) + 1);
                    db.ExecuteSQL($"UPDATE t SET name = '{value}' WHERE id = 1");
                }

                // Force the auto-compaction (B3) so the arena GC is measured too.
                Assert(db.TryGetTable("t", out var table));
                var concrete = (DataStructures.Table)table;
                concrete.CompactStorage();

                sw.Stop();
                long growth = DataBytes(dir) - startBytes;
                Console.WriteLine($"  {label}: {VariableUpdates:N0} updates + compact in {sw.Elapsed.TotalSeconds:F2}s, storage growth {growth:N0} bytes");
            }
            finally
            {
                (db as IDisposable)?.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    private static void RunSelectWhere(DatabaseFactory factory)
    {
        foreach (var fixedWidth in new[] { true, false })
        {
            var label = fixedWidth ? "fixed-width" : "legacy     ";
            var (db, dir) = CreateDatabase(factory, fixedWidth);
            try
            {
                db.ExecuteSQL("CREATE TABLE s (id INTEGER PRIMARY KEY, category INTEGER, payload TEXT)");
                for (int i = 0; i < SelectRows; i += 1000)
                {
                    var rows = new List<Dictionary<string, object>>(1000);
                    for (int j = 0; j < 1000; j++)
                    {
                        rows.Add(new Dictionary<string, object>
                        {
                            ["id"] = i + j,
                            ["category"] = i + j,
                            ["payload"] = $"payload-{i + j}-with-some-length",
                        });
                    }

                    db.InsertBatch("s", rows);
                }

                // Warm-up
                db.ExecuteQuery("SELECT * FROM s WHERE category = -1");

                var sw = Stopwatch.StartNew();
                long rowsReturned = 0;
                for (int r = 0; r < SelectRounds; r++)
                {
                    rowsReturned += db.ExecuteQuery("SELECT * FROM s WHERE category = -1").Count;
                }

                sw.Stop();
                double perQueryMs = sw.Elapsed.TotalMilliseconds / SelectRounds;
                Console.WriteLine($"  {label}: {SelectRounds} scans ({SelectRows:N0} rows each, non-indexed WHERE) in {sw.Elapsed.TotalSeconds:F2}s → {perQueryMs:F2} ms/query (rows returned: {rowsReturned})");
            }
            finally
            {
                (db as IDisposable)?.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    private static void RunInsertThroughput(DatabaseFactory factory)
    {
        const int InsertCount = 100_000;
        const int BatchSize = 5_000;

        foreach (var fixedWidth in new[] { true, false })
        {
            var label = fixedWidth ? "fixed-width" : "legacy     ";
            var (db, dir) = CreateDatabase(factory, fixedWidth);
            try
            {
                db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, score REAL, flag BOOLEAN)");

                // Warm-up batch.
                db.InsertBatch("t", Enumerable.Range(0, 1000).Select(i => new Dictionary<string, object>
                {
                    ["id"] = i,
                    ["name"] = $"warm-{i}",
                    ["score"] = i * 0.5,
                    ["flag"] = (i & 1) == 0,
                }).ToList());

                var sw = Stopwatch.StartNew();
                for (int batch = 0; batch < InsertCount; batch += BatchSize)
                {
                    var rows = new List<Dictionary<string, object>>(BatchSize);
                    for (int i = batch; i < batch + BatchSize && i < InsertCount; i++)
                    {
                        rows.Add(new Dictionary<string, object>
                        {
                            ["id"] = 1000 + i,
                            ["name"] = $"user-{i}",
                            ["score"] = i * 0.5,
                            ["flag"] = (i & 1) == 0,
                        });
                    }

                    db.InsertBatch("t", rows);
                }

                sw.Stop();
                double perSec = InsertCount / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"  {label}: {InsertCount:N0} inserts in {sw.Elapsed.TotalSeconds:F2}s → {perSec:N0} rows/s");
            }
            finally
            {
                (db as IDisposable)?.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }

    private static void Assert(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed");
        }
    }
}
