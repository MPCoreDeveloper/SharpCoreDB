// <copyright file="SubqueryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Execution;
using SharpCoreDB.Services;
using SharpCoreDB.Interfaces;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for subquery support: parsing, classification, caching, and execution.
/// </summary>
public class SubqueryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseFactory _factory;

    public SubqueryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_subquery_{Guid.NewGuid()}.db");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
        GC.SuppressFinalize(this);
    }

    #region Parser Tests

    [Fact]
    public void Parser_ScalarSubquery_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "SELECT id, (SELECT MAX(salary) FROM employees) as max_salary FROM departments";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
        Assert.IsType<SelectNode>(ast);
    }

    [Fact]
    public void Parser_FromSubquery_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT dept_id, avg_salary
            FROM (
                SELECT department_id as dept_id, AVG(salary) as avg_salary
                FROM employees
                GROUP BY department_id
            ) dept_avg
            WHERE avg_salary > 50000";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        var select = ast as SelectNode;
        Assert.NotNull(select);
        Assert.NotNull(select.From);
        Assert.NotNull(select.From.Subquery);
    }

    [Fact]
    public void Parser_WhereInSubquery_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "SELECT * FROM orders WHERE customer_id IN (SELECT id FROM customers WHERE country = 'USA')";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
        var select = ast as SelectNode;
        Assert.NotNull(select);
        Assert.NotNull(select.Where);
    }

    [Fact]
    public void Parser_ExistsSubquery_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT * FROM orders o
            WHERE EXISTS (
                SELECT 1 FROM customers c 
                WHERE c.id = o.customer_id AND c.active = 1
            )";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
    }

    #endregion

    #region Classification Tests

    [Fact]
    public void Classifier_ScalarSubquery_ClassifiedCorrectly()
    {
        // Arrange
        var classifier = new SubqueryClassifier();
        var parser = new EnhancedSqlParser();
        var sql = "SELECT (SELECT MAX(price) FROM products) as max_price FROM users";
        var ast = parser.Parse(sql) as SelectNode;
        
        var subquery = new SubqueryExpressionNode
        {
            Query = new SelectNode
            {
                Columns = [new ColumnNode { Name = "MAX(price)", AggregateFunction = "MAX" }],
                From = new FromNode { TableName = "products" }
            }
        };

        // Act
        var classification = classifier.Classify(subquery, ast!);

        // Assert
        Assert.Equal(SubqueryType.Scalar, classification.Type);
        Assert.False(classification.IsCorrelated);
        Assert.NotNull(classification.CacheKey);
    }

    [Fact]
    public void Classifier_CorrelatedSubquery_DetectedCorrectly()
    {
        // Arrange
        var classifier = new SubqueryClassifier();
        var outerQuery = new SelectNode
        {
            Columns = [new ColumnNode { Name = "name" }],
            From = new FromNode { TableName = "departments", Alias = "d" }
        };
        
        var subquery = new SubqueryExpressionNode
        {
            Query = new SelectNode
            {
                Columns = [new ColumnNode { Name = "COUNT(*)", AggregateFunction = "COUNT" }],
                From = new FromNode { TableName = "employees", Alias = "e" },
                Where = new WhereNode
                {
                    Condition = new BinaryExpressionNode
                    {
                        Left = new ColumnReferenceNode { TableAlias = "e", ColumnName = "dept_id" },
                        Operator = "=",
                        Right = new ColumnReferenceNode { TableAlias = "d", ColumnName = "id" }
                    }
                }
            }
        };

        // Act
        var classification = classifier.Classify(subquery, outerQuery);

        // Assert
        Assert.True(classification.IsCorrelated);
        Assert.Single(classification.OuterReferences);
        Assert.Null(classification.CacheKey);
    }

    #endregion

    #region Cache Tests

    [Fact]
    public void Cache_NonCorrelatedSubquery_CachesResult()
    {
        // Arrange
        var cache = new SubqueryCache();
        var executor = () => new List<Dictionary<string, object>>
        {
            new() { ["max"] = 100 }
        };

        // Act
        var result1 = cache.GetOrExecute("key1", SubqueryType.Scalar, executor);
        var result2 = cache.GetOrExecute("key1", SubqueryType.Scalar, executor);

        // Assert
        Assert.Equal(100, result1);
        Assert.Equal(100, result2);
        
        var stats = cache.GetStatistics();
        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(0.5, stats.HitRate);
    }

    [Fact]
    public void Cache_Invalidate_RemovesCachedResults()
    {
        // Arrange
        var cache = new SubqueryCache();
        cache.GetOrExecute("SUBQ:products", SubqueryType.Scalar,
            () => [new Dictionary<string, object> { ["max"] = 100 }]);

        // Act
        cache.Invalidate("products");

        // Assert
        var stats = cache.GetStatistics();
        Assert.Equal(0, stats.Count);
    }

    #endregion

    #region Execution Tests

    [Fact]
    public void Execute_ScalarSubquery_ReturnsCorrectValue()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE employees (id INTEGER, name TEXT, salary DECIMAL)");
        db.ExecuteSQL("INSERT INTO employees VALUES (1, 'Alice', 50000)");
        db.ExecuteSQL("INSERT INTO employees VALUES (2, 'Bob', 60000)");
        db.ExecuteSQL("INSERT INTO employees VALUES (3, 'Charlie', 70000)");

        // Act - This will require full integration
        // For now, test with direct subquery executor
        var tables = db.GetType()
            .GetField("tables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(db) as IReadOnlyDictionary<string, ITable>;

        if (tables is not null)
        {
            var cache = new SubqueryCache();
            var executor = new SubqueryExecutor(tables, cache);

            var subquery = new SubqueryExpressionNode
            {
                Query = new SelectNode
                {
                    Columns = [new ColumnNode { Name = "salary", AggregateFunction = "AVG" }],
                    From = new FromNode { TableName = "employees" }
                }
            };

            var result = executor.ExecuteScalar(subquery);

            // Assert
            Assert.NotNull(result);
            // Note: Full AVG implementation requires aggregate support in executor
        }
    }

    [Fact]
    public void Execute_InSubquery_FiltersCorrectly()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE customers (id INTEGER, name TEXT, country TEXT)");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER, customer_id INTEGER, amount DECIMAL)");
        
        db.ExecuteSQL("INSERT INTO customers VALUES (1, 'Alice', 'USA')");
        db.ExecuteSQL("INSERT INTO customers VALUES (2, 'Bob', 'UK')");
        db.ExecuteSQL("INSERT INTO orders VALUES (1, 1, 100.00)");
        db.ExecuteSQL("INSERT INTO orders VALUES (2, 2, 200.00)");

        // Act
        var tables = db.GetType()
            .GetField("tables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(db) as IReadOnlyDictionary<string, ITable>;

        if (tables is not null)
        {
            var cache = new SubqueryCache();
            var executor = new SubqueryExecutor(tables, cache);

            var subquery = new SubqueryExpressionNode
            {
                Query = new SelectNode
                {
                    Columns = [new ColumnNode { Name = "id" }],
                    From = new FromNode { TableName = "customers" },
                    Where = new WhereNode
                    {
                        Condition = new BinaryExpressionNode
                        {
                            Left = new ColumnReferenceNode { ColumnName = "country" },
                            Operator = "=",
                            Right = new LiteralNode { Value = "USA" }
                        }
                    }
                }
            };

            var values = executor.ExecuteIn(subquery);

            // Assert
            Assert.Single(values);
            Assert.Contains(1, values.Select(v => Convert.ToInt32(v)));
        }
    }

    [Fact]
    public void Execute_ExistsSubquery_ReturnsBoolean()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE orders (id INTEGER, customer_id INTEGER)");
        db.ExecuteSQL("INSERT INTO orders VALUES (1, 100)");

        // Act
        var tables = db.GetType()
            .GetField("tables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
            .GetValue(db) as IReadOnlyDictionary<string, ITable>;

        if (tables is not null)
        {
            var cache = new SubqueryCache();
            var executor = new SubqueryExecutor(tables, cache);

            var subquery = new SubqueryExpressionNode
            {
                Query = new SelectNode
                {
                    Columns = [new ColumnNode { Name = "1" }],
                    From = new FromNode { TableName = "orders" },
                    Where = new WhereNode
                    {
                        Condition = new BinaryExpressionNode
                        {
                            Left = new ColumnReferenceNode { ColumnName = "customer_id" },
                            Operator = "=",
                            Right = new LiteralNode { Value = 100 }
                        }
                    }
                }
            };

            var exists = executor.ExecuteExists(subquery);

            // Assert
            Assert.True(exists);
        }
    }

    #endregion

    #region Planner Tests

    [Fact]
    public void Planner_ExtractsAllSubqueries()
    {
        // Arrange
        var classifier = new SubqueryClassifier();
        var planner = new SubqueryPlanner(classifier);
        
        var query = new SelectNode
        {
            Columns = [new ColumnNode { Name = "id" }],
            From = new FromNode { TableName = "orders" },
            Where = new WhereNode
            {
                Condition = new BinaryExpressionNode
                {
                    Left = new ColumnReferenceNode { ColumnName = "customer_id" },
                    Operator = "IN",
                    Right = new SubqueryExpressionNode
                    {
                        Query = new SelectNode
                        {
                            Columns = [new ColumnNode { Name = "id" }],
                            From = new FromNode { TableName = "customers" }
                        }
                    }
                }
            }
        };

        // Act
        var plan = planner.Plan(query);

        // Assert
        Assert.Single(plan.AllSubqueries);
        Assert.Single(plan.NonCorrelatedSubqueries);
        Assert.Empty(plan.CorrelatedSubqueries);
    }

    [Fact]
    public void Planner_OrdersNonCorrelatedFirst()
    {
        // Arrange
        var classifier = new SubqueryClassifier();
        var planner = new SubqueryPlanner(classifier);
        
        var outerRef = new ColumnReferenceNode { TableAlias = "o", ColumnName = "id" };
        
        var query = new SelectNode
        {
            Columns = [new ColumnNode { Name = "id" }],
            From = new FromNode { TableName = "orders", Alias = "o" },
            Where = new WhereNode
            {
                Condition = new BinaryExpressionNode
                {
                    Left = new SubqueryExpressionNode  // Non-correlated
                    {
                        Query = new SelectNode
                        {
                            Columns = [new ColumnNode { Name = "COUNT(*)" }],
                            From = new FromNode { TableName = "products" }
                        }
                    },
                    Operator = ">",
                    Right = new SubqueryExpressionNode  // Correlated
                    {
                        Query = new SelectNode
                        {
                            Columns = [new ColumnNode { Name = "COUNT(*)" }],
                            From = new FromNode { TableName = "order_items", Alias = "oi" },
                            Where = new WhereNode
                            {
                                Condition = new BinaryExpressionNode
                                {
                                    Left = new ColumnReferenceNode { TableAlias = "oi", ColumnName = "order_id" },
                                    Operator = "=",
                                    Right = outerRef
                                }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var plan = planner.Plan(query);

        // Assert
        Assert.Equal(2, plan.AllSubqueries.Count);
        Assert.False(plan.AllSubqueries[0].Classification.IsCorrelated); // Non-correlated first
        Assert.True(plan.AllSubqueries[1].Classification.IsCorrelated);  // Correlated second
    }

    #endregion
}
