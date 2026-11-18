// <copyright file="CrashDurabilityTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// What survives a hard process crash on directory storage, and how a damaged metadata file is reported.
/// <para>
/// The crash cases run the scenario in a child test process that kills itself (no Dispose, no finalizer, no flush) and
/// reopen the database in this process. A process kill keeps whatever reached the operating system, so these pin
/// crash consistency, not power-loss durability: the fsync of in-place writes and the atomic metadata replace
/// (known issues §17 and §16) are verified by reading the code, not by these tests. The PageBased cases that do not hold
/// yet are skipped with the known issue that tracks them (§15).
/// </para>
/// </summary>
public sealed class CrashDurabilityTests : IDisposable
{
    private const string ScenarioVariable = "SCDB_CRASH_SCENARIO";
    private const string PathVariable = "SCDB_CRASH_PATH";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_Crash_{Guid.NewGuid():N}");

    public CrashDurabilityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp directory.
        }
    }

    private static IDatabase Open(string path)
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        return services.BuildServiceProvider().GetRequiredService<DatabaseFactory>().Create(path, "pw");
    }

    /// <summary>The child half: runs only when spawned by <see cref="RunCrashScenario"/>, then kills its process.</summary>
    [Fact]
    public void CrashChild_RunsTheScenarioAndKillsItsProcess()
    {
        var scenario = Environment.GetEnvironmentVariable(ScenarioVariable);
        if (string.IsNullOrEmpty(scenario))
        {
            return;
        }

        var db = Open(Environment.GetEnvironmentVariable(PathVariable)!);
        switch (scenario)
        {
            case "autocommit-update":
                db.ExecuteSQL("UPDATE t SET n = 999 WHERE id = 1");
                break;
            case "autocommit-insert-delete":
                db.ExecuteSQL("INSERT INTO t (id, n) VALUES (100, 100)");
                db.ExecuteSQL("DELETE FROM t WHERE id = 2");
                break;
            case "batch":
                db.ExecuteBatchSQL(["UPDATE t SET n = 999 WHERE id = 1", "UPDATE t SET n = 998 WHERE id = 3"]);
                break;
            case "transaction-committed":
                db.BeginBatchUpdate();
                db.ExecuteSQL("UPDATE t SET n = 999 WHERE id = 1");
                db.ExecuteSQL("INSERT INTO t (id, n) VALUES (100, 100)");
                db.ExecuteSQL("DELETE FROM t WHERE id = 2");
                db.EndBatchUpdate();
                break;
            case "transaction-uncommitted":
                db.BeginBatchUpdate();
                db.ExecuteSQL("UPDATE t SET n = 999 WHERE id = 1");
                db.ExecuteSQL("INSERT INTO t (id, n) VALUES (100, 100)");
                db.ExecuteSQL("DELETE FROM t WHERE id = 2");
                break;
        }

        Process.GetCurrentProcess().Kill();
    }

    public static TheoryData<string, string, string> HoldingCases => new()
    {
        { "columnar", "autocommit-update", "1:999 2:2 3:3" },
        { "columnar", "autocommit-insert-delete", "1:1 3:3 100:100" },
        { "columnar", "batch", "1:999 2:2 3:998" },
        { "columnar", "transaction-committed", "1:999 3:3 100:100" },
        { "columnar", "transaction-uncommitted", "1:1 2:2 3:3" },
        { "page-based", "transaction-committed", "1:999 3:3 100:100" },
        { "page-based", "transaction-uncommitted", "1:1 2:2 3:3" },
    };

    [Theory]
    [MemberData(nameof(HoldingCases))]
    public void Crash_AfterTheScenario_LeavesTheCommittedState(string storage, string scenario, string expected)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ScenarioVariable)))
        {
            return; // inside a crash child
        }

        Assert.Equal(expected, RunCrashScenario(storage, scenario));
    }

    [Fact(Skip = "Known issue §15: a PageBased autocommit statement stays in the page cache until a flush, so a crash loses it.")]
    public void Crash_AfterAPageBasedAutocommitUpdate_KeepsTheUpdate() =>
        Assert.Equal("1:999 2:2 3:3", RunCrashScenario("page-based", "autocommit-update"));

    [Fact(Skip = "Known issue §15: ExecuteBatchSQL does not flush the PageBased page cache, so a crash loses a completed batch.")]
    public void Crash_AfterAPageBasedBatch_KeepsTheBatch() =>
        Assert.Equal("1:999 2:2 3:998", RunCrashScenario("page-based", "batch"));

    [Fact]
    public void Open_WithAnEmptyMetadataFile_ReportsAnInterruptedSave_NotTheWrongPassword()
    {
        var path = CreateSeeded("columnar");
        File.WriteAllBytes(Path.Combine(path, "meta.dat"), []);

        var error = Assert.ThrowsAny<InvalidOperationException>(() => Open(path));

        Assert.Contains("interrupted", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_WithATruncatedMetadataFile_ReportsAnInterruptedSave()
    {
        var path = CreateSeeded("columnar");
        var meta = Path.Combine(path, "meta.dat");
        File.WriteAllBytes(meta, File.ReadAllBytes(meta)[..10]);

        var error = Assert.ThrowsAny<InvalidOperationException>(() => Open(path));

        Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MetadataSave_LeavesNoTempFile_AndAStaleTempFileDoesNotAffectOpen()
    {
        var path = CreateSeeded("columnar");
        Assert.False(File.Exists(Path.Combine(path, "meta.dat.tmp")));

        // A crash between writing the temp file and the rename leaves it behind; the old meta.dat is intact.
        File.WriteAllBytes(Path.Combine(path, "meta.dat.tmp"), [1, 2, 3]);
        var db = Open(path);
        db.ExecuteSQL("CREATE TABLE u (id INTEGER PRIMARY KEY)");
        (db as IDisposable)?.Dispose();

        var reopened = Open(path);
        Assert.Equal(3, reopened.ExecuteQuery("SELECT id FROM t").Count);
        Assert.Empty(reopened.ExecuteQuery("SELECT id FROM u"));
        (reopened as IDisposable)?.Dispose();
    }

    private string CreateSeeded(string storage)
    {
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var seed = Open(path);
        seed.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)" + (storage == "page-based" ? " STORAGE = PAGE_BASED" : ""));
        seed.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2), (3, 3)");
        (seed as IDisposable)?.Dispose();
        return path;
    }

    private string RunCrashScenario(string storage, string scenario)
    {
        var path = CreateSeeded(storage);
        var start = new ProcessStartInfo(
            Environment.ProcessPath!,
            $"-filterVSTest \"FullyQualifiedName~{nameof(CrashDurabilityTests)}.{nameof(CrashChild_RunsTheScenarioAndKillsItsProcess)}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment[ScenarioVariable] = scenario;
        start.Environment[PathVariable] = path;
        using (var child = Process.Start(start)!)
        {
            child.StandardOutput.ReadToEnd();
            child.WaitForExit();
        }

        var reopened = Open(path);
        try
        {
            return string.Join(" ", reopened.ExecuteQuery("SELECT id, n FROM t")
                .Select(r => (Id: Convert.ToInt32(r["id"], CultureInfo.InvariantCulture), N: Convert.ToInt32(r["n"], CultureInfo.InvariantCulture)))
                .OrderBy(r => r.Id)
                .Select(r => $"{r.Id}:{r.N}"));
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }
    }
}
