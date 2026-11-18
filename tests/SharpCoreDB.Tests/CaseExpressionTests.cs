// <copyright file="CaseExpressionTests.cs" company="MPCoreDeveloper">
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
/// SQLite's <c>CASE [operand] WHEN … THEN … [ELSE …] END</c> in the scalar-expression evaluator — UPDATE SET values,
/// CHECK constraints, ORDER BY keys and the SELECT list.
/// <para>
/// Up to 2.1.4 CASE was not parsed: <c>SET grade = CASE WHEN n &gt;= 5 THEN 'high' ELSE 'low' END</c> fell back to the
/// literal reading and stored the statement text, and a CHECK written with CASE was left to an evaluator that rejected
/// every row.
/// </para>
/// </summary>
public sealed class CaseExpressionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_Case_{Guid.NewGuid():N}");

    public CaseExpressionTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

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

    private IDatabase Seed()
    {
        var db = _factory.Create(Path.Combine(_root, $"db_{Guid.NewGuid():N}"), "pw");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, kind TEXT, grade TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, n, kind, grade) VALUES (1, 2, 'a', NULL), (2, 7, 'b', NULL), (3, NULL, 'c', NULL)");
        return db;
    }

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    [Fact]
    public void Update_SetToASearchedCase_TakesTheFirstTrueBranch()
    {
        var db = Seed();

        db.ExecuteSQL("UPDATE t SET grade = CASE WHEN n >= 5 THEN 'high' WHEN n IS NULL THEN 'none' ELSE 'low' END");

        Assert.Equal(["1,low", "2,high", "3,none"], Rows(db, "SELECT id, grade FROM t").Order());
    }

    [Fact]
    public void Update_SetToASimpleCase_ComparesTheOperand()
    {
        var db = Seed();

        db.ExecuteSQL("UPDATE t SET grade = CASE kind WHEN 'a' THEN 'first' WHEN 'b' THEN 'second' END");

        Assert.Equal(["1,first", "2,second", "3,NULL"], Rows(db, "SELECT id, grade FROM t").Order());
    }

    [Fact]
    public void Update_SetToANestedCaseWithArithmetic_IsComputedPerRow()
    {
        var db = Seed();

        db.ExecuteSQL("UPDATE t SET n = CASE WHEN n IS NULL THEN 0 ELSE CASE WHEN n > 5 THEN n * 10 ELSE n + 1 END END");

        Assert.Equal(["1,3", "2,70", "3,0"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Fact]
    public void Check_WrittenWithCase_IsEnforced()
    {
        var db = _factory.Create(Path.Combine(_root, $"db_{Guid.NewGuid():N}"), "pw");
        db.ExecuteSQL("CREATE TABLE p (id INTEGER PRIMARY KEY, kind TEXT, amount INTEGER, " +
                      "CHECK (CASE WHEN kind = 'refund' THEN amount < 0 ELSE amount > 0 END))");

        db.ExecuteSQL("INSERT INTO p (id, kind, amount) VALUES (1, 'sale', 10)");
        db.ExecuteSQL("INSERT INTO p (id, kind, amount) VALUES (2, 'refund', -5)");
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO p (id, kind, amount) VALUES (3, 'refund', 5)"));

        Assert.Equal(["1,10", "2,-5"], Rows(db, "SELECT id, amount FROM p").Order());
    }

    [Fact]
    public void OrderBy_ACaseKey_SortsByIt()
    {
        var db = Seed();

        Assert.Equal(["2", "1", "3"], Rows(db, "SELECT id FROM t ORDER BY CASE kind WHEN 'b' THEN 0 WHEN 'a' THEN 1 ELSE 2 END"));
    }

    [Fact]
    public void Select_ACaseColumn_IsEvaluatedPerRow()
    {
        var db = Seed();

        Assert.Equal(["1,small", "2,big", "3,unknown"],
            Rows(db, "SELECT id, CASE WHEN n > 5 THEN 'big' WHEN n IS NULL THEN 'unknown' ELSE 'small' END AS size FROM t").Order());
    }
}
