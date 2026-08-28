// <copyright file="WhereInRegressionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
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
/// Regression tests for GitHub issue #339: WHERE col IN (...) silently returns all rows.
/// Covers literal lists, parameterized lists, single-value lists and NOT IN across both
/// single-file (.scdb) and directory storage modes.
/// </summary>
public sealed class WhereInRegressionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private readonly string _scdbPath;

    public WhereInRegressionTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_WhereIn_Dir_{Guid.NewGuid():N}");
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_WhereIn_Scdb_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    private static void Seed(IDatabase db)
    {
        db.ExecuteSQL("CREATE TABLE kg_nodes_test (id TEXT PRIMARY KEY, node_type TEXT NOT NULL, external_id TEXT NOT NULL)");
        db.ExecuteSQL("INSERT INTO kg_nodes_test VALUES ('A', 'WorkItem', 'WI-1')");
        db.ExecuteSQL("INSERT INTO kg_nodes_test VALUES ('B', 'WorkItem', 'WI-2')");
        db.ExecuteSQL("INSERT INTO kg_nodes_test VALUES ('C', 'Person', 'P-1')");
    }

    private static int CountRows(IDatabase db, string sql, Dictionary<string, object?>? parameters = null)
        => db.ExecuteQuery(sql, parameters).Count;

    // --- Single-file mode ---

    [Theory]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('Person')", 1)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem', 'Person')", 3)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('WorkItem')", 1)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('WorkItem', 'Person')", 0)]
    public void SingleFile_WhereIn_ReturnsMatchingRows(string sql, int expected)
    {
        var db = _factory.Create(_scdbPath, "pw");
        try
        {
            Seed(db);
            Assert.Equal(expected, CountRows(db, sql));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFile_WhereIn_Parameterized_ReturnsMatchingRows()
    {
        var db = _factory.Create(_scdbPath, "pw");
        try
        {
            Seed(db);
            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    // --- Directory mode ---

    [Theory]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('Person')", 1)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem', 'Person')", 3)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('WorkItem')", 1)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('WorkItem', 'Person')", 0)]
    public void Directory_WhereIn_ReturnsMatchingRows(string sql, int expected)
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            Seed(db);
            Assert.Equal(expected, CountRows(db, sql));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Directory_WhereIn_Parameterized_ReturnsMatchingRows()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            Seed(db);
            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
