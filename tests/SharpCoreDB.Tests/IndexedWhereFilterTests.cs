// <copyright file="IndexedWhereFilterTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// Tests for indexed WHERE clause compilation (Phase 2.5).
/// </summary>
public class IndexedWhereFilterTests
{
    [Fact]
    public void Compile_WithWhereColumnNotInSelect_IncludesWhereColumnInIndices()
    {
        // Arrange
        const string sql = "SELECT name FROM products WHERE stock > 10";

        // Act
        var plan = QueryCompiler.Compile(sql);

        // Assert
        Assert.True(plan?.ColumnIndices.ContainsKey("stock") == true);
    }

    [Fact]
    public void IndexedWhereFilter_WhenConditionMet_ReturnsTrue()
    {
        // Arrange
        const string sql = "SELECT name FROM products WHERE stock > 10";
        var plan = QueryCompiler.Compile(sql) ?? throw new InvalidOperationException("Compilation failed.");
        var stockIndex = plan.ColumnIndices["stock"];
        var row = new IndexedRowData(plan.ColumnIndices);
        row[stockIndex] = 15;

        // Act
        var result = plan.WhereFilterIndexed?.Invoke(row);

        // Assert
        Assert.True(result == true);
    }

    [Fact]
    public void IndexedWhereFilter_WhenConditionNotMet_ReturnsFalse()
    {
        // Arrange
        const string sql = "SELECT name FROM products WHERE stock > 10";
        var plan = QueryCompiler.Compile(sql) ?? throw new InvalidOperationException("Compilation failed.");
        var stockIndex = plan.ColumnIndices["stock"];
        var row = new IndexedRowData(plan.ColumnIndices);
        row[stockIndex] = 5;

        // Act
        var result = plan.WhereFilterIndexed?.Invoke(row);

        // Assert
        Assert.True(result == false);
    }
}
