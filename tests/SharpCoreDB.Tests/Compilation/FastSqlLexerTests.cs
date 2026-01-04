// <copyright file="FastSqlLexerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Compilation;

using SharpCoreDB.Services.Compilation;
using System;
using System.Linq;
using Xunit;

/// <summary>
/// Unit tests for FastSqlLexer (zero-allocation tokenizer).
/// </summary>
public class FastSqlLexerTests
{
    [Fact]
    public void Tokenize_SimpleSelect_ReturnsCorrectTokens()
    {
        // Arrange
        var sql = "SELECT id, name FROM users";
        var lexer = new FastSqlLexer(sql);

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        Assert.NotNull(tokens);
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "SELECT");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Identifier && t.GetString(sql.AsSpan()) == "id");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Identifier && t.GetString(sql.AsSpan()) == "name");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "FROM");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Identifier && t.GetString(sql.AsSpan()) == "users");
    }

    [Fact]
    public void Tokenize_SelectWithWhere_ParsesWhereClause()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE age > 18";
        var lexer = new FastSqlLexer(sql);

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "WHERE");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Identifier && t.GetString(sql.AsSpan()) == "age");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Operator && t.GetString(sql.AsSpan()) == ">");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Number && t.GetString(sql.AsSpan()) == "18");
    }

    [Fact]
    public void Tokenize_SelectWithStringLiteral_PreservesString()
    {
        // Arrange
        var sql = "SELECT * FROM users WHERE name = 'John'";
        var lexer = new FastSqlLexer(sql);

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        var stringToken = tokens.FirstOrDefault(t => t.Type == FastSqlLexer.TokenType.String);
        Assert.NotNull(stringToken);
        Assert.Equal("'John'", stringToken.GetString(sql.AsSpan()));
    }

    [Fact]
    public void Tokenize_SelectWithOrderBy_ParsesOrderClause()
    {
        // Arrange
        var sql = "SELECT * FROM users ORDER BY name ASC";
        var lexer = new FastSqlLexer(sql);

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "ORDER");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "BY");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "ASC");
    }

    [Fact]
    public void Tokenize_SelectWithLimit_ParsesLimitClause()
    {
        // Arrange
        var sql = "SELECT * FROM users LIMIT 10";
        var lexer = new FastSqlLexer(sql);

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Keyword && t.GetString(sql.AsSpan()) == "LIMIT");
        Assert.Contains(tokens, t => t.Type == FastSqlLexer.TokenType.Number && t.GetString(sql.AsSpan()) == "10");
    }

    [Fact]
    public void Tokenize_Operators_ParsesAllOperatorTypes()
    {
        // Arrange
        var sql = "WHERE a = b AND c != d OR e < f AND g > h";
        var lexer = new FastSqlLexer(sql);

        // Act
        var tokens = lexer.Tokenize();

        // Assert
        var operators = tokens.Where(t => t.Type == FastSqlLexer.TokenType.Operator).ToList();
        Assert.Contains(operators, t => t.GetString(sql.AsSpan()) == "=");
        Assert.Contains(operators, t => t.GetString(sql.AsSpan()) == "!=");
        Assert.Contains(operators, t => t.GetString(sql.AsSpan()) == "<");
        Assert.Contains(operators, t => t.GetString(sql.AsSpan()) == ">");
    }

    [Fact]
    public void GetSpan_ReturnsZeroCopyView()
    {
        // Arrange
        var sql = "SELECT id FROM users";
        var lexer = new FastSqlLexer(sql);
        var tokens = lexer.Tokenize();

        // Act
        var selectToken = tokens[0];
        var span = selectToken.GetSpan(sql.AsSpan());

        // Assert
        Assert.Equal("SELECT", new string(span));
        // Verify span properties
        Assert.Equal(6, span.Length);
    }
}
