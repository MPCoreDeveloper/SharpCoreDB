// <copyright file="ReopenRoundTripMatrixTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// Round-trip matrix across storage variants (directory fixed-width default, directory legacy
/// variable-length, single-file JSON, single-file fixed-width) for values and operations that
/// exercise the length-prefixed overflow-arena layout: empty TEXT values interleaved with
/// non-empty ones, UPDATEs that flip a value between empty and non-empty, INSERTs and DELETEs
/// after such values, each followed by a full reopen + content verification.
/// Guards against "valid writer output treated as end-of-data by a reader" regressions
/// (e.g. zero-length arena records silently truncating an .ovf reload).
/// </summary>
public sealed class ReopenRoundTripMatrixTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private readonly string _scdbPath;

    public ReopenRoundTripMatrixTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_RoundTrip_Dir_{Guid.NewGuid():N}");
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_RoundTrip_Scdb_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    public static TheoryData<string, DatabaseConfig> Cases => new()
    {
        { "directory-fixedwidth-default", new DatabaseConfig() },
        { "directory-legacy-variable", new DatabaseConfig { AutoFixedWidthRecords = false } },
        { "singlefile-legacy-json", new DatabaseConfig() },
        { "singlefile-fixedwidth", new DatabaseConfig { FixedWidthRecordLayout = true } },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void RoundTrip_ReopenWithEmptyValues_KeepsDataIntact(string variant, DatabaseConfig config)
    {
        var path = variant.StartsWith("singlefile", StringComparison.Ordinal) ? _scdbPath : _dirPath;
        RunScenario(variant, path, config);
    }

    [Fact]
    public void DirectoryLegacy_UpdateThenDelete_DoesNotResurrectAfterReopen()
    {
        // Regression for the legacy (variable-length) delete-after-update resurrection: an UPDATE
        // appends a new version, DELETE only tombstones the newest version, and an older stale
        // version used to win the reopen index rebuild. The delete-time purge must tombstone the
        // remaining older versions of the deleted key too.
        string path = Path.Combine(Path.GetTempPath(), $"SCDB_RoundTrip_LegacyDelete_{Guid.NewGuid():N}");
        var config = new DatabaseConfig { AutoFixedWidthRecords = false };
        try
        {
            WithDb(path, config, db =>
            {
                db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, tag TEXT, note TEXT)");
                db.ExecuteSQL("INSERT INTO t VALUES (1, 'a', 'keep-1')");
                db.ExecuteSQL("INSERT INTO t VALUES (2, 'b', 'keep-2')");
                db.ExecuteSQL("UPDATE t SET tag = 'a2' WHERE id = 1");
                db.ExecuteSQL("UPDATE t SET tag = 'a3' WHERE id = 1"); // two stale versions
            });

            // Delete the updated row in a fresh session, then verify it stays deleted after reopen.
            WithDb(path, config, db =>
            {
                AssertRowsDirect(db, [(1L, "a3", "keep-1"), (2L, "b", "keep-2")]);
                db.ExecuteSQL("DELETE FROM t WHERE id = 1");
            });

            WithDb(path, config, db =>
            {
                AssertRowsDirect(db, [(2L, "b", "keep-2")]);
            });
        }
        finally
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }

    private static void AssertRowsDirect(IDatabase db, params (long Id, string Tag, string Note)[] expected)
    {
        var rows = db.ExecuteQuery("SELECT id, tag, note FROM t ORDER BY id", new Dictionary<string, object?>());
        Assert.True(rows.Count == expected.Length, $"expected {expected.Length} rows, got {rows.Count}");
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Convert.ToInt64(rows[i]["id"]) == expected[i].Id, "id mismatch");
            Assert.True(string.Equals(Convert.ToString(rows[i]["tag"]), expected[i].Tag, StringComparison.Ordinal), "tag mismatch");
            Assert.True(string.Equals(Convert.ToString(rows[i]["note"]), expected[i].Note, StringComparison.Ordinal), "note mismatch");
        }
    }

    private void RunScenario(string variant, string path, DatabaseConfig config)
    {
        // Fresh storage for every variant run (theory cases may share a class instance).
        if (Directory.Exists(path)) Directory.Delete(path, true);
        if (File.Exists(path)) File.Delete(path);

        // Phase 0: seed rows where empty TEXT values are interleaved with non-empty ones.
        WithDb(path, config, db =>
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, tag TEXT, note TEXT, score INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, '', 'first', 10)");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'b2', '', 20)");
            db.ExecuteSQL("INSERT INTO t VALUES (3, 'c3', 'third', 30)");
            db.ExecuteSQL("INSERT INTO t VALUES (4, '', '', 40)");
            db.ExecuteSQL("INSERT INTO t VALUES (5, 'a considerably longer tag value for the overflow block', 'five', 50)");
        });

        WithDb(path, config, db =>
        {
            AssertTableEquals(db, variant,
                Row(1, "", "first", 10), Row(2, "b2", "", 20), Row(3, "c3", "third", 30),
                Row(4, "", "", 40), Row(5, "a considerably longer tag value for the overflow block", "five", 50));
        });

        // Phase 1: UPDATEs that flip empty <-> non-empty (both directions) then reopen again.
        WithDb(path, config, db =>
        {
            db.ExecuteSQL("UPDATE t SET note = 'changed' WHERE id = 2");
            db.ExecuteSQL("UPDATE t SET tag = '' WHERE id = 3");
            db.ExecuteSQL("UPDATE t SET note = '' WHERE id = 5");
            db.ExecuteSQL("UPDATE t SET tag = '4-updated' WHERE id = 4");

            // Same-session probe: the row cache must already reflect the updates.
            AssertTableEquals(db, variant,
                Row(1, "", "first", 10), Row(2, "b2", "changed", 20), Row(3, "", "third", 30),
                Row(4, "4-updated", "", 40), Row(5, "a considerably longer tag value for the overflow block", "", 50));
        });

        WithDb(path, config, db =>
        {
            AssertTableEquals(db, variant,
                Row(1, "", "first", 10), Row(2, "b2", "changed", 20), Row(3, "", "third", 30),
                Row(4, "4-updated", "", 40), Row(5, "a considerably longer tag value for the overflow block", "", 50));
        });

        // Phase 2: delete the previously-updated row (id 3), insert more rows after empty values,
        // update again, and reopen a third time. The legacy variable-length variant exercises the
        // delete-after-update purge (older stale versions of a deleted key must not resurrect).
        WithDb(path, config, db =>
        {
            db.ExecuteSQL("DELETE FROM t WHERE id = 3");
            db.ExecuteSQL("INSERT INTO t VALUES (6, '', 'six', 60)");
            db.ExecuteSQL("INSERT INTO t VALUES (7, 'g7', '', 70)");
            db.ExecuteSQL("UPDATE t SET tag = 's1' WHERE id = 1");

            // Same-session probe: the row cache must already reflect the DELETE.
            AssertTableEquals(db, variant,
                Row(1, "s1", "first", 10), Row(2, "b2", "changed", 20),
                Row(4, "4-updated", "", 40), Row(5, "a considerably longer tag value for the overflow block", "", 50),
                Row(6, "", "six", 60), Row(7, "g7", "", 70));
        });

        WithDb(path, config, db =>
        {
            AssertTableEquals(db, variant,
                Row(1, "s1", "first", 10), Row(2, "b2", "changed", 20),
                Row(4, "4-updated", "", 40), Row(5, "a considerably longer tag value for the overflow block", "", 50),
                Row(6, "", "six", 60), Row(7, "g7", "", 70));
        });
    }

    private void WithDb(string path, DatabaseConfig config, Action<IDatabase> action)
    {
        var db = Open(path, config);
        try
        {
            action(db);
            db.Flush();
            db.ForceSave();
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    private IDatabase Open(string path, DatabaseConfig config)
        => _factory.Create(path, "pw", isReadOnly: false, config: config);

    private static (long Id, string Tag, string Note, long Score) Row(long id, string tag, string note, long score)
        => (id, tag, note, score);

    private static void AssertTableEquals(
        IDatabase db,
        string variant,
        params (long Id, string Tag, string Note, long Score)[] expected)
    {
        var rows = db.ExecuteQuery("SELECT id, tag, note, score FROM t ORDER BY id", new Dictionary<string, object?>());
        string actualSummary = string.Join(
            " | ",
            rows.Select(r => $"id={Convert.ToString(r["id"])},tag=[{Convert.ToString(r["tag"])}],note=[{Convert.ToString(r["note"])}],score={Convert.ToString(r["score"])}"));
        Assert.True(
            rows.Count == expected.Length,
            $"[{variant}] row count mismatch: expected {expected.Length}, actual {rows.Count}. rows: {actualSummary}");

        for (int i = 0; i < expected.Length; i++)
        {
            var actual = rows[i];
            var want = expected[i];
            Assert.True(
                Convert.ToInt64(actual["id"]) == want.Id,
                $"[{variant}] row {i}: id mismatch (expected {want.Id}, actual {Convert.ToString(actual["id"])}). rows: {actualSummary}");
            Assert.True(
                string.Equals(Convert.ToString(actual["tag"]), want.Tag, StringComparison.Ordinal),
                $"[{variant}] row {i}: tag mismatch (expected [{want.Tag}], actual [{Convert.ToString(actual["tag"])}]). rows: {actualSummary}");
            Assert.True(
                string.Equals(Convert.ToString(actual["note"]), want.Note, StringComparison.Ordinal),
                $"[{variant}] row {i}: note mismatch (expected [{want.Note}], actual [{Convert.ToString(actual["note"])}]). rows: {actualSummary}");
            Assert.True(
                Convert.ToInt64(actual["score"]) == want.Score,
                $"[{variant}] row {i}: score mismatch (expected {want.Score}, actual {Convert.ToString(actual["score"])}). rows: {actualSummary}");
        }
    }
}