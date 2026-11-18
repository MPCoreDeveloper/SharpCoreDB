// <copyright file="JoinAndSubqueryOrderByTests.cs" company="MPCoreDeveloper">
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
/// ORDER BY on the JOIN path and on the AST path (derived tables, IN-subqueries): every key, each with its own direction,
/// and expression keys.
/// <para>
/// Up to 2.1.4 the JOIN path read only the first token of the first key — <c>ORDER BY LENGTH(p.name) DESC</c> sorted by
/// nothing and <c>ORDER BY c.score * -1</c> sorted ascending by score — and ignored OFFSET. The AST path read each key as a
/// bare name, so <c>ORDER BY UPPER(name)</c> left "(name)" as unparsed trailing content and sorted nothing; its subquery
/// executor read the first key only.
/// </para>
/// </summary>
public sealed class JoinAndSubqueryOrderByTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_JoinOrder_{Guid.NewGuid():N}");

    public JoinAndSubqueryOrderByTests()
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

    private (IDatabase Db, string Path) Open(string mode, string ddl)
    {
        var path = Path.Combine(_root, mode == "single-file" ? $"db_{Guid.NewGuid():N}.scdb" : $"db_{Guid.NewGuid():N}");
        var db = Reopen(mode, path);
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        return (db, path);
    }

    private IDatabase Reopen(string mode, string path) => mode == "single-file"
        ? _factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault())
        : _factory.Create(path, "pw");

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    private IDatabase Seed()
    {
        var (db, _) = Open("columnar", "CREATE TABLE p (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE c (id INTEGER PRIMARY KEY, pid INTEGER, score INTEGER)");
        db.ExecuteSQL("INSERT INTO p (id, name) VALUES (1, 'bob'), (2, 'Al'), (3, 'carl'), (4, 'dee')");
        db.ExecuteSQL("INSERT INTO c (id, pid, score) VALUES (1, 1, 5), (2, 2, 9), (3, 3, 1), (4, 4, 5)");
        return db;
    }

    private static string[] Names(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => Convert.ToString(r["name"], CultureInfo.InvariantCulture)!)];

    [Fact]
    public void Join_OrderByFunctionDescending_SortsByTheFunction()
    {
        var db = Seed();
        Assert.Equal(["carl", "bob", "dee", "Al"],
            Names(db, "SELECT p.name, c.score FROM p JOIN c ON c.pid = p.id ORDER BY LENGTH(p.name) DESC, p.name"));
    }

    [Fact]
    public void Join_OrderByArithmetic_SortsByTheComputedValue()
    {
        var db = Seed();
        Assert.Equal(["Al", "bob", "dee", "carl"],
            Names(db, "SELECT p.name, c.score FROM p JOIN c ON c.pid = p.id ORDER BY c.score * -1, p.name"));
    }

    [Fact]
    public void Join_OrderByTwoKeys_UsesTheSecondKeyForTies()
    {
        var db = Seed();
        Assert.Equal(["carl", "dee", "bob", "Al"],
            Names(db, "SELECT p.name, c.score FROM p JOIN c ON c.pid = p.id ORDER BY c.score, p.name DESC"));
    }

    [Fact]
    public void Join_LimitWithOffset_SkipsTheOffsetRows()
    {
        var db = Seed();
        Assert.Equal(["bob", "carl"],
            Names(db, "SELECT p.name, c.score FROM p JOIN c ON c.pid = p.id ORDER BY p.name LIMIT 2 OFFSET 1"));
    }

    [Fact]
    public void DerivedTable_OrderByFunction_SortsByTheFunction()
    {
        var db = Seed();
        Assert.Equal(["Al", "bob", "dee", "carl"], Names(db, "SELECT name FROM (SELECT name FROM p) ORDER BY LENGTH(name), name"));
    }

    [Fact]
    public void InSubquery_OrderByFunctionDescending_SortsByTheFunction()
    {
        var db = Seed();
        Assert.Equal(["dee", "carl", "bob", "Al"],
            Names(db, "SELECT name FROM p WHERE id IN (SELECT pid FROM c) ORDER BY UPPER(name) DESC"));
    }
}
