// <copyright file="ExecuteQueryFastWhereTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// <see cref="Database.ExecuteQueryFast"/> filters with the same WHERE semantics as <c>ExecuteQuery</c>, and the public
/// <see cref="SqlParserPerformanceOptimizations.CompileWhereClause"/> never accepts a row it cannot evaluate.
/// <para>
/// Up to 2.1.4 the fast path keyed each row's values <c>col0</c>, <c>col1</c>, … before evaluating the WHERE, so a
/// condition naming a column matched nothing (<c>grp = 2</c> returned 0 rows), and a condition the compiled predicate
/// could not parse fell back to "accept every row" (<c>length(name) = 3</c> returned the whole table).
/// </para>
/// </summary>
public sealed class ExecuteQueryFastWhereTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_QueryFast_{Guid.NewGuid():N}");
    private readonly Database _db;

    public ExecuteQueryFastWhereTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _db = (Database)factory.Create(_root, "pw");
        _db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, grp INTEGER, name TEXT)");
        _db.ExecuteSQL("INSERT INTO t (id, grp, name) VALUES (1, 2, 'bb')");
        _db.ExecuteSQL("INSERT INTO t (id, grp, name) VALUES (2, 1, 'a')");
        _db.ExecuteSQL("INSERT INTO t (id, grp, name) VALUES (3, 2, 'ccc')");
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

    [Theory]
    [InlineData("SELECT * FROM t WHERE grp = 2", new[] { 1, 3 })]
    [InlineData("SELECT * FROM t WHERE name = 'a'", new[] { 2 })]
    [InlineData("SELECT * FROM t WHERE grp IN (1)", new[] { 2 })]
    [InlineData("SELECT * FROM t WHERE name LIKE 'c%'", new[] { 3 })]
    [InlineData("SELECT * FROM t WHERE grp = 2 AND name = 'bb'", new[] { 1 })]
    [InlineData("SELECT * FROM t WHERE length(name) = 3", new[] { 3 })]
    public void ExecuteQueryFast_WithWhere_ReturnsTheMatchingRows(string sql, int[] expectedIds)
    {
        var ids = _db.ExecuteQueryFast(sql)
            .Select(r => Convert.ToInt32(r.GetValueBoxed(0), CultureInfo.InvariantCulture))
            .Order()
            .ToArray();

        Assert.Equal(expectedIds, ids);
    }

    [Fact]
    public void CompileWhereClause_WithAConditionItCannotParse_EvaluatesItInsteadOfAcceptingEveryRow()
    {
        var predicate = SqlParserPerformanceOptimizations.CompileWhereClause("length(name) = 3");

        Assert.False(predicate(Row("bb")));
        Assert.True(predicate(Row("ccc")));
    }

    private static Dictionary<string, object> Row(string name) =>
        new(StringComparer.OrdinalIgnoreCase) { ["id"] = 1L, ["name"] = name };
}
