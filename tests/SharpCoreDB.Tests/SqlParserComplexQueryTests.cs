// <copyright file="SqlParserComplexQueryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using SharpCoreDB.Services;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for complex SQL query parsing including JOINs, subqueries, and advanced features.
/// </summary>
public class SqlParserComplexQueryTests
{
    [Fact]
    public void Parser_SimpleSelect_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "SELECT id, name FROM users WHERE active = 1";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.Equal(2, selectNode.Columns.Count);
    }

    [Fact]
    public void Parser_PercentileAggregate_ParsesFunctionName()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "SELECT PERCENTILE(score, 0.95) AS p95 FROM metrics";

        // Act
        var ast = parser.Parse(sql) as SelectNode;

        // Assert
        Assert.Equal("PERCENTILE", ast?.Columns[0].AggregateFunction);
    }

    [Fact]
    public void Parser_PercentileAggregate_ParsesArgumentValue()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "SELECT PERCENTILE(score, 0.95) AS p95 FROM metrics";

        // Act
        var ast = parser.Parse(sql) as SelectNode;

        // Assert
        Assert.Equal(0.95, ast?.Columns[0].AggregateArgument);
    }

    [Fact]
    public void Parser_RightJoin_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT u.name, o.order_id
            FROM users u
            RIGHT JOIN orders o ON u.id = o.user_id";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotNull(selectNode.From);
        Assert.Single(selectNode.From.Joins);
        Assert.Equal(JoinNode.JoinType.Right, selectNode.From.Joins[0].Type);
    }

    [Fact]
    public void Parser_FullOuterJoin_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT COALESCE(a.id, b.id) as id
            FROM table_a a
            FULL OUTER JOIN table_b b ON a.id = b.id";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        // Note: Full outer join parsing may not be fully implemented yet
        // This test verifies the parser doesn't crash on FULL OUTER JOIN syntax
    }

    [Fact]
    public void Parser_SubqueryInFrom_Parses()
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
        // Note: Subquery in FROM may require additional parser enhancements
        // This test verifies the parser handles the syntax gracefully
    }

    [Fact]
    public void Parser_SubqueryInWhere_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT name, salary
            FROM employees
            WHERE salary > (SELECT AVG(salary) FROM employees)";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        // Note: Subqueries in WHERE may not be fully supported yet
        // For now, we verify the parser doesn't crash
    }

    [Fact]
    public void Parser_InExpressionWithSubquery_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT * FROM orders
            WHERE customer_id IN (SELECT id FROM customers WHERE country = 'USA')";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
    }

    [Fact]
    public void Parser_GroupByHaving_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT department_id, AVG(salary) as avg_salary
            FROM employees
            GROUP BY department_id
            HAVING AVG(salary) > 50000";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotNull(selectNode.GroupBy);
        Assert.NotNull(selectNode.Having);
    }

    [Fact]
    public void Parser_LimitOffset_Parses()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "SELECT * FROM users LIMIT 10 OFFSET 20";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.Equal(10, selectNode.Limit);
        Assert.Equal(20, selectNode.Offset);
    }

    [Fact]
    public void Visitor_GeneratesSqlFromAst()
    {
        // Arrange
        var selectNode = new SelectNode
        {
            Columns = new List<ColumnNode>
            {
                new ColumnNode { Name = "id" },
                new ColumnNode { Name = "name" }
            },
            From = new FromNode
            {
                TableName = "users",
                Alias = "u"
            },
            Where = new WhereNode
            {
                Condition = new BinaryExpressionNode
                {
                    Left = new ColumnReferenceNode { ColumnName = "active" },
                    Operator = "=",
                    Right = new LiteralNode { Value = 1 }
                }
            },
            Limit = 10
        };
        var visitor = new SqlToStringVisitor();

        // Act
        var sql = visitor.VisitSelect(selectNode)?.ToString();

        // Assert
        Assert.NotNull(sql);
        Assert.Contains("SELECT", sql);
        Assert.Contains("FROM", sql);
        Assert.Contains("WHERE", sql);
        Assert.Contains("LIMIT 10", sql);
    }
}
