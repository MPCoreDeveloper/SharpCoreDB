// <copyright file="HashIndexAutoCreationGateTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using System;
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

    private Database CreateDb(bool enableHashIndexes) =>
        (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableHashIndexes = enableHashIndexes,
        });

    [Fact]
    public void AutoCreation_RegistersOneHashIndexPerColumnByDefault()
    {
        // The table's index statistics are read while the database is open on purpose: the table's rwLock lives
        // with the database, and a fixture that closes it first would fail on a disposed lock rather than on the
        // property under test.
        using var db = CreateDb(enableHashIndexes: true);
        db.ExecuteSQL("CREATE TABLE gated_on (name TEXT, email TEXT, age INTEGER, score REAL)");
        Assert.True(db.TryGetTable("gated_on", out var probe));
        var table = Assert.IsType<Table>(probe);

        var registered = table.GetIndexLoadStatistics();

        Assert.Equal(table.Columns.Count, registered.Count);
        foreach (var column in table.Columns)
        {
            Assert.True(registered.ContainsKey(column), $"expected an auto-registered index on '{column}'");
        }
    }

    [Fact]
    public void AutoCreation_Disabled_RegistersNoHashIndexAtAll()
    {
        // The dial's whole point: with this off there is no per-column index set, so the cost of maintaining it
        // is measurable. If this assertion ever passes while the other one fails, the gate has stopped firing.
        using var db = CreateDb(enableHashIndexes: false);
        db.ExecuteSQL("CREATE TABLE gated_off (name TEXT, email TEXT, age INTEGER, score REAL)");
        Assert.True(db.TryGetTable("gated_off", out var probe));
        var table = Assert.IsType<Table>(probe);

        Assert.Empty(table.GetIndexLoadStatistics());
    }

    [Fact]
    public void AutoCreation_Disabled_StillServesQueriesCorrectly()
    {
        // Removing the auto indexes must not change results, only cost: rows still insert, read and delete.
        const string Table = "no_indexes";
        using (var db = (Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = true,
            EnableHashIndexes = false,
        }))
        {
            db.ExecuteSQL($"CREATE TABLE {Table} (name TEXT, score REAL)");
            db.InsertBatch(Table, [new System.Collections.Generic.Dictionary<string, object>
            {
                ["name"] = "u1",
                ["score"] = 1.5,
            }]);

            var read = db.ExecuteQuery($"SELECT score FROM {Table} WHERE name = @name",
                new System.Collections.Generic.Dictionary<string, object?> { ["@name"] = "u1" });
            Assert.Single(read);
            Assert.Equal(1.5, Convert.ToDouble(read[0]["score"], System.Globalization.CultureInfo.InvariantCulture));

            Assert.Equal(1, db.DeleteBatch(Table, [("name", "u1")]));
            Assert.Empty(db.ExecuteQuery($"SELECT score FROM {Table}"));
        }
    }
}
