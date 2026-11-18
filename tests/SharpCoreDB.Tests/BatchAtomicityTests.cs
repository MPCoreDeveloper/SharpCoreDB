// <copyright file="BatchAtomicityTests.cs" company="MPCoreDeveloper">
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
/// <c>ExecuteBatchSQL</c> is one atomic unit: a statement that fails leaves every table as it was before the batch.
/// <para>
/// Up to 2.1.4 two paths broke that. A batch with a SELECT, or one mixing statement kinds, ran its statements one at a
/// time without a transaction, so the statements before a failure stayed (on every storage mode). And the grouped
/// executor's own transaction rolled back the storage buffer only, which PageBased tables do not write through, so a
/// PageBased table kept the batch's earlier changes.
/// </para>
/// </summary>
public sealed class BatchAtomicityTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_BatchAtomic_{Guid.NewGuid():N}");

    public BatchAtomicityTests()
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

    private IDatabase Seed(string mode)
    {
        var db = Open(mode, "CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER CHECK (n < 10), name TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, n, name) VALUES (1, 1, 'a'), (2, 2, 'b')");
        return db;
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void UpdateBatch_FailingOnItsSecondStatement_ChangesNothing(string mode)
    {
        var db = Seed(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteBatchSQL(["UPDATE t SET n = 5 WHERE id = 1", "UPDATE t SET n = 50 WHERE id = 2"]));

        Assert.Equal(["1,1", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void MixedBatch_FailingOnItsLastStatement_ChangesNothing(string mode)
    {
        var db = Seed(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteBatchSQL([
            "UPDATE t SET n = 6 WHERE id = 1",
            "INSERT INTO t (id, n, name) VALUES (3, 3, 'c')",
            "DELETE FROM t WHERE id = 2",
            "UPDATE t SET n = n + 100 WHERE id = 1"]));

        Assert.Equal(["1,1", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void BatchWithASelect_FailingAfterAWrite_ChangesNothing(string mode)
    {
        var db = Seed(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteBatchSQL([
            "UPDATE t SET n = 7 WHERE id = 1",
            "SELECT id FROM t",
            "INSERT INTO t (id, n, name) VALUES (1, 1, 'duplicate key')"]));

        Assert.Equal(["1,1", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void MixedBatch_WithoutAFailure_AppliesEveryStatementInOrder(string mode)
    {
        var db = Seed(mode);

        db.ExecuteBatchSQL([
            "DELETE FROM t WHERE id = 2",
            "INSERT INTO t (id, n, name) VALUES (2, 9, 'again')",
            "UPDATE t SET n = n + 1 WHERE id = 1",
            "SELECT id FROM t"]);

        Assert.Equal(["1,2", "2,9"], Rows(db, "SELECT id, n FROM t").Order());
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void FailingBatch_InsideAnOuterTransaction_IsUndoneByTheOuterRollback(string mode)
    {
        var db = Seed(mode);

        db.BeginBatchUpdate();
        db.ExecuteSQL("UPDATE t SET n = 3 WHERE id = 2");
        Assert.ThrowsAny<Exception>(() => db.ExecuteBatchSQL(["UPDATE t SET n = 4 WHERE id = 1", "UPDATE t SET n = 40 WHERE id = 1"]));
        db.CancelBatchUpdate();

        Assert.Equal(["1,1", "2,2"], Rows(db, "SELECT id, n FROM t").Order());
    }
}
