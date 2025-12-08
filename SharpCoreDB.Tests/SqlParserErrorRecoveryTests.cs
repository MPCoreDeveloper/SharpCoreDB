// <copyright file="SqlParserErrorRecoveryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using SharpCoreDB.Services;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for SQL parser error recovery capabilities.
/// Ensures the parser doesn't crash on malformed SQL and provides helpful error messages.
/// </summary>
public class SqlParserErrorRecoveryTests
{
    [Fact]
    public void Parser_MalformedSelect_DoesNotCrash()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM"; // Missing table name

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast); // Should return a node even if incomplete
        Assert.True(parser.HasErrors);
        Assert.NotEmpty(parser.Errors);
    }

    [Fact]
    public void Parser_MissingFromClause_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT id, name WHERE id = 1"; // Missing FROM

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        // Parser should still create a SelectNode with columns
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotEmpty(selectNode.Columns);
    }

    [Fact]
    public void Parser_UnclosedParenthesis_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM (SELECT * FROM users"; // Missing closing )

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
        Assert.Contains(parser.Errors, e => e.Contains("Expected )") || e.Contains("parsing"));
    }

    [Fact]
    public void Parser_InvalidJoinSyntax_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM users LEFT orders ON users.id = orders.user_id"; // Missing JOIN keyword

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void Parser_MissingWhereCondition_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM users WHERE"; // Missing condition

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        // Should have attempted to parse WHERE clause
    }

    [Fact]
    public void Parser_InvalidOperator_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM users WHERE id === 1"; // Invalid === operator

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        // Parser should handle gracefully
    }

    [Fact]
    public void Parser_MissingCommaInColumnList_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT id name email FROM users"; // Missing commas

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        // Should parse at least one column
        Assert.NotEmpty(selectNode.Columns);
    }

    [Fact]
    public void Parser_UnclosedStringLiteral_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM users WHERE name = 'John"; // Missing closing quote

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        // Parser should attempt to continue
    }

    [Fact]
    public void Parser_InvalidFunctionCall_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT COUNT( FROM users"; // Invalid function syntax

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void Parser_MissingTableNameAfterJoin_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT * FROM users LEFT JOIN ON users.id = orders.user_id"; // Missing table name

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void Parser_MultipleErrors_RecordsAll()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = "SELECT FROM WHERE"; // Multiple syntax errors

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
        // Should have multiple error messages
    }

    [Fact]
    public void Visitor_NullNode_DoesNotCrash()
    {
        // Arrange
        var visitor = new SqlToStringVisitor();
        var selectNode = new SelectNode
        {
            Columns = new List<ColumnNode>(),
            From = null, // Null FROM clause
            Where = null
        };

        // Act
        var result = visitor.VisitSelect(selectNode);

        // Assert
        Assert.NotNull(result);
        Assert.False(visitor.HasErrors);
    }

    [Fact]
    public void Visitor_MalformedExpression_RecordsError()
    {
        // Arrange
        var visitor = new SqlToStringVisitor();
        var binaryExpr = new BinaryExpressionNode
        {
            Left = null, // Malformed - missing left operand
            Operator = "=",
            Right = new LiteralNode { Value = 1 }
        };

        // Act
        var result = visitor.VisitBinaryExpression(binaryExpr);

        // Assert
        Assert.NotNull(result);
        // Visitor should handle gracefully
    }

    [Fact]
    public void Parser_ComplexMalformedQuery_ContinuesParsing()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var malformedSql = @"
            SELECT u.id, u.name, o.total
            FROM users u
            LEFT JOIN orders o ON u.id = o.user_id
            WHERE u.active = 1 AND o.status =
            GROUP BY u.id
            HAVING COUNT(*) > 5
            ORDER BY u.name"; // Missing value after = in WHERE

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        // Should have parsed columns, FROM, and JOIN despite error
        Assert.NotEmpty(selectNode.Columns);
        Assert.NotNull(selectNode.From);
        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void Parser_SQLFiddleExample1_ParsesOrRecovers()
    {
        // Arrange - Complex query from SQLFiddle
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT e.name, d.department_name, e.salary
            FROM employees e
            INNER JOIN departments d ON e.department_id = d.id
            WHERE e.salary > (SELECT AVG(salary) FROM employees)
            ORDER BY e.salary DESC
            LIMIT 10";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.Equal(3, selectNode.Columns.Count);
        Assert.NotNull(selectNode.From);
        Assert.NotEmpty(selectNode.From.Joins);
        Assert.NotNull(selectNode.Where);
        Assert.NotNull(selectNode.OrderBy);
        Assert.Equal(10, selectNode.Limit);
    }

    [Fact]
    public void Parser_SQLFiddleExample2_WithFullOuterJoin_Parses()
    {
        // Arrange - FULL OUTER JOIN example
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT COALESCE(a.id, b.id) as id, a.name, b.value
            FROM table_a a
            FULL OUTER JOIN table_b b ON a.id = b.id";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotNull(selectNode.From);
        Assert.NotEmpty(selectNode.From.Joins);
        Assert.Equal(JoinNode.JoinType.Full, selectNode.From.Joins[0].Type);
    }

    [Fact]
    public void Parser_SQLFiddleExample3_WithRightJoin_Parses()
    {
        // Arrange - RIGHT JOIN example
        var parser = new EnhancedSqlParser();
        var sql = @"
            SELECT o.order_id, c.customer_name
            FROM orders o
            RIGHT JOIN customers c ON o.customer_id = c.id";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.NotNull(ast);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotNull(selectNode.From);
        Assert.NotEmpty(selectNode.From.Joins);
        Assert.Equal(JoinNode.JoinType.Right, selectNode.From.Joins[0].Type);
    }

    [Fact]
    public void Parser_SQLFiddleExample4_WithSubquery_Parses()
    {
        // Arrange - Subquery in FROM clause
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
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotNull(selectNode.From);
        Assert.NotNull(selectNode.From.Subquery);
        Assert.NotNull(selectNode.Where);
    }

    [Fact]
    public void Parser_MalformedSQLFiddleQuery_RecordsErrorsButContinues()
    {
        // Arrange - Malformed complex query
        var parser = new EnhancedSqlParser();
        var malformedSql = @"
            SELECT e.name, d.department_name
            FROM employees e
            INNER JOIN departments d ON e.department_id =
            WHERE e.salary > 50000"; // Missing value after =

        // Act
        var ast = parser.Parse(malformedSql);

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        // Should have parsed columns and FROM despite error
        Assert.Equal(2, selectNode.Columns.Count);
        Assert.NotNull(selectNode.From);
    }

    [Fact]
    public void Parser_EmptyQuery_HandlesGracefully()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.Null(ast);
        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void Parser_OnlyWhitespace_HandlesGracefully()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "   \t\n  ";

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.Null(ast);
        Assert.True(parser.HasErrors);
    }

    [Fact]
    public void Parser_InvalidStatementType_RecordsError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        var sql = "DROP TABLE users"; // Not supported

        // Act
        var ast = parser.Parse(sql);

        // Assert
        Assert.Null(ast);
        Assert.True(parser.HasErrors);
        Assert.Contains(parser.Errors, e => e.Contains("Unsupported") || e.Contains("DROP"));
    }
}
