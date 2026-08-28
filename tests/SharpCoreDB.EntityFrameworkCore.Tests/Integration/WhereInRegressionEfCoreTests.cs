namespace SharpCoreDB.EntityFrameworkCore.Tests.Integration;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore.Storage;
using System;
using System.Data.Common;
using System.IO;

/// <summary>
/// End-to-end regression tests for GitHub issue #339 (WHERE col IN (...) returns ALL rows),
/// mirroring the reporter's standalone repro that uses <see cref="SharpCoreDBConnection"/>
/// directly against a single-file (.scdb) database.
/// </summary>
public sealed class WhereInRegressionEfCoreTests : IDisposable
{
    private readonly string _dbPath;

    public WhereInRegressionEfCoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"scdb-in-regression-{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException) { /* cleanup best-effort */ }
    }

    private SharpCoreDBConnection CreateConnection()
    {
        var services = new ServiceCollection()
            .AddSingleton($"DataSource={_dbPath};Password=test;")
            .AddSingleton<SharpCoreDB.DatabaseFactory>()
            .BuildServiceProvider();

        return new SharpCoreDBConnection(services, $"DataSource={_dbPath};Password=test;");
    }

    private static void SeedNodes(SharpCoreDBConnection conn)
    {
        using (var c = conn.CreateCommand())
        {
            c.CommandText = """
                CREATE TABLE kg_nodes_test (
                    id TEXT PRIMARY KEY,
                    node_type TEXT NOT NULL,
                    external_id TEXT NOT NULL
                )
                """;
            c.ExecuteNonQuery();
        }

        foreach (var node in new[] { ("A", "WorkItem", "WI-1"), ("B", "WorkItem", "WI-2"), ("C", "Person", "P-1") })
        {
            using var c = conn.CreateCommand();
            c.CommandText = "INSERT INTO kg_nodes_test (id, node_type, external_id) VALUES (@id, @nt, @ei)";
            AddParameter(c, "@id", node.Item1);
            AddParameter(c, "@nt", node.Item2);
            AddParameter(c, "@ei", node.Item3);
            c.ExecuteNonQuery();
        }
    }

    private static void AddParameter(DbCommand c, string name, object value)
    {
        var p = c.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        c.Parameters.Add(p);
    }

    private static int CountRows(SharpCoreDBConnection conn, string sql)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        using var r = c.ExecuteReader();
        int rows = 0;
        while (r.Read())
        {
            rows++;
        }

        return rows;
    }

    [Fact]
    public void WhereIn_LiteralList_ReturnsMatchingRows_NotAllRows()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        // 3 rows total: WorkItem, WorkItem, Person. A filter that does NOT match every row
        // must never return the whole table.
        Assert.Equal(2, CountRows(conn, "SELECT id FROM kg_nodes_test WHERE node_type IN ('WorkItem')"));
        Assert.Equal(1, CountRows(conn, "SELECT id FROM kg_nodes_test WHERE node_type IN ('Person')"));
        Assert.Equal(1, CountRows(conn, "SELECT id FROM kg_nodes_test WHERE node_type NOT IN ('WorkItem')"));

        // Control: plain equality still works.
        Assert.Equal(2, CountRows(conn, "SELECT id FROM kg_nodes_test WHERE node_type = 'WorkItem'"));
    }

    [Fact]
    public void WhereIn_ParameterizedList_ReturnsMatchingRows_NotAllRows()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)";
            AddParameter(c, "@p0", "WorkItem");
            AddParameter(c, "@p1", "Person");
            using var r = c.ExecuteReader();
            int rows = 0;
            while (r.Read())
            {
                rows++;
            }

            Assert.Equal(3, rows);
        }

        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0)";
            AddParameter(c, "@p0", "Person");
            using var r = c.ExecuteReader();
            int rows = 0;
            while (r.Read())
            {
                rows++;
            }

            Assert.Equal(1, rows);
        }
    }
}
