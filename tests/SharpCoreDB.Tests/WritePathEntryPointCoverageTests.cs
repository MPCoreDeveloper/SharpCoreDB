// <copyright file="WritePathEntryPointCoverageTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Diagnostics;
using SharpCoreDB.Interfaces;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Guards plan §5.5 (ii): every statement-level entry point of the write path must reach the write-path
/// profiler, and the second batch dispatcher must be attributed at all. Before that work
/// <c>ExecuteSQL(sql, parameters)</c>, the positional helper, both <c>ExecuteSQLAsync</c> overloads and
/// <c>ExecuteBatchSQLAsync</c> recorded nothing — the 2026-09-15 sweep wired <c>ExecuteSQL(string)</c> and
/// left the identical blocks beside it — so a stage report looked complete while an ADO.NET-shaped
/// caller's per-statement cost was invisible. Each entry point is measured on its own here, so a failure
/// names the one that fell out of coverage.
/// </summary>
[Collection("WritePathProfilerTests")] // process-wide counters — see WritePathProfilerTestCollection
public sealed class WritePathEntryPointCoverageTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dir;

    public WritePathEntryPointCoverageTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dir = Path.Combine(Path.GetTempPath(), $"SCDB_EntryPoint_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        WritePathProfiler.Disable();

        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Calls recorded for one stage since the last <see cref="WritePathProfiler.Reset"/>.</summary>
    private static long Calls(string stage) =>
        WritePathProfiler.Snapshot().Single(r => r.Stage == stage).Calls;

    /// <summary>
    /// The five statement-level DML entry points must each record the <c>dispatch</c> envelope (the write
    /// lock, the shared-parser fetch and the hand-off) and the <c>stmt-validate</c> stage (the per-statement
    /// security and parameter check). The two async overloads are what this pins today: they carried no
    /// stamp at all until §5.5 (ii), and they reached the same parser as <c>ExecuteSQL(string)</c> without
    /// ever calling <see cref="SharpCoreDB.Services.SqlQueryValidator.ValidateQuery"/> — the sync sibling had
    /// validated since 2026-09-15, so the async entry point a caller reaches through ADO.NET bypassed a
    /// check its own twin enforced (fixed 2026-09-27, session 75).
    /// </summary>
    [Fact]
    public async Task EveryStatementEntryPoint_ReachesTheDispatchEnvelope()
    {
        await using IDatabase db = _factory.Create(
            _dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });

        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");

        List<(string EntryPoint, Func<Task> Call)> entryPoints =
        [
            ("ExecuteSQL(sql)", () =>
            {
                db.ExecuteSQL("INSERT INTO docs VALUES (1, 'a', 1.5)");
                return Task.CompletedTask;
            }),
            ("ExecuteSQL(sql, params)", () =>
            {
                db.ExecuteSQL(
                    "INSERT INTO docs VALUES (@id, @name, @score)",
                    new Dictionary<string, object?> { ["@id"] = 2, ["@name"] = "b", ["@score"] = 2.5 });
                return Task.CompletedTask;
            }),
            ("ExecuteSQL(sql, positional)", () =>
            {
                // This helper is on the concrete Database only (not on IDatabase), and it delegates to the
                // parameterized overload above after boxing the arguments into an @p{i} dictionary.
                ((Database)db).ExecuteSQL("INSERT INTO docs VALUES (@p0, @p1, @p2)", 3, "c", 3.5);
                return Task.CompletedTask;
            }),
            ("ExecuteSQLAsync(sql)", async () => await db.ExecuteSQLAsync("INSERT INTO docs VALUES (4, 'd', 4.5)")),
            ("ExecuteSQLAsync(sql, params)", async () => await db.ExecuteSQLAsync(
                "INSERT INTO docs VALUES (@id, @name, @score)",
                new Dictionary<string, object?> { ["@id"] = 5, ["@name"] = "e", ["@score"] = 5.5 }))
        ];

        foreach ((string entryPoint, Func<Task> call) in entryPoints)
        {
            WritePathProfiler.Reset();
            WritePathProfiler.Enable();
            await call();
            WritePathProfiler.Disable();

            Assert.True(Calls("dispatch") >= 1, $"{entryPoint} did not reach the dispatch envelope");

            // §5.5 (ii) follow-up (session 75): every entry point that validates records `stmt-validate`.
            // The two async overloads skipped the validator entirely before that, so a stage report could
            // not even show that a security check was missing on them.
            Assert.True(Calls("stmt-validate") >= 1, $"{entryPoint} did not reach the validation stage");
        }
    }

    /// <summary>
    /// The second batch dispatcher (<c>ExecuteBatchSQLAsync</c>) had no stamp at all. Its classification
    /// loop is the only place that can report that cost — the rows it hands to <c>Table.InsertBatch</c>
    /// never reach the parser, so before the wiring this call recorded zero <c>parse</c> calls however many
    /// statements it classified.
    /// </summary>
    [Fact]
    public async Task SecondBatchDispatcher_ClassificationAndCommit_AreAttributed()
    {
        const int statements = 5;

        await using IDatabase db = _factory.Create(
            _dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });

        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT)");

        WritePathProfiler.Reset();
        WritePathProfiler.Enable();
        await db.ExecuteBatchSQLAsync(
            [.. Enumerable.Range(10, statements).Select(i => $"INSERT INTO docs VALUES ({i}, 'n{i}')")]);
        WritePathProfiler.Disable();

        Assert.True(
            Calls("parse") >= statements,
            $"expected >= {statements} parse calls from the async batch classification, got {Calls("parse")}");
        Assert.True(
            Calls("commit") >= 1,
            $"expected the async commit to be attributed, got {Calls("commit")}");
    }

    /// <summary>
    /// The dictionary-free SQL batch-INSERT fast path — <c>Database.InsertBatch(table, rows, columns)</c> →
    /// <c>Table.InsertBatch(object[][], columnOrder)</c>, the shape the SQL batch parser's INSERT and the
    /// harness's batch arms use — must be attributed on *every* stage its dictionary sibling records.
    /// Session 80 found it recorded none of them: the 2026-09-15 sweep stamped the dictionary overload and
    /// its helper, and the array overload (added later, for <c>PreparedInsertStatement</c>) was never
    /// stamped, which is the same duplicated-block gap the two entry points above close. The evidence is
    /// <c>artifacts/s80-probe-array-insert.txt</c>: the array shape's INSERT profile attributed 344,7 ms of a
    /// 560 ms pass (62 %) with no <c>validate</c>, <c>validate-only</c> or <c>encode</c> row, so
    /// <c>index-maint</c> looked like 43,6 % of the whole path when it was 43,6 % of the attributed part.
    /// </summary>
    [Fact]
    public async Task DictionaryFreeBatchInsertPath_IsAttributedOnEveryStage()
    {
        await using IDatabase db = _factory.Create(
            _dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });

        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");

        List<object[]> rows = [[1, "a", 1.5], [2, "b", 2.5], [3, "c", 3.5]];
        List<string> columns = ["id", "name", "score"];

        WritePathProfiler.Reset();
        WritePathProfiler.Enable();
        ((Database)db).InsertBatch("docs", rows, columns);
        WritePathProfiler.Disable();

        Assert.True(Calls("validate") >= 1, "the dictionary-free batch INSERT did not reach the validate stage");
        Assert.True(Calls("validate-only") >= 1, "validation and serialization are not separated on this path");
        Assert.True(Calls("encode") >= 1, "the dictionary-free batch INSERT did not reach the encode stage");
        Assert.True(Calls("row-locate") >= 1, "the upfront primary-key check on this path is not attributed");
    }
}
