// <copyright file="ParametricInsertTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Tests;

/// <summary>
/// Regression tests for parameterized INSERT/SELECT binding.
/// Guards against the substring-based named-parameter binding bug where a parameter name that
/// is a prefix of another (e.g. @t vs @tid) corrupted the longer placeholder
/// (e.g. INSERT ... VALUES (@id, @s, @t, @r, @tid) stored "200id" in tenant_id).
/// </summary>
public sealed class ParametricInsertTests
{
    private static (IDatabase Db, string Path) CreateDatabase(string suffix)
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scdb_param_insert_{Guid.NewGuid():N}{suffix}");
        return (factory.Create(dbPath, "test123"), dbPath);
    }

    private static void CleanupDatabase(IDatabase db, string dbPath)
    {
        try
        {
            if (db is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // Ignore disposal errors
        }

        try
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
            else if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(11)]
    public void Insert_NamedParameters_MultipleColumns_LandInCorrectColumns(int columnCount)
    {
        var (db, dbPath) = CreateDatabase("");
        var table = "t_" + columnCount;

        try
        {
            db.ExecuteSQL(
                $"CREATE TABLE {table} (a TEXT, b INTEGER, c REAL, d TEXT, e TEXT, f TEXT, g TEXT, h TEXT, i TEXT, j TEXT, k TEXT)");

            var columns = new List<string>();
            var placeholders = new List<string>();
            var parameters = new Dictionary<string, object?>();
            var expected = new Dictionary<string, object?>();

            for (var i = 0; i < columnCount; i++)
            {
                var col = ((char)('a' + i)).ToString();
                columns.Add(col);
                placeholders.Add("@" + col);

                object value = (i % 3) switch
                {
                    0 => "value-" + i,
                    1 => i * 10L,
                    _ => i + 0.5,
                };

                parameters["@" + col] = value;
                expected[col] = value;
            }

            var sql = $"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})";
            db.ExecuteSQL(sql, parameters);

            var rows = db.ExecuteQuery($"SELECT * FROM {table}");
            Assert.Single(rows);

            foreach (var kvp in expected)
            {
                Assert.Equal(
                    Convert.ToString(kvp.Value, System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToString(rows[0][kvp.Key], System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void Insert_NamedParameters_PrefixCollision_TenantIdNotCorrupted()
    {
        var (db, dbPath) = CreateDatabase("");

        try
        {
            db.ExecuteSQL(
                "CREATE TABLE kg_edges_test (id TEXT PRIMARY KEY, source_graph_node_id BIGINT NOT NULL, " +
                "target_graph_node_id BIGINT NOT NULL, relation_type TEXT NOT NULL, tenant_id TEXT NOT NULL)");

            db.ExecuteSQL(
                "INSERT INTO kg_edges_test (id, source_graph_node_id, target_graph_node_id, relation_type, tenant_id) " +
                "VALUES (@id, @s, @t, @r, @tid)",
                new Dictionary<string, object?>
                {
                    ["@id"] = "ROW_A",
                    ["@s"] = 100L,
                    ["@t"] = 200L,
                    ["@r"] = "depends-on",
                    ["@tid"] = "test-tenant",
                });

            var rows = db.ExecuteQuery(
                "SELECT id, source_graph_node_id, target_graph_node_id, relation_type, tenant_id " +
                "FROM kg_edges_test WHERE id = 'ROW_A'");

            Assert.Single(rows);
            Assert.Equal("ROW_A", rows[0]["id"]);
            Assert.Equal(100L, Convert.ToInt64(rows[0]["source_graph_node_id"]));
            Assert.Equal(200L, Convert.ToInt64(rows[0]["target_graph_node_id"]));
            Assert.Equal("depends-on", rows[0]["relation_type"]);
            Assert.Equal("test-tenant", rows[0]["tenant_id"]);
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void Select_NamedParameters_PrefixCollision_ReturnsCorrectRows()
    {
        var (db, dbPath) = CreateDatabase("");

        try
        {
            db.ExecuteSQL("CREATE TABLE t (id TEXT, tenant_id TEXT, value INTEGER)");
            db.ExecuteSQL("INSERT INTO t VALUES ('a', 'tenant-a', 1)");
            db.ExecuteSQL("INSERT INTO t VALUES ('b', 'tenant-b', 2)");

            var rows = db.ExecuteQuery(
                "SELECT * FROM t WHERE id = @t AND tenant_id = @tid",
                new Dictionary<string, object?>
                {
                    ["@t"] = "a",
                    ["@tid"] = "tenant-a",
                });

            Assert.Single(rows);
            Assert.Equal(1, Convert.ToInt32(rows[0]["value"]));
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void Update_RepeatedNamedParameter_ReplacesEveryOccurrence()
    {
        var (db, dbPath) = CreateDatabase("");

        try
        {
            db.ExecuteSQL("CREATE TABLE t (a TEXT, b TEXT)");
            db.ExecuteSQL("INSERT INTO t VALUES ('x', 'x')");

            db.ExecuteSQL(
                "UPDATE t SET a = @v, b = @v WHERE a = 'x'",
                new Dictionary<string, object?> { ["@v"] = "updated" });

            var rows = db.ExecuteQuery("SELECT a, b FROM t");
            Assert.Single(rows);
            Assert.Equal("updated", rows[0]["a"]);
            Assert.Equal("updated", rows[0]["b"]);
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void Insert_NamedParameters_SingleFileMode_LandInCorrectColumns()
    {
        var (db, dbPath) = CreateDatabase(".scdb");

        try
        {
            db.ExecuteSQL("CREATE TABLE t (id TEXT, source TEXT, target TEXT, tenant_id TEXT)");

            db.ExecuteSQL(
                "INSERT INTO t (id, source, target, tenant_id) VALUES (@id, @s, @t, @tid)",
                new Dictionary<string, object?>
                {
                    ["@id"] = "row1",
                    ["@s"] = "src",
                    ["@t"] = "tgt",
                    ["@tid"] = "tenant-1",
                });

            var rows = db.ExecuteQuery("SELECT id, source, target, tenant_id FROM t WHERE id = 'row1'");

            Assert.Single(rows);
            Assert.Equal("row1", rows[0]["id"]);
            Assert.Equal("src", rows[0]["source"]);
            Assert.Equal("tgt", rows[0]["target"]);
            Assert.Equal("tenant-1", rows[0]["tenant_id"]);
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }
}

