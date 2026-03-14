// <copyright file="EngineLimitationFixTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Linq;
using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// Tests verifying fixes for known engine limitations:
/// 1. IS NULL / IS NOT NULL WHERE filter
/// 2. COALESCE() in EnhancedSqlParser
/// 3. EnhancedSqlParser error recovery (trailing tokens)
/// 4. LINQ enum Convert expressions
/// </summary>
public sealed class EngineLimitationFixTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly Database _db;

    public EngineLimitationFixTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_engine_limits_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDbPath);

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();

        _db = new Database(
            sp,
            _testDbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try { await _db.DisposeAsync().ConfigureAwait(false); } catch { }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            if (Directory.Exists(_testDbPath))
                Directory.Delete(_testDbPath, true);
        }
        catch { }
    }

    #region IS NULL / IS NOT NULL

    [Fact]
    public void WhereIsNull_WithNullValues_ReturnsCorrectRows()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE null_test (id INTEGER, name TEXT)");
        _db.ExecuteBatchSQL([
            "INSERT INTO null_test VALUES (1, 'Alice')",
            "INSERT INTO null_test VALUES (2, NULL)",
            "INSERT INTO null_test VALUES (3, 'Charlie')",
            "INSERT INTO null_test VALUES (4, NULL)"
        ]);

        // Act
        var results = _db.ExecuteQuery("SELECT * FROM null_test WHERE name IS NULL");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r["name"] is null or DBNull,
            $"Expected null/DBNull but got: {r["name"]}"));
    }

    [Fact]
    public void WhereIsNotNull_WithNullValues_ReturnsCorrectRows()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE notnull_test (id INTEGER, name TEXT)");
        _db.ExecuteBatchSQL([
            "INSERT INTO notnull_test VALUES (1, 'Alice')",
            "INSERT INTO notnull_test VALUES (2, NULL)",
            "INSERT INTO notnull_test VALUES (3, 'Charlie')",
            "INSERT INTO notnull_test VALUES (4, NULL)"
        ]);

        // Act
        var results = _db.ExecuteQuery("SELECT * FROM notnull_test WHERE name IS NOT NULL");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.NotNull(r["name"]));
    }

    [Fact]
    public void WhereIsNull_WhenAllValuesPresent_ReturnsEmpty()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE allpresent (id INTEGER, name TEXT)");
        _db.ExecuteBatchSQL([
            "INSERT INTO allpresent VALUES (1, 'Alice')",
            "INSERT INTO allpresent VALUES (2, 'Bob')"
        ]);

        // Act
        var results = _db.ExecuteQuery("SELECT * FROM allpresent WHERE name IS NULL");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void WhereIsNotNull_WhenAllNull_ReturnsEmpty()
    {
        // Arrange
        _db.ExecuteSQL("CREATE TABLE allnull (id INTEGER, name TEXT)");
        _db.ExecuteBatchSQL([
            "INSERT INTO allnull VALUES (1, NULL)",
            "INSERT INTO allnull VALUES (2, NULL)"
        ]);

        // Act
        var results = _db.ExecuteQuery("SELECT * FROM allnull WHERE name IS NOT NULL");

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region COALESCE in EnhancedSqlParser

    [Fact]
    public void EnhancedParser_Coalesce_ParsesCorrectly()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT COALESCE(name, 'unknown') FROM users");

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors, string.Join("; ", parser.Errors));
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotEmpty(selectNode.Columns);
        Assert.NotNull(selectNode.Columns[0].Expression);
    }

    [Fact]
    public void EnhancedParser_CoalesceWithAlias_ParsesCorrectly()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT COALESCE(col1, col2, 0) AS result FROM data");

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors, string.Join("; ", parser.Errors));
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.Equal("result", selectNode.Columns[0].Alias);
    }

    [Fact]
    public void EnhancedParser_IIF_ParsesCorrectly()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT IIF(status = 1, 'active', 'inactive') FROM users");

        // Assert
        Assert.NotNull(ast);
        // IIF should be parsed as a scalar function
        var selectNode = ast as SelectNode;
        Assert.NotNull(selectNode);
        Assert.NotNull(selectNode.Columns[0].Expression);
    }

    #endregion

    #region EnhancedSqlParser Error Recovery

    [Fact]
    public void EnhancedParser_TrailingJunk_SetsHasErrors()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT * FROM users GARBAGE TOKENS");

        // Assert
        Assert.NotNull(ast);
        Assert.True(parser.HasErrors);
        Assert.Contains(parser.Errors, e => e.Contains("trailing", StringComparison.OrdinalIgnoreCase)
                                           || e.Contains("Unexpected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnhancedParser_ValidSql_NoTrailingErrors()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT id, name FROM users WHERE id = 1");

        // Assert
        Assert.NotNull(ast);
        Assert.False(parser.HasErrors, string.Join("; ", parser.Errors));
    }

    #endregion

    #region LINQ Enum Convert

    private enum TestStatus
    {
        Inactive = 0,
        Active = 1,
        Suspended = 2
    }

    [Fact]
    public void LinqTranslator_EnumComparison_DoesNotThrow()
    {
        // Arrange
        var translator = new GenericLinqToSqlTranslator<TestItem>();

        // Build an expression that includes Convert (how LINQ represents enum casts)
        // x => x.Status == (int)TestStatus.Active  generates  Convert(TestStatus.Active, Int32)
        Expression<Func<TestItem, bool>> predicate = x => x.Status == (int)TestStatus.Active;
        var body = predicate.Body;

        // Act — this would throw NotSupportedException before the fix
        var (sql, parameters) = translator.Translate(predicate);

        // Assert
        Assert.Contains("Status", sql);
    }

    private sealed class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Status { get; set; }
    }

    #endregion
}
