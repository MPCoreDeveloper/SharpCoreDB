// <copyright file="QueryOptimizationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Query;

using System;
using System.Linq;
using System.Threading.Tasks;
using SharpCoreDB.Query;
using SharpCoreDB.Storage;
using Xunit;

/// <summary>
/// Tests for Phase 7: Advanced Query Optimization.
/// ✅ SCDB Phase 7: Verifies SIMD filtering, columnar storage, cost-based optimizer, parallel execution, and materialized views.
/// </summary>
public sealed class QueryOptimizationTests
{
    private readonly ITestOutputHelper _output;

    public QueryOptimizationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ========================================
    // SIMD Filter Tests
    // ========================================

    [Fact]
    public void SimdFilter_IsSimdSupported_ReportsHardwareCapability()
    {
        // Act
        var isSupported = SimdFilter.IsSimdSupported;

        // Assert
        _output.WriteLine($"✓ SIMD supported: {isSupported}");
        _output.WriteLine($"  Vector<int> size: {SimdFilter.VectorSize<int>()}");
        _output.WriteLine($"  Vector<double> size: {SimdFilter.VectorSize<double>()}");
    }

    [Fact]
    public void SimdFilter_FilterEquals_FindsMatchingValues()
    {
        // Arrange
        var values = Enumerable.Range(0, 1000).ToArray();
        int target = 500;

        // Act
        var indices = SimdFilter.FilterEquals(values, target);

        // Assert
        Assert.Single(indices);
        Assert.Equal(500, indices[0]);
        _output.WriteLine($"✓ SIMD filter found {indices.Length} matches");
    }

    [Fact]
    public void SimdFilter_FilterGreaterThan_FindsValuesAboveThreshold()
    {
        // Arrange
        var values = Enumerable.Range(0, 100).ToArray();
        int threshold = 90;

        // Act
        var indices = SimdFilter.FilterGreaterThan(values, threshold);

        // Assert
        Assert.Equal(9, indices.Length); // 91-99
        Assert.All(indices, i => Assert.True(values[i] > threshold));
        _output.WriteLine($"✓ Found {indices.Length} values > {threshold}");
    }

    [Fact]
    public void SimdFilter_FilterRange_FindsValuesInRange()
    {
        // Arrange
        var values = Enumerable.Range(0, 100).ToArray();
        int min = 20;
        int max = 30;

        // Act
        var indices = SimdFilter.FilterRange(values, min, max);

        // Assert
        Assert.Equal(10, indices.Length); // 20-29
        Assert.All(indices, i => Assert.True(values[i] >= min && values[i] < max));
        _output.WriteLine($"✓ Found {indices.Length} values in [{min}, {max})");
    }

    [Fact]
    public void SimdFilter_FilterDoubles_WorksWithFloatingPoint()
    {
        // Arrange
        var values = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        double threshold = 75.5;

        // Act
        var indices = SimdFilter.FilterGreaterThan(values, threshold);

        // Assert
        Assert.Equal(24, indices.Length); // 76-99
        _output.WriteLine($"✓ Found {indices.Length} doubles > {threshold}");
    }

    // ========================================
    // Columnar Storage Tests
    // ========================================

    [Fact]
    public void ColumnarStorage_AddColumn_CreatesColumn()
    {
        // Arrange
        using var storage = new ColumnarStorage<int>();

        // Act
        storage.AddColumn("id");
        storage.AddColumn("age");

        // Assert
        Assert.Equal(2, storage.ColumnCount);
        _output.WriteLine($"✓ Created {storage.ColumnCount} columns");
    }

    [Fact]
    public void ColumnarStorage_InsertRow_StoresData()
    {
        // Arrange
        using var storage = new ColumnarStorage<string>();
        storage.AddColumn("name");
        storage.AddColumn("email");

        // Act
        storage.InsertRow(new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["email"] = "alice@test.com"
        });

        // Assert
        Assert.Equal(1, storage.RowCount);
        var names = storage.GetColumn("name");
        Assert.Single(names);
        Assert.Equal("Alice", names[0]);
        _output.WriteLine($"✓ Inserted {storage.RowCount} rows");
    }

    [Fact]
    public void ColumnarStorage_GetStats_ReportsCompression()
    {
        // Arrange
        using var storage = new ColumnarStorage<int>();
        storage.AddColumn("value");

        for (int i = 0; i < 1000; i++)
        {
            storage.InsertRow(new Dictionary<string, int> { ["value"] = i % 10 }); // Repetitive data
        }

        // Act
        var stats = storage.GetStats();

        // Assert
        Assert.Equal(1000, stats.RowCount);
        Assert.True(stats.UncompressedBytes > 0);
        Assert.True(stats.CompressedBytes > 0);
        _output.WriteLine($"✓ Compression ratio: {stats.CompressionRatio:F2}x");
        _output.WriteLine($"  Uncompressed: {stats.UncompressedBytes} bytes");
        _output.WriteLine($"  Compressed: {stats.CompressedBytes} bytes");
    }

    // ========================================
    // Cost-Based Optimizer Tests
    // ========================================

    [Fact]
    public void CostBasedOptimizer_EstimateCardinality_CalculatesResultSize()
    {
        // Arrange
        var optimizer = new CostBasedOptimizer();
        optimizer.RegisterTable("users", new TableStatistics
        {
            TableName = "users",
            RowCount = 10000,
            DistinctValues = new Dictionary<string, long> { ["email"] = 9000 }
        });

        var query = new QueryPlan
        {
            TableName = "users",
            Filters =
            [
                new FilterExpression
                {
                    ColumnName = "email",
                    Operator = FilterOperator.Equals,
                    Value = "test@example.com"
                }
            ]
        };

        // Act
        var cardinality = optimizer.EstimateCardinality(query);

        // Assert
        Assert.True(cardinality > 0);
        Assert.True(cardinality < 10000);
        _output.WriteLine($"✓ Estimated cardinality: {cardinality} rows");
    }

    [Fact]
    public void CostBasedOptimizer_EstimateCost_CalculatesQueryCost()
    {
        // Arrange
        var optimizer = new CostBasedOptimizer();
        optimizer.RegisterTable("products", new TableStatistics
        {
            TableName = "products",
            RowCount = 5000
        });

        var query = new QueryPlan
        {
            TableName = "products",
            Filters =
            [
                new FilterExpression
                {
                    ColumnName = "price",
                    Operator = FilterOperator.Range,
                    Value = (100, 200)
                }
            ],
            RequiresSort = true
        };

        // Act
        var cost = optimizer.EstimateCost(query);

        // Assert
        Assert.True(cost > 0);
        _output.WriteLine($"✓ Estimated cost: {cost:F2}");
    }

    [Fact]
    public void CostBasedOptimizer_OptimizeJoinOrder_OrdersTablesBySize()
    {
        // Arrange
        var optimizer = new CostBasedOptimizer();
        optimizer.RegisterTable("orders", new TableStatistics { TableName = "orders", RowCount = 10000 });
        optimizer.RegisterTable("users", new TableStatistics { TableName = "users", RowCount = 1000 });
        optimizer.RegisterTable("products", new TableStatistics { TableName = "products", RowCount = 5000 });

        var tables = new List<string> { "orders", "users", "products" };
        var joins = new List<JoinCondition>();

        // Act
        var optimized = optimizer.OptimizeJoinOrder(tables, joins);

        // Assert
        Assert.Equal("users", optimized[0]); // Smallest first
        _output.WriteLine($"✓ Optimized join order: {string.Join(" -> ", optimized)}");
    }

    // ========================================
    // Parallel Query Executor Tests
    // ========================================

    [Fact]
    public async Task ParallelExecutor_ParallelScan_FiltersInParallel()
    {
        // Arrange
        using var executor = new ParallelQueryExecutor();
        var data = Enumerable.Range(0, 10000).ToList();

        // Act
        var results = await executor.ParallelScanAsync(data, x => x % 2 == 0);

        // Assert
        Assert.Equal(5000, results.Count);
        Assert.All(results, x => Assert.True(x % 2 == 0));
        _output.WriteLine($"✓ Parallel scan returned {results.Count} results");
    }

    [Fact]
    public async Task ParallelExecutor_ParallelAggregate_SumsInParallel()
    {
        // Arrange
        using var executor = new ParallelQueryExecutor();
        var data = Enumerable.Range(1, 100).ToList();

        // Act
        var sum = await executor.ParallelAggregateAsync(
            data,
            0,
            (acc, x) => acc + x,
            (a, b) => a + b);

        // Assert
        Assert.Equal(5050, sum); // Sum of 1-100
        _output.WriteLine($"✓ Parallel aggregate sum: {sum}");
    }

    [Fact]
    public async Task ParallelExecutor_ParallelGroupBy_GroupsInParallel()
    {
        // Arrange
        using var executor = new ParallelQueryExecutor();
        var data = Enumerable.Range(0, 100).ToList();

        // Act: Group by mod 10
        var groups = await executor.ParallelGroupByAsync(
            data,
            x => x % 10,
            g => g.Count());

        // Assert
        Assert.Equal(10, groups.Count);
        Assert.All(groups.Values, count => Assert.Equal(10, count));
        _output.WriteLine($"✓ Parallel group-by created {groups.Count} groups");
    }

    [Fact]
    public async Task ParallelExecutor_ParallelSort_SortsCorrectly()
    {
        // Arrange
        using var executor = new ParallelQueryExecutor();
        var data = Enumerable.Range(0, 10000).Reverse().ToList();

        // Act
        var sorted = await executor.ParallelSortAsync(data);

        // Assert
        Assert.Equal(10000, sorted.Count);
        Assert.Equal(0, sorted[0]);
        Assert.Equal(9999, sorted[^1]);
        _output.WriteLine($"✓ Parallel sort completed for {sorted.Count} items");
    }

    // ========================================
    // Materialized View Tests
    // ========================================

    [Fact]
    public void MaterializedView_Refresh_CachesData()
    {
        // Arrange
        int callCount = 0;
        var view = new MaterializedView<int>(
            "test_view",
            () =>
            {
                callCount++;
                return Enumerable.Range(0, 100);
            },
            RefreshStrategy.Manual);

        // Act
        view.Refresh();
        var data1 = view.GetData().ToList();
        var data2 = view.GetData().ToList();

        // Assert
        Assert.Equal(100, data1.Count);
        Assert.Equal(1, callCount); // Query executed only once
        _output.WriteLine($"✓ View cached {data1.Count} rows, query called {callCount} times");
    }

    [Fact]
    public void MaterializedView_IsStale_DetectsOldData()
    {
        // Arrange
        var view = new MaterializedView<int>(
            "test_view",
            () => Enumerable.Range(0, 10),
            RefreshStrategy.Manual);

        view.Refresh();

        // Act & Assert
        Assert.False(view.IsStale(TimeSpan.FromSeconds(1)));
        System.Threading.Thread.Sleep(1100);
        Assert.True(view.IsStale(TimeSpan.FromSeconds(1)));
        _output.WriteLine("✓ Staleness detection works");
    }

    [Fact]
    public void MaterializedView_OnAccess_RefreshesAutomatically()
    {
        // Arrange
        var view = new MaterializedView<int>(
            "auto_view",
            () => Enumerable.Range(0, 50),
            RefreshStrategy.OnAccess);

        // Act
        var data = view.GetData().ToList();

        // Assert
        Assert.Equal(50, data.Count);
        Assert.True(view.IsInitialized);
        _output.WriteLine($"✓ Auto-refresh on access: {data.Count} rows");
    }

    [Fact]
    public void MaterializedViewManager_RegisterView_ManagesViews()
    {
        // Arrange
        using var manager = new MaterializedViewManager();
        var view1 = new MaterializedView<int>("view1", () => Enumerable.Range(0, 10));
        var view2 = new MaterializedView<int>("view2", () => Enumerable.Range(0, 20));

        // Act
        manager.RegisterView(view1);
        manager.RegisterView(view2);

        // Assert
        Assert.Equal(2, manager.ViewCount);
        var retrieved = manager.GetView<int>("view1");
        Assert.NotNull(retrieved);
        Assert.Equal("view1", retrieved.Name);
        _output.WriteLine($"✓ Manager registered {manager.ViewCount} views");
    }

    [Fact]
    public void MaterializedViewManager_RefreshAll_RefreshesAllViews()
    {
        // Arrange
        using var manager = new MaterializedViewManager();
        var view1 = new MaterializedView<int>("view1", () => Enumerable.Range(0, 10));
        var view2 = new MaterializedView<int>("view2", () => Enumerable.Range(0, 20));

        manager.RegisterView(view1);
        manager.RegisterView(view2);

        // Act
        manager.RefreshAll();

        // Assert
        Assert.True(view1.IsInitialized);
        Assert.True(view2.IsInitialized);
        _output.WriteLine("✓ Refreshed all views");
    }
}
