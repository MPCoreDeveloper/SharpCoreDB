// <copyright file="SqlDialectTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using SharpCoreDB.Services;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for SQL dialect support and translation.
/// </summary>
public class SqlDialectTests
{
    [Fact]
    public void StandardDialect_SupportsAllJoinTypes()
    {
        // Arrange
        var dialect = new StandardSqlDialect();

        // Assert
        Assert.True(dialect.SupportsRightJoin);
        Assert.True(dialect.SupportsFullOuterJoin);
        Assert.True(dialect.SupportsSubqueriesInFrom);
        Assert.True(dialect.SupportsSubqueriesInWhere);
        Assert.True(dialect.SupportsLimitOffset);
        Assert.True(dialect.SupportsCTE);
        Assert.True(dialect.SupportsWindowFunctions);
    }

    [Fact]
    public void SqliteDialect_DoesNotSupportRightJoin()
    {
        // Arrange
        var dialect = new SqliteDialect();

        // Assert
        Assert.False(dialect.SupportsRightJoin);
        Assert.False(dialect.SupportsFullOuterJoin);
        Assert.True(dialect.SupportsReturning);
    }

    [Fact]
    public void SharpCoreDbDialect_SupportsAllFeatures()
    {
        // Arrange
        var dialect = new SharpCoreDbDialect();

        // Assert
        Assert.True(dialect.SupportsRightJoin);
        Assert.True(dialect.SupportsFullOuterJoin);
        Assert.True(dialect.SupportsSubqueriesInFrom);
        Assert.True(dialect.SupportsSubqueriesInWhere);
        Assert.True(dialect.SupportsLimitOffset);
        Assert.True(dialect.SupportsCTE);
        Assert.True(dialect.SupportsWindowFunctions);
    }

    [Fact]
    public void PostgreSqlDialect_TranslatesFunctions()
    {
        // Arrange
        var dialect = new PostgreSqlDialect();

        // Act & Assert
        Assert.Equal("LENGTH", dialect.TranslateFunction("LEN"));
        Assert.Equal("NOW()", dialect.TranslateFunction("GETDATE"));
    }

    [Fact]
    public void MySqlDialect_TranslatesFunctions()
    {
        // Arrange
        var dialect = new MySqlDialect();

        // Act & Assert
        Assert.Equal("CHAR_LENGTH", dialect.TranslateFunction("LEN"));
        Assert.Equal("NOW()", dialect.TranslateFunction("GETDATE"));
        Assert.Equal("UTC_TIMESTAMP()", dialect.TranslateFunction("GETUTCDATE"));
    }

    [Fact]
    public void SqlServerDialect_TranslatesFunctions()
    {
        // Arrange
        var dialect = new SqlServerDialect();

        // Act & Assert
        Assert.Equal("SUBSTRING", dialect.TranslateFunction("SUBSTR"));
        Assert.Equal("LEN", dialect.TranslateFunction("LENGTH"));
    }

    [Fact]
    public void SharpCoreDbDialect_TranslatesUlidFunctions()
    {
        // Arrange
        var dialect = new SharpCoreDbDialect();

        // Act & Assert
        Assert.Equal("ULID", dialect.TranslateFunction("ULID"));
        Assert.Equal("ULID_NEW", dialect.TranslateFunction("NEWULID"));
    }

    [Fact]
    public void StandardDialect_FormatsLimitClause()
    {
        // Arrange
        var dialect = new StandardSqlDialect();

        // Act & Assert
        Assert.Equal("LIMIT 10", dialect.FormatLimitClause(10, null));
        Assert.Equal("LIMIT 10 OFFSET 5", dialect.FormatLimitClause(10, 5));
        Assert.Equal(string.Empty, dialect.FormatLimitClause(null, null));
    }

    [Fact]
    public void MySqlDialect_FormatsLimitClauseWithOffset()
    {
        // Arrange
        var dialect = new MySqlDialect();

        // Act
        var result = dialect.FormatLimitClause(10, 5);

        // Assert
        Assert.Equal("LIMIT 5, 10", result);
    }

    [Fact]
    public void SqlServerDialect_FormatsLimitClause()
    {
        // Arrange
        var dialect = new SqlServerDialect();

        // Act & Assert
        Assert.Equal("TOP 10", dialect.FormatLimitClause(10, null));
        Assert.Contains("OFFSET 5 ROWS", dialect.FormatLimitClause(10, 5));
        Assert.Contains("FETCH NEXT 10 ROWS ONLY", dialect.FormatLimitClause(10, 5));
    }

    [Fact]
    public void SqlDialectFactory_CreatesSharpCoreDbDialect()
    {
        // Act
        var dialect = SqlDialectFactory.Create("sharpcoredb");

        // Assert
        Assert.IsType<SharpCoreDbDialect>(dialect);
        Assert.Equal("SharpCoreDB", dialect.Name);
    }

    [Fact]
    public void SqlDialectFactory_DefaultsToSharpCoreDb()
    {
        // Act
        var dialect = SqlDialectFactory.Create("unknown_dialect");

        // Assert
        Assert.IsType<SharpCoreDbDialect>(dialect);
    }

    [Fact]
    public void SqlDialectFactory_DefaultProperty_ReturnsSharpCoreDb()
    {
        // Act
        var dialect = SqlDialectFactory.Default;

        // Assert
        Assert.IsType<SharpCoreDbDialect>(dialect);
    }
}
