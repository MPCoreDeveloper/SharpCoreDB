// <copyright file="FormatCompatPolicyTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// Encoding of the compatibility policy in <c>docs/manual/upgrade-and-downgrade.md</c>:
/// (1) files written in the legacy variable-length format (the pre-marker / pre-fixed-width
/// representation) must read back and keep working across reopen cycles, and (2) commit-time
/// tombstone markers written by the current version must stay stable across repeated reopen cycles
/// and coexist with rows appended afterwards. These are the forward-compatibility guarantees that
/// make downgrade the only unsupported direction.
/// </summary>
public sealed class FormatCompatPolicyTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public FormatCompatPolicyTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_FormatCompat_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private static void InsertRange(IDatabase db, int from, int to)
    {
        var stmts = new List<string>(to - from + 1);
        for (int i = from; i <= to; i++)
        {
            stmts.Add($"INSERT INTO docs VALUES ({i}, 'user{i}', {i * 0.5})");
        }

        db.ExecuteBatchSQL(stmts);
        db.Flush();
    }

    private static void DeleteRange(IDatabase db, int from, int to)
    {
        var stmts = new List<string>(to - from + 1);
        for (int i = from; i <= to; i++)
        {
            stmts.Add($"DELETE FROM docs WHERE id = {i}");
        }

        db.ExecuteBatchSQL(stmts);
        db.Flush();
    }

    [Fact]
    public void LegacyVariableLengthFile_ReadsBackAndKeepsWorking_AcrossReopens()
    {
        // A legacy variable-length database (no fixed-width layout) is byte-compatible with the
        // pre-fixed-width / pre-marker format. It must open, round-trip and then accept marker
        // writes from the current version.
        IDatabase? db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true, AutoFixedWidthRecords = false, FixedWidthRecordLayout = false });
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertRange(db, 1, 300);
            Assert.Equal(300, db.ExecuteQuery("SELECT id FROM docs").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // First reopen = "old file read by new version" (no markers written yet).
        db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true, AutoFixedWidthRecords = false, FixedWidthRecordLayout = false });
        try
        {
            Assert.Equal(300, db.ExecuteQuery("SELECT id FROM docs").Count);
            DeleteRange(db, 1, 40);
            Assert.Equal(260, db.ExecuteQuery("SELECT id FROM docs").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Final reopen: the legacy rows plus current-version tombstone markers coexist.
        db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig { NoEncryptMode = true, AutoFixedWidthRecords = false, FixedWidthRecordLayout = false });
        try
        {
            Assert.Equal(260, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 10"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 41"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void CommitTimeTombstoneMarkers_StableAcrossReopenCycles_AndCoexistWithAppends()
    {
        IDatabase? db = _factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig());
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            InsertRange(db, 1, 1000);
            DeleteRange(db, 1, 500);
            Assert.Equal(500, db.ExecuteQuery("SELECT id FROM docs").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }

        db = _factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig());
        try
        {
            Assert.Equal(500, db.ExecuteQuery("SELECT id FROM docs").Count);
            InsertRange(db, 1001, 1200); // appends after the marker region
            Assert.Equal(700, db.ExecuteQuery("SELECT id FROM docs").Count);
            DeleteRange(db, 1001, 1100);
        }
        finally { (db as IDisposable)?.Dispose(); }

        db = _factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig());
        try
        {
            Assert.Equal(600, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1050"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1101"));
            Assert.Single(db.ExecuteQuery("SELECT id FROM docs WHERE id = 1200"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
