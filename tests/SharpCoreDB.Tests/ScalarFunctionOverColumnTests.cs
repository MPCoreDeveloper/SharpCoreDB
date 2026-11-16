// <copyright file="ScalarFunctionOverColumnTests.cs" company="MPCoreDeveloper">
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
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// A scalar function applied to a column — <c>strftime('%Y', at)</c>, <c>upper(name)</c>, <c>length(name)</c> — is
/// evaluated per row, in the SELECT list and in WHERE, on both storage modes, as SQLite does.
/// <para>
/// Up to 2.1.4 only a SELECT without FROM evaluated functions. Over a table the SELECT list returned the whole row
/// unchanged (so <c>SELECT upper(name) FROM t</c> read back the <c>id</c> column), and every WHERE evaluator
/// treated the left operand as a column name: <c>strftime(…, at) = …</c> matched no row. linq2db's SQLite dialect
/// wraps every <see cref="DateTime"/> comparison in <c>strftime</c>, so every date filter written through the
/// linq2db adapter returned nothing.
/// </para>
/// </summary>
public sealed class ScalarFunctionOverColumnTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_ScalarOverColumn_{Guid.NewGuid():N}");

    public ScalarFunctionOverColumnTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

    public static TheoryData<string> StorageModes => new() { "directory", "single-file" };

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

    // ── SELECT list ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Select_FunctionOfColumn_ReturnsOnlyTheFunctionValue(string mode)
    {
        var db = Open(mode);

        var row = Assert.Single(db.ExecuteQuery("SELECT upper(name) FROM ev WHERE id = 1"));

        Assert.Equal("ALPHA", Assert.Single(row).Value);
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Select_ColumnAndAliasedFunction_KeepsOrderAndAlias(string mode)
    {
        var db = Open(mode);

        var row = Assert.Single(db.ExecuteQuery("SELECT id, strftime('%Y', at) AS y FROM ev WHERE id = 2"));

        Assert.Equal(["id", "y"], row.Keys);
        Assert.Equal(2L, Convert.ToInt64(row["id"], CultureInfo.InvariantCulture));
        Assert.Equal("2025", row["y"]);
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Select_DateTimeFunctionsOfColumn_UseSqliteFormats(string mode)
    {
        var db = Open(mode);

        var row = Assert.Single(db.ExecuteQuery("SELECT date(at), time(at), datetime(at) FROM ev WHERE id = 3"));

        Assert.Equal(["2026-03-10", "23:59:59", "2026-03-10 23:59:59"], row.Values);
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Select_FunctionOfNullColumn_IsNull(string mode)
    {
        var db = Open(mode);

        var row = Assert.Single(db.ExecuteQuery("SELECT length(name) AS n FROM ev WHERE id = 4"));

        Assert.True(row["n"] is null or DBNull);
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Select_CastOfFunction_ReturnsInteger(string mode)
    {
        var db = Open(mode);

        var row = Assert.Single(db.ExecuteQuery("SELECT CAST(strftime('%m', at) AS INTEGER) AS m FROM ev WHERE id = 2"));

        Assert.Equal(6L, row["m"]);
    }

    // ── WHERE ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("directory", "strftime('%Y', at) = '2026'", new[] { 1, 3 })]
    [InlineData("single-file", "strftime('%Y', at) = '2026'", new[] { 1, 3 })]
    [InlineData("directory", "strftime('%Y-%m-%d %H:%M:%f', [x].[at]) = strftime('%Y-%m-%d %H:%M:%f', '2026-01-01 12:00:00.000')", new[] { 1 })]
    [InlineData("single-file", "strftime('%Y-%m-%d %H:%M:%f', [x].[at]) = strftime('%Y-%m-%d %H:%M:%f', '2026-01-01 12:00:00.000')", new[] { 1 })]
    [InlineData("directory", "strftime('%Y-%m-%d %H:%M:%f', [x].[at]) > strftime('%Y-%m-%d %H:%M:%f', '2026-01-01 00:00:00.000')", new[] { 1, 3 })]
    [InlineData("single-file", "strftime('%Y-%m-%d %H:%M:%f', [x].[at]) > strftime('%Y-%m-%d %H:%M:%f', '2026-01-01 00:00:00.000')", new[] { 1, 3 })]
    [InlineData("directory", "length(name) = 4", new[] { 2 })]
    [InlineData("single-file", "length(name) = 4", new[] { 2 })]
    [InlineData("directory", "upper(name) = 'GAMMA'", new[] { 3 })]
    [InlineData("single-file", "upper(name) = 'GAMMA'", new[] { 3 })]
    [InlineData("directory", "substr(name, 1, 1) = 'A'", new[] { 1 })]
    [InlineData("single-file", "substr(name, 1, 1) = 'A'", new[] { 1 })]
    [InlineData("directory", "CAST(strftime('%Y', at) AS INTEGER) = 2025", new[] { 2 })]
    [InlineData("single-file", "CAST(strftime('%Y', at) AS INTEGER) = 2025", new[] { 2 })]
    [InlineData("directory", "lower(name) LIKE 'g%'", new[] { 3 })]
    [InlineData("single-file", "lower(name) LIKE 'g%'", new[] { 3 })]
    [InlineData("directory", "date(at) = '2026-03-10' AND id > 1", new[] { 3 })]
    [InlineData("single-file", "date(at) = '2026-03-10' AND id > 1", new[] { 3 })]
    [InlineData("directory", "strftime('%Y', at) = '2026' OR name = 'beta'", new[] { 1, 2, 3 })]
    [InlineData("single-file", "strftime('%Y', at) = '2026' OR name = 'beta'", new[] { 1, 2, 3 })]
    [InlineData("directory", "length(name) IS NULL", new[] { 4 })]
    [InlineData("single-file", "length(name) IS NULL", new[] { 4 })]
    // Two text values compare as text, as in SQLite: '01' is not '1'.
    [InlineData("directory", "substr(at, 6, 2) = '1'", new int[0])]
    [InlineData("single-file", "substr(at, 6, 2) = '1'", new int[0])]
    [InlineData("directory", "substr(at, 6, 2) = '01'", new[] { 1 })]
    [InlineData("single-file", "substr(at, 6, 2) = '01'", new[] { 1 })]
    public void Where_FunctionOfColumn_FiltersRows(string mode, string where, int[] expectedIds)
    {
        var db = Open(mode);

        var ids = Ids(db.ExecuteQuery($"SELECT id FROM ev x WHERE {where}"));

        Assert.Equal(expectedIds, ids);
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void CountStar_WithFunctionInWhere_CountsMatchingRows(string mode)
    {
        var db = Open(mode);

        var row = Assert.Single(db.ExecuteQuery("SELECT COUNT(*) FROM ev WHERE strftime('%Y', at) = '2026'"));

        Assert.Equal(2L, Convert.ToInt64(Assert.Single(row).Value, CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Update_WithFunctionInWhere_ChangesOnlyMatchingRows(string mode)
    {
        var db = Open(mode);

        db.ExecuteSQL("UPDATE ev SET name = 'changed' WHERE strftime('%Y', at) = '2025'");

        var names = db.ExecuteQuery("SELECT id, name FROM ev")
            .ToDictionary(r => Convert.ToInt32(r["id"], CultureInfo.InvariantCulture), r => r["name"] as string);
        Assert.Equal("Alpha", names[1]);
        Assert.Equal("changed", names[2]);
        Assert.Equal("Gamma", names[3]);
        Assert.Null(names[4]);
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Delete_WithFunctionInWhere_DeletesOnlyMatchingRows(string mode)
    {
        var db = Open(mode);

        db.ExecuteSQL("DELETE FROM ev WHERE date(at) = '2026-03-10'");

        Assert.Equal([1, 2, 4], Ids(db.ExecuteQuery("SELECT id FROM ev")));
    }

    [Theory]
    [MemberData(nameof(StorageModes))]
    public void Where_FunctionOfColumnWithSubquery_FiltersRowsOnTheAstPath(string mode)
    {
        // A subquery routes the statement to the AST executor, which mapped a function operand to NULL.
        var db = Open(mode);

        var ids = Ids(db.ExecuteQuery("SELECT id FROM ev WHERE strftime('%Y', at) = '2026' AND id IN (SELECT id FROM ev)"));

        Assert.Equal([1, 3], ids);
    }

    // ── SELECT without FROM (literal arguments) ─────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT date('2026-03-10 23:59:59')", "2026-03-10")]
    [InlineData("SELECT time('2026-03-10 23:59:59.999')", "23:59:59")]
    [InlineData("SELECT datetime('2026-03-10T23:59:59')", "2026-03-10 23:59:59")]
    [InlineData("SELECT strftime('%Y-%m-%d %H:%M', '2026-01-02T03:04:05+02:00')", "2026-01-02 01:04")]
    [InlineData("SELECT date('2026-01-31', '+1 month')", "2026-03-03")]
    [InlineData("SELECT date('2026-03-10', 'start of month')", "2026-03-01")]
    [InlineData("SELECT strftime('%j %w %%', '2026-03-10')", "069 2 %")]
    [InlineData("SELECT upper('abc')", "ABC")]
    public void SelectWithoutFrom_ScalarFunctions_MatchSqlite(string sql, string expected)
    {
        var db = Open("directory");

        var row = Assert.Single(db.ExecuteQuery(sql));

        Assert.Equal(expected, Convert.ToString(Assert.Single(row).Value, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("SELECT julianday('2000-01-01 12:00:00')", 2451545.0)]
    [InlineData("SELECT unixepoch('1970-01-02 00:00:00')", 86400.0)]
    public void SelectWithoutFrom_NumericDateFunctions_MatchSqlite(string sql, double expected)
    {
        var db = Open("directory");

        var row = Assert.Single(db.ExecuteQuery(sql));

        Assert.Equal(expected, Convert.ToDouble(Assert.Single(row).Value, CultureInfo.InvariantCulture), 6);
    }

    [Fact]
    public void SelectWithoutFrom_InvalidDate_IsNull()
    {
        var db = Open("directory");

        var row = Assert.Single(db.ExecuteQuery("SELECT date('not a date') AS d"));

        Assert.True(row["d"] is null or DBNull);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private IDatabase Open(string mode)
    {
        var db = mode == "single-file"
            ? _factory.CreateWithOptions(Path.Combine(_root, $"db_{Guid.NewGuid():N}.scdb"), "pw", DatabaseOptions.CreateSingleFileDefault())
            : _factory.Create(Path.Combine(_root, $"db_{Guid.NewGuid():N}"), "pw");

        db.ExecuteSQL("CREATE TABLE ev (id INTEGER PRIMARY KEY, name TEXT, at TEXT)");
        db.ExecuteSQL("INSERT INTO ev (id, name, at) VALUES (1, 'Alpha', '2026-01-01 12:00:00.000')");
        db.ExecuteSQL("INSERT INTO ev (id, name, at) VALUES (2, 'beta', '2025-06-15 08:30:00.250')");
        db.ExecuteSQL("INSERT INTO ev (id, name, at) VALUES (3, 'Gamma', '2026-03-10 23:59:59.999')");
        db.ExecuteSQL("INSERT INTO ev (id, name, at) VALUES (4, NULL, NULL)");
        return db;
    }

    private static int[] Ids(List<Dictionary<string, object>> rows) =>
        [.. rows.Select(r => Convert.ToInt32(r["id"], CultureInfo.InvariantCulture)).Order()];
}
