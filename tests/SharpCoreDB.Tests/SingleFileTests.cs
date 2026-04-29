// <copyright file="SingleFileTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Xunit;
using SharpCoreDB;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

/// <summary>
/// Basic tests for single-file (.scdb) database functionality.
/// </summary>
public class SingleFileTests : IDisposable
{
    private readonly string _testFilePath;
    private readonly DatabaseFactory _factory;

    public SingleFileTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void CreateSingleFileDatabase_Success()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();

        // Act
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Assert
        Assert.NotNull(db);
        Assert.Equal(StorageMode.SingleFile, db.StorageMode);
        Assert.True(File.Exists(_testFilePath));

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task SingleFileDatabase_Vacuum_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act
        var result = await db.VacuumAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(VacuumMode.Quick, result.Mode);

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void SingleFileDatabase_GetStorageStatistics_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act
        var stats = db.GetStorageStatistics();

        // Assert
        Assert.True(stats.TotalSize > 0);
        Assert.True(stats.BlockCount >= 0);

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task SingleFileDatabase_ExecuteQuery_StorageStats_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act
        var results = db.ExecuteQuery("SELECT * FROM STORAGE");

        // Assert
        Assert.Single(results);
        Assert.Contains("TotalSize", results[0]);
        Assert.Contains("BlockCount", results[0]);

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void SingleFileDatabase_Flush_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        // Act - should not throw
        db.Flush();

        // Assert - no exception thrown

        // Cleanup
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void SingleFileDatabase_PrepareSelect_ReturnsCompiledStatement()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE prepared_users (id INTEGER, name TEXT)");

            // Act
            var stmt = db.Prepare("SELECT * FROM prepared_users WHERE id = 1");

            // Assert
            Assert.True(stmt.IsCompiled);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task SingleFileDatabase_ExecutePreparedAsync_InsertWithParameters_Works()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            db.ExecuteSQL("CREATE TABLE prepared_insert (id INTEGER, name TEXT)");
            var stmt = db.Prepare("INSERT INTO prepared_insert VALUES (?, ?)");

            // Act
            await db.ExecutePreparedAsync(stmt, new Dictionary<string, object?>
            {
                ["0"] = 1,
                ["1"] = "Alice"
            }, cancellationToken);

            var results = db.ExecuteQuery("SELECT * FROM prepared_insert WHERE id = 1");

            // Assert
            Assert.Single(results);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFileDatabase_ExecuteCompiledQuery_WithPreparedSelect_ReturnsRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE compiled_users (id INTEGER, name TEXT)");
            db.ExecuteBatchSQL([
                "INSERT INTO compiled_users VALUES (1, 'Alice')",
                "INSERT INTO compiled_users VALUES (2, 'Bob')"
            ]);
            db.Flush();
            db.ForceSave();

            var stmt = db.Prepare("SELECT * FROM compiled_users WHERE id = 2");

            // Act
            var results = db.ExecuteCompiledQuery(stmt);

            // Assert
            Assert.Single(results);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SingleFileDatabase_ExecuteCompiled_WithParameterizedPlan_ReturnsRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE compiled_params (id INTEGER, name TEXT)");
            db.ExecuteBatchSQL([
                "INSERT INTO compiled_params VALUES (1, 'Alice')",
                "INSERT INTO compiled_params VALUES (2, 'Bob')"
            ]);
            db.Flush();
            db.ForceSave();

            var stmt = db.Prepare("SELECT * FROM compiled_params WHERE id = ?");

            // Act
            var results = db.ExecuteCompiled(stmt.CompiledPlan!, new Dictionary<string, object?>
            {
                ["0"] = 1
            });

            // Assert
            Assert.Single(results);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Tests for issue #227: quoted identifiers (e.g. "TableName", "ColumnName") in SingleFileDatabase SQL parser.
    /// FluentMigrator generates SQL with double-quoted identifiers; the parser must handle them correctly.
    /// </summary>
    [Fact]
    public void ExecuteSQL_CreateTable_WithQuotedTableName_CreatesTableSuccessfully()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            // Act — FluentMigrator generates quoted identifiers like this
            db.ExecuteSQL(@"CREATE TABLE IF NOT EXISTS ""__SharpMigrations"" (""Version"" INTEGER NOT NULL, PRIMARY KEY (""Version""))");

            // Assert — table should exist without throwing
            Assert.True(db.TryGetTable("__SharpMigrations", out _));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteSQL_CreateTable_WithQuotedTableName_StripsQuotesFromColumnNames()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            // Act
            db.ExecuteSQL(@"CREATE TABLE IF NOT EXISTS ""migrations"" (""Version"" INTEGER NOT NULL, ""AppliedOn"" TEXT NOT NULL, PRIMARY KEY (""Version""))");
            db.ExecuteSQL(@"INSERT INTO ""migrations"" (""Version"", ""AppliedOn"") VALUES (1, '2026-01-01')");

            // Assert — columns should be stored without quotes so SELECT works
            var rows = db.ExecuteQuery(@"SELECT * FROM ""migrations""");
            Assert.Single(rows);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteSQL_CreateTable_WithTableLevelPrimaryKeyAndQuotedColumn_DetectsPrimaryKey()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            // Act — table-level PRIMARY KEY with quoted column, as generated by FluentMigrator
            db.ExecuteSQL(@"CREATE TABLE ""pk_test"" (""Id"" INTEGER NOT NULL, ""Name"" TEXT, PRIMARY KEY (""Id""))");
            db.ExecuteSQL(@"INSERT INTO ""pk_test"" (""Id"", ""Name"") VALUES (42, 'hello')");

            var rows = db.ExecuteQuery(@"SELECT * FROM ""pk_test""");
            Assert.Single(rows);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteSQL_DropTable_WithQuotedTableName_DropsSuccessfully()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL(@"CREATE TABLE ""drop_me"" (""Id"" INTEGER NOT NULL)");

            // Act
            db.ExecuteSQL(@"DROP TABLE IF EXISTS ""drop_me""");

            // Assert
            Assert.False(db.TryGetTable("drop_me", out _));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// JOIN tests — single-file SELECT is routed through the shared SqlParser,
    /// so all join variants already work. These tests lock in that behaviour.
    /// </summary>
    [Fact]
    public void ExecuteQuery_InnerJoin_ReturnsMatchingRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE orders (order_id INTEGER, customer_id INTEGER, amount REAL)");
            db.ExecuteSQL("CREATE TABLE customers (customer_id INTEGER, name TEXT)");
            db.ExecuteBatchSQL([
                "INSERT INTO customers VALUES (1, 'Alice')",
                "INSERT INTO customers VALUES (2, 'Bob')",
                "INSERT INTO orders VALUES (10, 1, 99.50)",
                "INSERT INTO orders VALUES (11, 1, 45.00)",
                "INSERT INTO orders VALUES (12, 2, 200.00)"
            ]);

            // Act
            var results = db.ExecuteQuery(
                "SELECT orders.order_id, customers.name FROM orders " +
                "INNER JOIN customers ON orders.customer_id = customers.customer_id");

            // Assert — all three orders have a matching customer
            Assert.Equal(3, results.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteQuery_LeftJoin_IncludesUnmatchedLeftRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE products (product_id INTEGER, name TEXT)");
            db.ExecuteSQL("CREATE TABLE reviews (review_id INTEGER, product_id INTEGER, score INTEGER)");
            db.ExecuteBatchSQL([
                "INSERT INTO products VALUES (1, 'Widget')",
                "INSERT INTO products VALUES (2, 'Gadget')",
                "INSERT INTO reviews VALUES (100, 1, 5)"
                // product 2 has no review
            ]);

            // Act
            var results = db.ExecuteQuery(
                "SELECT products.name, reviews.score FROM products " +
                "LEFT JOIN reviews ON products.product_id = reviews.product_id");

            // Assert — both products appear; Gadget row has NULL score
            Assert.Equal(2, results.Count);
            var gadget = results.First(r => r["name"]?.ToString() == "Gadget");
            // SQL NULL is represented as DBNull.Value in dictionary results
            Assert.True(gadget["score"] is null or DBNull);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteQuery_Join_WithWhereFilter_ReturnsFilteredRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE emp (emp_id INTEGER, dept_id INTEGER, name TEXT)");
            db.ExecuteSQL("CREATE TABLE dept (dept_id INTEGER, dept_name TEXT)");
            db.ExecuteBatchSQL([
                "INSERT INTO dept VALUES (1, 'Engineering')",
                "INSERT INTO dept VALUES (2, 'Marketing')",
                "INSERT INTO emp VALUES (1, 1, 'Alice')",
                "INSERT INTO emp VALUES (2, 1, 'Bob')",
                "INSERT INTO emp VALUES (3, 2, 'Carol')"
            ]);

            // Act — only Engineering employees
            var results = db.ExecuteQuery(
                "SELECT emp.name, dept.dept_name FROM emp " +
                "INNER JOIN dept ON emp.dept_id = dept.dept_id " +
                "WHERE dept.dept_name = 'Engineering'");

            // Assert
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("Engineering", r["dept_name"]?.ToString()));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void ExecuteQuery_ThreeTableJoin_ReturnsCorrectRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);

        try
        {
            db.ExecuteSQL("CREATE TABLE authors (author_id INTEGER, author_name TEXT)");
            db.ExecuteSQL("CREATE TABLE books (book_id INTEGER, author_id INTEGER, title TEXT)");
            db.ExecuteSQL("CREATE TABLE sales (sale_id INTEGER, book_id INTEGER, qty INTEGER)");
            db.ExecuteBatchSQL([
                "INSERT INTO authors VALUES (1, 'Tolkien')",
                "INSERT INTO books VALUES (10, 1, 'The Hobbit')",
                "INSERT INTO books VALUES (11, 1, 'LOTR')",
                "INSERT INTO sales VALUES (100, 10, 5)",
                "INSERT INTO sales VALUES (101, 11, 12)"
            ]);

            // Act
            var results = db.ExecuteQuery(
                "SELECT authors.author_name, books.title, sales.qty FROM authors " +
                "INNER JOIN books ON authors.author_id = books.author_id " +
                "INNER JOIN sales ON books.book_id = sales.book_id");

            // Assert — both books sold by Tolkien appear
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal("Tolkien", r["author_name"]?.ToString()));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void AlterTable_RenameColumn_UpdatesRowData()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE items (id INTEGER, old_name TEXT)");
            db.ExecuteBatchSQL(["INSERT INTO items VALUES (1, 'alpha')", "INSERT INTO items VALUES (2, 'beta')"]);

            // Act
            db.ExecuteSQL("ALTER TABLE items RENAME COLUMN old_name TO new_name");

            // Assert — renamed column is queryable
            var results = db.ExecuteQuery("SELECT new_name FROM items ORDER BY id");
            Assert.Equal(2, results.Count);
            Assert.Equal("alpha", results[0]["new_name"]?.ToString());
            Assert.Equal("beta", results[1]["new_name"]?.ToString());
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void AlterTable_DropColumn_RemovesColumnFromRows()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE products (id INTEGER, name TEXT, temp_col TEXT)");
            db.ExecuteBatchSQL(["INSERT INTO products VALUES (1, 'widget', 'x')", "INSERT INTO products VALUES (2, 'gadget', 'y')"]);

            // Act
            db.ExecuteSQL("ALTER TABLE products DROP COLUMN temp_col");

            // Assert — remaining column still readable; dropped column absent
            var results = db.ExecuteQuery("SELECT id, name FROM products ORDER BY id");
            Assert.Equal(2, results.Count);
            Assert.Equal("widget", results[0]["name"]?.ToString());
            Assert.DoesNotContain("temp_col", results[0].Keys);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void CreateIndex_And_DropIndex_UpdatesIndexRegistry()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE orders (id INTEGER, customer TEXT)");

            // Act — create index
            db.ExecuteSQL("CREATE INDEX idx_customer ON orders (customer)");

            // Assert — index visible via sqlite_master
            var after = db.ExecuteQuery("SELECT name FROM sqlite_master WHERE type='index' AND name='idx_customer'");
            Assert.Single(after);

            // Act — drop index
            db.ExecuteSQL("DROP INDEX idx_customer");

            // Assert — index gone
            var gone = db.ExecuteQuery("SELECT name FROM sqlite_master WHERE type='index' AND name='idx_customer'");
            Assert.Empty(gone);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void PragmaTableInfo_ReturnsColumnMetadata()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE employees (emp_id INTEGER PRIMARY KEY, emp_name TEXT NOT NULL)");

            // Act
            var info = db.ExecuteQuery("PRAGMA table_info(employees)");

            // Assert
            Assert.Equal(2, info.Count);
            var names = info.Select(r => r["name"]?.ToString()).ToList();
            Assert.Contains("emp_id", names);
            Assert.Contains("emp_name", names);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void AlterTable_AddColumn_AppendsNewColumn()
    {
        // Arrange
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = _factory.CreateWithOptions(_testFilePath, "test_password", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE users (id INTEGER, username TEXT)");
            db.ExecuteSQL("INSERT INTO users VALUES (1, 'alice')");

            // Act
            db.ExecuteSQL("ALTER TABLE users ADD COLUMN email TEXT");

            // Assert — new column present in schema probe
            var info = db.ExecuteQuery("PRAGMA table_info(users)");
            var names = info.Select(r => r["name"]?.ToString()).ToList();
            Assert.Contains("email", names);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
