// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Text;
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
    const string EngineArgPrefix = "--engine=";
    const string EnginePageBased = "pagebased";
    const string BannerTop = "╔══════════════════════════════════════════════════════════╗";
    const string BannerBottom = "╚══════════════════════════════════════════════════════════╝";
    const string ResultsDirName = "results";
    const string GateBaselineFolder = "baselines";
    const string GateBaselineFile = "dual-mode-baseline.json";
    const double GateDefaultFactor = 1.5;
    const double GateNoisySpread = 2.5;
    const int GateExitRegression = 1;
    const int GateExitInconclusive = 2;
    const string BenchDbPassword = "bench123"; // NOSONAR:S2068 - throwaway local benchmark credential, not a real secret
    const string EmailColumn = "email";
    const string ScoreColumn = "score";
    const string NameParam = "@name";
    const string CreateDocsIndexSql = "CREATE INDEX idx_docs_name ON docs(name)";
    const string SelectDocsByNameSql = "SELECT * FROM docs WHERE name = @name";

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

        // Optional: --multirowinsert → focused multi-row `INSERT … VALUES (…),(…)` micro-benchmark. This
        // statement shape used to lower to one Table.Insert per row — one standalone write-through append
        // each — and now routes to the batched core, so the mode exists to measure exactly that change.
        if (args.Any(a => a.Equals("--multirowinsert", StringComparison.OrdinalIgnoreCase)))
        {
            RunMultiRowInsertMicroBenchmark();
            return;
        }

        // Optional: --fixedwidth → run the fixed-width vs legacy before/after benchmark only.
        if (args.Any(a => a.Equals("--fixedwidth", StringComparison.OrdinalIgnoreCase)))
        {
            FixedWidthBenchmark.Run();
            return;
        }

        // Optional: --pk-profile → the write-path profiler's stage report for the `--pk` harness's UPDATE
        // arm, for one engine. Plan §6 (B2) asked for exactly this: the PageBased UPDATE trap (29,407 vs
        // 420,187 ops/sec) has survived two code-reading guesses, so the attribution has to come from the
        // instrumented stages and not from another plausible story — the treatment `--multirowinsert` gets.
        if (args.Any(a => a.Equals("--pk-profile", StringComparison.OrdinalIgnoreCase)))
        {
            RunPkUpdateProfile(ParseEngineType(args));
            return;
        }

        // Optional: --pk-profile-delete → the same treatment for the DELETE arm (plan §9 priority 1), because
        // the DELETE column is the one this session re-scoped the append-only work onto.
        if (args.Any(a => a.Equals("--pk-profile-delete", StringComparison.OrdinalIgnoreCase)))
        {
            RunPkDeleteProfile(ParseEngineType(args));
            return;
        }

        // Optional: --pk → fair PK-based comparison: SharpCoreDB on a table with an
        // `id INTEGER PRIMARY KEY` (mirroring the SQLite harness schema) with UPDATE/DELETE by PK,
        // so the PK B-tree fast paths and the recommended usage are measured vs SQLite.
        if (args.Any(a => a.Equals("--pk", StringComparison.OrdinalIgnoreCase)))
        {
            RunPkComparison(ParseEngineType(args));
            return;
        }

        // Optional: --pk-default → fair PK comparison using PURE default DatabaseConfig
        // (NoEncryptMode stays at its default false, no harness-only flags). Proves that the
        // out-of-the-box default path engages the fixed-width fast paths vs SQLite.
        if (args.Any(a => a.Equals("--pk-default", StringComparison.OrdinalIgnoreCase)))
        {
            RunPkDefaultComparison(ParseEngineType(args));
            return;
        }

        // Optional: --pk-ab → same-window interleaved A/B: runs arm A and arm B as alternating
        // rep pairs (A1,B1,A2,B2,...) and reports the PER-REP median ratio B/A per phase, so
        // machine drift affects both arms of each pair equally. Arms are config variants named by
        // SHARPCOREDB_PK_AB_ARM_A / SHARPCOREDB_PK_AB_ARM_B (defaults: pure default vs 'plain').
        // Optional: --dual-mode → run the CRUD workload in EVERY encryption configuration and print
        // the columns side by side, so the cost of protection is a published per-operation number
        // instead of a hidden tax. See docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md §3-1c.
        if (args.Any(a => a.Equals("--dual-mode", StringComparison.OrdinalIgnoreCase)))
        {
            RunDualModeComparison(ParseEngineType(args));
            return;
        }

        // Optional: --gate → the §2.4 write-path regression gate. Runs the same §2 protocol as
        // --dual-mode and compares every operation against a committed baseline, exiting non-zero on a
        // regression beyond the tolerance factor. --write-baseline re-records that baseline.
        if (args.Any(a => a.Equals("--gate", StringComparison.OrdinalIgnoreCase))
            || args.Any(a => a.Equals("--write-baseline", StringComparison.OrdinalIgnoreCase))
            || args.Any(a => a.StartsWith("--gate-", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = RunRegressionGate(
                ParseEngineType(args),
                ParseGateFactor(args),
                ParseGateBaseline(args),
                args.Any(a => a.Equals("--write-baseline", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        if (args.Any(a => a.Equals("--pk-ab", StringComparison.OrdinalIgnoreCase)))
        {
            RunPkAbComparison(ParseEngineType(args));
            return;
        }

        // Optional: --engine=appendonly (default) | --engine=pagebased
        // PageBased is the v2.0 in-place-update engine (WP10-WP13 storage engine roadmap).
        var engineType = ParseEngineType(args);
        var engineLabel = engineType == SharpCoreDB.Interfaces.StorageEngineType.PageBased ? "PageBased" : "AppendOnly";

        Console.WriteLine(BannerTop);
        Console.WriteLine("║  SharpCoreDB vs BLite vs LiteDB vs SQLite               ║");
        Console.WriteLine("║  Comparative Document CRUD Benchmark                     ║");
        Console.WriteLine(BannerBottom);
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
        var dir = ResultsDirName;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"comparative_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nResults saved to: {path}");
    }

    /// <summary>
    /// Resolves the optional <c>--engine=</c> argument into a storage engine type
    /// (<c>pagebased</c> → <see cref="SharpCoreDB.Interfaces.StorageEngineType.PageBased"/>,
    /// anything else → <see cref="SharpCoreDB.Interfaces.StorageEngineType.AppendOnly"/>).
    /// </summary>
    static SharpCoreDB.Interfaces.StorageEngineType ParseEngineType(string[] args)
    {
        var engineArg = args.FirstOrDefault(a => a.StartsWith(EngineArgPrefix, StringComparison.OrdinalIgnoreCase));
        return engineArg is not null
            && engineArg.Substring(EngineArgPrefix.Length).Equals(EnginePageBased, StringComparison.OrdinalIgnoreCase)
                ? SharpCoreDB.Interfaces.StorageEngineType.PageBased
                : SharpCoreDB.Interfaces.StorageEngineType.AppendOnly;
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
            db.ExecuteSQL(CreateDocsIndexSql);

            for (int batch = 0; batch < rows; batch += 10_000)
            {
                var list = new List<Dictionary<string, object>>(10_000);
                for (int i = batch; i < batch + 10_000; i++)
                {
                    list.Add(new Dictionary<string, object>
                    {
                        ["name"] = $"User{i}",
                        [EmailColumn] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        [ScoreColumn] = i * 0.1,
                        ["data"] = $"payload-{i}",
                    });
                }

                db.InsertBatch("docs", list);
            }

            db.Flush();

            // Warmup (JIT + index load).
            for (int i = 0; i < 1000; i++)
            {
                db.ExecuteQuery(SelectDocsByNameSql,
                    new Dictionary<string, object?> { [NameParam] = $"User{i}" });
                db.FindByIndex("docs", "name", $"User{i}");
            }

            double[] sqlTimes = new double[reps];
            double[] directTimes = new double[reps];

            for (int r = 0; r < reps; r++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < queries; i++)
                {
                    db.ExecuteQuery(SelectDocsByNameSql,
                        new Dictionary<string, object?> { [NameParam] = $"User{i}" });
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
            catch { /* best-effort temp-dir cleanup */ }
        }
    }

    /// <summary>
    /// Query-cache switch for every arm, from <c>SHARPCOREDB_QUERY_CACHE=off</c>. The multi-row arm's
    /// statements carry distinct literals per row, so every cache lookup is a miss there; running it with the
    /// cache off measures what the cache costs when it can never hit, which is the datum for whether a
    /// never-repeating statement should be cached at all (plan §11). Missing or any other value keeps the
    /// harness's tuned default. The config property is init-only, so this must be read in the object
    /// initializer inside <see cref="BuildConfig"/> — assigning it after construction does not compile (CS8852).
    /// </summary>
    static bool QueryCacheOverride()
    {
        var value = Environment.GetEnvironmentVariable("SHARPCOREDB_QUERY_CACHE");
        return !(value is not null && value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Forces the fixed-width record layout for the document-CRUD job, from <c>SHARPCOREDB_MAIN_FIXEDWIDTH=1</c>.
    /// Diagnostic only (plan §9 priority 1): the default job's schema declares no PRIMARY KEY, and
    /// <c>SqlParser.DDL.cs</c> grants the fixed-width layout only to tables with an explicitly declared PK, so that
    /// arm runs legacy variable-length records and cannot take the in-place UPDATE patch. Running the identical job
    /// with the layout forced separates the two candidate gates — the layout and the PK-equality predicate — instead
    /// of leaving them entangled in one 5.4× spread.
    /// </summary>
    static bool MainFixedWidthOverride() =>
        Environment.GetEnvironmentVariable("SHARPCOREDB_MAIN_FIXEDWIDTH") == "1";

    /// <summary>
    /// Overrides <see cref="DatabaseConfig.FixedWidthInlineValueBytes"/> from <c>SHARPCOREDB_INLINE_BYTES</c>.
    /// Diagnostic only (plan §4b and §9 priority 1): a fixed-width record sends every variable-length value to the
    /// overflow arena unless its slot can hold the value inline, which is why forcing the layout on the PK-less
    /// document-CRUD job measured <em>worse</em> there. This switch quantifies that half of the package in isolation.
    /// Default 0 keeps the historical layout byte for byte, and the property is inert on legacy variable-length
    /// tables, so it only bites when <c>SHARPCOREDB_MAIN_FIXEDWIDTH</c> is also set.
    /// </summary>
    static int InlineBytesOverride()
    {
        var value = Environment.GetEnvironmentVariable("SHARPCOREDB_INLINE_BYTES");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }

    /// <summary>
    /// Focused multi-row <c>INSERT … VALUES (…), (…)</c> micro-benchmark. This statement shape used to lower
    /// to one <see cref="SharpCoreDB.DataStructures.Table.Insert"/> call per row — i.e. one standalone
    /// write-through append per row — and now routes to the batched core when the table has no per-row-only
    /// semantics. Each repetition runs on a fresh database so append-only growth cannot skew it; min, median
    /// and max are reported rather than a single run.
    /// </summary>
    static void RunMultiRowInsertMicroBenchmark()
    {
        const int inserts = 20_000;
        int reps = 5;

        // Rows per statement is the dimension that separates per-row cost from per-statement cost: at a
        // fixed row total, a constant per-row cost keeps rows/s flat, whereas anything super-linear in the
        // statement length shows up as big statements costing more per row. Overridable for that bracket.
        int rowsPerStatement = 1_000;
        var rowsEnv = Environment.GetEnvironmentVariable("SHARPCOREDB_MULTIROW_ROWS");
        if (int.TryParse(rowsEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRows) && parsedRows > 0)
        {
            rowsPerStatement = parsedRows;
        }

        // Diagnostic runs only need the stage report, not five timed reps of a deliberately slow shape.
        var repsEnv = Environment.GetEnvironmentVariable("SHARPCOREDB_MULTIROW_REPS");
        if (int.TryParse(repsEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedReps) && parsedReps > 0)
        {
            reps = parsedReps;
        }

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<DatabaseFactory>();
        // Diagnostic: force the per-record at-rest encryption posture for THIS mode only, so cost and
        // allocation can be attributed to it. Unset keeps the product default, so the headline number is
        // unchanged by this switch existing.
        static bool? MultiRowAtRestOverride() =>
            Environment.GetEnvironmentVariable("SHARPCOREDB_MULTIROW_ATREST") switch
            {
                "0" => false,
                "1" => true,
                _ => null,
            };

        var config = BuildConfig(SharpCoreDB.Interfaces.StorageEngineType.AppendOnly, atRestRecords: MultiRowAtRestOverride());

        var statements = BuildMultiRowInsertStatements(inserts, rowsPerStatement);
        double[] times = new double[reps];

        // Diagnostics: whether the overflow arena is actually used for this schema, and how big the two files
        // are. If the arena is in play, its per-value write cost is the leading explanation for the
        // validate-and-serialize stamp; if it is not, the cost is somewhere else entirely.
        static (long OvfBytes, long DataBytes) FileSizes(SharpCoreDB.Database db)
        {
            if (!db.TryGetTable("docs", out var t) || t is not SharpCoreDB.DataStructures.Table dt || string.IsNullOrEmpty(dt.DataFile))
            {
                return (0, 0);
            }

            var ovf = Path.ChangeExtension(dt.DataFile, ".ovf");
            return (File.Exists(ovf) ? new FileInfo(ovf).Length : 0,
                    File.Exists(dt.DataFile) ? new FileInfo(dt.DataFile).Length : 0);
        }

        double RunPass()
        {
            var path = Path.Combine(Path.GetTempPath(), $"scdb-multirow-{Guid.NewGuid()}");
            using (var db = (SharpCoreDB.Database)factory.Create(path, "pw", isReadOnly: false, config: config))
            {
                var before = FileSizes(db);
                db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
                db.ExecuteSQL(CreateDocsIndexSql);

                var sw = Stopwatch.StartNew();
                long allocBefore = GC.GetTotalAllocatedBytes(precise: false);
                int gen0Before = GC.CollectionCount(0);
                foreach (var stmt in statements)
                {
                    db.ExecuteSQL(stmt);
                }

                sw.Stop();
                var elapsed = sw.Elapsed.TotalSeconds;
                long allocBytes = GC.GetTotalAllocatedBytes(precise: false) - allocBefore;
                int gen0 = GC.CollectionCount(0) - gen0Before;
                var after = FileSizes(db);
                Console.WriteLine($"    [diag] data file {after.DataBytes:N0} B · overflow arena {after.OvfBytes:N0} B (before: {before.DataBytes:N0}/{before.OvfBytes:N0})");
                Console.WriteLine($"    [diag] allocated {allocBytes:N0} B ({allocBytes / Math.Max(1, inserts):N0} B/row) · gen0 collections {gen0}");
                try { Directory.Delete(path, true); } catch { /* best-effort temp-dir cleanup */ }
                return elapsed;
            }
        }

        for (int r = 0; r < reps; r++)
        {
            times[r] = RunPass();
        }

        Array.Sort(times);
        double median = times[reps / 2];

        Console.WriteLine();
        Console.WriteLine($"═══ Multi-row INSERT … VALUES micro-benchmark ({inserts:N0} rows, {rowsPerStatement:N0} rows/statement, {statements.Count} statements, median of {reps}) ═══");
        Console.WriteLine($"  rows/s (median) : {inserts / median:N0}");
        Console.WriteLine($"  min {times[0]:F3}s   median {median:F3}s   max {times[^1]:F3}s");
        Console.WriteLine($"  median µs/row   : {median * 1_000_000 / inserts:F2}");
        Console.WriteLine($"  median ms/statement : {median * 1000 / statements.Count:F2}");

        // One further, untimed pass with the write-path profiler on, so the stage breakdown for this exact
        // workload is available without perturbing the timings above.
        SharpCoreDB.Diagnostics.WritePathProfiler.Reset();
        SharpCoreDB.Diagnostics.WritePathProfiler.Enable();
        double profiled = RunPass();
        SharpCoreDB.Diagnostics.WritePathProfiler.Disable();
        Console.WriteLine();
        Console.WriteLine($"  profiled pass: {profiled:F3}s");
        Console.WriteLine(SharpCoreDB.Diagnostics.WritePathProfiler.Report());
    }

    /// <summary>
    /// Builds multi-row <c>INSERT … VALUES</c> statements carrying <paramref name="rowsPerStatement"/> tuples
    /// each. Built once, outside the timed region, because it is caller work.
    /// </summary>
    static List<string> BuildMultiRowInsertStatements(int totalRows, int rowsPerStatement)
    {
        var statements = new List<string>((totalRows / rowsPerStatement) + 1);
        var sb = new StringBuilder(rowsPerStatement * 96);

        for (int start = 0; start < totalRows; start += rowsPerStatement)
        {
            int count = Math.Min(rowsPerStatement, totalRows - start);
            sb.Clear();
            sb.Append("INSERT INTO docs (id, name, email, age, score, data) VALUES ");

            for (int i = 0; i < count; i++)
            {
                int id = start + i;
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('(').Append(id).Append(", 'User").Append(id)
                    .Append("', 'user").Append(id).Append("@example.com', ").Append(id % 100)
                    .Append(", ").Append(id).Append(".5, 'data-").Append(id).Append("')");
            }

            statements.Add(sb.ToString());
        }

        return statements;
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
                db.ExecuteSQL(CreateDocsIndexSql);

                // Build the statements once (outside the timed region — this is caller work,
                // identical for SQLite in the comparative benchmark).
                var stmtBatches = BuildSqlStatementBatches(inserts, batch);

                var sw = Stopwatch.StartNew();
                foreach (var stmts in stmtBatches)
                {
                    db.ExecuteBatchSQL(stmts);
                }

                sw.Stop();
                sqlTimes[r] = sw.Elapsed.TotalSeconds;
            }

            try { Directory.Delete(sqlPath, true); } catch { /* best-effort temp-dir cleanup */ }

            var directPath = Path.Combine(Path.GetTempPath(), $"scdb-insert-direct-{Guid.NewGuid()}");
            using (var db = (SharpCoreDB.Database)factory.Create(directPath, "pw", isReadOnly: false, config: config))
            {
                db.ExecuteSQL("CREATE TABLE docs (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
                db.ExecuteSQL(CreateDocsIndexSql);

                var rowBatches = BuildRowBatches(inserts, batch);

                var sw = Stopwatch.StartNew();
                foreach (var rows in rowBatches)
                {
                    db.InsertBatch("docs", rows);
                }

                sw.Stop();
                directTimes[r] = sw.Elapsed.TotalSeconds;
            }

            try { Directory.Delete(directPath, true); } catch { /* best-effort temp-dir cleanup */ }
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

    /// <summary>
    /// Builds the SQL INSERT statement batches for the insert micro-benchmark (outside the timed
    /// region — this is caller work, identical for SQLite in the comparative benchmark).
    /// </summary>
    static List<List<string>> BuildSqlStatementBatches(int inserts, int batch)
    {
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

        return stmtBatches;
    }

    /// <summary>
    /// Builds the Direct-API row batches for the insert micro-benchmark (outside the timed region).
    /// </summary>
    static List<List<Dictionary<string, object>>> BuildRowBatches(int inserts, int batch)
    {
        var rowBatches = new List<List<Dictionary<string, object>>>();
        for (int b = 0; b < inserts; b += batch)
        {
            var rows = new List<Dictionary<string, object>>(batch);
            for (int i = b; i < b + batch; i++)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["name"] = $"User{i}",
                    [EmailColumn] = $"user{i}@test.com",
                    ["age"] = 20 + i % 60,
                    [ScoreColumn] = i * 0.1,
                    ["data"] = $"payload-{i}",
                });
            }

            rowBatches.Add(rows);
        }

        return rowBatches;
    }

    static DatabaseConfig BuildConfig(
        SharpCoreDB.Interfaces.StorageEngineType engineType,
        bool fixedWidth = false,
        bool noEncrypt = true,
        bool? atRestRecords = null)
    {
        return new DatabaseConfig
        {
            NoEncryptMode = noEncrypt,
            // Per-record at-rest encryption of table payloads (the 8-byte magic header format).
            // null keeps whatever the PRODUCT default is (true since the 2026-09-13 flip; read from a
            // fresh instance so a benchmark arm can never drift from it), true/false force it. The old
            // "default" arm forced it OFF — a configuration no default database has had since the flip,
            // see docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md §3-1c/§3-1d.
            EnableAtRestRecordEncryption = atRestRecords ?? ProductAtRestDefault,
            StorageEngineType = engineType,
            // The fair PK comparison intentionally isolates the record-layout variable: the legacy
            // arm opts out of the AutoFixedWidthRecords default so it measures true variable-length
            // records; the fixed-width arm forces FixedWidthRecordLayout.
            AutoFixedWidthRecords = !fixedWidth,
            FixedWidthRecordLayout = fixedWidth,
            // §4b inline capacity — diagnostic (SHARPCOREDB_INLINE_BYTES). Off (0) by default so every published
            // number below keeps the historical layout.
            FixedWidthInlineValueBytes = InlineBytesOverride(),
            UseGroupCommitWal = false,
            EnableAdaptiveWalBatching = false,
            HighSpeedInsertMode = true,
            GroupCommitSize = 1000,
            // Durability mode only — since the 2026-09-16 reversal it no longer implies append buffering (see
            // Storage.BuffersAppends). The A/B that produced that reversal ran through this switch: the tuned
            // arm declared Async, so A1 had made it buffer appends too, and re-running the identical arm with
            // FullSync restored FW UPDATE to 356,135 from 230,722 (+54 %), DELETE to 216,909 from 168,804
            // (+28 %) and INSERT to 99,575 from 81,344 (+22 %), with SQLite's own reference within 5 %.
            // SHARPCOREDB_BUFFERED_APPENDS=1 (below) is now the switch that selects the appends-buffered
            // regime, so either posture can be measured without touching durability.
            WalDurabilityMode = Environment.GetEnvironmentVariable("SHARPCOREDB_WAL_DURABILITY") == "fullsync"
                ? SharpCoreDB.Services.DurabilityMode.FullSync
                : SharpCoreDB.Services.DurabilityMode.Async,
            EnablePageCache = true,
            PageCacheCapacity = 10_000,
            UseMemoryMapping = true,
            UseBufferedIO = true,
            EnableHashIndexes = true,
            // DELETE index maintenance (plan §7). Deferred maintenance is the PRODUCT DEFAULT since
            // v2.1, so this arm follows the default unless the env var explicitly opts out — set
            // SHARPCOREDB_DEFER_DELETE_INDEXES=0 to measure the eager behaviour.
            EnableDeferredDeleteIndexes = Environment.GetEnvironmentVariable("SHARPCOREDB_DEFER_DELETE_INDEXES") != "0",
            // Buffered appends (product default: off). This is the switch that decides whether an append —
            // in the table file or in the overflow arena — does a write-through file open per value or
            // shares one buffer, so the arms need to be able to turn it on. Set
            // SHARPCOREDB_BUFFERED_APPENDS=1 to measure it.
            EnableBufferedAppends = Environment.GetEnvironmentVariable("SHARPCOREDB_BUFFERED_APPENDS") == "1",
            // Query cache (product default: on). Keyed by statement text, so it only pays for itself when
            // statements repeat; set SHARPCOREDB_QUERY_CACHE=off to measure an all-miss workload (plan §11).
            EnableQueryCache = QueryCacheOverride(),
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

    /// <summary>
    /// The product's per-record at-rest encryption default, read from a fresh
    /// <see cref="DatabaseConfig"/> so a benchmark arm can never drift from it. It flipped to
    /// <see langword="true"/> on 2026-09-13 (plan §3-1c deliverable 2), and the arms report it.
    /// </summary>
    static bool ProductAtRestDefault => new DatabaseConfig().EnableAtRestRecordEncryption;

    /// <summary>
    /// Builds the variant DatabaseConfig used by the --pk-default arm. The variant name comes from
    /// SHARPCOREDB_PK_DEFAULT_VARIANT or the --pk-ab arm selector; "" is the pure default config.
    /// </summary>
    private static DatabaseConfig BuildPkDefaultVariantConfig(
        SharpCoreDB.Interfaces.StorageEngineType engineType,
        string? variant)
    {
        return variant switch
        {
            "async" => new DatabaseConfig { StorageEngineType = engineType, WalDurabilityMode = SharpCoreDB.Services.DurabilityMode.Async },
            "bufferedio" => new DatabaseConfig { StorageEngineType = engineType, UseBufferedIO = true },
            "novalidate" => new DatabaseConfig
            {
                StorageEngineType = engineType,
                SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled,
                StrictParameterValidation = false,
            },
            "noadaptive" => new DatabaseConfig { StorageEngineType = engineType, EnableAdaptiveWalBatching = false },
            "hsinsert" => new DatabaseConfig { StorageEngineType = engineType, HighSpeedInsertMode = true },
            // "plain" and "tuned" both select the tuned harness config built by BuildConfig; the
            // only difference is that "tuned" turns the NoEncryptMode flag off (isolating it).
            "plain" => BuildConfig(engineType, fixedWidth: true),
            "tuned" => BuildConfig(engineType, fixedWidth: true, noEncrypt: false),
            _ => new DatabaseConfig { StorageEngineType = engineType },
        };
    }

    static BenchmarkResult RunSharpCoreDB(SharpCoreDB.Interfaces.StorageEngineType engineType)
        => RunSharpCoreDbMode(engineType, noEncrypt: true, atRestRecords: null);

    /// <summary>
    /// Runs the SQL CRUD workload in one encryption configuration:
    /// <paramref name="noEncrypt"/> true is the raw-speed arm (<c>NoEncryptMode=true</c>, everything
    /// plaintext); false is an encrypted-metadata run, with
    /// <paramref name="atRestRecords"/> <see langword="null"/> keeping the PRODUCT default for
    /// per-record at-rest encryption (<see langword="true"/> since the 2026-09-13 flip) and an explicit
    /// <see langword="true"/>/<see langword="false"/> overriding it.
    /// </summary>
    static BenchmarkResult RunSharpCoreDbMode(
        SharpCoreDB.Interfaces.StorageEngineType engineType, bool noEncrypt, bool? atRestRecords)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench-sharpcoredb-{Guid.NewGuid()}");
        var result = new BenchmarkResult();

        try
        {
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredService<DatabaseFactory>();
            var config = BuildConfig(engineType, fixedWidth: MainFixedWidthOverride(), noEncrypt: noEncrypt, atRestRecords: atRestRecords);

            using var db = (SharpCoreDB.Database)factory.Create(
                dbPath: dbPath,
                masterPassword: BenchDbPassword,
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
            db.ExecuteSQL(CreateDocsIndexSql);

            // §9 priority-1 diagnostics: report the layout this table actually resolved to. The schema declares no
            // PK, so AutoFixedWidthRecords cannot apply and the arm runs legacy variable-length records unless
            // SHARPCOREDB_MAIN_FIXEDWIDTH=1 forced FixedWidthRecordLayout — which is the variable under test.
            if (db.TryGetTable("docs", out var layoutProbe) && layoutProbe is SharpCoreDB.DataStructures.Table probeTable)
            {
                Console.WriteLine(
                    $"    [diag] docs layout: IsFixedWidthRecords={probeTable.IsFixedWidthRecords} " +
                    $"(config FixedWidthRecordLayout={config.FixedWidthRecordLayout}, AutoFixedWidthRecords={config.AutoFixedWidthRecords})");
            }

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
                        [EmailColumn] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        [ScoreColumn] = i * 0.1,
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
                db.ExecuteQuery(SelectDocsByNameSql, new Dictionary<string, object?>
                {
                    [NameParam] = $"User{i}"
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
                masterPassword: BenchDbPassword,
                isReadOnly: false,
                config: config);

            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");

            db.ExecuteSQL(CreateDocsIndexSql);

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
                        [EmailColumn] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        [ScoreColumn] = i * 0.1,
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
                masterPassword: BenchDbPassword,
                isReadOnly: false,
                config: config);

            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");

            db.ExecuteSQL(CreateDocsIndexSql);

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
                        [EmailColumn] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        [ScoreColumn] = i * 0.1,
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
                var parameters = new Dictionary<string, object?> { [NameParam] = $"User{i}" };
                int matched = 0;
                foreach (var row in db.ExecuteQueryStruct(SelectDocsByNameSql, parameters))
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
                    var pName = cmd.CreateParameter(); pName.ParameterName = NameParam; pName.Value = $"User{i}"; cmd.Parameters.Add(pName);
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
    // PK-based "fair usage" SharpCoreDB scenario
    // ══════════════════════════════════════

    /// <summary>
    /// SharpCoreDB on the same schema/API shape SQLite gets in the harness: an
    /// <c>id INTEGER PRIMARY KEY</c> table, batched inserts, and UPDATE/DELETE by primary key via
    /// <c>ExecuteBatchSQL</c> (single transaction). This exercises the PK B-tree fast paths and the
    /// recommended usage; the no-PK harness scenario above under-measures the engine on DML.
    /// </summary>
    static BenchmarkResult RunSharpCoreDBPk(
        SharpCoreDB.Interfaces.StorageEngineType engineType,
        bool fixedWidth = false,
        bool useDefaultConfig = false,
        string? defaultVariant = null,
        bool noEncrypt = true,
        bool? atRestRecords = null,
        bool profileUpdateArm = false,
        bool profileDeleteArm = false)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"bench-sharpcoredb-pk-{Guid.NewGuid()}");
        var result = new BenchmarkResult();

        try
        {
            var services = new ServiceCollection();
            services.AddSharpCoreDB();
            var sp = services.BuildServiceProvider();

            var factory = sp.GetRequiredService<DatabaseFactory>();
            DatabaseConfig config;
            if (useDefaultConfig)
            {
                var variant = defaultVariant
                    ?? Environment.GetEnvironmentVariable("SHARPCOREDB_PK_DEFAULT_VARIANT")?.ToLowerInvariant();
                config = BuildPkDefaultVariantConfig(engineType, variant);
            }
            else
            {
                config = BuildConfig(engineType, fixedWidth, noEncrypt, atRestRecords);
            }

            using var db = (SharpCoreDB.Database)factory.Create(
                dbPath: dbPath,
                masterPassword: BenchDbPassword,
                isReadOnly: false,
                config: config);

            db.ExecuteSQL(@"CREATE TABLE docs (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");
            db.ExecuteSQL(CreateDocsIndexSql);

            // INSERT (batched via InsertBatch with explicit ids, mirroring SQLite's rowid 1..N)
            var sw = Stopwatch.StartNew();
            for (int batch = 0; batch < InsertCount; batch += BatchSize)
            {
                int end = Math.Min(batch + BatchSize, InsertCount);
                var rows = new List<Dictionary<string, object>>(end - batch);
                for (int i = batch; i < end; i++)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["id"] = i + 1,
                        ["name"] = $"User{i}",
                        [EmailColumn] = $"user{i}@test.com",
                        ["age"] = 20 + i % 60,
                        [ScoreColumn] = i * 0.1,
                        ["data"] = $"payload-{i}",
                    });
                }

                db.InsertBatch("docs", rows);
            }

            db.Flush();
            sw.Stop();
            result.InsertTime = sw.Elapsed.TotalSeconds;
            result.InsertOpsPerSec = (int)(InsertCount / result.InsertTime);
            Console.WriteLine($"  INSERT {InsertCount:N0}: {result.InsertTime:F2}s ({result.InsertOpsPerSec:N0} ops/sec)");

            // READ by PK
            sw.Restart();
            for (int i = 1; i <= ReadCount; i++)
            {
                db.ExecuteQuery("SELECT * FROM docs WHERE id = @id", new Dictionary<string, object?> { ["@id"] = i });
            }

            sw.Stop();
            result.ReadTime = sw.Elapsed.TotalSeconds;
            result.ReadOpsPerSec = (int)(ReadCount / result.ReadTime);
            Console.WriteLine($"  READ   {ReadCount:N0}: {result.ReadTime:F2}s ({result.ReadOpsPerSec:N0} ops/sec)");

            // UPDATE by PK (single ExecuteBatchSQL transaction, like SQLite's single tx)
            // --pk-profile turns the profiler on for THIS arm only: Reset clears whatever the INSERT/READ
            // arms recorded (they run with it off unless the environment variable is set), so the printed
            // report describes the UPDATE batch and nothing else. The timed arms never take this branch.
            if (profileUpdateArm)
            {
                SharpCoreDB.Diagnostics.WritePathProfiler.Reset();
                SharpCoreDB.Diagnostics.WritePathProfiler.Enable();
            }

            sw.Restart();
            var updateStmts = new List<string>(UpdateCount);
            for (int i = 1; i <= UpdateCount; i++)
            {
                updateStmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = {0:F1} WHERE id = {1}", i * 99.9, i));
            }

            db.ExecuteBatchSQL(updateStmts);
            db.Flush();
            sw.Stop();
            result.UpdateTime = sw.Elapsed.TotalSeconds;
            result.UpdateOpsPerSec = (int)(UpdateCount / result.UpdateTime);
            Console.WriteLine($"  UPDATE {UpdateCount:N0}: {result.UpdateTime:F2}s ({result.UpdateOpsPerSec:N0} ops/sec)");

            if (profileUpdateArm)
            {
                SharpCoreDB.Diagnostics.WritePathProfiler.Disable();
                Console.WriteLine();
                Console.WriteLine($"  profiled UPDATE pass: {result.UpdateTime:F2}s "
                    + $"({result.UpdateOpsPerSec:N0} ops/sec, {result.UpdateTime * 1_000_000 / UpdateCount:F2} µs/update)");
                Console.WriteLine(SharpCoreDB.Diagnostics.WritePathProfiler.Report());
            }

            // DELETE by PK
            // --pk-profile-delete turns the profiler on for THIS arm only, exactly as --pk-profile does for
            // UPDATE, and Reset clears the INSERT/READ/UPDATE stamps so the report describes the DELETE batch
            // and nothing else. Plan §9 priority 1 is this column (0.51× SQLite), and the call counts are the
            // question: the contiguous fixed-width delete fast path buffers its tombstones in ONE stamped
            // region, so a single `engine-write` call means the fast path ran for the whole batch while 10,000
            // means it fell through to the per-statement generic route.
            if (profileDeleteArm)
            {
                SharpCoreDB.Diagnostics.WritePathProfiler.Reset();
                SharpCoreDB.Diagnostics.WritePathProfiler.Enable();
            }

            sw.Restart();
            var deleteStmts = new List<string>(DeleteCount);
            for (int i = 1; i <= DeleteCount; i++)
            {
                deleteStmts.Add($"DELETE FROM docs WHERE id = {i}");
            }

            db.ExecuteBatchSQL(deleteStmts);
            db.Flush();
            sw.Stop();
            result.DeleteTime = sw.Elapsed.TotalSeconds;
            result.DeleteOpsPerSec = (int)(DeleteCount / result.DeleteTime);
            Console.WriteLine($"  DELETE {DeleteCount:N0}: {result.DeleteTime:F2}s ({result.DeleteOpsPerSec:N0} ops/sec)");

            if (profileDeleteArm)
            {
                SharpCoreDB.Diagnostics.WritePathProfiler.Disable();
                Console.WriteLine();
                Console.WriteLine($"  profiled DELETE pass: {result.DeleteTime:F2}s "
                    + $"({result.DeleteOpsPerSec:N0} ops/sec, {result.DeleteTime * 1_000_000 / DeleteCount:F2} µs/delete)");
                Console.WriteLine(SharpCoreDB.Diagnostics.WritePathProfiler.Report());
            }
        }
        finally
        {
            try { if (Directory.Exists(dbPath)) Directory.Delete(dbPath, true); } catch { /* temp */ }
        }

        return result;
    }

    /// <summary>
    /// D1: runs a fair-PK arm several times and reports the per-phase median time so the printed
    /// ops/s is robust to run-to-run noise (single-shot arms fluctuated by roughly ±30%).
    /// </summary>
    private static BenchmarkResult RunPkMedian(Func<BenchmarkResult> arm, int reps = 3)
    {
        // Allow quick single-shot profiling via SHARPCOREDB_BENCH_REPS (diagnostic only).
        if (int.TryParse(Environment.GetEnvironmentVariable("SHARPCOREDB_BENCH_REPS"), out int envReps) && envReps > 0)
        {
            reps = envReps;
        }

        var runs = new List<BenchmarkResult>(reps);
        for (int r = 0; r < reps; r++)
        {
            runs.Add(arm());
        }

        static double MedianOf(IEnumerable<BenchmarkResult> xs, Func<BenchmarkResult, double> select)
        {
            var sorted = xs.Select(select).Where(x => x > 0).OrderBy(x => x).ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }

        var median = new BenchmarkResult
        {
            InsertTime = MedianOf(runs, static r => r.InsertTime),
            ReadTime = MedianOf(runs, static r => r.ReadTime),
            UpdateTime = MedianOf(runs, static r => r.UpdateTime),
            DeleteTime = MedianOf(runs, static r => r.DeleteTime),
        };

        median.InsertOpsPerSec = median.InsertTime > 0 ? (int)(InsertCount / median.InsertTime) : 0;
        median.ReadOpsPerSec = median.ReadTime > 0 ? (int)(ReadCount / median.ReadTime) : 0;
        median.UpdateOpsPerSec = median.UpdateTime > 0 ? (int)(UpdateCount / median.UpdateTime) : 0;
        median.DeleteOpsPerSec = median.DeleteTime > 0 ? (int)(DeleteCount / median.DeleteTime) : 0;
        return median;
    }

    /// <summary>
    /// Runs the fair PK scenario (SharpCoreDB vs SQLite) and prints the comparison.
    /// </summary>
    /// <summary>
    /// Plan §6 (B2): prints the write-path profiler's stage report for the exact UPDATE arm the `--pk`
    /// parity table is measured on — the same schema, the same fixed-width plaintext arm, the same 10,000
    /// <c>UPDATE … WHERE id = ?</c> statements in one <c>ExecuteBatchSQL</c> transaction. No timed reps: the
    /// question is where the ~34 µs/update goes, not how much of it there is, and the per-stage call counts
    /// are what turn a plausible story into an attributable one.
    /// </summary>
    static void RunPkUpdateProfile(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var engineLabel = engineType == SharpCoreDB.Interfaces.StorageEngineType.PageBased ? "PageBased" : "AppendOnly";
        Console.WriteLine($"═══ UPDATE-arm stage profile: {engineLabel}, fixed-width plaintext (the --pk parity arm) ═══");
        Console.WriteLine($"    {UpdateCount:N0} UPDATE … WHERE id = ? statements in ONE ExecuteBatchSQL transaction, over {InsertCount:N0} rows");
        Console.WriteLine();

        var result = RunSharpCoreDBPk(engineType, fixedWidth: true, profileUpdateArm: true);

        Console.WriteLine();
        Console.WriteLine($"  UPDATE: {result.UpdateOpsPerSec:N0} ops/sec ({result.UpdateTime * 1_000_000 / UpdateCount:F2} µs/update)");
        Console.WriteLine("  Run the same command with --engine=<the other engine> to read the two reports side by side.");
    }

    /// <summary>
    /// Plan §9 priority 1: prints the write-path profiler's stage report for the exact DELETE arm the `--pk`
    /// parity table is measured on — the same schema, the same fixed-width plaintext arm, the same 10,000
    /// <c>DELETE FROM docs WHERE id = ?</c> statements in one <c>ExecuteBatchSQL</c> transaction. The call
    /// counts carry the answer: <c>TryBulkDeleteContiguousFixedWidth</c> buffers its tombstones in a single
    /// stamped region, so <b>one</b> <c>engine-write</c> call means the contiguous fast path ran once for the
    /// whole batch, while <b>10,000</b> means the batch fell through to the per-statement generic route.
    /// </summary>
    static void RunPkDeleteProfile(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var engineLabel = engineType == SharpCoreDB.Interfaces.StorageEngineType.PageBased ? "PageBased" : "AppendOnly";
        Console.WriteLine($"═══ DELETE-arm stage profile: {engineLabel}, fixed-width plaintext (the --pk parity arm) ═══");
        Console.WriteLine($"    {DeleteCount:N0} DELETE FROM docs WHERE id = ? statements in ONE ExecuteBatchSQL transaction, over {InsertCount:N0} rows");
        Console.WriteLine();

        var result = RunSharpCoreDBPk(engineType, fixedWidth: true, profileDeleteArm: true);

        Console.WriteLine();
        Console.WriteLine($"  DELETE: {result.DeleteOpsPerSec:N0} ops/sec ({result.DeleteTime * 1_000_000 / DeleteCount:F2} µs/delete)");
        Console.WriteLine("  One engine-write call = the contiguous fast path ran once for the batch; 10,000 = per-statement fallback.");
    }

    static void RunPkComparison(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var engineLabel = engineType == SharpCoreDB.Interfaces.StorageEngineType.PageBased ? "PageBased" : "AppendOnly";
        Console.WriteLine(BannerTop);
        Console.WriteLine("║  Fair PK comparison: SharpCoreDB vs SQLite              ║");
        Console.WriteLine("║  (id INTEGER PRIMARY KEY, UPDATE/DELETE by PK)           ║");
        Console.WriteLine(BannerBottom);
        Console.WriteLine();
        Console.WriteLine($"Engine: {engineLabel}");
        Console.WriteLine("(fair-PK arms run 3x; median time per phase is reported)");

        Console.WriteLine("━━━ SharpCoreDB (SQL, PK, legacy variable-length, plaintext) ━━━");
        var scdb = RunPkMedian(() => RunSharpCoreDBPk(engineType));
        Console.WriteLine();

        Console.WriteLine("━━━ SharpCoreDB (SQL, PK, fixed-width, plaintext) ━━━");
        var scdbFw = RunPkMedian(() => RunSharpCoreDBPk(engineType, fixedWidth: true));
        Console.WriteLine();

        // §0.1-6: every target table must carry the encryption posture beside the plaintext number. The
        // arms above are NoEncryptMode=true (BuildConfig's default); this arm is the PRODUCT default
        // posture (at-rest records on, read from a fresh DatabaseConfig so it cannot drift).
        Console.WriteLine("━━━ SharpCoreDB (SQL, PK, fixed-width, at-rest default) ━━━");
        var scdbFwAtRest = RunPkMedian(() => RunSharpCoreDBPk(engineType, fixedWidth: true, noEncrypt: false));
        Console.WriteLine();

        Console.WriteLine("━━━ SQLite (reference) ━━━");
        var sqlite = RunPkMedian(() => RunSQLite());
        Console.WriteLine();

        Console.WriteLine("║ Database                     │ INSERT     │ READ     │ UPDATE   │ DELETE   ║");
        Console.WriteLine($"║ SharpCoreDB legacy  plaintext│ {scdb.InsertOpsPerSec,10:N0} │ {scdb.ReadOpsPerSec,8:N0} │ {scdb.UpdateOpsPerSec,8:N0} │ {scdb.DeleteOpsPerSec,8:N0} ║");
        Console.WriteLine($"║ SharpCoreDB FW      plaintext│ {scdbFw.InsertOpsPerSec,10:N0} │ {scdbFw.ReadOpsPerSec,8:N0} │ {scdbFw.UpdateOpsPerSec,8:N0} │ {scdbFw.DeleteOpsPerSec,8:N0} ║");
        Console.WriteLine($"║ SharpCoreDB FW      at-rest  │ {scdbFwAtRest.InsertOpsPerSec,10:N0} │ {scdbFwAtRest.ReadOpsPerSec,8:N0} │ {scdbFwAtRest.UpdateOpsPerSec,8:N0} │ {scdbFwAtRest.DeleteOpsPerSec,8:N0} ║");
        Console.WriteLine($"║ SQLite                       │ {sqlite.InsertOpsPerSec,10:N0} │ {sqlite.ReadOpsPerSec,8:N0} │ {sqlite.UpdateOpsPerSec,8:N0} │ {sqlite.DeleteOpsPerSec,8:N0} ║");
        Console.WriteLine($"\n  UPDATE gap vs SQLite: legacy {sqlite.UpdateOpsPerSec / (double)scdb.UpdateOpsPerSec:F1}x   "
            + $"fixed-width {sqlite.UpdateOpsPerSec / (double)scdbFw.UpdateOpsPerSec:F1}x   "
            + $"fixed-width at-rest {sqlite.UpdateOpsPerSec / (double)scdbFwAtRest.UpdateOpsPerSec:F1}x");
        Console.WriteLine($"  DELETE gap vs SQLite: legacy {sqlite.DeleteOpsPerSec / (double)scdb.DeleteOpsPerSec:F1}x   "
            + $"fixed-width {sqlite.DeleteOpsPerSec / (double)scdbFw.DeleteOpsPerSec:F1}x   "
            + $"fixed-width at-rest {sqlite.DeleteOpsPerSec / (double)scdbFwAtRest.DeleteOpsPerSec:F1}x");
        Console.WriteLine($"  INSERT gap vs SQLite: legacy {sqlite.InsertOpsPerSec / (double)scdb.InsertOpsPerSec:F1}x   "
            + $"fixed-width {sqlite.InsertOpsPerSec / (double)scdbFw.InsertOpsPerSec:F1}x   "
            + $"fixed-width at-rest {sqlite.InsertOpsPerSec / (double)scdbFwAtRest.InsertOpsPerSec:F1}x");
        Console.WriteLine($"  At-rest tax (same arm shape): INSERT {scdbFw.InsertOpsPerSec / (double)scdbFwAtRest.InsertOpsPerSec:F2}x   "
            + $"READ {scdbFw.ReadOpsPerSec / (double)scdbFwAtRest.ReadOpsPerSec:F2}x   "
            + $"UPDATE {scdbFw.UpdateOpsPerSec / (double)scdbFwAtRest.UpdateOpsPerSec:F2}x   "
            + $"DELETE {scdbFw.DeleteOpsPerSec / (double)scdbFwAtRest.DeleteOpsPerSec:F2}x");

        var results = new Dictionary<string, BenchmarkResult>
        {
            ["SharpCoreDB (SQL, PK, legacy, plaintext)"] = scdb,
            ["SharpCoreDB (SQL, PK, fixed-width, plaintext)"] = scdbFw,
            ["SharpCoreDB (SQL, PK, fixed-width, at-rest default)"] = scdbFwAtRest,
            ["SQLite"] = sqlite,
        };

        var dir = ResultsDirName;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"pk_comparative_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nResults saved to: {path}");
    }

    /// <summary>
    /// P3 hardening: fair PK comparison where the SharpCoreDB arm uses a PURE default
    /// <see cref="DatabaseConfig"/> (no NoEncryptMode / page-cache / query-cache harness flags) —
    /// only the engine type is pinned to the runner's engine. Proves the out-of-the-box default
    /// path (Columnar + fixed-width PK tables) engages the fast paths against SQLite.
    /// </summary>
    static void RunPkDefaultComparison(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var engineLabel = engineType == SharpCoreDB.Interfaces.StorageEngineType.PageBased ? "PageBased" : "AppendOnly";
        Console.WriteLine(BannerTop);
        Console.WriteLine("║  Fair PK with DEFAULT DatabaseConfig vs SQLite           ║");
        Console.WriteLine("║  (NoEncryptMode=false; no harness-only performance flags)║");
        Console.WriteLine(BannerBottom);
        Console.WriteLine();
        Console.WriteLine($"Engine: {engineLabel}");
        Console.WriteLine("(median of 3 per phase)");

        Console.WriteLine("━━━ SharpCoreDB (SQL, PK, default config = Columnar fixed-width) ━━━");
        var scdb = RunPkMedian(() => RunSharpCoreDBPk(engineType, useDefaultConfig: true));
        Console.WriteLine();

        Console.WriteLine("━━━ SQLite (reference) ━━━");
        var sqlite = RunPkMedian(() => RunSQLite());
        Console.WriteLine();

        Console.WriteLine("║ Database      │ INSERT     │ READ     │ UPDATE   │ DELETE   ║");
        Console.WriteLine($"║ SharpCoreDB   │ {scdb.InsertOpsPerSec,10:N0} │ {scdb.ReadOpsPerSec,8:N0} │ {scdb.UpdateOpsPerSec,8:N0} │ {scdb.DeleteOpsPerSec,8:N0} ║");
        Console.WriteLine($"║ SQLite        │ {sqlite.InsertOpsPerSec,10:N0} │ {sqlite.ReadOpsPerSec,8:N0} │ {sqlite.UpdateOpsPerSec,8:N0} │ {sqlite.DeleteOpsPerSec,8:N0} ║");
        Console.WriteLine($"\n  UPDATE gap: SQLite vs default-config {sqlite.UpdateOpsPerSec / (double)scdb.UpdateOpsPerSec:F1}x");
        Console.WriteLine($"  DELETE gap: SQLite vs default-config {sqlite.DeleteOpsPerSec / (double)scdb.DeleteOpsPerSec:F1}x");

        var results = new Dictionary<string, BenchmarkResult>
        {
            ["SharpCoreDB (SQL, PK, default config)"] = scdb,
            ["SQLite"] = sqlite,
        };

        var dir = ResultsDirName;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"pk_default_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nResults saved to: {path}");
    }

    /// <summary>
    /// P3c: same-window interleaved A/B comparison of two default-config variants. Each rep runs
    /// arm A then arm B back-to-back, and the result reports the per-rep paired ratio B/A per phase
    /// (median), so slow-machine windows affect both arms of a pair and cancel out.
    /// </summary>
    static void RunPkAbComparison(SharpCoreDB.Interfaces.StorageEngineType engineType)
    {
        var armA = Environment.GetEnvironmentVariable("SHARPCOREDB_PK_AB_ARM_A")?.ToLowerInvariant() ?? string.Empty;
        var armB = Environment.GetEnvironmentVariable("SHARPCOREDB_PK_AB_ARM_B")?.ToLowerInvariant() ?? "plain";
        int reps = 3;
        if (int.TryParse(Environment.GetEnvironmentVariable("SHARPCOREDB_BENCH_REPS"), out int envReps) && envReps > 0)
        {
            reps = envReps;
        }

        Console.WriteLine(BannerTop);
        Console.WriteLine("║  Same-window interleaved A/B (default-config variants)    ║");
        Console.WriteLine(BannerBottom);
        Console.WriteLine();
        Console.WriteLine($"Arm A variant: '{armA}'   Arm B variant: '{armB}'   reps: {reps}");
        Console.WriteLine("(each rep: A then B back-to-back; per-rep ratios cancel drift)");
        Console.WriteLine();

        var listA = new List<BenchmarkResult>(reps);
        var listB = new List<BenchmarkResult>(reps);
        for (int r = 0; r < reps; r++)
        {
            Console.WriteLine($"── rep {r + 1}/{reps} · A='{armA}' ──");
            listA.Add(RunSharpCoreDBPk(engineType, useDefaultConfig: true, defaultVariant: armA));
            Console.WriteLine($"── rep {r + 1}/{reps} · B='{armB}' ──");
            listB.Add(RunSharpCoreDBPk(engineType, useDefaultConfig: true, defaultVariant: armB));
        }

        static double Med(IEnumerable<double> xs)
        {
            var sorted = xs.Where(x => x > 0).OrderBy(x => x).ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }

        static int Ops(IEnumerable<BenchmarkResult> runs, Func<BenchmarkResult, int> select)
        {
            var arr = runs.Select(select).Where(x => x > 0).OrderBy(x => x).ToArray();
            return arr.Length == 0 ? 0 : arr[arr.Length / 2];
        }

        static double Ratio(IEnumerable<BenchmarkResult> a, IEnumerable<BenchmarkResult> b, Func<BenchmarkResult, int> sel)
        {
            var ratios = a.Zip(b, (x, y) => sel(x) > 0 ? sel(y) / (double)sel(x) : 0.0);
            return Med(ratios);
        }

        var aUpdate = Ops(listA, static r => r.UpdateOpsPerSec);
        var bUpdate = Ops(listB, static r => r.UpdateOpsPerSec);
        var aDelete = Ops(listA, static r => r.DeleteOpsPerSec);
        var bDelete = Ops(listB, static r => r.DeleteOpsPerSec);

        Console.WriteLine("║ phase   │ A median ops/s │ B median ops/s │ median B/A (per rep) ║");
        Console.WriteLine($"║ UPDATE  │ {aUpdate,13:N0} │ {bUpdate,14:N0} │ {Ratio(listA, listB, static r => r.UpdateOpsPerSec):F2}x");
        Console.WriteLine($"║ DELETE  │ {aDelete,13:N0} │ {bDelete,14:N0} │ {Ratio(listA, listB, static r => r.DeleteOpsPerSec):F2}x");
        Console.WriteLine($"║ INSERT  │ {Ops(listA, static r => r.InsertOpsPerSec),13:N0} │ {Ops(listB, static r => r.InsertOpsPerSec),14:N0} │ {Ratio(listA, listB, static r => r.InsertOpsPerSec):F2}x");
        Console.WriteLine($"║ READ    │ {Ops(listA, static r => r.ReadOpsPerSec),13:N0} │ {Ops(listB, static r => r.ReadOpsPerSec),14:N0} │ {Ratio(listA, listB, static r => r.ReadOpsPerSec):F2}x");

        var summary = new Dictionary<string, object>
        {
            ["armA"] = armA,
            ["armB"] = armB,
            ["medianUpdateRatio_B_over_A"] = Ratio(listA, listB, static r => r.UpdateOpsPerSec),
            ["medianDeleteRatio_B_over_A"] = Ratio(listA, listB, static r => r.DeleteOpsPerSec),
            ["medianInsertRatio_B_over_A"] = Ratio(listA, listB, static r => r.InsertOpsPerSec),
            ["medianReadRatio_B_over_A"] = Ratio(listA, listB, static r => r.ReadOpsPerSec),
        };

        var dir = ResultsDirName;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"pk_ab_{armA}_{armB}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"\nResults saved to: {path}");
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
    /// <summary>
    /// Runs the CRUD workload in every encryption configuration and prints the columns side by side.
    /// Medians over <paramref name="reps"/> runs per arm, with the arm order alternated per rep so a
    /// warming machine cannot systematically favour one configuration (the <c>--pk-ab</c> pattern).
    /// </summary>
    /// <remarks>
    /// TWO arms since the 2026-09-13 flip, labelled literally: <c>raw</c> is
    /// <c>NoEncryptMode=true</c> (the documented opt-out, every file plaintext) and <c>default</c> is
    /// the product default, which now encrypts table records as well as metadata. Before the flip the
    /// default stored table records as PLAINTEXT while still encrypting metadata; that configuration is
    /// now only reachable by forcing <c>EnableAtRestRecordEncryption=false</c>, so it is no longer an
    /// arm — the §3-1c audit table stays as the historical record. Publish the columns together or not
    /// at all.
    /// </remarks>
    static void RunDualModeComparison(SharpCoreDB.Interfaces.StorageEngineType engineType, int reps = 3)
    {
        const int Failed = -1;
        Console.WriteLine();
        Console.WriteLine(BannerTop);
        Console.WriteLine("║ Encryption-mode comparison — one workload, two configurations          ║");
        Console.WriteLine(BannerBottom);
        Console.WriteLine("    raw      NoEncryptMode=true              (opt-out; plaintext everywhere)");
        Console.WriteLine("    default  product default                 (table data encrypted)");
        Console.WriteLine($"    product default: NoEncryptMode=false, EnableAtRestRecordEncryption={ProductAtRestDefault}");
        Console.WriteLine($"  engine={engineType} · inserts={InsertCount:N0} · reads/updates/deletes={ReadCount:N0} each · reps={reps}");
        Console.WriteLine();

        var raw = new List<BenchmarkResult>();
        var deflt = new List<BenchmarkResult>();

        for (int rep = 0; rep < reps; rep++)
        {
            // Alternate the order per rep: machine drift then affects both arms, not just one.
            if (rep % 2 == 0)
            {
                deflt.Add(RunArm(engineType, noEncrypt: false, atRestRecords: null, "default", Failed));
                raw.Add(RunArm(engineType, noEncrypt: true, atRestRecords: null, "raw", Failed));
            }
            else
            {
                raw.Add(RunArm(engineType, noEncrypt: true, atRestRecords: null, "raw", Failed));
                deflt.Add(RunArm(engineType, noEncrypt: false, atRestRecords: null, "default", Failed));
            }

            Console.WriteLine($"     rep {rep + 1}/{reps} complete");
        }

        Console.WriteLine();
        Console.WriteLine($"  {"operation",-10}{"raw",13}{"default",13}{"raw/default",14}");
        PrintModeRow("INSERT", raw, deflt, static r => r.InsertOpsPerSec, Failed);
        PrintModeRow("READ", raw, deflt, static r => r.ReadOpsPerSec, Failed);
        PrintModeRow("UPDATE", raw, deflt, static r => r.UpdateOpsPerSec, Failed);
        PrintModeRow("DELETE", raw, deflt, static r => r.DeleteOpsPerSec, Failed);
        Console.WriteLine();
        Console.WriteLine("  /default is the multiplier paid for the encrypted default versus the opt-out arm.");
        Console.WriteLine("  FAILED means that configuration could not complete the workload (message printed above).");

        try
        {
            var payload = new
            {
                timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                engine = engineType.ToString(),
                reps,
                inserts = InsertCount,
                reads = ReadCount,
                updates = UpdateCount,
                deletes = DeleteCount,
                arms = new
                {
                    raw = "NoEncryptMode=true (opt-out; plaintext everywhere)",
                    @default = $"product default (EnableAtRestRecordEncryption={ProductAtRestDefault})",
                },
                raw = raw.Select(ToRecord).ToList(),
                @default = deflt.Select(ToRecord).ToList(),
            };

            // Anchor the archive at the PROJECT directory, not the process CWD: `dotnet run` may be
            // invoked from the repo root, and the project's results/ folder is the tracked evidence
            // location (the comparative_*.json runs live there).
            string projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            string resultsDir = Path.Combine(projectDir, ResultsDirName);
            Directory.CreateDirectory(resultsDir);
            string path = Path.Combine(resultsDir, $"dual-mode-{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"  JSON archived: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (could not archive JSON: {ex.Message})");
        }
    }

    static object ToRecord(BenchmarkResult r) => new
    {
        r.InsertOpsPerSec,
        r.ReadOpsPerSec,
        r.UpdateOpsPerSec,
        r.DeleteOpsPerSec,
    };

    // ══════════════════════════════════════
    // §2.4 — the write-path regression gate
    // ══════════════════════════════════════

    /// <summary>
    /// The baseline shape written by <c>--write-baseline</c>. It is deliberately the same shape the
    /// <c>--dual-mode</c> archive already uses (two arm arrays of per-operation ops/sec), so any archived
    /// dual-mode run can also serve as a baseline via <c>--gate-baseline=&lt;path&gt;</c>.
    /// </summary>
    sealed class GateBaseline
    {
        public List<BenchmarkResult> Raw { get; set; } = [];
        public List<BenchmarkResult> Default { get; set; } = [];
    }

    /// <summary>
    /// The §2.4 regression gate: runs the accepted §2 protocol (the dual-mode arms, medians over
    /// alternating reps) and compares every operation against a committed baseline, returning non-zero
    /// when one has regressed beyond the tolerance factor.
    /// <para>
    /// It exists because a silent write-path regression has already shipped twice on this branch: the
    /// UPDATE cost between 2.0 and 2.1 moved inside the noise band with nothing to catch it, and the first
    /// version of the deferred-DELETE reconcile made random-key DELETE **4× slower** (294,185 → 70,248
    /// ops/sec) while all 2,321 tests stayed green. Neither was visible without running this harness by hand.
    /// </para>
    /// <para>
    /// The tolerance is deliberately generous. The machine's documented run-to-run band is ±20%, so a factor
    /// near 1 would fire on noise; 1.5× still catches the regressions worth catching (both of the above were
    /// ≥2×) without making the gate a coin flip. A failure means "look on a quiet machine", not "revert
    /// immediately" — which is why this job is non-gating.
    /// </para>
    /// <para>
    /// Options: <c>--gate-factor=&lt;double&gt;</c>, <c>--gate-baseline=&lt;path&gt;</c>, and
    /// <c>--write-baseline</c> (re-records the committed baseline — an explicit, reviewable act, never
    /// automatic).
    /// </para>
    /// </summary>
    static int RunRegressionGate(
        SharpCoreDB.Interfaces.StorageEngineType engineType,
        double factor,
        string? baselineArg,
        bool writeBaseline,
        int reps = 3)
    {
        const int Failed = -1;
        const string RawArm = "raw";
        const string DefaultArm = "default";

        string projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        string baselinePath = baselineArg is null
            ? Path.Combine(projectDir, GateBaselineFolder, GateBaselineFile)
            : Path.GetFullPath(baselineArg);

        Console.WriteLine();
        Console.WriteLine("━━━ Write-path regression gate (§2.4) ━━━");
        Console.WriteLine($"  engine={engineType} · reps={reps} · tolerance={factor:0.00}x");
        Console.WriteLine(writeBaseline
            ? $"  recording baseline: {baselinePath}"
            : $"  baseline:           {baselinePath}");
        Console.WriteLine();

        // The §2 protocol: the same alternating-rep arms the dual-mode comparison uses, so the two
        // artefacts stay directly comparable and any archived dual-mode JSON is a valid baseline.
        var raw = new List<BenchmarkResult>();
        var deflt = new List<BenchmarkResult>();
        for (int rep = 0; rep < reps; rep++)
        {
            if (rep % 2 == 0)
            {
                deflt.Add(RunArm(engineType, noEncrypt: false, atRestRecords: null, DefaultArm, Failed));
                raw.Add(RunArm(engineType, noEncrypt: true, atRestRecords: null, RawArm, Failed));
            }
            else
            {
                raw.Add(RunArm(engineType, noEncrypt: true, atRestRecords: null, RawArm, Failed));
                deflt.Add(RunArm(engineType, noEncrypt: false, atRestRecords: null, DefaultArm, Failed));
            }

            Console.WriteLine($"     rep {rep + 1}/{reps} complete");
        }

        if (raw.Any(static r => r.InsertOpsPerSec <= 0) || deflt.Any(static r => r.InsertOpsPerSec <= 0))
        {
            Console.WriteLine();
            Console.WriteLine("  GATE FAILED: an arm could not complete the workload (see the message above).");
            return GateExitRegression;
        }

        var currentRaw = MedianOf(raw);
        var currentDefault = MedianOf(deflt);

        // §2 requires min–median–max, never a single run — and a run whose own reps disagree wildly
        // cannot support any verdict, however it compares. So the spread is measured and reported first.
        double worstSpread = Math.Max(MaxSpread(raw), MaxSpread(deflt));
        Console.WriteLine();
        Console.WriteLine($"  rep spread (max ÷ min across the {reps} reps — the run's own noise):");
        Console.WriteLine($"    raw     {SpreadLine(raw)}");
        Console.WriteLine($"    default {SpreadLine(deflt)}");
        Console.WriteLine($"    worst   {worstSpread:F2}x   (a quiet machine sits near 1.0)");

        if (worstSpread > GateNoisySpread)
        {
            Console.WriteLine();
            Console.WriteLine($"  GATE INCONCLUSIVE (exit {GateExitInconclusive}): the reps disagree by {worstSpread:F2}x, over the");
            Console.WriteLine($"  {GateNoisySpread:F2}x limit, so this run measures the machine's load and not the code.");
            Console.WriteLine("  Nothing is concluded. Re-run on a quiet machine.");
            return GateExitInconclusive;
        }

        if (writeBaseline)
        {
            return WriteGateBaseline(baselinePath, engineType, reps, currentRaw, currentDefault);
        }

        return CompareAgainstBaseline(baselinePath, factor, currentRaw, currentDefault);
    }

    /// <summary>
    /// Compares a run's medians against the baseline and reports the verdict. Split out of
    /// <see cref="RunRegressionGate"/> so the recording half and the checking half stay readable.
    /// </summary>
    static int CompareAgainstBaseline(
        string baselinePath,
        double factor,
        BenchmarkResult currentRaw,
        BenchmarkResult currentDefault)
    {
        if (!File.Exists(baselinePath))
        {
            Console.WriteLine();
            Console.WriteLine($"  GATE FAILED: no baseline at {baselinePath}");
            Console.WriteLine("  Record one with --write-baseline on a quiet machine, then commit it.");
            return GateExitRegression;
        }

        List<BenchmarkResult> baselineRaw;
        List<BenchmarkResult> baselineDefault;
        try
        {
            var payload = JsonSerializer.Deserialize<GateBaseline>(
                File.ReadAllText(baselinePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            baselineRaw = payload?.Raw ?? [];
            baselineDefault = payload?.Default ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"  GATE FAILED: could not read the baseline ({ex.GetType().Name}: {ex.Message}).");
            return GateExitRegression;
        }

        if (baselineRaw.Count == 0 || baselineDefault.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  GATE FAILED: the baseline at {baselinePath} carries no raw/default measurements.");
            return GateExitRegression;
        }

        var baseRaw = MedianOf(baselineRaw);
        var baseDefault = MedianOf(baselineDefault);

        Console.WriteLine();
        Console.WriteLine("  metric                   baseline     current    ratio   verdict");
        bool regressed = false;
        regressed |= GateRow("raw INSERT", baseRaw.InsertOpsPerSec, currentRaw.InsertOpsPerSec, factor);
        regressed |= GateRow("raw READ", baseRaw.ReadOpsPerSec, currentRaw.ReadOpsPerSec, factor);
        regressed |= GateRow("raw UPDATE", baseRaw.UpdateOpsPerSec, currentRaw.UpdateOpsPerSec, factor);
        regressed |= GateRow("raw DELETE", baseRaw.DeleteOpsPerSec, currentRaw.DeleteOpsPerSec, factor);
        regressed |= GateRow("default INSERT", baseDefault.InsertOpsPerSec, currentDefault.InsertOpsPerSec, factor);
        regressed |= GateRow("default READ", baseDefault.ReadOpsPerSec, currentDefault.ReadOpsPerSec, factor);
        regressed |= GateRow("default UPDATE", baseDefault.UpdateOpsPerSec, currentDefault.UpdateOpsPerSec, factor);
        regressed |= GateRow("default DELETE", baseDefault.DeleteOpsPerSec, currentDefault.DeleteOpsPerSec, factor);
        Console.WriteLine();
        Console.WriteLine("  ratio = baseline ÷ current, so >1 is slower than the baseline.");
        Console.WriteLine();

        if (regressed)
        {
            Console.WriteLine($"  GATE FAILED: at least one metric is slower than baseline × {factor:0.00}.");
            Console.WriteLine("  The band is ±20%, so re-run on a quiet machine before trusting a single");
            Console.WriteLine("  failure — then fix the regression, or re-record the baseline on purpose.");
            return GateExitRegression;
        }

        Console.WriteLine($"  GATE PASSED: nothing is slower than baseline × {factor:0.00}.");
        return 0;
    }

    /// <summary>
    /// Median of each operation's ops/sec across an arm's reps, as a single result. Uses the upper-middle
    /// element for an even rep count, matching the convention the PK harness already uses.
    /// </summary>
    static BenchmarkResult MedianOf(IReadOnlyList<BenchmarkResult> runs)
    {
        static int Median(IReadOnlyList<BenchmarkResult> runs, Func<BenchmarkResult, int> pick)
        {
            var values = runs.Select(pick).Where(static v => v > 0).OrderBy(static v => v).ToArray();
            return values.Length == 0 ? 0 : values[values.Length / 2];
        }

        return new BenchmarkResult
        {
            InsertOpsPerSec = Median(runs, static r => r.InsertOpsPerSec),
            ReadOpsPerSec = Median(runs, static r => r.ReadOpsPerSec),
            UpdateOpsPerSec = Median(runs, static r => r.UpdateOpsPerSec),
            DeleteOpsPerSec = Median(runs, static r => r.DeleteOpsPerSec),
        };
    }

    /// <summary>Prints one gate row. Returns true when the metric regressed beyond the tolerance.</summary>
    static bool GateRow(string metric, int baseline, int current, double factor)
    {
        double ratio = baseline > 0 && current > 0 ? baseline / (double)current : 0;
        bool regressed = ratio > factor;

        // "watch" is the band below the tolerance: a 1.25×+ slowdown is worth a look even when it passes,
        // because two of those in a row is how a regression arrives without ever tripping the gate.
        string verdict = current <= 0 ? "FAILED" : regressed ? "REGRESSED" : ratio > 1.25 ? "watch" : "ok";
        Console.WriteLine($"  {metric,-20} {baseline,10:N0} {current,11:N0} {ratio,7:F2}x   {verdict}");
        return regressed;
    }

    /// <summary>One arm's per-metric rep spread, compact enough for a single console line.</summary>
    static string SpreadLine(IReadOnlyList<BenchmarkResult> runs) =>
        $"I {Spread(runs, static r => r.InsertOpsPerSec):F2}x   R {Spread(runs, static r => r.ReadOpsPerSec):F2}x   " +
        $"U {Spread(runs, static r => r.UpdateOpsPerSec):F2}x   D {Spread(runs, static r => r.DeleteOpsPerSec):F2}x";

    /// <summary>The worst of one arm's four per-metric rep spreads.</summary>
    static double MaxSpread(IReadOnlyList<BenchmarkResult> runs) =>
        Math.Max(
            Math.Max(Spread(runs, static r => r.InsertOpsPerSec), Spread(runs, static r => r.ReadOpsPerSec)),
            Math.Max(Spread(runs, static r => r.UpdateOpsPerSec), Spread(runs, static r => r.DeleteOpsPerSec)));

    /// <summary>max ÷ min for one metric across an arm's reps; 1.0 when there is nothing to compare.</summary>
    static double Spread(IReadOnlyList<BenchmarkResult> runs, Func<BenchmarkResult, int> pick)
    {
        var values = runs.Select(pick).Where(static v => v > 0).ToArray();
        return values.Length < 2 ? 1.0 : values.Max() / (double)values.Min();
    }

    /// <summary>
    /// Records the baseline from this run's medians, in the archived dual-mode shape. One entry per arm —
    /// the median of the reps — so the file stays small, is reviewable in a diff, and is re-readable by the
    /// gate (and by anything that already reads a dual-mode result).
    /// </summary>
    static int WriteGateBaseline(
        string path,
        SharpCoreDB.Interfaces.StorageEngineType engineType,
        int reps,
        BenchmarkResult raw,
        BenchmarkResult deflt)
    {
        try
        {
            var payload = new
            {
                timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                engine = engineType.ToString(),
                reps,
                note = "One entry per arm: the median of that arm's reps, not a single rep.",
                raw = new[] { ToRecord(raw) },
                @default = new[] { ToRecord(deflt) },
            };

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine();
            Console.WriteLine($"  baseline recorded: {path}");
            Console.WriteLine("  Review the diff before committing: a baseline recorded during a regression");
            Console.WriteLine("  silently accepts that regression for every later run.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"  COULD NOT RECORD BASELINE: {ex.GetType().Name}: {ex.Message}");
            return GateExitRegression;
        }
    }

    /// <summary>Reads <c>--gate-factor=&lt;double&gt;</c> (default 1.5); values below 1 are ignored.</summary>
    static double ParseGateFactor(string[] args)
    {
        const string prefix = "--gate-factor=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && double.TryParse(arg[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
                && factor >= 1.0)
            {
                return factor;
            }
        }

        return GateDefaultFactor;
    }

    /// <summary>Reads <c>--gate-baseline=&lt;path&gt;</c>; null means the committed baseline.</summary>
    static string? ParseGateBaseline(string[] args)
    {
        const string prefix = "--gate-baseline=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>Runs one arm, turning a failure into a marked result instead of killing the run.</summary>
    static BenchmarkResult RunArm(
        SharpCoreDB.Interfaces.StorageEngineType engineType,
        bool noEncrypt,
        bool? atRestRecords,
        string label,
        int failed)
    {
        try
        {
            return RunSharpCoreDbMode(engineType, noEncrypt, atRestRecords);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  !! {label} arm FAILED: {ex.GetType().Name}: {ex.Message}");
            return new BenchmarkResult
            {
                InsertOpsPerSec = failed,
                ReadOpsPerSec = failed,
                UpdateOpsPerSec = failed,
                DeleteOpsPerSec = failed,
            };
        }
    }

    static void PrintModeRow(
        string op,
        List<BenchmarkResult> raw,
        List<BenchmarkResult> deflt,
        Func<BenchmarkResult, int> select,
        int failed)
    {
        int r = MedianOps(raw, select, failed);
        int d = MedianOps(deflt, select, failed);
        Console.WriteLine($"  {op,-10}{Cell(r),13}{Cell(d),13}{Ratio(r, d),14}");
    }

    static int MedianOps(List<BenchmarkResult> xs, Func<BenchmarkResult, int> select, int failed)
    {
        if (xs.Count == 0)
        {
            return failed;
        }

        var sorted = xs.Select(select).OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }

    static string Cell(int ops) => ops < 0 ? "FAILED" : ops.ToString("N0", CultureInfo.InvariantCulture);

    static string Ratio(int numerator, int denominator)
        => numerator < 0 || denominator <= 0 ? "n/a" : $"{(double)numerator / denominator:F2}x";

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
