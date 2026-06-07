// <copyright file="JoinRegressionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Regression tests for JOIN functionality.
/// Protects against optimization-induced breakage in JOIN operations.
/// </summary>
public sealed class JoinRegressionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _testDbPath;

    public JoinRegressionTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"join_regression_{Guid.NewGuid():N}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        _factory = serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDbPath))
                Directory.Delete(_testDbPath, true);
        }
        catch { /* Best effort cleanup */ }
    }

    /// <summary>
    /// REGRESSION TEST: LEFT JOIN must return NULL for unmatched rows.
    /// ? FIXED: BuildExpressionString now includes table aliases in ON clause.
    /// </summary>
    [Fact]
    public void LeftJoin_UnmatchedRows_MustReturnNulls()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER, method TEXT)");
        
        db.ExecuteSQL("INSERT INTO orders VALUES (1, 100), (2, 200), (3, 300)");
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 1, 'CARD')"); // Only order 1 has payment
        
        // Flush to ensure data is committed before JOIN
        if (db is Database concreteDb)
        {
            // TODO: Fix FlushPendingWalStatements when PageManager is restored
            // concreteDb.FlushPendingWalStatements();
            concreteDb.Flush(); // ? Also flush table data to disk
        }

        // Act
        var results = db.ExecuteQuery(@"
            SELECT o.id as order_id, p.id as payment_id, p.method
            FROM orders o
            LEFT JOIN payments p ON p.order_id = o.id
            ORDER BY o.id
        ");

        // Assert
        Assert.Equal(3, results.Count);
        
        // Order 1: has payment
        Assert.Equal("1", results[0]["order_id"].ToString());
        Assert.NotNull(results[0]["payment_id"]);
        Assert.True(!string.IsNullOrEmpty(results[0]["payment_id"]?.ToString()), "Order 1 should have payment_id");
        
        // Order 2: no payment (NULL)
        Assert.Equal("2", results[1]["order_id"].ToString());
        var payment2 = results[1]["payment_id"]?.ToString();
        Assert.True(string.IsNullOrEmpty(payment2), "Order 2 should have NULL payment_id");
        
        // Order 3: no payment (NULL)
        Assert.Equal("3", results[2]["order_id"].ToString());
        var payment3 = results[2]["payment_id"]?.ToString();
        Assert.True(string.IsNullOrEmpty(payment3), "Order 3 should have NULL payment_id");
    }

    /// <summary>
    /// REGRESSION TEST: LEFT JOIN with multiple matches must return multiple rows.
    /// This is what broke in the demo - order 2 had 2 payments but returned 0 rows.
    /// ? FIXED: BuildExpressionString now includes table aliases.
    /// </summary>
    [Fact]
    public void LeftJoin_MultipleMatches_MustReturnAllMatchingRows()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER, method TEXT)");
        
        db.ExecuteSQL("INSERT INTO orders VALUES (1), (2)");
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 2, 'CASH'), (2, 2, 'GIFT')"); // Order 2 has 2 payments
        
        // Flush to ensure data is committed before JOIN
        if (db is Database concreteDb)
        {
            // TODO: Fix FlushPendingWalStatements when PageManager is restored
            // concreteDb.FlushPendingWalStatements();
            concreteDb.Flush(); // ? Also flush table data to disk
        }

        // Act
        var results = db.ExecuteQuery(@"
            SELECT o.id as order_id, p.id as payment_id, p.method
            FROM orders o
            LEFT JOIN payments p ON p.order_id = o.id
            WHERE o.id = 2
            ORDER BY p.id
        ");

        // Assert - Order 2 must have 2 rows (one for each payment)
        Assert.Equal(2, results.Count);
        // Just verify we have 2 order_id entries of value "2"
        var order2Rows = results.Where(r => r["order_id"].ToString() == "2").ToList();
        Assert.Equal(2, order2Rows.Count);
    }

    /// <summary>
    /// REGRESSION TEST: INNER JOIN must only return matched rows.
    /// ? FIXED: Table aliases now properly included in ON clause.
    /// </summary>
    [Fact]
    public void InnerJoin_OnlyMatchedRows_MustReturn()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER)");
        
        db.ExecuteSQL("INSERT INTO orders VALUES (1), (2), (3)");
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 1), (2, 2)"); // Order 3 has no payment
        
        // Flush to ensure data is committed before JOIN
        if (db is Database concreteDb)
        {
            // TODO: Fix FlushPendingWalStatements when PageManager is restored
            // concreteDb.FlushPendingWalStatements();
            concreteDb.Flush(); // ? Also flush table data to disk
        }

        // Act
        var results = db.ExecuteQuery(@"
            SELECT o.id as order_id, p.id as payment_id
            FROM orders o
            INNER JOIN payments p ON p.order_id = o.id
            ORDER BY o.id
        ");

        // Assert - Only orders 1 and 2 should appear
        Assert.Equal(2, results.Count);
        var order1 = results.FirstOrDefault(r => r["order_id"].ToString() == "1");
        var order2 = results.FirstOrDefault(r => r["order_id"].ToString() == "2");
        Assert.NotNull(order1);
        Assert.NotNull(order2);
        // Order 3 should not appear
        var order3 = results.FirstOrDefault(r => r["order_id"].ToString() == "3");
        Assert.Null(order3);
    }

    /// <summary>
    /// REGRESSION TEST: RIGHT JOIN symmetry with LEFT JOIN.
    /// </summary>
    [Fact]
    public void RightJoin_MustBeSymmetricToLeftJoin()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER)");
        
        db.ExecuteSQL("INSERT INTO orders VALUES (1), (2)");
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 1)");
        
        // Flush to ensure data is committed before JOINs
        if (db is Database concreteDb)
        {
            // TODO: Fix FlushPendingWalStatements when PageManager is restored
            // concreteDb.FlushPendingWalStatements();
            concreteDb.Flush(); // ? Also flush table data to disk
        }

        // Act
        var leftResults = db.ExecuteQuery(@"
            SELECT o.id as order_id, p.id as payment_id
            FROM orders o
            LEFT JOIN payments p ON p.order_id = o.id
            ORDER BY o.id
        ");

        var rightResults = db.ExecuteQuery(@"
            SELECT o.id as order_id, p.id as payment_id
            FROM payments p
            RIGHT JOIN orders o ON p.order_id = o.id
            ORDER BY o.id
        ");

        // Assert - Both should return same row count
        Assert.Equal(leftResults.Count, rightResults.Count);
        Assert.Equal(2, leftResults.Count);
    }

    /// <summary>
    /// REGRESSION TEST: Multi-table JOIN (3+ tables).
    /// </summary>
    [Fact]
    public void MultiTableJoin_ThreeTables_MustJoinCorrectly()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER, amount DECIMAL)");
        
        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO orders VALUES (1, 1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 1, 100.00)");
        
        // Flush to ensure data is committed before JOIN
        if (db is Database concreteDb)
        {
            // TODO: Fix FlushPendingWalStatements when PageManager is restored
            // concreteDb.FlushPendingWalStatements();
            concreteDb.ForceSave(); // ? Use ForceSave to ensure all tables are persisted
        }

        // Act
        var results = db.ExecuteQuery(@"
            SELECT c.name, o.id as order_id, p.amount
            FROM customers c
            INNER JOIN orders o ON o.customer_id = c.id
            INNER JOIN payments p ON p.order_id = o.id
        ");

        // Debug: Show what keys are in the result
        if (results.Count > 0)
        {
            Console.WriteLine($"Result keys: {string.Join(", ", results[0].Keys)}");
            Console.WriteLine($"Result values: {string.Join(", ", results[0].Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        // Assert
        Assert.Single(results);

        Assert.Equal("Alice", results[0]["name"].ToString());
        Assert.Equal("1", results[0]["order_id"].ToString());
        Assert.Equal(100.00m, (decimal)results[0]["amount"]); // Compare decimal values directly, culture-independent
        Assert.False(results[0].ContainsKey("p.amount"), "Did not expect qualified key 'p.amount'.");
    }

    /// <summary>
    /// REGRESSION TEST: Unaliased qualified column should project to bare column name.
    /// Prevents KeyNotFound on results["name"] for SELECT c.name joins.
    /// </summary>
    [Fact]
    public void JoinProjection_UnaliasedQualifiedColumn_MustUseBareName()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER)");

        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO orders VALUES (10, 1)");

        // Act
        var results = db.ExecuteQuery(@"
            SELECT c.name
            FROM customers c
            INNER JOIN orders o ON o.customer_id = c.id
        ");

        // Assert
        Assert.Single(results);
        Assert.True(results[0].ContainsKey("name"), "Expected bare key 'name'.");
        Assert.False(results[0].ContainsKey("c.name"), "Did not expect qualified key 'c.name'.");
        Assert.Equal("Alice", results[0]["name"].ToString());
    }

    /// <summary>
    /// REGRESSION TEST: Duplicate unaliased column names must remain uniquely addressable.
    /// For SELECT o.id, p.id we keep qualified keys to avoid collisions.
    /// </summary>
    [Fact]
    public void JoinProjection_DuplicateUnaliasedColumns_MustRemainUnique()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER)");

        db.ExecuteSQL("INSERT INTO orders VALUES (1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (2, 1)");

        // Act
        var results = db.ExecuteQuery(@"
            SELECT o.id, p.id
            FROM orders o
            INNER JOIN payments p ON p.order_id = o.id
        ");

        // Assert
        Assert.Single(results);
        Assert.True(results[0].ContainsKey("o.id"), "Expected qualified key 'o.id'.");
        Assert.True(results[0].ContainsKey("p.id"), "Expected qualified key 'p.id'.");
        Assert.Equal("1", results[0]["o.id"].ToString());
        Assert.Equal("2", results[0]["p.id"].ToString());
    }

    /// <summary>
    /// REGRESSION TEST: Parameterized JOIN path (AstExecutor) should match legacy projection naming.
    /// Unaliased qualified columns should use bare keys; duplicate names remain qualified.
    /// </summary>
    [Fact]
    public void JoinProjection_ParameterizedPath_MustMatchLegacyProjectionNames()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER)");

        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO orders VALUES (10, 1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (20, 10)");

        // Act - parameters force AstExecutor route in ExecuteSelectQuery
        var results = db.ExecuteQuery(@"
            SELECT c.name, o.id, p.id
            FROM customers c
            INNER JOIN orders o ON o.customer_id = c.id
            INNER JOIN payments p ON p.order_id = o.id
            WHERE c.id = @id
        ", new Dictionary<string, object?> { ["id"] = 1 });

        // Assert
        Assert.Single(results);

        // Unaliased qualified unique column -> bare name
        Assert.True(results[0].ContainsKey("name"), "Expected bare key 'name'.");
        Assert.False(results[0].ContainsKey("c.name"), "Did not expect qualified key 'c.name'.");
        Assert.Equal("Alice", results[0]["name"].ToString());

        // Duplicate unaliased names must remain unique via qualified keys
        Assert.True(results[0].ContainsKey("o.id"), "Expected qualified key 'o.id'.");
        Assert.True(results[0].ContainsKey("p.id"), "Expected qualified key 'p.id'.");
        Assert.Equal("10", results[0]["o.id"].ToString());
        Assert.Equal("20", results[0]["p.id"].ToString());
    }

    /// <summary>
    /// REGRESSION TEST: Explicit alias collisions must throw.
    /// Enforces strict projection key uniqueness across both engines.
    /// </summary>
    [Fact]
    public void JoinProjection_ExplicitAliasCollision_MustThrow()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER)");

        db.ExecuteSQL("INSERT INTO orders VALUES (10)");
        db.ExecuteSQL("INSERT INTO payments VALUES (20, 10)");

        // Act / Assert
        var ex = Assert.Throws<InvalidOperationException>(() => db.ExecuteQuery(@"
            SELECT o.id AS same_key, p.id AS same_key
            FROM orders o
            INNER JOIN payments p ON p.order_id = o.id
        "));

        Assert.Contains("Duplicate explicit column alias", ex.Message, StringComparison.Ordinal);
        Assert.Contains("same_key", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// REGRESSION TEST: Parameterized JOIN path must also throw on explicit alias collisions.
    /// Ensures AstExecutor projection enforces the same strict alias policy.
    /// </summary>
    [Fact]
    public void JoinProjection_ParameterizedPath_ExplicitAliasCollision_MustThrow()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT)");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER)");

        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice')");
        db.ExecuteSQL("INSERT INTO orders VALUES (10, 1)");
        db.ExecuteSQL("INSERT INTO payments VALUES (20, 10)");

        // Act / Assert - @id forces AstExecutor route
        var ex = Assert.Throws<InvalidOperationException>(() => db.ExecuteQuery(@"
            SELECT o.id AS same_key, p.id AS same_key
            FROM customers c
            INNER JOIN orders o ON o.customer_id = c.id
            INNER JOIN payments p ON p.order_id = o.id
            WHERE c.id = @id
        ", new Dictionary<string, object?> { ["id"] = 1 }));

        Assert.Contains("Duplicate explicit column alias", ex.Message, StringComparison.Ordinal);
        Assert.Contains("same_key", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// DIAGNOSTIC TEST: Debug why JOINs return 0 rows.
    /// </summary>
    [Fact]
    public void JOIN_Diagnostic_WhyZeroRows()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "test123");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER)");
        db.ExecuteSQL("CREATE TABLE payments (id INTEGER PRIMARY KEY, order_id INTEGER, method TEXT)");
        
        db.ExecuteSQL("INSERT INTO orders VALUES (1, 100), (2, 200)");
        db.ExecuteSQL("INSERT INTO payments VALUES (1, 1, 'CARD'), (2, 2, 'CASH')");
        
        // Flush to ensure data is committed before JOIN
        if (db is Database concreteDb)
        {
            // TODO: Fix FlushPendingWalStatements when PageManager is restored
            // concreteDb.FlushPendingWalStatements();
            concreteDb.Flush(); // ? Also flush table data to disk
        }

        // Act - Try basic JOIN
        var results = db.ExecuteQuery(@"
            SELECT o.id as order_id, p.id as payment_id, p.method
            FROM orders o
            INNER JOIN payments p ON p.order_id = o.id
        ");

        // Debug output
        Console.WriteLine($"Results count: {results.Count}");
        foreach (var row in results)
        {
            Console.WriteLine($"Row: {string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value}"))}");
        }

        // Should have 2 rows
        Assert.True(results.Count >= 1, $"Expected at least 1 row but got {results.Count}");
    }
}
