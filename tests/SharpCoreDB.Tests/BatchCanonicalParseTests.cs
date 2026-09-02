// <copyright file="BatchCanonicalParseTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Phase-2 canonical batch-DML fast parse: pin the observable behaviour of
/// <c>ExecuteBatchSQL</c> for canonical single-row UPDATE/DELETE statements AND for tricky
/// non-canonical shapes that must fall back to the general regex path (embedded commas,
/// keywords inside string literals, multi-column SET, non-= operators, whitespace in literals).
/// Both routes must produce identical results.
/// </summary>
public sealed class BatchCanonicalParseTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public BatchCanonicalParseTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_BatchCanonical_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

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
                return rows[0][key]?.ToString();
            }
        }

        return null;
    }

    private static double? Num(IDatabase db, string sql, string column)
    {
        var rows = db.ExecuteQuery(sql);
        if (rows.Count == 0)
        {
            return null;
        }

        foreach (var key in rows[0].Keys)
        {
            if (key.Equals(column, StringComparison.OrdinalIgnoreCase) && rows[0][key] is not null)
            {
                return Convert.ToDouble(rows[0][key], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    [Fact]
    public void CanonicalSingleSetUpdate_UpdatesRow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, email TEXT, age INTEGER, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User1', 'u1@x', 30, 0.1)");

            db.ExecuteBatchSQL(["UPDATE t SET score = 99.5 WHERE name = 'User1'"]);

            Assert.Equal(99.5, Num(db, "SELECT score FROM t WHERE name = 'User1'", "score"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SetValue_WithCommaAndWhereKeywordInsideString_IsHandled()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, data TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'a,b')");
            db.ExecuteSQL("INSERT INTO t VALUES (2, 'c')");

            // data value contains a comma and the word WHERE inside the string literal.
            db.ExecuteBatchSQL(["UPDATE t SET data = 'x, WHERE y' WHERE id = 1"]);

            Assert.Equal("x, WHERE y", Scalar(db, "SELECT data FROM t WHERE id = 1", "data"));
            Assert.Equal("c", Scalar(db, "SELECT data FROM t WHERE id = 2", "data"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void MultiColumnSet_FallsBackAndUpdatesAll()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, a TEXT, b INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES (1, 'x', 10)");

            db.ExecuteBatchSQL(["UPDATE t SET a = 'y', b = 42 WHERE id = 1"]);

            Assert.Equal("y", Scalar(db, "SELECT a FROM t WHERE id = 1", "a"));
            Assert.Equal(42, Num(db, "SELECT b FROM t WHERE id = 1", "b"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void CanonicalDelete_RemovesRow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, age INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES ('a', 1)");
            db.ExecuteSQL("INSERT INTO t VALUES ('b', 2)");

            db.ExecuteBatchSQL(["DELETE FROM t WHERE name = 'a'"]);

            Assert.Null(Scalar(db, "SELECT name FROM t WHERE name = 'a'", "name"));
            Assert.Equal("b", Scalar(db, "SELECT name FROM t WHERE name = 'b'", "name"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void WhereLiteral_WithSpaces_IsHandled()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT, score REAL)");
            db.ExecuteSQL("INSERT INTO t VALUES ('User 1', 1.0)");

            db.ExecuteBatchSQL(["UPDATE t SET score = 2.0 WHERE name = 'User 1'"]);
            Assert.Equal(2.0, Num(db, "SELECT score FROM t WHERE name = 'User 1'", "score"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}

