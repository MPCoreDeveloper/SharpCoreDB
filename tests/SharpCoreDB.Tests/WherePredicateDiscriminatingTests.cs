// <copyright file="WherePredicateDiscriminatingTests.cs" company="MPCoreDeveloper">
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
/// Discriminating regression tests for GitHub issue #348 (multi-value WHERE IN (...) and OR
/// predicates). Every assertion uses a NON-MATCHING value somewhere in the list so that a
/// "returns all rows" (tautology) bug is detected: the expected row count is always a strict
/// SUBSET of the table when the predicate should filter.
/// </summary>
public sealed class WherePredicateDiscriminatingTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private readonly string _scdbPath;

    public WherePredicateDiscriminatingTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_WhereDisc_Dir_{Guid.NewGuid():N}");
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_WhereDisc_Scdb_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    /// <summary>Seeds the kg_nodes_test table: 2× WorkItem + 1× Person (3 rows total).</summary>
    private static void Seed(IDatabase db)
    {
        db.ExecuteSQL("CREATE TABLE kg_nodes_test (id TEXT PRIMARY KEY, node_type TEXT NOT NULL, external_id TEXT NOT NULL)");
        db.ExecuteSQL("INSERT INTO kg_nodes_test VALUES ('A', 'WorkItem', 'WI-1')");
        db.ExecuteSQL("INSERT INTO kg_nodes_test VALUES ('B', 'WorkItem', 'WI-2')");
        db.ExecuteSQL("INSERT INTO kg_nodes_test VALUES ('C', 'Person', 'P-1')");
    }

    private static int CountRows(IDatabase db, string sql, Dictionary<string, object?>? parameters = null)
        => db.ExecuteQuery(sql, parameters).Count;

    // --- Single-file mode (.scdb) ---

    [Theory]
    // The reporter's exact probe values (all values MATCH, so the correct count is the FULL
    // 3-row table — this proves the multi-value IN is NOT "returning all rows" incorrectly).
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem', 'Person')", 3)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem', 'DoesNotExist')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('DoesNotExist', 'WorkItem')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('DoesNotExist1', 'DoesNotExist2')", 0)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('Person', 'DoesNotExist')", 2)]
    public void SingleFile_WhereIn_LiteralList_Discriminating(string sql, int expected)
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
    public void SingleFile_WhereIn_ParameterizedMultiValue_Discriminating()
    {
        var db = _factory.Create(_scdbPath, "pw");
        try
        {
            Seed(db);

            // Second value does not match: must return the 2 WorkItem rows, never all 3.
            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            // 5-value list with a single matching value.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1, @p2, @p3, @p4)",
                new Dictionary<string, object?>
                {
                    ["@p0"] = "DoesNotExist0", ["@p1"] = "DoesNotExist1", ["@p2"] = "WorkItem",
                    ["@p3"] = "DoesNotExist3", ["@p4"] = "DoesNotExist4"
                });
            Assert.Equal(2, rows.Count);

            // Reporter's exact values: both match, so the FULL table is correct.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFile_WhereIn_ValuesMultiRow_Discriminating()
    {
        var db = _factory.Create(_scdbPath, "pw");
        try
        {
            Seed(db);

            // Multi-row VALUES with a non-matching second row: must return 2, not 3.
            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (VALUES (@p0), (@p1))",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            // Reporter's exact values: both match → full table.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (VALUES (@p0), (@p1))",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFile_Where_OrChain_Discriminating()
    {
        var db = _factory.Create(_scdbPath, "pw");
        try
        {
            Seed(db);

            // Second operand does not match: must return the 2 WorkItem rows, never all 3.
            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type = @p0 OR node_type = @p1",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            // Reporter's exact values: both match → full table is correct.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type = @p0 OR node_type = @p1",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFile_Where_ParenthesizedOr_Discriminating()
    {
        var db = _factory.Create(_scdbPath, "pw");
        try
        {
            Seed(db);

            // Parenthesized OR with a non-matching second operand: must return 2 (was 0 — issue #348).
            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE (node_type = @p0 OR node_type = @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            // Parenthesized OR, both match → full table.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE (node_type = @p0 OR node_type = @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);

            // Parenthesized AND+OR: node_type=WorkItem AND (external_id=WI-1 OR non-match) → 1.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type = @p0 AND (external_id = @p1 OR external_id = @p2)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "WI-1", ["@p2"] = "DoesNotExist" });
            Assert.Equal(1, rows.Count);

            // Double-wrapped parentheses.
            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE ((node_type = @p0 OR node_type = @p1))",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }



    // --- Directory mode ---

    [Theory]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem', 'DoesNotExist')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('DoesNotExist', 'WorkItem')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('DoesNotExist1', 'DoesNotExist2')", 0)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('Person', 'DoesNotExist')", 2)]
    [InlineData("SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem', 'Person')", 3)]
    public void Directory_WhereIn_LiteralList_Discriminating(string sql, int expected)
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
    public void Directory_WhereIn_ParameterizedMultiValue_Discriminating()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            Seed(db);

            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Directory_WhereIn_ValuesMultiRow_Discriminating()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            Seed(db);

            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type IN (VALUES (@p0), (@p1))",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Directory_Where_OrChain_Discriminating()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            Seed(db);

            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type = @p0 OR node_type = @p1",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type = @p0 OR node_type = @p1",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void Directory_Where_ParenthesizedOr_Discriminating()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            Seed(db);

            var rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE (node_type = @p0 OR node_type = @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "DoesNotExist" });
            Assert.Equal(2, rows.Count);

            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE (node_type = @p0 OR node_type = @p1)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "Person" });
            Assert.Equal(3, rows.Count);

            rows = db.ExecuteQuery(
                "SELECT id FROM kg_nodes_test WHERE node_type = @p0 AND (external_id = @p1 OR external_id = @p2)",
                new Dictionary<string, object?> { ["@p0"] = "WorkItem", ["@p1"] = "WI-1", ["@p2"] = "DoesNotExist" });
            Assert.Equal(1, rows.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}


