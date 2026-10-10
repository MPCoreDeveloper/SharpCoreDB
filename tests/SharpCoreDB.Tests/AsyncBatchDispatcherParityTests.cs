// <copyright file="AsyncBatchDispatcherParityTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// The two batch dispatchers must stay twins. Since 2026-09-27 (session 75) <c>ExecuteBatchSQL</c> and
/// <c>ExecuteBatchSQLAsync</c> run one shared classifier and one shared executor, and these facts pin the
/// behaviour that had drifted apart before that:
/// <list type="bullet">
/// <item>the INSERT column list — SQLite assigns VALUES by name, so a permuted or a partial list is legal
/// and must land in the columns it names (measured before the fix: <c>(score, id, name) VALUES (9.5, 1,
/// 'perm')</c> was stored as <c>id=0 name=1 score=0</c>, a silent wrong row);</item>
/// <item>the transaction contract — a batch called while the caller already holds a transaction must not
/// open a second one (every provider throws <c>Transaction already in progress</c>), and must leave the
/// commit to the caller.</item>
/// </list>
/// </summary>
public sealed class AsyncBatchDispatcherParityTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dir;

    public AsyncBatchDispatcherParityTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dir = Path.Combine(Path.GetTempPath(), $"SCDB_AsyncBatchParity_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private IDatabase Create(string name)
        => _factory.Create(
            Path.Combine(_dir, name),
            "pw",
            isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true });

    /// <summary>Renders one table's rows, one line per row, keys sorted so the rendering is stable.</summary>
    private static List<string> Rows(IDatabase db)
    {
        List<string> lines = [];
        foreach (var row in db.ExecuteQuery("SELECT * FROM docs"))
        {
            lines.Add(string.Join(
                "|",
                row.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => string.Format(
                    CultureInfo.InvariantCulture, "{0}={1}", kv.Key.ToLowerInvariant(), kv.Value ?? "<null>"))));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>Reads the named column of the first row the query returns.</summary>
    private static string? Scalar(IDatabase db, string sql, string column)
    {
        var rows = db.ExecuteQuery(sql);
        if (rows.Count == 0)
        {
            return null;
        }

        foreach (var key in rows[0].Keys)
        {
            if (key.Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToString(rows[0][key], CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private const string Ddl = "CREATE TABLE docs (id INTEGER, name TEXT, score REAL)";

    /// <summary>
    /// Same statements, both dispatchers, identical rows — the list-less shape the batch harness uses.
    /// </summary>
    [Fact]
    public async Task ExecuteBatchSQLAsync_SameStatementsAsTheSyncDispatcher_StoresTheSameRows()
    {
        string[] statements =
        [
            "INSERT INTO docs VALUES (1, 'a', 1.5)",
            "INSERT INTO docs VALUES (2, 'b', 2.5)"
        ];

        await using IDatabase sync = Create("same_sync");
        sync.ExecuteSQL(Ddl);
        sync.ExecuteBatchSQL(statements);

        await using IDatabase asyncDb = Create("same_async");
        asyncDb.ExecuteSQL(Ddl);
        await asyncDb.ExecuteBatchSQLAsync(statements);

        Assert.Equal(["id=1|name=a|score=1.5", "id=2|name=b|score=2.5"], Rows(sync));
        Assert.Equal(Rows(sync), Rows(asyncDb));
    }

    /// <summary>
    /// A permuted full column list must assign by name, as SQLite does. Before session 75 the array fast
    /// path ignored the list and wrote the values positionally against the table's column order, so this
    /// exact statement stored <c>id=0 name=1 score=0</c> without an error.
    /// </summary>
    [Fact]
    public async Task ExecuteBatchSQLAsync_PermutedColumnList_AssignsValuesByName()
    {
        string[] statements = ["INSERT INTO docs (score, id, name) VALUES (9.5, 1, 'perm')"];

        await using IDatabase sync = Create("permuted_sync");
        sync.ExecuteSQL(Ddl);
        sync.ExecuteBatchSQL(statements);

        await using IDatabase asyncDb = Create("permuted_async");
        asyncDb.ExecuteSQL(Ddl);
        await asyncDb.ExecuteBatchSQLAsync(statements);

        foreach (IDatabase db in new[] { sync, asyncDb })
        {
            Assert.Equal("1", Scalar(db, "SELECT id, name, score FROM docs", "id"));
            Assert.Equal("perm", Scalar(db, "SELECT id, name, score FROM docs", "name"));
            Assert.Equal("9.5", Scalar(db, "SELECT id, name, score FROM docs", "score"));
        }

        Assert.Equal(Rows(sync), Rows(asyncDb));
    }

    /// <summary>
    /// Two different column shapes in one batch: each row is assigned by its own list, and the shapes do not
    /// share a column order (the buckets are keyed per table <em>and</em> shape).
    /// </summary>
    [Fact]
    public async Task ExecuteBatchSQLAsync_TwoColumnShapesInOneBatch_AssignsEachRowByItsOwnList()
    {
        string[] statements =
        [
            "INSERT INTO docs (id, name) VALUES (2, 'two')",
            "INSERT INTO docs (name, id) VALUES ('three', 3)"
        ];

        await using IDatabase sync = Create("shapes_sync");
        sync.ExecuteSQL(Ddl);
        sync.ExecuteBatchSQL(statements);

        await using IDatabase asyncDb = Create("shapes_async");
        asyncDb.ExecuteSQL(Ddl);
        await asyncDb.ExecuteBatchSQLAsync(statements);

        foreach (IDatabase db in new[] { sync, asyncDb })
        {
            Assert.Equal("two", Scalar(db, "SELECT id, name FROM docs WHERE id = 2", "name"));
            Assert.Equal("three", Scalar(db, "SELECT id, name FROM docs WHERE id = 3", "name"));
        }

        Assert.Equal(Rows(sync), Rows(asyncDb));
    }

    /// <summary>
    /// A batch inside a caller's transaction: the dispatcher must not open a second one and must leave the
    /// commit to the caller. Before session 75 the async dispatcher began its own transaction
    /// unconditionally, and every provider throws <c>Transaction already in progress</c> on a second begin —
    /// so the sync sibling worked where the async one threw.
    /// </summary>
    [Fact]
    public async Task ExecuteBatchSQLAsync_WhileACallerTransactionIsOpen_LeavesTheCommitToTheCaller()
    {
        string[] statements = ["INSERT INTO docs VALUES (1, 'a', 1.5)"];
        string[] expected = ["id=1|name=a|score=1.5"];

        // The sync arm first: it is the contract the async arm now matches.
        await using IDatabase sync = Create("caller_transaction_sync");
        sync.ExecuteSQL(Ddl);
        var syncConcrete = (Database)sync;
        syncConcrete.BeginStorageTransactionOnly();
        sync.ExecuteBatchSQL(statements);
        syncConcrete.CommitStorageTransaction();
        Assert.Equal(expected, Rows(sync));

        await using IDatabase asyncDb = Create("caller_transaction_async");
        asyncDb.ExecuteSQL(Ddl);
        var asyncConcrete = (Database)asyncDb;
        asyncConcrete.BeginStorageTransactionOnly();

        // Red before the fix: InvalidOperationException("Transaction already in progress").
        await asyncDb.ExecuteBatchSQLAsync(statements);

        // The caller's commit still has a transaction to commit — had the batch committed internally, this
        // would throw "No active transaction" instead, and the rows would already be durable.
        asyncConcrete.CommitStorageTransaction();

        Assert.Equal(expected, Rows(asyncDb));
    }
}
