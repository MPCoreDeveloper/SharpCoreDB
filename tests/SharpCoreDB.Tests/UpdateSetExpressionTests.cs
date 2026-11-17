// <copyright file="UpdateSetExpressionTests.cs" company="MPCoreDeveloper">
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
/// An UPDATE whose SET value is an expression (<c>SET n = n + 1</c>), statement atomicity of a multi-row UPDATE, and an
/// UPDATE without WHERE.
/// <para>
/// Up to 2.1.4 every SET value was parsed as a literal of the column's type: <c>SET n = n + 5</c> failed ("Invalid value
/// 'n + 5'") and <c>SET label = name</c> on a TEXT column stored the four letters <c>name</c>. NOT NULL and CHECK were
/// checked row by row as rows were written, so a statement that failed on its second row kept its first. And an UPDATE
/// without WHERE threw "Invalid UPDATE syntax", although SQLite changes every row.
/// </para>
/// </summary>
public sealed class UpdateSetExpressionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_UpdateSet_{Guid.NewGuid():N}");

    public UpdateSetExpressionTests()
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

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_SetValueReadsTheRow_IsComputedPerRow(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, name TEXT, label TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, n, name, label) VALUES (1, 1, 'ann', 'x'), (2, 2, 'bob', 'y')");

        db.ExecuteSQL("UPDATE t SET n = n * 10 + id, label = upper(name) || '-' || n WHERE id > 0");

        Assert.Equal(["1,11,ann,ANN-1", "2,22,bob,BOB-2"], Rows(db, "SELECT id, n, name, label FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_SwappingTwoColumns_ReadsTheValuesFromBeforeTheStatement(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, a TEXT, b TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, a, b) VALUES (1, 'left', 'right')");

        db.ExecuteSQL("UPDATE t SET a = b, b = a WHERE id = 1");

        Assert.Equal(["1,right,left"], Rows(db, "SELECT id, a, b FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_ConstantExpression_IsEvaluatedForANumericColumn(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 0)");

        db.ExecuteSQL("UPDATE t SET n = 2 * 3 WHERE id = 1");

        Assert.Equal(["1,6"], Rows(db, "SELECT id, n FROM t"));
    }

    [Fact]
    public void Update_ComputedValues_AreConvertedToEachColumnsType()
    {
        var (db, _) = Open("columnar", "CREATE TABLE t (id INTEGER PRIMARY KEY, r REAL, big BIGINT, flag BOOLEAN, d DATETIME, label TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, r, big, flag, d, label) VALUES (1, 1.5, 10, 0, '2026-01-01 00:00:00', 'x')");

        db.ExecuteSQL("UPDATE t SET r = r * 2, big = big * 1000000000, flag = length(label) > 0, " +
                      "d = datetime(d, '+1 day'), label = id || '-' || r WHERE id = 1");

        var row = db.ExecuteQuery("SELECT r, big, flag, d, label FROM t WHERE id = 1").Single();
        Assert.Equal(3.0, Convert.ToDouble(row["r"], CultureInfo.InvariantCulture));
        Assert.Equal(10_000_000_000L, Convert.ToInt64(row["big"], CultureInfo.InvariantCulture));
        Assert.True(Convert.ToBoolean(row["flag"], CultureInfo.InvariantCulture));
        Assert.Equal(new DateTime(2026, 1, 2), Convert.ToDateTime(row["d"], CultureInfo.InvariantCulture).Date);
        Assert.Equal("1-1.5", row["label"]);
    }

    [Fact]
    public void Update_ComputedValueTooLargeForAnIntegerColumn_IsRejectedAndChangesNothing()
    {
        var (db, _) = Open("columnar", "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 2000000000)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET n = n * 4 WHERE id = 1"));

        Assert.Equal(["1,2000000000"], Rows(db, "SELECT id, n FROM t"));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void BatchUpdate_SetValueReadsTheRow_SeesTheEarlierStatement(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2)");

        db.ExecuteBatchSQL(["UPDATE t SET n = n + 1 WHERE id = 1", "UPDATE t SET n = n + 1 WHERE id = 1"]);

        Assert.Equal(["1,3", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Fact]
    public void Update_SetValueReadsTheRow_IsComputedPerRowOnSingleFile()
    {
        var (db, _) = Open("single-file", "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (1, 1, 'ann'), (2, 2, 'bob')");

        db.ExecuteSQL("UPDATE t SET n = n + 5, name = upper(name) WHERE id > 0");

        Assert.Equal(["1,6,ANN", "2,7,BOB"], Rows(db, "SELECT id, n, name FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_CheckFailsOnALaterRow_LeavesEveryRowUnchanged(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER CHECK (n < 10))");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 8), (3, 2)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET n = n + 5 WHERE id > 0"));

        Assert.Equal(["1,1", "2,8", "3,2"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_LiteralFailingAColumnCheckOnALaterRow_LeavesEveryRowUnchanged(string mode)
    {
        // Row 1 passes (2 > 1), row 2 fails (2 > 2): up to 2.1.4 row 1 was already written when row 2 failed.
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER CHECK (n > id))");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 5), (2, 5), (3, 5)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET n = 2 WHERE id > 0"));

        Assert.Equal(["1,5", "2,5", "3,5"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_LiteralFailingATableCheckOnALaterRow_LeavesEveryRowUnchanged(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, CHECK (n <> id))");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 0), (2, 0), (3, 0)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET n = 2 WHERE id > 0"));

        Assert.Equal(["1,0", "2,0", "3,0"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_NotNullFailsOnALaterRow_LeavesEveryRowUnchanged(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER NOT NULL, o INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n, o) VALUES (1, 1, 1), (2, 2, NULL), (3, 3, 3)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET n = o WHERE id > 0"));

        Assert.Equal(["1,1,1", "2,2,NULL", "3,3,3"], Rows(db, "SELECT id, n, o FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_WithoutWhere_ChangesEveryRow(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, label) VALUES (1, 'a'), (2, 'b')");

        db.ExecuteSQL("UPDATE t SET label = 'z'");

        Assert.Equal(["1,z", "2,z"], Rows(db, "SELECT id, label FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_LiteralContainingTheWordWhere_KeepsTheWholeLiteral(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, label TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, label) VALUES (1, 'a'), (2, 'b')");

        db.ExecuteSQL("UPDATE t SET label = 'see where it goes' WHERE id = 2");

        Assert.Equal(["1,a", "2,see where it goes"], Rows(db, "SELECT id, label FROM t").Order());
    }
}
