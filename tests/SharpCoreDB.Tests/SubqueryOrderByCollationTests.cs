// <copyright file="SubqueryOrderByCollationTests.cs" company="MPCoreDeveloper">
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
/// ORDER BY on the AST path (derived tables, IN-subqueries) honours <c>COLLATE</c> and <c>NULLS FIRST|LAST</c>, as the
/// single-table and JOIN paths do.
/// <para>
/// Up to 2.1.4 the AST parser read only ASC/DESC after a key: <c>COLLATE NOCASE</c> was left as unparsed trailing content
/// — and took the DESC after it along — and <c>NULLS LAST</c> was ignored; a descending sort also moved NULLs to the end
/// regardless of what the statement said.
/// </para>
/// </summary>
public sealed class SubqueryOrderByCollationTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_SubOrder_{Guid.NewGuid():N}");

    public SubqueryOrderByCollationTests()
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

    private IDatabase Seed()
    {
        var db = Open("columnar", "CREATE TABLE p (id INTEGER PRIMARY KEY, name TEXT, score INTEGER)");
        db.ExecuteSQL("INSERT INTO p (id, name, score) VALUES (1, 'b', 5), (2, 'A', NULL), (3, 'C', 1), (4, 'a', 9)");
        return db;
    }

    private static string[] Names(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => Convert.ToString(r["name"], CultureInfo.InvariantCulture)!)];

    [Fact]
    public void DerivedTable_OrderByTextWithoutCollate_SortsAsBinary()
    {
        // SQLite's default collation is BINARY: upper case sorts before lower case. The AST path compared text
        // case-insensitively, so 'a' came before 'C'.
        var db = Seed();
        Assert.Equal(["A", "C", "a", "b"], Names(db, "SELECT name FROM (SELECT name FROM p) ORDER BY name"));
    }

    [Fact]
    public void InSubqueryWithOrderByAndLimit_SelectsTheRowsItsOwnOrderPicks()
    {
        // The subquery's own ORDER BY decides which rows its LIMIT keeps: the two highest scores, ties by name DESC.
        var db = Seed();
        db.ExecuteSQL("INSERT INTO p (id, name, score) VALUES (5, 'd', 9)");

        Assert.Equal(["a", "d"], Names(db, "SELECT name FROM p WHERE id IN (SELECT id FROM p ORDER BY score DESC, name DESC LIMIT 2) ORDER BY name"));
    }

    [Fact]
    public void DerivedTable_OrderByCollateNoCase_SortsCaseInsensitively()
    {
        var db = Seed();
        Assert.Equal(["A", "a", "b", "C"], Names(db, "SELECT name FROM (SELECT name, id FROM p) ORDER BY name COLLATE NOCASE, id"));
    }

    [Fact]
    public void InSubquery_OrderByCollateNoCaseDescending_KeepsTheDirection()
    {
        var db = Seed();
        Assert.Equal(["C", "b", "A", "a"], Names(db, "SELECT name FROM p WHERE id IN (SELECT id FROM p) ORDER BY name COLLATE NOCASE DESC, id"));
    }

    [Fact]
    public void DerivedTable_OrderByNullsLast_PutsNullsAtTheEnd()
    {
        var db = Seed();
        Assert.Equal(["C", "b", "a", "A"], Names(db, "SELECT name FROM (SELECT name, score FROM p) ORDER BY score NULLS LAST"));
    }

    [Fact]
    public void DerivedTable_OrderByDescendingNullsFirst_PutsNullsFirst()
    {
        var db = Seed();
        Assert.Equal(["A", "a", "b", "C"], Names(db, "SELECT name FROM (SELECT name, score FROM p) ORDER BY score DESC NULLS FIRST"));
    }

    [Fact]
    public void DerivedTable_OrderByDescending_PutsNullsLastByDefault()
    {
        var db = Seed();
        Assert.Equal(["a", "b", "C", "A"], Names(db, "SELECT name FROM (SELECT name, score FROM p) ORDER BY score DESC"));
    }
}
