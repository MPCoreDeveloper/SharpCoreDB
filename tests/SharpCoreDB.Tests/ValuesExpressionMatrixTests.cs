// <copyright file="ValuesExpressionMatrixTests.cs" company="MPCoreDeveloper">
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
/// A data-driven matrix for option 2 of issue #525. Every unquoted expression in <c>INSERT … VALUES</c> or
/// <c>UPDATE … SET</c> must store exactly what its equivalent literal stores — for every value type, storage mode and
/// statement shape. Each case is a differential test: it inserts/updates an expression and the literal it evaluates to,
/// then asserts the two stored values are identical. The matrix is several hundred cases.
/// </summary>
public sealed class ValuesExpressionMatrixTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_ValuesMatrix_{Guid.NewGuid():N}");

    public ValuesExpressionMatrixTests()
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

    private IDatabase Open(string mode, string ddl)
    {
        var path = Path.Combine(_root, mode == "single-file" ? $"db_{Guid.NewGuid():N}.scdb" : $"db_{Guid.NewGuid():N}");
        var db = mode == "single-file"
            ? _factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault())
            : _factory.Create(path, "pw");
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        return db;
    }

    private static string Scalar(IDatabase db, string sql)
    {
        var value = db.ExecuteQuery(sql).First().Values.First();
        return value is null or DBNull ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static readonly (string Expr, string Literal)[] IntegerExpressions =
    [
        ("2 * 3", "6"),
        ("7 + 2", "9"),
        ("10 - 4", "6"),
        ("10 / 4", "2"),
        ("10 % 3", "1"),
        ("2 * 3 + 1", "7"),
        ("2 + 3 * 4", "14"),
        ("(2 + 3) * 4", "20"),
        ("100 / 10 / 2", "5"),
        ("abs(-7)", "7"),
        ("0 - 5", "-5"),
    ];

    private static readonly (string Expr, string Literal)[] TextExpressions =
    [
        ("upper('abc')", "'ABC'"),
        ("lower('ABC')", "'abc'"),
        ("length('hello')", "5"),
        ("'a' || 'b'", "'ab'"),
        ("upper('ab') || lower('CD')", "'ABcd'"),
        ("substr('abcdef', 2, 3)", "'bcd'"),
        ("upper(lower('AbC'))", "'ABC'"),
    ];

    private static readonly string[] ValueTypes = ["INTEGER", "BIGINT", "REAL", "DECIMAL", "TEXT", "BOOLEAN"];
    private static readonly string[] AllModes = ["columnar", "page-based", "single-file"];
    private static readonly string[] TwoModes = ["columnar", "page-based"];

    public static TheoryData<string, string, string, string> InsertIntegerCases => Build(AllModes, ValueTypes, IntegerExpressions);
    public static TheoryData<string, string, string, string> UpdateIntegerCases => Build(TwoModes, ValueTypes, IntegerExpressions);
    public static TheoryData<string, string, string, string> BatchIntegerCases => Build(TwoModes, ValueTypes, IntegerExpressions);
    public static TheoryData<string, string, string, string> InsertTextCases => Build(AllModes, ["TEXT"], TextExpressions);
    public static TheoryData<string, string, string, string> UpdateTextCases => Build(TwoModes, ["TEXT"], TextExpressions);
    public static TheoryData<string, string, string, string> BatchTextCases => Build(TwoModes, ["TEXT"], TextExpressions);

    private static TheoryData<string, string, string, string> Build(string[] modes, string[] types, (string Expr, string Literal)[] expressions)
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var mode in modes)
        {
            foreach (var type in types)
            {
                foreach (var (expr, literal) in expressions)
                {
                    data.Add(mode, type, expr, literal);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InsertIntegerCases))]
    [MemberData(nameof(InsertTextCases))]
    public void Insert_Expression_StoresSameAsEquivalentLiteral(string mode, string type, string expr, string literal)
    {
        var db = Open(mode, $"CREATE TABLE t (id INTEGER PRIMARY KEY, v {type})");

        db.ExecuteSQL($"INSERT INTO t (id, v) VALUES (1, {expr})");
        db.ExecuteSQL($"INSERT INTO t (id, v) VALUES (2, {literal})");

        Assert.Equal(Scalar(db, "SELECT v FROM t WHERE id = 2"), Scalar(db, "SELECT v FROM t WHERE id = 1"));
    }

    [Theory]
    [MemberData(nameof(UpdateIntegerCases))]
    [MemberData(nameof(UpdateTextCases))]
    public void Update_Expression_StoresSameAsEquivalentLiteral(string mode, string type, string expr, string literal)
    {
        var db = Open(mode, $"CREATE TABLE t (id INTEGER PRIMARY KEY, v {type})");
        db.ExecuteSQL("INSERT INTO t (id) VALUES (1), (2)");

        db.ExecuteSQL($"UPDATE t SET v = {expr} WHERE id = 1");
        db.ExecuteSQL($"UPDATE t SET v = {literal} WHERE id = 2");

        Assert.Equal(Scalar(db, "SELECT v FROM t WHERE id = 2"), Scalar(db, "SELECT v FROM t WHERE id = 1"));
    }

    [Theory]
    [MemberData(nameof(BatchIntegerCases))]
    [MemberData(nameof(BatchTextCases))]
    public void BatchInsert_Expression_StoresSameAsEquivalentLiteral(string mode, string type, string expr, string literal)
    {
        var db = Open(mode, $"CREATE TABLE t (id INTEGER PRIMARY KEY, v {type})");

        db.ExecuteBatchSQL([$"INSERT INTO t (id, v) VALUES (1, {expr})", $"INSERT INTO t (id, v) VALUES (2, {literal})"]);

        Assert.Equal(Scalar(db, "SELECT v FROM t WHERE id = 2"), Scalar(db, "SELECT v FROM t WHERE id = 1"));
    }
}
