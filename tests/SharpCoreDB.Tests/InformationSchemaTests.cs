// <copyright file="InformationSchemaTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for the engine-level <c>information_schema</c> metadata views
/// (see <c>src/SharpCoreDB/Services/SqlParser.InformationSchema.cs</c>).
/// Covers the query the compatibility smoke test sends over REST, every populated view, the
/// supported predicate/projection surface and the loud-failure paths.
/// Serial because views and triggers are process-wide registries.
/// </summary>
[Collection("SerialTriggerTests")]
public sealed class InformationSchemaTests : IDisposable
{
    private const string ViewName = "info_active_users";

    private readonly string testDbPath;
    private readonly Database db;

    public InformationSchemaTests()
    {
        SqlParser.ClearAllTriggersForTesting();

        testDbPath = Path.Combine(Path.GetTempPath(), $"info_schema_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        db = new Database(
            new ServiceCollection().AddSharpCoreDB().BuildServiceProvider(),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);

        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT NOT NULL, score REAL)");
        db.ExecuteSQL("CREATE TABLE orders (order_id INTEGER PRIMARY KEY, user_id INTEGER)");
    }

    public void Dispose()
    {
        try { db.ExecuteSQL($"DROP VIEW IF EXISTS {ViewName}"); } catch { }
        try { SqlParser.ClearAllTriggersForTesting(); } catch { }
        try { db.Dispose(); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(100);

        if (Directory.Exists(testDbPath))
        {
            try { Directory.Delete(testDbPath, recursive: true); } catch { }
        }
    }

    // ──────────────────────────────────────────────
    //  The documented queries (smoke test / Vectors guide)
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectTables_SmokeQueryShape_ReturnsRowsWithRequestedColumns()
    {
        // The exact statement tests/CompatibilitySmoke/smoke_tests.py posts to /api/v1/query.
        var rows = db.ExecuteQuery("SELECT table_schema, table_name FROM information_schema.tables LIMIT 5");

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(["table_schema", "table_name"], row.Keys));
        Assert.All(rows, row => Assert.Equal("public", row["table_schema"]));
        Assert.Contains(rows, row => (string)row["table_name"] == "users");
    }

    [Fact]
    public void SelectTables_BaseTables_ReportBaseTableType()
    {
        var rows = db.ExecuteQuery(
            "SELECT table_name, table_type, is_insertable_into FROM information_schema.tables WHERE table_schema = 'public'");

        var users = rows.Single(row => (string)row["table_name"] == "users");
        Assert.Equal("BASE TABLE", users["table_type"]);
        Assert.Equal("YES", users["is_insertable_into"]);
    }

    [Fact]
    public void SelectTables_LiteralInformationSchemaInPredicate_StaysOnNormalPath()
    {
        db.ExecuteSQL("CREATE TABLE metrics (name TEXT, kind TEXT)");
        db.ExecuteSQL("INSERT INTO metrics VALUES ('tables', 'information_schema')");

        var rows = db.ExecuteQuery("SELECT name FROM metrics WHERE kind = 'information_schema'");

        Assert.Single(rows);
        Assert.Equal("tables", rows[0]["name"]);
    }

    [Fact]
    public void SelectViews_AfterCreateView_ListsViewAsView()
    {
        db.ExecuteSQL($"CREATE VIEW {ViewName} AS SELECT id, email FROM users");

        var tables = db.ExecuteQuery(
            $"SELECT table_name, table_type FROM information_schema.tables WHERE table_name = '{ViewName}'");
        Assert.Single(tables);
        Assert.Equal("VIEW", tables[0]["table_type"]);

        var views = db.ExecuteQuery(
            $"SELECT table_name, view_definition FROM information_schema.views WHERE table_name = '{ViewName}'");
        Assert.Single(views);
        Assert.Contains("FROM users", (string)views[0]["view_definition"], StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    //  columns / schemata
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectColumns_OrdersByOrdinalPosition_WithMappedTypes()
    {
        var rows = db.ExecuteQuery(
            "SELECT column_name, ordinal_position, data_type, is_nullable FROM information_schema.columns " +
            "WHERE table_name = 'users' ORDER BY ordinal_position");

        Assert.Equal(["id", "email", "score"], rows.Select(row => (string)row["column_name"]));
        Assert.Equal([1, 2, 3], rows.Select(row => (int)row["ordinal_position"]));
        Assert.Equal(["integer", "text", "double precision"], rows.Select(row => (string)row["data_type"]));
    }

    [Fact]
    public void SelectColumns_NotNullColumn_ReportsNoForIsNullable()
    {
        var rows = db.ExecuteQuery(
            "SELECT column_name, is_nullable FROM information_schema.columns WHERE table_name = 'users'");

        Assert.Equal("NO", rows.Single(row => (string)row["column_name"] == "email")["is_nullable"]);
        Assert.Equal("YES", rows.Single(row => (string)row["column_name"] == "score")["is_nullable"]);
    }

    [Fact]
    public void SelectColumns_Unprojected_ExposesFullStandardColumnList()
    {
        var rows = db.ExecuteQuery("SELECT * FROM information_schema.columns WHERE table_name = 'users' LIMIT 1");

        Assert.Single(rows);
        Assert.Equal(44, rows[0].Count);
        Assert.Contains("column_default", rows[0].Keys);
        Assert.Contains("is_updatable", rows[0].Keys);
        Assert.Null(rows[0]["character_maximum_length"]);
    }

    [Fact]
    public void SelectSchemata_ReturnsPublicAndInformationSchema()
    {
        var rows = db.ExecuteQuery("SELECT schema_name FROM information_schema.schemata ORDER BY schema_name");

        Assert.Contains(rows, row => (string)row["schema_name"] == "public");
        Assert.Contains(rows, row => (string)row["schema_name"] == "information_schema");
    }

    // ──────────────────────────────────────────────
    //  indexes (the query docs/Vectors points at)
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectIndexes_AfterCreateIndex_ReportsNamedHashIndex()
    {
        db.ExecuteSQL("CREATE INDEX idx_users_email ON users(email)");

        var rows = db.ExecuteQuery(
            "SELECT index_name, column_name, index_type, is_unique FROM information_schema.indexes WHERE table_name = 'users'");

        var named = rows.Single(row => (string)row["index_name"] == "idx_users_email");
        Assert.Equal("email", named["column_name"]);
        Assert.Equal("HASH", named["index_type"]);
        Assert.Equal("NO", named["is_unique"]);
    }

    [Fact]
    public void SelectIndexes_PrimaryKeyColumn_IsMarkedPrimary()
    {
        var rows = db.ExecuteQuery(
            "SELECT index_name, is_primary FROM information_schema.indexes WHERE table_name = 'orders'");

        Assert.Contains(rows, row => (string)row["index_name"] == "order_id" && (string)row["is_primary"] == "YES");
    }

    [Fact]
    public void SelectIndexes_AfterCreateVectorIndex_ReportsDeclaredIndexType()
    {
        db.ExecuteSQL("CREATE TABLE documents (doc_id INTEGER PRIMARY KEY, embedding VECTOR(8))");
        db.ExecuteSQL("CREATE VECTOR INDEX idx_documents_embedding ON documents(embedding) USING HNSW");

        var rows = db.ExecuteQuery(
            "SELECT * FROM information_schema.indexes WHERE table_name = 'documents'");

        var vectorIndex = rows.Single(row => (string)row["index_name"] == "idx_documents_embedding");
        Assert.Equal("embedding", vectorIndex["column_name"]);
        Assert.Equal("HNSW", vectorIndex["index_type"]);
    }

    // ──────────────────────────────────────────────
    //  predicate / projection surface
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectCountStar_ReturnsEngineCountRow()
    {
        var expected = db.ExecuteQuery("SELECT table_name FROM information_schema.tables").Count;

        var counted = db.ExecuteQuery("SELECT COUNT(*) FROM information_schema.tables");
        Assert.Single(counted);
        Assert.Equal((long)expected, counted[0]["cnt"]);

        var aliased = db.ExecuteQuery("SELECT COUNT(*) AS table_count FROM information_schema.tables");
        Assert.Equal((long)expected, aliased[0]["table_count"]);
    }

    [Fact]
    public void SelectWithLikeAndInPredicates_FilterRows()
    {
        var like = db.ExecuteQuery(
            "SELECT table_name FROM information_schema.tables WHERE table_name LIKE 'us%'");
        Assert.Equal(["users"], like.Select(row => (string)row["table_name"]));

        var inList = db.ExecuteQuery(
            "SELECT table_name FROM information_schema.tables WHERE table_name IN ('users', 'orders')");
        Assert.Equal(["orders", "users"], inList.Select(row => (string)row["table_name"]).Order());
    }

    [Fact]
    public void SelectWithNotLikeAndCompositePredicate_FilterRows()
    {
        var rows = db.ExecuteQuery(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE (table_name NOT LIKE '%zz%' AND table_schema = 'public') AND table_type = 'BASE TABLE'");

        Assert.Contains(rows, row => (string)row["table_name"] == "users");
        Assert.DoesNotContain(rows, row => (string)row["table_name"] == ViewName);
    }

    [Fact]
    public void SelectLimitAndOffset_PageDeterministically()
    {
        var all = db.ExecuteQuery("SELECT table_name FROM information_schema.tables ORDER BY table_name");
        var page = db.ExecuteQuery("SELECT table_name FROM information_schema.tables ORDER BY table_name LIMIT 1 OFFSET 1");

        Assert.True(all.Count >= 2);
        Assert.Single(page);
        Assert.Equal(all[1]["table_name"], page[0]["table_name"]);
    }

    [Fact]
    public void SelectOrderByDescending_ReversesAscendingOrder()
    {
        var ascending = db.ExecuteQuery("SELECT table_name FROM information_schema.tables ORDER BY table_name");
        var descending = db.ExecuteQuery("SELECT table_name FROM information_schema.tables ORDER BY table_name DESC");

        Assert.Equal(ascending[0]["table_name"], descending[^1]["table_name"]);
    }

    [Fact]
    public void SelectOrderByUnprojectedDeclaredColumn_IsStillAValidSortKey()
    {
        // Ordering runs before projection, so `table_schema` is a legal sort key even when the
        // SELECT list omits it.
        var rows = db.ExecuteQuery(
            "SELECT table_name FROM information_schema.tables ORDER BY table_schema, table_name");

        Assert.NotEmpty(rows);
        Assert.Equal(["table_name"], rows[0].Keys);
    }

    [Fact]
    public void SelectWithColumnAlias_RenamesProjectedColumn()
    {
        var rows = db.ExecuteQuery(
            "SELECT table_name AS name FROM information_schema.tables WHERE table_name = 'users'");

        Assert.Single(rows);
        Assert.Equal(["name"], rows[0].Keys);
        Assert.Equal("users", rows[0]["name"]);
    }

    // ──────────────────────────────────────────────
    //  loud failures + regressions
    // ──────────────────────────────────────────────

    [Fact]
    public void SelectUnknownView_ThrowsInsteadOfReturningEmpty()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => db.ExecuteQuery("SELECT * FROM information_schema.bogus_view"));

        Assert.Contains("information_schema.bogus_view does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectUnknownColumn_ThrowsInsteadOfIgnoringPredicate()
    {
        Assert.Throws<NotSupportedException>(
            () => db.ExecuteQuery("SELECT table_name FROM information_schema.tables WHERE bogus_column = 'x'"));
    }

    [Fact]
    public void SelectTrigger_AfterCreateTrigger_ReportsTimingAndEvent()
    {
        db.ExecuteSQL("CREATE TABLE audit_log (id INTEGER PRIMARY KEY, msg TEXT)");
        db.ExecuteSQL(
            "CREATE TRIGGER info_audit AFTER INSERT ON orders BEGIN INSERT INTO audit_log VALUES (99, 'inserted') END");

        var rows = db.ExecuteQuery(
            "SELECT trigger_name, event_object_table, event_manipulation, action_timing " +
            "FROM information_schema.triggers WHERE trigger_name = 'info_audit'");

        Assert.Single(rows);
        Assert.Equal("orders", rows[0]["event_object_table"]);
        Assert.Equal("INSERT", rows[0]["event_manipulation"]);
        Assert.Equal("AFTER", rows[0]["action_timing"]);
    }

    [Fact]
    public void SelectSqliteMaster_StaysOnItsOwnPath()
    {
        var rows = db.ExecuteQuery("SELECT name, type FROM sqlite_master WHERE type = 'table'");

        Assert.Contains(rows, row => (string)row["name"] == "users");
    }
}
