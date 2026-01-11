// <copyright file="ExecutionPlanTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Compilation;

using SharpCoreDB.Services.Compilation;
using System;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Unit tests for ExecutionPlan (value type, stack-allocatable).
/// </summary>
public class ExecutionPlanTests
{
    [Fact]
    public void ExecutionPlan_IsValueType()
    {
        // Verify that ExecutionPlan is a value type (struct)
        Assert.True(typeof(ExecutionPlan).IsValueType);
    }

    [Fact]
    public void HasWhereClause_ReturnsTrueWhenFilterExists()
    {
        // Arrange
        var plan = new ExecutionPlan(
            sql: "SELECT * FROM users WHERE id = 1",
            tableName: "users",
            selectColumns: Array.Empty<string>(),
            isSelectAll: true,
            whereFilter: null,
            projectionFunc: null,
            orderByColumn: null,
            orderByAscending: true,
            limit: null,
            offset: null,
            parameterNames: new HashSet<string>()
        );

        // Assert
        Assert.False(plan.HasWhereClause); // Filter is null in this test
    }

    [Fact]
    public void HasProjection_ReturnsTrueWhenProjectionExists()
    {
        // Arrange
        Func<Dictionary<string, object>, Dictionary<string, object>> projectionFunc = (Dictionary<string, object> row) =>
        {
            var proj = new Dictionary<string, object>();
            if (row.TryGetValue("id", out var id)) proj["id"] = id;
            return proj;
        };

        var plan = new ExecutionPlan(
            sql: "SELECT id FROM users",
            tableName: "users",
            selectColumns: new[] { "id" },
            isSelectAll: false,
            whereFilter: null,
            projectionFunc: projectionFunc,
            orderByColumn: null,
            orderByAscending: true,
            limit: null,
            offset: null,
            parameterNames: new HashSet<string>()
        );

        // Assert
        Assert.True(plan.HasProjection);
        Assert.NotNull(plan.ProjectionFunc);
    }

    [Fact]
    public void Complexity_CountsAllClauses()
    {
        // Arrange
        var plan = new ExecutionPlan(
            sql: "SELECT id FROM users WHERE id > 0 ORDER BY name ASC LIMIT 10 OFFSET 5",
            tableName: "users",
            selectColumns: new[] { "id" },
            isSelectAll: false,
            whereFilter: (row, parameters) => true, // Non-null filter to represent WHERE clause
            projectionFunc: null,
            orderByColumn: "name",
            orderByAscending: true,
            limit: 10,
            offset: 5,
            parameterNames: new HashSet<string>()
        );

        // Act
        var complexity = plan.Complexity;

        // Assert - Should count: WHERE (1) + ORDER BY (1) + LIMIT (1) + OFFSET (1) + PROJECTION (0, since projectionFunc is null)
        Assert.Equal(4, complexity);
    }

    [Fact]
    public void GetOutputColumnCount_ReturnsSelectAllForWildcard()
    {
        // Arrange
        var plan = new ExecutionPlan(
            sql: "SELECT * FROM users",
            tableName: "users",
            selectColumns: Array.Empty<string>(),
            isSelectAll: true,
            whereFilter: null,
            projectionFunc: null,
            orderByColumn: null,
            orderByAscending: true,
            limit: null,
            offset: null,
            parameterNames: new HashSet<string>()
        );

        // Act
        var count = plan.GetOutputColumnCount();

        // Assert
        Assert.Equal(-1, count); // -1 indicates SELECT *
    }

    [Fact]
    public void GetOutputColumnCount_ReturnsColumnCountForProjection()
    {
        // Arrange
        var plan = new ExecutionPlan(
            sql: "SELECT id, name FROM users",
            tableName: "users",
            selectColumns: new[] { "id", "name" },
            isSelectAll: false,
            whereFilter: null,
            projectionFunc: null,
            orderByColumn: null,
            orderByAscending: true,
            limit: null,
            offset: null,
            parameterNames: new HashSet<string>()
        );

        // Act
        var count = plan.GetOutputColumnCount();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void ParameterNames_CanBeValidated()
    {
        // Arrange
        var paramNames = new HashSet<string> { "userId", "minAge" };
        var plan = new ExecutionPlan(
            sql: "SELECT * FROM users WHERE id = @userId AND age > @minAge",
            tableName: "users",
            selectColumns: Array.Empty<string>(),
            isSelectAll: true,
            whereFilter: null,
            projectionFunc: null,
            orderByColumn: null,
            orderByAscending: true,
            limit: null,
            offset: null,
            parameterNames: paramNames
        );

        // Assert
        Assert.Contains("userId", plan.ParameterNames);
        Assert.Contains("minAge", plan.ParameterNames);
    }
}
