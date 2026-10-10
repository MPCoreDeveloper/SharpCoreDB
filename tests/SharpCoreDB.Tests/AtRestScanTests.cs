// <copyright file="AtRestScanTests.cs" company="MPCoreDeveloper">
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
using System.Linq;
using Xunit;

/// <summary>
/// Plan §3-1g: a full table scan of an at-rest (per-record encrypted) database used to return ZERO
/// rows — in-session and after a reopen. The file is read through <c>ReadBytes</c>, which for an at-rest
/// file returns a decrypted, header-stripped re-pack of the records, so a record's buffer offset is NOT
/// its physical file offset; the columnar scan's stale-version filter (PK index position == record
/// position) therefore rejected every row as a superseded version. The scan now receives the physical
/// offsets through <c>IStorage.ReadBytesWithRecordOffsets</c> and compares against those. These tests
/// run both modes side by side so the plaintext behaviour they share cannot regress either.
/// </summary>
public sealed class AtRestScanTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public AtRestScanTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_AtRestScan_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase Open(string dir, DatabaseConfig config) =>
        _factory.Create(dir, "pw", isReadOnly: false, config: config);

    private static DatabaseConfig Raw => new() { NoEncryptMode = true };

    private static DatabaseConfig AtRest =>
        new() { NoEncryptMode = false, EnableAtRestRecordEncryption = true };

    private static List<string> Inserts(int from, int to)
    {
        var stmts = new List<string>(to - from + 1);
        for (int i = from; i <= to; i++)
        {
            stmts.Add(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO docs VALUES ({0}, 'user{0}', {1})", i, i * 0.5));
        }

        return stmts;
    }

    private static long CountOf(IDatabase db) =>
        Convert.ToInt64(db.ExecuteQuery("SELECT COUNT(*) AS n FROM docs")[0].Values.First());

    private string ModeDir(bool atRest) => Path.Combine(_dirPath, atRest ? "atrest" : "raw");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullScan_And_Count_SeeEveryRow_InSessionAndAfterReopen(bool atRest)
    {
        var dir = ModeDir(atRest);
        Directory.CreateDirectory(dir);

        var db = Open(dir, atRest ? AtRest : Raw);
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        db.ExecuteBatchSQL(Inserts(1, 2000));
        db.Flush();

        Assert.Equal(2000, db.ExecuteQuery("SELECT id FROM docs").Count);
        Assert.Equal(2000L, CountOf(db));
        (db as IDisposable)?.Dispose();

        db = Open(dir, atRest ? AtRest : Raw);
        try
        {
            Assert.Equal(2000, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Equal(2000L, CountOf(db));

            // The scanned rows must carry the real values — a wrong offset map would drop or corrupt them.
            var one = db.ExecuteQuery("SELECT score FROM docs WHERE id = 1500");
            Assert.Single(one);
            Assert.Equal(750.0, Convert.ToDouble(one[0]["score"]));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullScan_AfterDeletes_SeesExactlyTheLiveRows(bool atRest)
    {
        var dir = Path.Combine(_dirPath, atRest ? "deletes-atrest" : "deletes-raw");
        Directory.CreateDirectory(dir);

        var db = Open(dir, atRest ? AtRest : Raw);
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        db.ExecuteBatchSQL(Inserts(1, 2000));
        db.Flush();

        // One single-row DELETE (generic path) plus one contiguous batch (the bulk path): both leave a
        // length-prefix tombstone the scan has to skip without losing the records behind it.
        db.ExecuteSQL("DELETE FROM docs WHERE id = 500");
        var deletes = new List<string>(500);
        for (int i = 1001; i <= 1500; i++)
        {
            deletes.Add(string.Format(CultureInfo.InvariantCulture, "DELETE FROM docs WHERE id = {0}", i));
        }

        db.ExecuteBatchSQL(deletes);
        db.Flush();

        Assert.Equal(1499, db.ExecuteQuery("SELECT id FROM docs").Count);
        Assert.Equal(1499L, CountOf(db));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 500"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1200"));
        Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1501"));
        (db as IDisposable)?.Dispose();

        // Reopen: the PK index is rebuilt from this same scan, and the tombstones must survive it.
        db = Open(dir, atRest ? AtRest : Raw);
        try
        {
            Assert.Equal(1499, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Equal(1499L, CountOf(db));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 500"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1501"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void HashIndexLookup_SurvivesReopen_OnAtRestDatabase()
    {
        // The hash indexes are rebuilt from a table scan on reopen, so the §3-1g scan defect made every
        // hash lookup come back empty after reopening an at-rest database while PK lookups kept working.
        var dir = Path.Combine(_dirPath, "atrest-index");
        Directory.CreateDirectory(dir);

        var db = Open(dir, AtRest);
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");
        db.ExecuteBatchSQL(Inserts(1, 500));
        db.Flush();
        db.ExecuteSQL("DELETE FROM docs WHERE id = 42");
        db.Flush();
        (db as IDisposable)?.Dispose();

        db = Open(dir, AtRest);
        try
        {
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user100'"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user42'"));
            Assert.Equal(499, db.ExecuteQuery("SELECT id FROM docs").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilteredScan_EarlyWherePaths_KeepTheOffsetMapAligned(bool atRest)
    {
        // The scan's early-WHERE shortcuts skip records without deserializing them. Those skips must
        // still advance the physical-offset map, or every later row is checked against the wrong offset
        // and silently dropped — the failure would look like "filters return nothing".
        var dir = Path.Combine(_dirPath, atRest ? "filter-atrest" : "filter-raw");
        Directory.CreateDirectory(dir);

        var db = Open(dir, atRest ? AtRest : Raw);
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL, age INTEGER)");
            var inserts = new List<string>(2000);
            for (int i = 1; i <= 2000; i++)
            {
                inserts.Add(string.Format(CultureInfo.InvariantCulture,
                    "INSERT INTO docs VALUES ({0}, 'user{0}', {1}, {2})", i, i * 0.5, 20 + (i % 60)));
            }

            db.ExecuteBatchSQL(inserts);
            db.Flush();

            // String early-WHERE (fixed-width slot compare) and an integer equality, then a full scan
            // after both — the last assertions prove the map alignment survived the skipped records.
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user1750'"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'user9999'"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE age = 25 AND id = 5"));
            Assert.Equal(2000, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Equal(2000L, CountOf(db));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void StructScan_SeesEveryRow_OnAtRestVariableLengthDatabase()
    {
        // The zero-allocation StructRow scan has its own stale-version check over the same buffer.
        // Variable-length layout on purpose: fixed-width tables fall back to the dictionary path.
        var dir = Path.Combine(_dirPath, "atrest-struct");
        Directory.CreateDirectory(dir);

        var db = Open(dir, new DatabaseConfig
        {
            NoEncryptMode = false,
            EnableAtRestRecordEncryption = true,
            AutoFixedWidthRecords = false,
        });
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteBatchSQL(Inserts(1, 300));
            db.Flush();

            Assert.True(db.TryGetTable("docs", out var t));
            var table = Assert.IsType<Table>(t);
            Assert.False(table.IsFixedWidthRecords);
            Assert.Equal(300, table.SelectStruct().Count());
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
