// <copyright file="HashIndexAutoCreationGateTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

/// <summary>
/// Pins the <c>DatabaseConfig.EnableHashIndexes</c> gate from the outside, on the registration set itself.
/// <para>
/// Why it exists: until session 42 that property was read by nobody — the Columnar <c>CREATE TABLE</c> path
/// registered a hash index on the primary key and on every other column unconditionally — so the
/// <c>SHARPCOREDB_HASH_INDEXES</c> dial built to test a hypothesis could not test it. Session 46–49 then
/// measured the *cost* of that auto-index set (docs-shape INSERT 0,90× → 1,38× against SQLite with the dial
/// off), so the gate is now load-bearing evidence rather than a flag: if it ever silently stops working, the
/// measurement it enables becomes unaskable again. This test is the tripwire.
/// </para>
/// <para>
/// <b>The contract changed in the session that implemented §9 row 5</b> (owner-decided 2026-09-26): the
/// auto-index set is not created per column at <c>CREATE TABLE</c> any more. Only a user-declared PRIMARY KEY
/// is registered up front; every other column registers itself the first time an operation filters on it
/// (<c>Table.EnsureAutoHashIndexRegistered</c>, called by every lookup gate), and the dial still turns the
/// whole thing off. The old "one index per column by default" assertions were rewritten to this contract —
/// deliberately, because that behaviour is what the owner changed — and the last two tests pin the defect the
/// implementation uncovered: an index built from the data file <em>after</em> a length-changing update used to
/// answer with the superseded row.
/// </para>
/// </summary>
public sealed class HashIndexAutoCreationGateTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public HashIndexAutoCreationGateTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_HashIndexGate_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private Database CreateDb(bool enableHashIndexes, bool autoFixedWidth = true) =>
        (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableHashIndexes = enableHashIndexes,
            AutoFixedWidthRecords = autoFixedWidth,
        });

    private static Table TableOf(Database db, string name)
    {
        // The table's index statistics are read while the database is open on purpose: the table's rwLock
        // lives with the database, and a fixture that closes it first would fail on a disposed lock rather
        // than on the property under test.
        Assert.True(db.TryGetTable(name, out var probe));
        return Assert.IsType<Table>(probe);
    }

    private static void InsertOne(Database db, string table, params (string Column, object Value)[] values)
    {
        var row = new Dictionary<string, object>();
        foreach (var (column, value) in values)
        {
            row[column] = value;
        }

        db.InsertBatch(table, [row]);
    }

    [Fact]
    public void AutoCreation_WithDeclaredPrimaryKey_RegistersOnlyThatKey()
    {
        using var db = CreateDb(enableHashIndexes: true);
        db.ExecuteSQL("CREATE TABLE gated_on (id INTEGER PRIMARY KEY, name TEXT, email TEXT, age INTEGER, score REAL)");
        var table = TableOf(db, "gated_on");

        var registered = table.GetIndexLoadStatistics();

        Assert.Equal(1, table.TotalRegisteredIndexes);
        Assert.True(registered.ContainsKey("id"), "the declared primary key is registered up front");
        foreach (var column in new[] { "name", "email", "age", "score" })
        {
            Assert.False(registered.ContainsKey(column), $"'{column}' must register on demand, not at CREATE TABLE");
        }
    }

    [Fact]
    public void AutoCreation_WithoutDeclaredPrimaryKey_RegistersNothing()
    {
        // The hidden _rowid fallback is a primary key for the storage layer, but it is not registered: no
        // user query filters on it and the PK B-tree already serves its point lookups, so an index for it
        // would be per-write cost with no lookup benefit.
        using var db = CreateDb(enableHashIndexes: true);
        db.ExecuteSQL("CREATE TABLE gated_nopk (name TEXT, email TEXT, age INTEGER, score REAL)");
        var table = TableOf(db, "gated_nopk");

        Assert.Equal(0, table.TotalRegisteredIndexes);
        Assert.Empty(table.GetIndexLoadStatistics());
    }

    [Fact]
    public void OnDemand_FirstFilteredQuery_RegistersAndLoadsThatColumnOnly()
    {
        using var db = CreateDb(enableHashIndexes: true);
        db.ExecuteSQL("CREATE TABLE gated_query (name TEXT, email TEXT, score REAL)");
        InsertOne(db, "gated_query", ("name", "u1"), ("email", "u1@test.com"), ("score", 1.5));
        InsertOne(db, "gated_query", ("name", "u2"), ("email", "u2@test.com"), ("score", 2.5));

        var first = db.ExecuteQuery("SELECT score FROM gated_query WHERE name = @name",
            new Dictionary<string, object?> { ["@name"] = "u1" });
        Assert.Single(first);

        var table = TableOf(db, "gated_query");
        var registered = table.GetIndexLoadStatistics();
        Assert.True(registered.ContainsKey("name"), "the filtered column must be registered by the first query");
        Assert.True(registered["name"].IsLoaded, "and built, so the next query is a point lookup");
        Assert.False(registered.ContainsKey("email"), "columns nothing filtered on stay unregistered");
        Assert.Equal(1, table.TotalRegisteredIndexes);

        // The second query must agree with the first — the index the first query built serves it.
        var second = db.ExecuteQuery("SELECT score FROM gated_query WHERE name = @name",
            new Dictionary<string, object?> { ["@name"] = "u2" });
        Assert.Single(second);
        Assert.Equal(2.5, Convert.ToDouble(second[0]["score"], CultureInfo.InvariantCulture));

        // And an unfiltered read still sees every row: registering on demand must not hide rows.
        Assert.Equal(2, db.ExecuteQuery("SELECT name FROM gated_query").Count);
    }

    [Fact]
    public void DialOff_RegistersNothingOnDemandAndStillServesQueries()
    {
        // Removing the auto indexes must not change results, only cost: rows still insert, read and delete.
        // The dial now also disables the on-demand registration, so a filtered query falls through to the
        // B-tree / full scan.
        const string Table = "no_indexes";
        using (var db = (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableHashIndexes = false,
        }))
        {
            db.ExecuteSQL($"CREATE TABLE {Table} (name TEXT, score REAL)");
            InsertOne(db, Table, ("name", "u1"), ("score", 1.5));

            var read = db.ExecuteQuery($"SELECT score FROM {Table} WHERE name = @name",
                new Dictionary<string, object?> { ["@name"] = "u1" });
            Assert.Single(read);
            Assert.Equal(1.5, Convert.ToDouble(read[0]["score"], CultureInfo.InvariantCulture));

            Assert.Empty(TableOf(db, Table).GetIndexLoadStatistics());

            Assert.Equal(1, db.DeleteBatch(Table, [("name", "u1")]));
            Assert.Empty(db.ExecuteQuery($"SELECT score FROM {Table}"));
        }
    }

    [Fact]
    public void LateIndexBuild_AfterLengthChangingUpdate_IgnoresTheSupersededRow()
    {
        // Regression (found and fixed while implementing §9 row 5, session 71): the Columnar engine appends
        // a new version when an update changes a record's length and leaves the superseded record in the
        // file. An index registered AFTER that update was built by walking the file, so it indexed the
        // superseded record too and answered `WHERE v = 'old1'` with a row no scan returns and SQLite does
        // not either. The dial is off here on purpose: it is what keeps the column unregistered while the
        // update runs, so the explicit CREATE INDEX afterwards is a genuine late build.
        const string Table = "late_build";
        using var db = CreateDb(enableHashIndexes: false, autoFixedWidth: false);
        db.ExecuteSQL($"CREATE TABLE {Table} (name TEXT, v TEXT)");
        for (int i = 0; i < 3; i++)
        {
            InsertOne(db, Table, ("name", $"n{i}"), ("v", $"old{i}"));
        }

        db.ExecuteSQL($"UPDATE {Table} SET v = 'newvalue1-newvalue1-newvalue1' WHERE name = 'n1'");

        Assert.Equal(3, db.ExecuteQuery($"SELECT name FROM {Table}").Count);

        db.ExecuteSQL($"CREATE INDEX {Table}_idx_v ON {Table}(v)");

        Assert.Empty(db.ExecuteQuery($"SELECT name FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "old1" }));
        Assert.Single(db.ExecuteQuery($"SELECT name FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "newvalue1-newvalue1-newvalue1" }));
        Assert.Single(db.ExecuteQuery($"SELECT name FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "old2" }));
    }

    [Fact]
    public void LateIndexBuild_OnDemandAfterLengthChangingUpdate_IgnoresTheSupersededRow()
    {
        // The on-demand variant of the same defect: with the dial ON nothing is registered for `v` while the
        // update runs (only the declared PK is), so the FIRST filtered query on `v` is what registers and
        // builds the index — after the superseded record exists.
        const string Table = "late_ondemand";
        using var db = CreateDb(enableHashIndexes: true, autoFixedWidth: false);
        db.ExecuteSQL($"CREATE TABLE {Table} (id INTEGER PRIMARY KEY, v TEXT)");
        for (int i = 0; i < 3; i++)
        {
            InsertOne(db, Table, ("id", i), ("v", $"old{i}"));
        }

        db.ExecuteSQL($"UPDATE {Table} SET v = 'newvalue1-newvalue1-newvalue1' WHERE id = 1");
        Assert.Equal(1, TableOf(db, Table).TotalRegisteredIndexes); // the declared PK only

        Assert.Empty(db.ExecuteQuery($"SELECT id FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "old1" }));
        Assert.Single(db.ExecuteQuery($"SELECT id FROM {Table} WHERE v = @v",
            new Dictionary<string, object?> { ["@v"] = "newvalue1-newvalue1-newvalue1" }));
    }
}
