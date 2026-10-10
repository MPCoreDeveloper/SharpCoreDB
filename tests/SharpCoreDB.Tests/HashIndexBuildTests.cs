// <copyright file="HashIndexBuildTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Plan §3-1i: <c>CREATE TABLE</c> registers a hash index for EVERY column (SqlParser.DDL), built
/// lazily on the first query by <c>Table.EnsureIndexLoaded</c>. That build walked the raw data file with
/// the VARIABLE-LENGTH record decoder, so on a default (fixed-width) table it only indexed the rows
/// that happened to parse — and a plain `WHERE &lt;non-unique column&gt; = value` then returned a single
/// row instead of every match. It also stopped at the first tombstone, hiding every later row. The
/// build now decodes with the layout the records were written in and skips tombstones/empty slots.
/// </summary>
public sealed class HashIndexBuildTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public HashIndexBuildTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_IdxBuild_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    /// <summary>id, name, score, age — names and ages deliberately repeat.</summary>
    private static readonly (long Id, string Name, double Score, long Age)[] Seed =
    [
        (1, "dup", 1.5, 25),
        (2, "uniq", 2.5, 30),
        (3, "dup", 3.5, 25),
        (4, "dup", 4.5, 25),
        (5, "other", 5.5, 40),
        (6, "dup", 6.5, 25),
    ];

    private IDatabase Create(string suffix, bool fixedWidth, bool atRest = false)
    {
        var dir = Path.Combine(_dirPath, suffix);
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = !atRest,
            EnableAtRestRecordEncryption = atRest,
            AutoFixedWidthRecords = fixedWidth,
        });

        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL, age INTEGER)");
        foreach (var (id, name, score, age) in Seed)
        {
            db.ExecuteSQL(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO docs VALUES ({0}, '{1}', {2}, {3})", id, name, score, age));
        }

        db.Flush();
        return db;
    }

    private static List<long> Ids(IDatabase db, string where) =>
        db.ExecuteQuery($"SELECT id FROM docs WHERE {where} ORDER BY id")
          .ConvertAll(r => Convert.ToInt64(r["id"]));

    [Theory]
    [InlineData(true)]   // fixed-width (the default layout)
    [InlineData(false)]  // variable-length
    public void Equality_OnNonUniqueColumn_ReturnsEveryMatch(bool fixedWidth)
    {
        var db = Create($"dup-{fixedWidth}", fixedWidth);
        try
        {
            Assert.Equal([1L, 3L, 4L, 6L], Ids(db, "age = 25"));
            Assert.Equal([2L], Ids(db, "age = 30"));
            Assert.Equal([5L], Ids(db, "age = 40"));

            // The TEXT column is indexed the same way, and its payload lives in the overflow arena.
            Assert.Equal([1L, 3L, 4L, 6L], Ids(db, "name = 'dup'"));
            Assert.Equal([2L], Ids(db, "name = 'uniq'"));

            // A decimal literal on REAL (plan §3-1h) must also go through the index correctly.
            Assert.Equal([4L], Ids(db, "score = 4.5"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndexBuild_SkipsTombstones_WithoutLosingLaterRows(bool fixedWidth)
    {
        // A DELETE writes a negative length prefix. The build used to stop there, so every row AFTER the
        // first deleted one vanished from the index — and therefore from every `WHERE col = value`.
        var dir = Path.Combine(_dirPath, $"del-{fixedWidth}");
        Directory.CreateDirectory(dir);
        IDatabase OpenDb() => _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = true,
            AutoFixedWidthRecords = fixedWidth,
        });

        var db = OpenDb();
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL, age INTEGER)");
        foreach (var (id, name, score, age) in Seed)
        {
            db.ExecuteSQL(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO docs VALUES ({0}, '{1}', {2}, {3})", id, name, score, age));
        }

        db.Flush();
        db.ExecuteSQL("DELETE FROM docs WHERE id = 3");
        db.Flush();

        Assert.Equal([1L, 4L, 6L], Ids(db, "age = 25"));
        Assert.Equal([5L], Ids(db, "age = 40")); // row 5 sits physically behind the tombstone
        Assert.Equal([2L], Ids(db, "name = 'uniq'"));
        Assert.Empty(Ids(db, "age = 99"));
        (db as IDisposable)?.Dispose();

        // Same after a reopen: the index is rebuilt from the file on the next query.
        db = OpenDb();
        try
        {
            Assert.Equal([1L, 4L, 6L], Ids(db, "age = 25"));
            Assert.Equal([5L], Ids(db, "age = 40"));
            Assert.Equal([2L], Ids(db, "age = 30"));
            Assert.Empty(Ids(db, "id = 3")); // the deleted row stays deleted across the reopen
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
