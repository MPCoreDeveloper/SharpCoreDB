// <copyright file="BatchUpdateClassificationTests.cs" company="MPCoreDeveloper">
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
/// An UPDATE that <c>ExecuteBatchSQL</c>'s UPDATE fast path declines must run as that UPDATE.
/// <para>
/// Up to 2.1.4 the DELETE classifier, tried next, accepted the canonical UPDATE shape too: an UPDATE with a quoted SET
/// column (<c>SET "n" = 5</c>, as linq2db and EF Core write it) was executed as <c>DELETE … WHERE id = 1</c>, and the
/// batch deleted the rows it was asked to update.
/// </para>
/// </summary>
public sealed class BatchUpdateClassificationTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_BatchUpdate_{Guid.NewGuid():N}");

    public BatchUpdateClassificationTests()
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
    public void BatchUpdate_QuotedSetColumn_UpdatesTheRowsAndDeletesNone(string mode)
    {
        var (db, _) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, name TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (1, 1, 'ann'), (2, 2, 'bob')");

        db.ExecuteBatchSQL(["UPDATE t SET \"n\" = 5 WHERE id = 1", "UPDATE t SET \"name\" = 'z' WHERE id = 2"]);

        Assert.Equal(["1,5,ann", "2,2,z"], Rows(db, "SELECT id, n, name FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void BatchUpdate_QuotedSetColumn_IsKeptAfterReopen(string mode)
    {
        var (db, path) = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER)");
        db.ExecuteSQL("INSERT INTO t (id, n) VALUES (1, 1), (2, 2)");
        db.ExecuteBatchSQL(["UPDATE t SET \"n\" = 5 WHERE id = 1", "UPDATE t SET \"n\" = 6 WHERE id = 2"]);
        (db as IDisposable)?.Dispose();

        var reopened = Reopen(mode, path);
        Assert.Equal(["1,5", "2,6"], Rows(reopened, "SELECT id, n FROM t").Order());
        (reopened as IDisposable)?.Dispose();
    }
}
