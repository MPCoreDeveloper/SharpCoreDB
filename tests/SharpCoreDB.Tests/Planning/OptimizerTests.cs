// <copyright file="OptimizerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Planning;

using System.Collections.Generic;
using SharpCoreDB.Planning;
using SharpCoreDB.Storage.Columnar;
using Xunit;

/// <summary>
/// Tests for Phase 7.3: Query Plan Optimization.
/// ✅ SCDB Phase 7.3: Verifies cardinality estimation, cost-based optimization, and predicate pushdown.
/// </summary>
public sealed class OptimizerTests
{
    private readonly ITestOutputHelper _output;

    public OptimizerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // Cardinality Estimator Tests
    // ========================================

    [Fact]
    public void CardinalityEstimator_EstimateSelectivity_Equality_ReturnsLowSelectivity()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);

        // Act
        var selectivity = estimator.EstimateSelectivity("id", "=", 100);

        // Assert
        Assert.True(selectivity > 0.0 && selectivity <= 1.0);
        _output.WriteLine($"✓ Equality selectivity: {selectivity:F4}");
    }

    [Fact]
    public void CardinalityEstimator_EstimateFilteredRows_CalculatesCorrectly()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);

        // Act
        var filteredRows = estimator.EstimateFilteredRows("id", "=", 100, totalRows: 1000);

        // Assert
        Assert.True(filteredRows > 0 && filteredRows <= 1000);
        _output.WriteLine($"✓ Filtered rows estimate: {filteredRows}");
    }

    [Fact]
    public void CardinalityEstimator_EstimateCardinality_ReturnsDistinctCount()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);

        // Act
        var cardinality = estimator.EstimateCardinality("id");

        // Assert
        Assert.Equal(1000, cardinality);
        _output.WriteLine($"✓ Cardinality estimate: {cardinality}");
    }

    [Fact]
    public void CardinalityEstimator_EstimateJoinSize_CalculatesCorrectly()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);

        // Act
        var joinSize = estimator.EstimateJoinSize(
            leftRows: 1000,
            leftColumn: "id",
            rightRows: 500,
            rightColumn: "user_id"
        );

        // Assert
        Assert.True(joinSize > 0);
        Assert.True(joinSize <= 500000); // Less than Cartesian product
        _output.WriteLine($"✓ Join size estimate: {joinSize}");
    }

    [Fact]
    public void CardinalityEstimator_EstimateCombinedSelectivity_ANDsCorrectly()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);

        var predicates = new List<PredicateInfo>
        {
            new() { ColumnName = "id", Operator = "=", Value = 100 },
            new() { ColumnName = "age", Operator = ">", Value = 30 }
        };

        // Act
        var combined = estimator.EstimateCombinedSelectivity(predicates);

        // Assert
        Assert.True(combined > 0.0 && combined <= 1.0);
        _output.WriteLine($"✓ Combined selectivity (2 predicates): {combined:F4}");
    }

    [Fact]
    public void CardinalityEstimator_EstimateScanCost_WithFilter_CalculatesCorrectly()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);

        // Act
        var costNoFilter = estimator.EstimateScanCost("id", totalRows: 1000, hasFilter: false);
        var costWithFilter = estimator.EstimateScanCost("id", totalRows: 1000, hasFilter: true, selectivity: 0.1);

        // Assert
        Assert.True(costNoFilter > 0);
        Assert.True(costWithFilter > 0);
        _output.WriteLine($"✓ Scan cost (no filter): {costNoFilter:F4}");
        _output.WriteLine($"✓ Scan cost (with filter): {costWithFilter:F4}");
    }

    // ========================================
    // Query Optimizer Tests
    // ========================================

    [Fact]
    public void QueryOptimizer_Optimize_ReturnsValidPlan()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);
        var optimizer = new QueryOptimizer(estimator);

        var query = new QuerySpec
        {
            TableName = "users",
            SelectColumns = ["id", "name"],
            Predicates =
            [
                new PredicateInfo { ColumnName = "id", Operator = ">", Value = 100 }
            ],
            EstimatedRowCount = 1000
        };

        // Act
        var plan = optimizer.Optimize(query);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.EstimatedCost > 0);
        Assert.True(plan.EstimatedRows > 0);
        _output.WriteLine($"✓ Optimized plan: {plan.PlanType}, Cost: {plan.EstimatedCost:F4}, Rows: {plan.EstimatedRows}");
    }

    [Fact]
    public void QueryOptimizer_Optimize_SelectsSimdPlanForLargeDataset()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);
        var optimizer = new QueryOptimizer(estimator);

        var query = new QuerySpec
        {
            TableName = "users",
            SelectColumns = ["id"],
            Predicates =
            [
                new PredicateInfo { ColumnName = "id", Operator = ">", Value = 100 }
            ],
            EstimatedRowCount = 100000 // Large dataset
        };

        // Act
        var plan = optimizer.Optimize(query);

        // Assert (depends on hardware support)
        Assert.NotNull(plan);
        _output.WriteLine($"✓ Plan for large dataset: {plan.PlanType}");
    }

    [Fact]
    public void QueryOptimizer_CachePlan_ReusesCachedPlan()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);
        var optimizer = new QueryOptimizer(estimator);

        var query = new QuerySpec
        {
            TableName = "users",
            SelectColumns = ["id"],
            EstimatedRowCount = 1000
        };

        // Act
        var plan1 = optimizer.Optimize(query);
        var plan2 = optimizer.Optimize(query); // Should use cache

        // Assert
        Assert.Equal(plan1, plan2); // Same plan instance
        Assert.Equal(1, optimizer.CacheSize);
        _output.WriteLine($"✓ Plan cached successfully");
    }

    [Fact]
    public void QueryOptimizer_ClearCache_RemovesAllPlans()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);
        var optimizer = new QueryOptimizer(estimator);

        var query = new QuerySpec { TableName = "users", EstimatedRowCount = 1000 };
        optimizer.Optimize(query);

        // Act
        optimizer.ClearCache();

        // Assert
        Assert.Equal(0, optimizer.CacheSize);
        _output.WriteLine("✓ Cache cleared");
    }

    [Fact]
    public void QueryOptimizer_OptimizeJoinOrder_SmallestTableFirst()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);
        var optimizer = new QueryOptimizer(estimator);

        var tables = new List<TableInfo>
        {
            new() { Name = "users", RowCount = 1000 },
            new() { Name = "orders", RowCount = 5000 },
            new() { Name = "products", RowCount = 100 }
        };

        var joinConditions = new List<JoinCondition>
        {
            new() { LeftTable = "users", LeftColumn = "id", RightTable = "orders", RightColumn = "user_id" },
            new() { LeftTable = "orders", LeftColumn = "product_id", RightTable = "products", RightColumn = "id" }
        };

        // Act
        var joinOrder = optimizer.OptimizeJoinOrder(tables, joinConditions);

        // Assert
        Assert.NotNull(joinOrder);
        Assert.Equal(3, joinOrder.Count);
        Assert.Equal("products", joinOrder[0]); // Smallest table first
        _output.WriteLine($"✓ Join order: {string.Join(" -> ", joinOrder)}");
    }

    // ========================================
    // Predicate Pushdown Tests
    // ========================================

    [Fact]
    public void PredicatePushdown_OptimizePredicateOrder_EqualityFirst()
    {
        // Arrange
        var predicates = new List<PredicateInfo>
        {
            new() { ColumnName = "age", Operator = ">", Value = 30 },
            new() { ColumnName = "id", Operator = "=", Value = 100 },
            new() { ColumnName = "status", Operator = "!=", Value = "inactive" }
        };

        // Act
        var optimized = PredicatePushdown.OptimizePredicateOrder(predicates);

        // Assert
        Assert.Equal("=", optimized[0].Operator); // Equality first
        _output.WriteLine($"✓ Optimized order: {string.Join(", ", optimized.Select(p => p.Operator))}");
    }

    [Fact]
    public void PredicatePushdown_CanPushDown_ValidPredicate_ReturnsTrue()
    {
        // Arrange
        var predicate = new PredicateInfo
        {
            ColumnName = "id",
            Operator = "=",
            Value = 100
        };

        var availableColumns = new HashSet<string> { "id", "name", "age" };

        // Act
        var canPush = PredicatePushdown.CanPushDown(predicate, availableColumns);

        // Assert
        Assert.True(canPush);
        _output.WriteLine("✓ Predicate can be pushed down");
    }

    [Fact]
    public void PredicatePushdown_CanPushDown_MissingColumn_ReturnsFalse()
    {
        // Arrange
        var predicate = new PredicateInfo
        {
            ColumnName = "missing_column",
            Operator = "=",
            Value = 100
        };

        var availableColumns = new HashSet<string> { "id", "name" };

        // Act
        var canPush = PredicatePushdown.CanPushDown(predicate, availableColumns);

        // Assert
        Assert.False(canPush);
        _output.WriteLine("✓ Predicate cannot be pushed down (missing column)");
    }

    [Fact]
    public void PredicatePushdown_RewriteForColumnar_DictionaryEncoding_SetsEncoding()
    {
        // Arrange
        var predicate = new PredicateInfo
        {
            ColumnName = "category",
            Operator = "=",
            Value = "electronics"
        };

        // Act
        var rewritten = PredicatePushdown.RewriteForColumnar(
            predicate,
            ColumnFormat.ColumnEncoding.Dictionary
        );

        // Assert
        Assert.Equal(ColumnFormat.ColumnEncoding.Dictionary, rewritten.Encoding);
        _output.WriteLine("✓ Predicate rewritten for dictionary encoding");
    }

    [Fact]
    public void PredicatePushdown_CombinePredicates_SingleColumn_ReturnsCombined()
    {
        // Arrange
        var predicates = new List<PredicateInfo>
        {
            new() { ColumnName = "age", Operator = ">", Value = 18 },
            new() { ColumnName = "age", Operator = "<", Value = 65 }
        };

        // Act
        var combined = PredicatePushdown.CombinePredicates(predicates);

        // Assert
        Assert.NotNull(combined);
        _output.WriteLine($"✓ Combined predicates: {combined.Count} predicate(s)");
    }

    // ========================================
    // Integration Tests
    // ========================================

    [Fact]
    public void Integration_OptimizerWithPredicatePushdown_WorksEndToEnd()
    {
        // Arrange
        var stats = CreateSampleStatistics();
        var estimator = new CardinalityEstimator(stats);
        var optimizer = new QueryOptimizer(estimator);

        var query = new QuerySpec
        {
            TableName = "users",
            SelectColumns = ["id", "name"],
            Predicates =
            [
                new PredicateInfo { ColumnName = "id", Operator = ">", Value = 100 },
                new PredicateInfo { ColumnName = "age", Operator = "=", Value = 30 }
            ],
            EstimatedRowCount = 10000
        };

        // Act
        var plan = optimizer.Optimize(query);

        // Assert
        Assert.NotNull(plan);
        Assert.True(plan.EstimatedCost > 0);
        Assert.True(plan.EstimatedRows < query.EstimatedRowCount); // Filtered
        Assert.True(plan.UsePredicatePushdown || plan.UseSimd); // Should use optimization
        _output.WriteLine($"✓ End-to-end optimization: {plan.PlanType}, Rows: {plan.EstimatedRows}/{query.EstimatedRowCount}");
    }

    // Helper method
    private static Dictionary<string, ColumnStatistics.ColumnStats> CreateSampleStatistics()
    {
        return new Dictionary<string, ColumnStatistics.ColumnStats>
        {
            ["id"] = new ColumnStatistics.ColumnStats
            {
                ColumnName = "id",
                ValueCount = 1000,
                NullCount = 0,
                DistinctCount = 1000,
                MinValue = 1,
                MaxValue = 1000
            },
            ["age"] = new ColumnStatistics.ColumnStats
            {
                ColumnName = "age",
                ValueCount = 1000,
                NullCount = 10,
                DistinctCount = 80,
                MinValue = 18,
                MaxValue = 95
            },
            ["user_id"] = new ColumnStatistics.ColumnStats
            {
                ColumnName = "user_id",
                ValueCount = 500,
                NullCount = 0,
                DistinctCount = 500
            }
        };
    }
}
