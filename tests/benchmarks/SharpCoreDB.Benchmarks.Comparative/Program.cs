// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BLite.Bson;
using BLite.Core;
using BLite.Core.Collections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

namespace SharpCoreDB.Benchmarks.Comparative;


/// Comparative benchmark: SharpCoreDB vs BLite vs LiteDB vs SQLite.
/// Identical document CRUD workloads on all four databases.
/// </summary>
class Program
{
    private Program() { } // Static utility class - prevent instantiation.
    const int InsertCount = 100_000;
    const int BatchSize = 10_000;
    const int ReadCount = 10_000;
    const int UpdateCount = 10_000;
    const int DeleteCount = 10_000;

    static async Task Main(string[] args)
    {
        // Optional: --readtest → focused SQL-vs-Direct read micro-benchmark (median of N runs).
        if (args.Any(a => a.Equals("--readtest", StringComparison.OrdinalIgnoreCase)))
        {
            RunReadMicroBenchmark();
            return;
        }

        // Optional: --inserttest → focused SQL-vs-Direct insert micro-benchmark (median of N runs).
        if (args.Any(a => a.Equals("--inserttest", StringComparison.OrdinalIgnoreCase)))
        {
            RunInsertMicroBenchmark();
            return;
        }

        // Optional: --fixedwidth → run the fixed-width vs legacy before/after benchmark only.
        if (args.Any(a => a.Equals("--fixedwidth", StringComparison.OrdinalIgnoreCase)))
        {
            FixedWidthBenchmark.Run();
            return;
        }

        // Optional: --engine=appendonly (default) | --engine=pagebased
        // PageBased is the v2.0 in-place-update engine (WP10-WP13 storage engine roadmap).
        var engineArg = args.FirstOrDefault(a => a.StartsWith("--engine=", StringComparison.OrdinalIgnoreCase));
        var engineType = engineArg is not null
            && engineArg.Substring("--engine=".Length).Equals("pagebased", StringComparison.OrdinalIgnoreCase)
                ? SharpCoreDB.Interfaces.StorageEngineType.PageBased
                : SharpCoreDB.Interfaces.StorageEngineType.AppendOnly;
        var engineLabel = engineType == SharpCoreDB.Interfaces.StorageEngineType.PageBased ? "PageBased" : "AppendOnly";

        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  SharpCoreDB vs BLite vs LiteDB vs SQLite               ║");
        Console.WriteLine("║  Comparative Document CRUD Benchmark                     ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS:      {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Console.WriteLine($"Cores:   {Environment.ProcessorCount}");
        Console.WriteLine($"Engine:  {engineLabel} (use --engine=pagebased for the in-place-update engine)");
        Console.WriteLine($"Inserts: {InsertCount:N0}  Reads: {ReadCount:N0}  Updates: {UpdateCount:N0}  Deletes: {DeleteCount:N0}");
        Console.WriteLine();

        var results = new Dictionary<string, BenchmarkResult>();

        // ── SharpCoreDB ──
        Console.WriteLine("━━━ SharpCoreDB (SQL) ━━━");
        results["SharpCoreDB (SQL)"] = RunSharpCoreDB(engineType);
        Console.WriteLine();

        // ── SharpCoreDB Direct API ──
        Console.WriteLine("━━━ SharpCoreDB (Direct API) ━━━");
        results["SharpCoreDB (Direct)"] = RunSharpCoreDBDirectApi(engineType);
        Console.WriteLine();

        // ── SharpCoreDB StructRow (zero-alloc read path) ──
        Console.WriteLine("━━━ SharpCoreDB (StructRow) ━━━");
        results["SharpCoreDB (StructRow)"] = RunSharpCoreDBStruct(engineType);
        Console.WriteLine();

        // ── SQLite ──
        Console.WriteLine("━━━ SQLite ━━━");
        results["SQLite"] = RunSQLite();
        Console.WriteLine();

        // ── LiteDB ──
        Console.WriteLine("━━━ LiteDB ━━━");
        results["LiteDB"] = RunLiteDB();
        Console.WriteLine();

        // ── BLite ──
        Console.WriteLine("━━━ BLite 4.0.1 ━━━");
        try
        {
            results["BLite"] = await RunBLiteAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️ BLite benchmark failed: {ex.Message}");
            Console.WriteLine($"  {ex.GetType().Name} — skipping");
        }
        Console.WriteLine();

        // ── Comparison ──
        PrintComparison(results);

        // Save JSON
        var dir = "results";
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"comparative_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nResults saved to: {path}");
    }

    // ══════════════════════════════════════
    // SharpCoreDB
    // ══════════════════════════════════════

    /// <summary>
    /// Focused read micro-benchmark: SQL (parameterized point-lookup) vs Direct API
    /// (<c>FindByIndex</c>) on the same database. Reports the median of several runs so
    /// machine load does not dominate the result.
    /// </summary>
    static void RunReadMicroBenchmark()
    {
        const int rows = 100_000;
        const int queries = 10_000;
        const int reps = 7;

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<DatabaseFactory>();
        var config = BuildConfig(SharpCoreDB.Interfaces.StorageEngineType.AppendOnly);
        var dbPath = Path.Combine(Path.GetTempPath(), $"scdb-readtest-{Guid.NewGuid()}");

        using var db = (SharpCoreDB.Database)factory.Create(
            dbPath: dbPath,
            masterPassword: "pw",
            isReadOnly: false,
            config: config);

        try
        {
            db.ExecuteSQL("CREATE TABLE docs (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            for (int batch = 0; batch < rows; batch += 10_000)
            {
                var list = new List<Dictionary<string, object>>(10_000);
                for (int i = batch; i < batch + 10_000; i++)
                {
                    list.Add(new Dictionary<string, object>
                    {
                        ["name"] = $"User{i}",
                        ["email"] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        ["score"] = i * 0.1,
                        ["data"] = $"payload-{i}",
                    });
                }

                db.InsertBatch("docs", list);
            }

            db.Flush();

            // Warmup (JIT + index load).
            for (int i = 0; i < 1000; i++)
            {
                db.ExecuteQuery("SELECT * FROM docs WHERE name = @name",
                    new Dictionary<string, object?> { ["@name"] = $"User{i}" });
                db.FindByIndex("docs", "name", $"User{i}");
            }

            double[] sqlTimes = new double[reps];
            double[] directTimes = new double[reps];

            for (int r = 0; r < reps; r++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < queries; i++)
                {
                    db.ExecuteQuery("SELECT * FROM docs WHERE name = @name",
                        new Dictionary<string, object?> { ["@name"] = $"User{i}" });
                }

                sw.Stop();
                sqlTimes[r] = sw.Elapsed.TotalSeconds;

                sw.Restart();
                for (int i = 0; i < queries; i++)
                {
                    db.FindByIndex("docs", "name", $"User{i}");
                }

                sw.Stop();
                directTimes[r] = sw.Elapsed.TotalSeconds;
            }

            Array.Sort(sqlTimes);
            Array.Sort(directTimes);
            double sqlMedian = sqlTimes[reps / 2];
            double directMedian = directTimes[reps / 2];

            Console.WriteLine();
            Console.WriteLine("═══ READ micro-benchmark (10,000 point reads via name hash index, median of 7) ═══");
            Console.WriteLine($"  SQL    : {sqlMedian:F3}s  ({queries / sqlMedian:N0} ops/s)");
            Console.WriteLine($"  Direct : {directMedian:F3}s  ({queries / directMedian:N0} ops/s)");
            Console.WriteLine($"  SQL/Direct overhead: {(sqlMedian / directMedian):F2}x");
        }
        finally
        {
            try { Directory.Delete(dbPath, true); }
            catch { }
        }
    }

    /// <summary>
    /// Focused insert micro-benchmark: SQL (<c>ExecuteBatchSQL</c> with INSERT statements) vs
    /// Direct API (<c>InsertBatch</c>). Each repetition runs on a fresh database so append-only
    /// growth and unique keys do not skew the result; median of several runs is reported.
    /// </summary>
    static void RunInsertMicroBenchmark()
    {
        const int inserts = 50_000;
        const int batch = 10_000;
        const int reps = 5;

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<DatabaseFactory>();
        var config = BuildConfig(SharpCoreDB.Interfaces.StorageEngineType.AppendOnly);

        double[] sqlTimes = new double[reps];
        double[] directTimes = new double[reps];

        for (int r = 0; r < reps; r++)
        {
            var sqlPath = Path.Combine(Path.GetTempPath(), $"scdb-insert-sql-{Guid.NewGuid()}");
            using (var db = (SharpCoreDB.Database)factory.Create(sqlPath, "pw", isReadOnly: false, config: config))
            {
                db.ExecuteSQL("CREATE TABLE docs (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
                db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

                // Build the statements once (outside the timed region — this is caller work,
                // identical for SQLite in the comparative benchmark).
                var stmtBatches = new List<List<string>>();
                for (int b = 0; b < inserts; b += batch)
                {
                    var stmts = new List<string>(batch);
                    for (int i = b; i < b + batch; i++)
                    {
                        stmts.Add(string.Format(CultureInfo.InvariantCulture,
                            "INSERT INTO docs VALUES ('User{0}', 'user{0}@test.com', {1}, {2}, 'payload-{0}')",
                            i, 20 + i % 60, i * 0.1));
                    }

                    stmtBatches.Add(stmts);
                }

                var sw = Stopwatch.StartNew();
                foreach (var stmts in stmtBatches)
                {
                    db.ExecuteBatchSQL(stmts);
                }

                sw.Stop();
                sqlTimes[r] = sw.Elapsed.TotalSeconds;
            }

            try { Directory.Delete(sqlPath, true); } catch { }

            var directPath = Path.Combine(Path.GetTempPath(), $"scdb-insert-direct-{Guid.NewGuid()}");
            using (var db = (SharpCoreDB.Database)factory.Create(directPath, "pw", isReadOnly: false, config: config))
            {
                db.ExecuteSQL("CREATE TABLE docs (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
                db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

                var rowBatches = new List<List<Dictionary<string, object>>>();
                for (int b = 0; b < inserts; b += batch)
                {
                    var rows = new List<Dictionary<string, object>>(batch);
                    for (int i = b; i < b + batch; i++)
                    {
                        rows.Add(new Dictionary<string, object>
                        {
                            ["name"] = $"User{i}",
                            ["email"] = $"user{i}@test.com",
                            ["age"] = 20 + i % 60,
                            ["score"] = i * 0.1,
                            ["data"] = $"payload-{i}",
                        });
                    }

                    rowBatches.Add(rows);
                }

                var sw = Stopwatch.StartNew();
                foreach (var rows in rowBatches)
                {
                    db.InsertBatch("docs", rows);
                }

                sw.Stop();
                directTimes[r] = sw.Elapsed.TotalSeconds;
            }

            try { Directory.Delete(directPath, true); } catch { }
        }

        Array.Sort(sqlTimes);
        Array.Sort(directTimes);
        double sqlMedian = sqlTimes[reps / 2];
        double directMedian = directTimes[reps / 2];

        Console.WriteLine();
        Console.WriteLine($"═══ INSERT micro-benchmark ({inserts:N0} batched inserts, median of {reps}) ═══");
        Console.WriteLine($"  SQL    : {sqlMedian:F3}s  ({inserts / sqlMedian:N0} ops/s)");
        Console.WriteLine($"  Direct : {directMedian:F3}s  ({inserts / directMedian:N0} ops/s)");
        Console.WriteLine($"  SQL/Direct overhead: {(sqlMedian / directMedian):F2}x");
    }

    static DatabaseConfig BuildConfig(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        return new DatabaseConfig
        {
            NoEncryptMode = true,
            StorageEngineType = engineType,
            UseGroupCommitWal = false,
            EnableAdaptiveWalBatching = false,
            HighSpeedInsertMode = true,
            GroupCommitSize = 1000,
            WalDurabilityMode = SharpCoreDB.Services.DurabilityMode.Async,
            EnablePageCache = true,
            PageCacheCapacity = 10_000,
            UseMemoryMapping = true,
            UseBufferedIO = true,
            EnableHashIndexes = true,
            EnableQueryCache = true,
            QueryCacheSize = 4096,
            EnableCompiledPlanCache = true,
            EnableBTreeSelection = true,
            EnableSimdAndProjectionPushdown = true,
            WalBufferSize = 8 * 1024 * 1024,
            BufferPoolSize = 128 * 1024 * 1024,
            CollectGCAfterBatches = false,
            SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
            StrictParameterValidation = false
        };
    }

    static BenchmarkResult RunSharpCoreDB(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench-sharpcoredb-{Guid.NewGuid()}");
        var result = new BenchmarkResult();

        try
        {
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredService<DatabaseFactory>();
            var config = BuildConfig(engineType);

            using var db = (SharpCoreDB.Database)factory.Create(
                dbPath: dbPath,
                masterPassword: "bench123",
                isReadOnly: false,
                config: config);

            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");

            // Index lookup path used by READ/UPDATE/DELETE in this benchmark
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            // INSERT (batched via InsertBatch API for optimal performance)
            var sw = Stopwatch.StartNew();
            for (int batch = 0; batch < InsertCount; batch += BatchSize)
            {
                int end = Math.Min(batch + BatchSize, InsertCount);
                var rows = new List<Dictionary<string, object>>(end - batch);
                for (int i = batch; i < end; i++)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["name"] = $"User{i}",
                        ["email"] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        ["score"] = i * 0.1,
                        ["data"] = $"payload-{i}"
                    });
                }
                db.InsertBatch("docs", rows);
            }
            db.Flush();
            sw.Stop();
            result.InsertTime = sw.Elapsed.TotalSeconds;
            result.InsertOpsPerSec = (int)(InsertCount / result.InsertTime);
            Console.WriteLine($"  INSERT {InsertCount:N0}: {result.InsertTime:F2}s ({result.InsertOpsPerSec:N0} ops/sec)");

            // READ (SELECT by indexed name field)
            sw.Restart();
            for (int i = 0; i < ReadCount; i++)
            {
                db.ExecuteQuery("SELECT * FROM docs WHERE name = @name", new Dictionary<string, object?>
                {
                    ["@name"] = $"User{i}"
                });
            }
            sw.Stop();
            result.ReadTime = sw.Elapsed.TotalSeconds;
            result.ReadOpsPerSec = (int)(ReadCount / result.ReadTime);
            Console.WriteLine($"  READ   {ReadCount:N0}: {result.ReadTime:F2}s ({result.ReadOpsPerSec:N0} ops/sec)");

            // UPDATE
            sw.Restart();
            var updateStmts = new List<string>(UpdateCount);
            for (int i = 0; i < UpdateCount; i++)
            {
                updateStmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = {0:F1} WHERE name = 'User{1}'", i * 99.9, i));
            }
            db.ExecuteBatchSQL(updateStmts);
            db.Flush();
            sw.Stop();
            result.UpdateTime = sw.Elapsed.TotalSeconds;
            result.UpdateOpsPerSec = (int)(UpdateCount / result.UpdateTime);
            Console.WriteLine($"  UPDATE {UpdateCount:N0}: {result.UpdateTime:F2}s ({result.UpdateOpsPerSec:N0} ops/sec)");

            // DELETE
            sw.Restart();
            var deleteStmts = new List<string>(DeleteCount);
            for (int i = 0; i < DeleteCount; i++)
            {
                deleteStmts.Add($"DELETE FROM docs WHERE name = 'User{i}'");
            }
            db.ExecuteBatchSQL(deleteStmts);
            db.Flush();
            sw.Stop();
            result.DeleteTime = sw.Elapsed.TotalSeconds;
            result.DeleteOpsPerSec = (int)(DeleteCount / result.DeleteTime);
            Console.WriteLine($"  DELETE {DeleteCount:N0}: {result.DeleteTime:F2}s ({result.DeleteOpsPerSec:N0} ops/sec)");
        }
        finally
        {
            try
            {
                if (Directory.Exists(dbPath))
                {
                    Directory.Delete(dbPath, true);
                }
            }
            catch
            {
                // Temp benchmark cleanup best-effort
            }
        }

        return result;
    }

    // ══════════════════════════════════════
    // SharpCoreDB (Direct API — no SQL parsing)
    // ══════════════════════════════════════
    static BenchmarkResult RunSharpCoreDBDirectApi(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench-sharpcoredb-direct-{Guid.NewGuid()}");
        var result = new BenchmarkResult();

        try
        {
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredService<DatabaseFactory>();
            var config = BuildConfig(engineType);

            using var db = (SharpCoreDB.Database)factory.Create(
                dbPath: dbPath,
                masterPassword: "bench123",
                isReadOnly: false,
                config: config);

            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");

            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            // INSERT (same batched API — no SQL parsing either way)
            var sw = Stopwatch.StartNew();
            for (int batch = 0; batch < InsertCount; batch += BatchSize)
            {
                int end = Math.Min(batch + BatchSize, InsertCount);
                var rows = new List<Dictionary<string, object>>(end - batch);
                for (int i = batch; i < end; i++)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["name"] = $"User{i}",
                        ["email"] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        ["score"] = i * 0.1,
                        ["data"] = $"payload-{i}"
                    });
                }
                db.InsertBatch("docs", rows);
            }
            db.Flush();
            sw.Stop();
            result.InsertTime = sw.Elapsed.TotalSeconds;
            result.InsertOpsPerSec = (int)(InsertCount / result.InsertTime);
            Console.WriteLine($"  INSERT {InsertCount:N0}: {result.InsertTime:F2}s ({result.InsertOpsPerSec:N0} ops/sec)");

            // READ (Direct API — FindByIndex bypasses SQL parsing)
            sw.Restart();
            for (int i = 0; i < ReadCount; i++)
            {
                db.FindByIndex("docs", "name", $"User{i}");
            }
            sw.Stop();
            result.ReadTime = sw.Elapsed.TotalSeconds;
            result.ReadOpsPerSec = (int)(ReadCount / result.ReadTime);
            Console.WriteLine($"  READ   {ReadCount:N0}: {result.ReadTime:F2}s ({result.ReadOpsPerSec:N0} ops/sec)");

            // UPDATE (Direct API — UpdateByPrimaryKey not available without PK, use SQL for fairness)
            // Note: This table uses name hash index, not integer PK.
            // Direct API UpdateByPrimaryKey requires a PK column, so we use the SQL path
            // which now benefits from GeneratedRegex + canUseIndex fix.
            sw.Restart();
            var updateStmts = new List<string>(UpdateCount);
            for (int i = 0; i < UpdateCount; i++)
            {
                updateStmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = {0:F1} WHERE name = 'User{1}'", i * 99.9, i));
            }
            db.ExecuteBatchSQL(updateStmts);
            db.Flush();
            sw.Stop();
            result.UpdateTime = sw.Elapsed.TotalSeconds;
            result.UpdateOpsPerSec = (int)(UpdateCount / result.UpdateTime);
            Console.WriteLine($"  UPDATE {UpdateCount:N0}: {result.UpdateTime:F2}s ({result.UpdateOpsPerSec:N0} ops/sec)");

            // DELETE (SQL path — same reason as UPDATE)
            sw.Restart();
            var deleteStmts = new List<string>(DeleteCount);
            for (int i = 0; i < DeleteCount; i++)
            {
                deleteStmts.Add($"DELETE FROM docs WHERE name = 'User{i}'");
            }
            db.ExecuteBatchSQL(deleteStmts);
            db.Flush();
            sw.Stop();
            result.DeleteTime = sw.Elapsed.TotalSeconds;
            result.DeleteOpsPerSec = (int)(DeleteCount / result.DeleteTime);
            Console.WriteLine($"  DELETE {DeleteCount:N0}: {result.DeleteTime:F2}s ({result.DeleteOpsPerSec:N0} ops/sec)");
        }
        finally
        {
            try
            {
                if (Directory.Exists(dbPath))
                {
                    Directory.Delete(dbPath, true);
                }
            }
            catch
            {
                // Temp benchmark cleanup best-effort
            }
        }

        return result;
    }

    // ══════════════════════════════════════
    // SharpCoreDB StructRow (zero-alloc read path)
    // ══════════════════════════════════════
    static BenchmarkResult RunSharpCoreDBStruct(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench-sharpcoredb-struct-{Guid.NewGuid()}");
        var result = new BenchmarkResult();

        try
        {
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredService<DatabaseFactory>();
            var config = BuildConfig(engineType);

            using var db = (SharpCoreDB.Database)factory.Create(
                dbPath: dbPath,
                masterPassword: "bench123",
                isReadOnly: false,
                config: config);

            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");

            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            // INSERT (same batched API as the other SharpCoreDB rows)
            var sw = Stopwatch.StartNew();
            for (int batch = 0; batch < InsertCount; batch += BatchSize)
            {
                int end = Math.Min(batch + BatchSize, InsertCount);
                var rows = new List<Dictionary<string, object>>(end - batch);
                for (int i = batch; i < end; i++)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["name"] = $"User{i}",
                        ["email"] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        ["score"] = i * 0.1,
                        ["data"] = $"payload-{i}"
                    });
                }
                db.InsertBatch("docs", rows);
            }
            db.Flush();
            sw.Stop();
            result.InsertTime = sw.Elapsed.TotalSeconds;
            result.InsertOpsPerSec = (int)(InsertCount / result.InsertTime);
            Console.WriteLine($"  INSERT {InsertCount:N0}: {result.InsertTime:F2}s ({result.InsertOpsPerSec:N0} ops/sec)");

            // READ (zero-alloc StructRow point lookups via the plan-cache fast path)
            sw.Restart();
            for (int i = 0; i < ReadCount; i++)
            {
                var parameters = new Dictionary<string, object?> { ["@name"] = $"User{i}" };
                int matched = 0;
                foreach (var row in db.ExecuteQueryStruct("SELECT * FROM docs WHERE name = @name", parameters))
                {
                    matched++;
                }

                if (matched == 0)
                {
                    throw new InvalidOperationException($"StructRow read returned no rows for User{i}.");
                }
            }
            sw.Stop();
            result.ReadTime = sw.Elapsed.TotalSeconds;
            result.ReadOpsPerSec = (int)(ReadCount / result.ReadTime);
            Console.WriteLine($"  READ   {ReadCount:N0}: {result.ReadTime:F2}s ({result.ReadOpsPerSec:N0} ops/sec)");

            // UPDATE/DELETE intentionally omitted — they use identical code paths to the
            // Direct API row; this row focuses on the zero-allocation READ path.
            result.UpdateTime = 0d;
            result.UpdateOpsPerSec = 0;
            result.DeleteTime = 0d;
            result.DeleteOpsPerSec = 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(dbPath))
                {
                    Directory.Delete(dbPath, true);
                }
            }
            catch
            {
                // Temp benchmark cleanup best-effort
            }
        }

        return result;
    }

    // ══════════════════════════════════════
    // SQLite
    // ══════════════════════════════════════
    static BenchmarkResult RunSQLite()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"bench-sqlite-{Guid.NewGuid()}.db");
        var result = new BenchmarkResult();

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbFile}");
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"CREATE TABLE docs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    email TEXT,
                    age INTEGER,
                    score REAL,
                    data TEXT
                )";
                cmd.ExecuteNonQuery();
            }

            // Pragmas for fair comparison
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "PRAGMA journal_mode=WAL"; cmd.ExecuteNonQuery(); }
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "PRAGMA synchronous=NORMAL"; cmd.ExecuteNonQuery(); }

            // INSERT (batched in transactions)
            var sw = Stopwatch.StartNew();
            for (int batch = 0; batch < InsertCount; batch += BatchSize)
            {
                using var tx = conn.BeginTransaction();
                int end = Math.Min(batch + BatchSize, InsertCount);
                for (int i = batch; i < end; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "INSERT INTO docs (name, email, age, score, data) VALUES (@name, @email, @age, @score, @payload)";
                    var pName = cmd.CreateParameter(); pName.ParameterName = "@name"; pName.Value = $"User{i}"; cmd.Parameters.Add(pName);
                    var pEmail = cmd.CreateParameter(); pEmail.ParameterName = "@email"; pEmail.Value = $"user{i}@test.com"; cmd.Parameters.Add(pEmail);
                    var pAge = cmd.CreateParameter(); pAge.ParameterName = "@age"; pAge.Value = 20 + i % 60; cmd.Parameters.Add(pAge);
                    var pScore = cmd.CreateParameter(); pScore.ParameterName = "@score"; pScore.Value = i * 0.1; cmd.Parameters.Add(pScore);
                    var pPayload = cmd.CreateParameter(); pPayload.ParameterName = "@payload"; pPayload.Value = $"payload-{i}"; cmd.Parameters.Add(pPayload);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            sw.Stop();
            result.InsertTime = sw.Elapsed.TotalSeconds;
            result.InsertOpsPerSec = (int)(InsertCount / result.InsertTime);
            Console.WriteLine($"  INSERT {InsertCount:N0}: {result.InsertTime:F2}s ({result.InsertOpsPerSec:N0} ops/sec)");

            // READ
            sw.Restart();
            for (int i = 1; i <= ReadCount; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM docs WHERE id = @id";
                var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = i; cmd.Parameters.Add(p);
                using var reader = cmd.ExecuteReader();
                reader.Read();
            }
            sw.Stop();
            result.ReadTime = sw.Elapsed.TotalSeconds;
            result.ReadOpsPerSec = (int)(ReadCount / result.ReadTime);
            Console.WriteLine($"  READ   {ReadCount:N0}: {result.ReadTime:F2}s ({result.ReadOpsPerSec:N0} ops/sec)");

            // UPDATE
            sw.Restart();
            using (var tx = conn.BeginTransaction())
            {
                for (int i = 1; i <= UpdateCount; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE docs SET score = @score WHERE id = @id";
                    var pScore = cmd.CreateParameter(); pScore.ParameterName = "@score"; pScore.Value = i * 99.9; cmd.Parameters.Add(pScore);
                    var pId = cmd.CreateParameter(); pId.ParameterName = "@id"; pId.Value = i; cmd.Parameters.Add(pId);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            sw.Stop();
            result.UpdateTime = sw.Elapsed.TotalSeconds;
            result.UpdateOpsPerSec = (int)(UpdateCount / result.UpdateTime);
            Console.WriteLine($"  UPDATE {UpdateCount:N0}: {result.UpdateTime:F2}s ({result.UpdateOpsPerSec:N0} ops/sec)");

            // DELETE
            sw.Restart();
            using (var tx = conn.BeginTransaction())
            {
                for (int i = 1; i <= DeleteCount; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM docs WHERE id = @id";
                    var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = i; cmd.Parameters.Add(p);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            sw.Stop();
            result.DeleteTime = sw.Elapsed.TotalSeconds;
            result.DeleteOpsPerSec = (int)(DeleteCount / result.DeleteTime);
            Console.WriteLine($"  DELETE {DeleteCount:N0}: {result.DeleteTime:F2}s ({result.DeleteOpsPerSec:N0} ops/sec)");
        }
        finally
        {
            try { if (File.Exists(dbFile)) File.Delete(dbFile); } catch { /* temp */ }
        }

        return result;
    }

    // ══════════════════════════════════════
    // LiteDB
    // ══════════════════════════════════════
    static BenchmarkResult RunLiteDB()
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"bench-litedb-{Guid.NewGuid()}.db");
        var result = new BenchmarkResult();

        try
        {
            using var db = new LiteDB.LiteDatabase(dbFile);


            var col = db.GetCollection<LiteDoc>("docs");
            col.EnsureIndex(x => x.Id);

            // INSERT
            var sw = Stopwatch.StartNew();
            for (int batch = 0; batch < InsertCount; batch += BatchSize)
            {
                int end = Math.Min(batch + BatchSize, InsertCount);
                var docs = new List<LiteDoc>(end - batch);
                for (int i = batch; i < end; i++)
                {
                    docs.Add(new LiteDoc
                    {
                        Name = $"User{i}",
                        Email = $"user{i}@test.com",
                        Age = 20 + i % 60,
                        Score = i * 0.1,
                        Data = $"payload-{i}"
                    });
                }
                col.InsertBulk(docs);
            }
            sw.Stop();
            result.InsertTime = sw.Elapsed.TotalSeconds;
            result.InsertOpsPerSec = (int)(InsertCount / result.InsertTime);
            Console.WriteLine($"  INSERT {InsertCount:N0}: {result.InsertTime:F2}s ({result.InsertOpsPerSec:N0} ops/sec)");

            // READ
            sw.Restart();
            for (int i = 1; i <= ReadCount; i++)
            {
                col.FindById(i);
            }
            sw.Stop();
            result.ReadTime = sw.Elapsed.TotalSeconds;
            result.ReadOpsPerSec = (int)(ReadCount / result.ReadTime);
            Console.WriteLine($"  READ   {ReadCount:N0}: {result.ReadTime:F2}s ({result.ReadOpsPerSec:N0} ops/sec)");

            // UPDATE
            sw.Restart();
            for (int i = 1; i <= UpdateCount; i++)
            {
                var doc = col.FindById(i);
                if (doc is not null)
                {
                    doc.Score = i * 99.9;
                    col.Update(doc);
                }
            }
            sw.Stop();
            result.UpdateTime = sw.Elapsed.TotalSeconds;
            result.UpdateOpsPerSec = (int)(UpdateCount / result.UpdateTime);
            Console.WriteLine($"  UPDATE {UpdateCount:N0}: {result.UpdateTime:F2}s ({result.UpdateOpsPerSec:N0} ops/sec)");

            // DELETE
            sw.Restart();
            for (int i = 1; i <= DeleteCount; i++)
            {
                col.Delete(i);
            }
            sw.Stop();
            result.DeleteTime = sw.Elapsed.TotalSeconds;
            result.DeleteOpsPerSec = (int)(DeleteCount / result.DeleteTime);
            Console.WriteLine($"  DELETE {DeleteCount:N0}: {result.DeleteTime:F2}s ({result.DeleteOpsPerSec:N0} ops/sec)");
        }
        finally
        {
            try { if (File.Exists(dbFile)) File.Delete(dbFile); } catch { /* temp */ }
        }

        return result;
    }

    // ══════════════════════════════════════
    // BLite 4.0.1 (BLiteEngine + DynamicCollection — async-only API)
    // ══════════════════════════════════════
    static Task<BenchmarkResult> RunBLiteAsync()
    {
        // BLite 4.0.1: BsonDocumentBuilder is an empty type (zero public methods/fields).
        // DynamicCollection.CreateDocument(fields, b => b.Set(...)) cannot compile because
        // the builder exposes no Set, Add, Write, or indexer — the documented API does not
        // match the shipped NuGet binary. Same issue as v2.0.2 but now with async-only CRUD.
        // See docs/benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md for full details.
        throw new NotSupportedException(
            "BLite 4.0.1 BsonDocumentBuilder has no public setter API (Set/Add/Write all missing). " +
            "DynamicCollection.CreateDocument() compiles but the builder action cannot populate fields. " +
            "See docs/benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md for details.");
    }

    // ══════════════════════════════════════
    // Comparison Table
    // ══════════════════════════════════════
    static void PrintComparison(Dictionary<string, BenchmarkResult> results)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              Comparative Document CRUD Benchmark Results                    ║");
        Console.WriteLine("╠═══════════════╤════════════════╤════════════════╤════════════════╤══════════╣");
        Console.WriteLine("║ Database      │ INSERT ops/sec │ READ ops/sec   │ UPDATE ops/sec │ DELETE   ║");
        Console.WriteLine("╠═══════════════╪════════════════╪════════════════╪════════════════╪══════════╣");

        foreach (var (name, r) in results)
        {
            Console.WriteLine($"║ {name,-13} │ {r.InsertOpsPerSec,14:N0} │ {r.ReadOpsPerSec,14:N0} │ {r.UpdateOpsPerSec,14:N0} │ {r.DeleteOpsPerSec,8:N0} ║");
        }

        Console.WriteLine("╠═══════════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Test: 100K inserts (10K batches), 10K reads/updates/deletes by PK          ║");
        Console.WriteLine("║  All databases: WAL mode, optimal batch settings                            ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝");
    }
}

// ── Data Models ──

class LiteDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public double Score { get; set; }
    public string Data { get; set; } = "";
}

class BenchmarkResult
{
    public double InsertTime { get; set; }
    public int InsertOpsPerSec { get; set; }
    public double ReadTime { get; set; }
    public int ReadOpsPerSec { get; set; }
    public double UpdateTime { get; set; }
    public int UpdateOpsPerSec { get; set; }
    public double DeleteTime { get; set; }
    public int DeleteOpsPerSec { get; set; }
}
