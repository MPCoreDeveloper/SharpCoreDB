// <copyright file="UpdateReturningTests.cs" company="MPCoreDeveloper">
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
/// <c>UPDATE … RETURNING</c> reports the rows the UPDATE changed, as the UPDATE left them.
/// <para>
/// Up to 2.1.4 the rows were found by running the WHERE clause again after the update, so a SET that changed a column
/// the WHERE tests returned nothing: <c>UPDATE t SET status = 'done' WHERE status = 'open' RETURNING id</c> and
/// <c>UPDATE t SET n = n + 10 WHERE n &lt; 5 RETURNING n</c> both came back empty.
/// </para>
/// </summary>
public sealed class UpdateReturningTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_Returning_{Guid.NewGuid():N}");

    public UpdateReturningTests()
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
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var db = _factory.Create(path, "pw");
        db.ExecuteSQL(mode == "page-based" ? ddl + " STORAGE = PAGE_BASED" : ddl);
        return (db, path);
    }

    private static string[] Rows(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => string.Join(",", r.Values.Select(v => v is null or DBNull ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture))))];

    [Theory]
    [MemberData(nameof(Modes))]
    public void UpdateReturning_SetChangesTheWhereColumn_ReturnsTheChangedRows(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, status TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, status) VALUES (1, 'open'), (2, 'open'), (3, 'done')");

        var returned = Rows(db, "UPDATE t SET status = 'done' WHERE status = 'open' RETURNING id, status");

        Assert.Equal(["1,done", "2,done"], returned.Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UpdateReturning_ComputedSetValue_ReturnsTheNewValues(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2), (3, 9)");

        var returned = Rows(db, "UPDATE t SET n = n + 10 WHERE n < 5 RETURNING id, n");

        Assert.Equal(["1,11", "2,12"], returned.Order());
        Assert.Equal(["1,11", "2,12", "3,9"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UpdateReturning_Star_ReturnsEveryColumnOfTheUpdatedRow(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (1, 1, 'a'), (2, 2, 'b')");

        Assert.Equal(["1,0,a"], Rows(db, "UPDATE t SET n = 0 WHERE id = 1 RETURNING *"));
    }
}
