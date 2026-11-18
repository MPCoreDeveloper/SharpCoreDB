// <copyright file="UpsertTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// SQLite's INSERT conflict clauses: the upsert <c>ON CONFLICT [(cols)] DO NOTHING | DO UPDATE SET … [WHERE …]</c>,
/// <c>INSERT OR IGNORE|REPLACE|ABORT|FAIL</c> and <c>REPLACE INTO</c>, on single-column and composite keys.
/// <para>
/// Up to 2.1.4 none of them ran on the SQL path: <c>INSERT OR …</c> failed with "Unsupported SQL statement" and an
/// <c>ON CONFLICT</c> clause was ignored, so an upsert onto an existing key failed as a duplicate — the conflict handlers
/// existed but nothing called them. The composite lookup compared values without the column's collation, and the "same
/// row" check was always case-insensitive.
/// </para>
/// </summary>
public sealed class UpsertTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_Upsert_{Guid.NewGuid():N}");

    public UpsertTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

    public static TheoryData<string> Modes => new() { "columnar", "page-based" };

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

    private IDatabase Open(string mode, string ddl)
    {
        var db = _factory.Create(Path.Combine(_root, $"db_{Guid.NewGuid():N}"), "pw");
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        return db;
    }

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    [Theory]
    [MemberData(nameof(Modes))]
    public void OnConflictDoUpdate_SingleColumnKey_MergesIntoTheExistingRow(string mode)
    {
        var db = Open(mode, "CREATE TABLE s (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 1)");

        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 5) ON CONFLICT (code) DO UPDATE SET n = n + excluded.n");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('b', 7) ON CONFLICT (code) DO UPDATE SET n = n + excluded.n");

        Assert.Equal(["a,6", "b,7"], Rows(db, "SELECT code, n FROM s").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void OnConflictDoUpdate_WithAFalseWhere_LeavesTheRow(string mode)
    {
        var db = Open(mode, "CREATE TABLE s (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 1)");

        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 2) ON CONFLICT (code) DO UPDATE SET n = excluded.n WHERE excluded.n > 3");
        Assert.Equal(["a,1"], Rows(db, "SELECT code, n FROM s"));

        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 9) ON CONFLICT (code) DO UPDATE SET n = excluded.n WHERE excluded.n > 3");
        Assert.Equal(["a,9"], Rows(db, "SELECT code, n FROM s"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void OnConflict_CompositeKey_DoUpdateAndDoNothing(string mode)
    {
        var db = Open(mode, "CREATE TABLE u (a INTEGER NOT NULL, b INTEGER NOT NULL, v TEXT, PRIMARY KEY (a, b))");
        db.ExecuteSQL("INSERT INTO u (a, b, v) VALUES (1, 1, 'x'), (1, 2, 'y')");

        db.ExecuteSQL("INSERT INTO u (a, b, v) VALUES (1, 1, 'new') ON CONFLICT (a, b) DO UPDATE SET v = excluded.v");
        db.ExecuteSQL("INSERT INTO u (a, b, v) VALUES (1, 2, 'kept?') ON CONFLICT (a, b) DO NOTHING");
        db.ExecuteSQL("INSERT INTO u (a, b, v) VALUES (2, 2, 'z') ON CONFLICT DO NOTHING");

        Assert.Equal(["1,1,new", "1,2,y", "2,2,z"], Rows(db, "SELECT a, b, v FROM u").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void InsertOrIgnore_AndInsertOrReplace_AndReplaceInto(string mode)
    {
        var db = Open(mode, "CREATE TABLE s (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 1), ('b', 2)");

        db.ExecuteSQL("INSERT OR IGNORE INTO s (code, n) VALUES ('a', 100), ('c', 3)");
        db.ExecuteSQL("INSERT OR REPLACE INTO s (code, n) VALUES ('b', 20)");
        db.ExecuteSQL("REPLACE INTO s (code, n) VALUES ('c', 30)");

        Assert.Equal(["a,1", "b,20", "c,30"], Rows(db, "SELECT code, n FROM s").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void InsertOrAbort_OnAConflict_Throws(string mode)
    {
        var db = Open(mode, "CREATE TABLE s (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 1)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT OR ABORT INTO s (code, n) VALUES ('a', 2)"));
        Assert.Equal(["a,1"], Rows(db, "SELECT code, n FROM s"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void OnConflict_CompositeUnique_RespectsTheColumnCollation(string mode)
    {
        var db = Open(mode, "CREATE TABLE n (id INTEGER PRIMARY KEY, a TEXT COLLATE NOCASE, b TEXT, v TEXT, UNIQUE (a, b))");
        db.ExecuteSQL("INSERT INTO n (id, a, b, v) VALUES (1, 'abc', 'k', 'x')");

        // NOCASE column: 'ABC' is the same key, so the existing row is updated.
        db.ExecuteSQL("INSERT INTO n (id, a, b, v) VALUES (2, 'ABC', 'k', 'merged') ON CONFLICT (a, b) DO UPDATE SET v = excluded.v");
        // BINARY column: 'K' is a different key, so the row is inserted.
        db.ExecuteSQL("INSERT INTO n (id, a, b, v) VALUES (3, 'abc', 'K', 'new') ON CONFLICT (a, b) DO NOTHING");

        Assert.Equal(["1,abc,k,merged", "3,abc,K,new"], Rows(db, "SELECT id, a, b, v FROM n").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void InsertOrIgnore_FromASelect_SkipsExistingKeys(string mode)
    {
        var db = Open(mode, "CREATE TABLE s (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("CREATE TABLE src (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 1)");
        db.ExecuteSQL("INSERT INTO src (code, n) VALUES ('a', 100), ('b', 2)");

        db.ExecuteSQL("INSERT OR IGNORE INTO s (code, n) SELECT code, n FROM src");

        Assert.Equal(["a,1", "b,2"], Rows(db, "SELECT code, n FROM s").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Upsert_InABatch_IsApplied(string mode)
    {
        var db = Open(mode, "CREATE TABLE s (code TEXT PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO s (code, n) VALUES ('a', 1)");

        db.ExecuteBatchSQL([
            "INSERT INTO s (code, n) VALUES ('a', 100) ON CONFLICT (code) DO UPDATE SET n = excluded.n",
            "INSERT INTO s (code, n) VALUES ('b', 1) ON CONFLICT (code) DO NOTHING"]);

        Assert.Equal(["a,100", "b,1"], Rows(db, "SELECT code, n FROM s").Order());
    }
}
