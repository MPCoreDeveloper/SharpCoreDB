// <copyright file="PgCatalogServiceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.IntegrationTests;

/// <summary>
/// Tests for <see cref="SharpCoreDB.Server.Core.Catalog.PgCatalogService"/> metadata query interception.
/// Validates that catalog queries return well-formed rows from the live database schema.
/// </summary>
public sealed class PgCatalogServiceTests : IAsyncLifetime
{
    private readonly TestServerFixture _fixture = new();

    public async ValueTask InitializeAsync()
    {
        await _fixture.InitializeAsync();

        // Create tables for catalog introspection
        await _fixture.ExecuteSetupSqlAsync(
            "CREATE TABLE IF NOT EXISTS catalog_test_orders (id INTEGER, customer TEXT, amount DOUBLE)");
        await _fixture.ExecuteSetupSqlAsync(
            "CREATE TABLE IF NOT EXISTS catalog_test_products (product_id INTEGER, name TEXT, price DOUBLE, active BOOLEAN)");
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    // --------- scalar function queries ---------

    [Fact]
    public void TryHandleCatalogQuery_CurrentDatabase_ReturnsDatabaseName()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT current_database()",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Single(rows);
        Assert.Contains("current_database", columns);
        Assert.Equal("testdb", rows[0]["current_database"]);
    }

    [Fact]
    public void TryHandleCatalogQuery_Version_ReturnsVersionString()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT version()",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Single(rows);
        Assert.Contains("version", columns);
        Assert.Contains("SharpCoreDB", rows[0]["version"]?.ToString());
    }

    [Fact]
    public void TryHandleCatalogQuery_CurrentUser_ReturnsUserName()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT current_user",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Single(rows);
        Assert.Equal("admin", rows[0]["current_user"]);
    }

    // --------- information_schema.tables ---------

    [Fact]
    public void TryHandleCatalogQuery_InformationSchemaTables_ReturnsUserTables()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT table_name FROM information_schema.tables ORDER BY table_name",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("table_name", columns);
        Assert.Contains(rows, r => r["table_name"]?.ToString() == "catalog_test_orders");
        Assert.Contains(rows, r => r["table_name"]?.ToString() == "catalog_test_products");
        Assert.All(rows, r => Assert.Equal("BASE TABLE", r["table_type"]?.ToString()));
        Assert.All(rows, r => Assert.Equal("public", r["table_schema"]?.ToString()));
    }

    [Fact]
    public void TryHandleCatalogQuery_InformationSchemaTables_FilterByPublicSchema_ReturnsRows()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void TryHandleCatalogQuery_InformationSchemaTables_FilterByNonPublicSchema_ReturnsEmpty()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'pg_catalog'",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Empty(rows);
    }

    // --------- information_schema.columns ---------

    [Fact]
    public void TryHandleCatalogQuery_InformationSchemaColumns_ReturnsColumnsForTable()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = 'catalog_test_products'",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("column_name", columns);
        Assert.Contains("data_type", columns);
        Assert.Contains(rows, r => r["column_name"]?.ToString() == "product_id");
        Assert.Contains(rows, r => r["column_name"]?.ToString() == "name");
        Assert.Contains(rows, r => r["column_name"]?.ToString() == "price");
        Assert.Contains(rows, r => r["column_name"]?.ToString() == "active");
    }

    [Fact]
    public void TryHandleCatalogQuery_InformationSchemaColumns_HasOrdinalPosition()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT column_name, ordinal_position FROM information_schema.columns ORDER BY table_name, ordinal_position LIMIT 20",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.NotNull(r["ordinal_position"]));
    }

    // --------- pg_catalog views ---------

    [Fact]
    public void TryHandleCatalogQuery_PgTables_ReturnsUserTables()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'public'",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("tablename", columns);
        Assert.Contains(rows, r => r["tablename"]?.ToString() == "catalog_test_orders");
    }

    [Fact]
    public void TryHandleCatalogQuery_PgClass_ReturnsRelations()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT oid, relname, relkind FROM pg_catalog.pg_class",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("relname", columns);
        Assert.Contains("relkind", columns);
        Assert.Contains(rows, r => r["relname"]?.ToString() == "catalog_test_orders");
        Assert.All(rows, r => Assert.Equal("r", r["relkind"]?.ToString()));
    }

    [Fact]
    public void TryHandleCatalogQuery_PgNamespace_ContainsPublicSchema()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT oid, nspname FROM pg_catalog.pg_namespace",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("nspname", columns);
        Assert.Contains(rows, r => r["nspname"]?.ToString() == "public");
        Assert.Contains(rows, r => r["nspname"]?.ToString() == "pg_catalog");
        Assert.Contains(rows, r => r["nspname"]?.ToString() == "information_schema");
    }

    [Fact]
    public void TryHandleCatalogQuery_PgType_ContainsBaseTypes()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT oid, typname FROM pg_catalog.pg_type",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("typname", columns);
        Assert.Contains(rows, r => r["typname"]?.ToString() == "text");
        Assert.Contains(rows, r => r["typname"]?.ToString() == "int4");
        Assert.Contains(rows, r => r["typname"]?.ToString() == "bool");
    }

    [Fact]
    public void TryHandleCatalogQuery_PgAttribute_ReturnsColumnsForAllTables()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT attrelid, attname, atttypid FROM pg_catalog.pg_attribute",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("attname", columns);
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r["attname"]?.ToString() == "customer");
    }

    // --------- non-catalog queries ---------

    [Fact]
    public void TryHandleCatalogQuery_UserTableQuery_ReturnsFalse()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT * FROM catalog_test_orders",
            db, "testdb", "admin",
            out _, out _);

        // Assert
        Assert.False(handled);
    }

    [Fact]
    public void TryHandleCatalogQuery_InformationSchemaSchemata_ContainsPublic()
    {
        // Arrange
        var service = _fixture.GetPgCatalogService();
        var db = _fixture.DatabaseRegistry!.GetDatabase("testdb")!.Database;

        // Act
        var handled = service.TryHandleCatalogQuery(
            "SELECT schema_name FROM information_schema.schemata",
            db, "testdb", "admin",
            out var rows, out var columns);

        // Assert
        Assert.True(handled);
        Assert.Contains("schema_name", columns);
        Assert.Contains(rows, r => r["schema_name"]?.ToString() == "public");
    }
}
