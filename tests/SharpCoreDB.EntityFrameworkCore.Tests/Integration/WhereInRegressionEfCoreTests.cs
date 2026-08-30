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

    // --- Issue #340: discriminating provider cases. The IN filter must return a subset
    // (never the whole table), and the SQLite VALUES / tuple forms must work. ---

    [Fact]
    public void WhereIn_ParameterizedList_SubsetFilter_DoesNotReturnAllRows()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        using var c = conn.CreateCommand();
        c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1)";
        AddParameter(c, "@p0", "WorkItem");
        AddParameter(c, "@p1", "DoesNotExist");
        using var r = c.ExecuteReader();
        int rows = 0;
        while (r.Read())
        {
            rows++;
        }

        // The second list item matches nothing: a working filter returns the 2 WorkItem
        // rows. Returning 3 would mean the predicate is ignored (the regression).
        Assert.Equal(2, rows);
    }

    [Fact]
    public void WhereIn_VALUESForm_ReturnsMatchingRows()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type IN (VALUES (@p0))";
            AddParameter(c, "@p0", "WorkItem");
            using var r = c.ExecuteReader();
            int rows = 0;
            while (r.Read())
            {
                rows++;
            }

            Assert.Equal(2, rows);
        }

        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type IN (VALUES (@p0), (@p1))";
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
    }

    [Fact]
    public void ExecuteNonQuery_ReturnsAffectedRows()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        // Sanity: how many rows does a plain SELECT find for node_type = 'WorkItem'?
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type = 'WorkItem'";
            using var r = c.ExecuteReader();
            int rows = 0;
            while (r.Read())
            {
                rows++;
            }

            Assert.Equal(2, rows);
        }

        // UPDATE must report the number of matching rows (2 WorkItem rows).
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "UPDATE kg_nodes_test SET external_id = 'x' WHERE node_type = 'WorkItem'";
            Assert.Equal(2, c.ExecuteNonQuery());
        }

        // DELETE must report the number of deleted rows, not a constant 1.
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "DELETE FROM kg_nodes_test WHERE node_type = 'Person'";
            Assert.Equal(1, c.ExecuteNonQuery());
        }

        using (var c = conn.CreateCommand())
        {
            c.CommandText = "DELETE FROM kg_nodes_test WHERE node_type = 'WorkItem'";
            Assert.Equal(2, c.ExecuteNonQuery());
        }

        // Nothing left: DELETE over an empty result must report 0.
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "DELETE FROM kg_nodes_test WHERE node_type = 'WorkItem'";
            Assert.Equal(0, c.ExecuteNonQuery());
        }
    }

    [Fact]
    public void WhereIn_TupleValuesForm_ReturnsMatchingRows()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        using var c = conn.CreateCommand();
        c.CommandText = "SELECT id FROM kg_nodes_test WHERE (node_type, external_id) IN (VALUES (@nt, @ei))";
        AddParameter(c, "@nt", "WorkItem");
        AddParameter(c, "@ei", "WI-1");
        using var r = c.ExecuteReader();
        int rows = 0;
        while (r.Read())
        {
            rows++;
        }

        Assert.Equal(1, rows);
    }

    [Fact]
    public void WhereIn_ParameterizedList_SubsetFilter_DoesNotReturnAllRows_MultiValue()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        // 5-value list with a single matching value: a "returns all rows" bug would give 3,
        // the correct filter gives the 2 WorkItem rows.
        using var c = conn.CreateCommand();
        c.CommandText = "SELECT id FROM kg_nodes_test WHERE node_type IN (@p0, @p1, @p2, @p3, @p4)";
        AddParameter(c, "@p0", "DoesNotExist0");
        AddParameter(c, "@p1", "WorkItem");
        AddParameter(c, "@p2", "DoesNotExist2");
        AddParameter(c, "@p3", "DoesNotExist3");
        AddParameter(c, "@p4", "DoesNotExist4");
        using var r = c.ExecuteReader();
        int rows = 0;
        while (r.Read())
        {
            rows++;
        }

        Assert.Equal(2, rows);
    }

    [Fact]
    public void Where_ParenthesizedOr_ReturnsMatchingRows_NotZero()
    {
        using var conn = CreateConnection();
        conn.Open();
        SeedNodes(conn);

        // Issue #348: "(a OR b)" must filter like "a OR b". With a non-matching second
        // operand this must return the 2 WorkItem rows, never 0 (the pre-fix behavior).
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE (node_type = @p0 OR node_type = @p1)";
            AddParameter(c, "@p0", "WorkItem");
            AddParameter(c, "@p1", "DoesNotExist");
            using var r = c.ExecuteReader();
            int rows = 0;
            while (r.Read())
            {
                rows++;
            }

            Assert.Equal(2, rows);
        }

        // Parenthesized OR where both operands match → full table (3).
        using (var c = conn.CreateCommand())
        {
            c.CommandText = "SELECT id FROM kg_nodes_test WHERE (node_type = @p0 OR node_type = @p1)";
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
    }
}
