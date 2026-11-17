// <copyright file="PageBasedSecondaryIndexTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// An explicit <c>CREATE INDEX</c> on a PageBased table serves equality lookups and stays correct across INSERT, UPDATE,
/// DELETE and a reopen (known issue §21).
/// <para>
/// PageBased tables do not create a column index on first use, as columnar tables do (<c>Table.AutoHashIndexesEnabled</c>:
/// a deliberate choice, and SQLite's behaviour too), so a lookup on a non-key column without an index scans the table —
/// measured ~80 ms for 20,000 rows, against ~0.02–0.1 ms once <c>CREATE INDEX</c> has been run. These tests pin that the
/// index route returns exactly the rows a scan would.
/// </para>
/// </summary>
public sealed class PageBasedSecondaryIndexTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_PageIdx_{Guid.NewGuid():N}");

    public PageBasedSecondaryIndexTests()
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

    private static int[] Ids(IDatabase db, string sql) =>
        [.. db.ExecuteQuery(sql).Select(r => Convert.ToInt32(r["id"], System.Globalization.CultureInfo.InvariantCulture)).Order()];

    [Fact]
    public void ExplicitIndex_OnAPageBasedTable_ServesLookupsThroughWritesAndAReopen()
    {
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var db = _factory.Create(path, "pw");
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL) STORAGE = PAGE_BASED");
        db.ExecuteBatchSQL([.. Enumerable.Range(1, 500).Select(i => $"INSERT INTO docs (id, name, score) VALUES ({i}, 'User{i % 50}', {i}.5)")]);
        db.ExecuteSQL("CREATE INDEX ix_name ON docs (name)");

        Assert.Equal(Enumerable.Range(1, 500).Where(i => i % 50 == 7).ToArray(), Ids(db, "SELECT id FROM docs WHERE name = 'User7'"));

        db.ExecuteSQL("UPDATE docs SET name = 'Renamed' WHERE id = 7");
        db.ExecuteSQL("INSERT INTO docs (id, name, score) VALUES (1000, 'User7', 1.0)");
        db.ExecuteSQL("DELETE FROM docs WHERE id = 57");
        var expected = Enumerable.Range(1, 500).Where(i => i % 50 == 7 && i != 7 && i != 57).Append(1000).ToArray();
        Assert.Equal(expected, Ids(db, "SELECT id FROM docs WHERE name = 'User7'"));
        Assert.Equal([7], Ids(db, "SELECT id FROM docs WHERE name = 'Renamed'"));
        (db as IDisposable)?.Dispose();

        var reopened = _factory.Create(path, "pw");
        Assert.Equal(expected, Ids(reopened, "SELECT id FROM docs WHERE name = 'User7'"));
        Assert.Equal([7], Ids(reopened, "SELECT id FROM docs WHERE name = 'Renamed'"));
        (reopened as IDisposable)?.Dispose();
    }
}
