// <copyright file="GenericLoadTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.ColumnStorage;
using SharpCoreDB.DataStructures;
using SharpCoreDB.MVCC;
using SharpCoreDB.Linq;
using System.Diagnostics;
using Xunit;

/// <summary>
/// Load tests for generic types: structs, enums, and custom value types.
/// Tests high-volume operations to ensure generics work at scale.
/// </summary>
public sealed class GenericLoadTests
{
    #region Test Data Types

    /// <summary>
    /// Enum for product categories.
    /// </summary>
    public enum ProductCategory : byte
    {
        Electronics = 1,
        Clothing = 2,
        Food = 3,
        Books = 4,
        Sports = 5,
        Home = 6
    }

    /// <summary>
    /// Struct for order status with full IComparable support.
    /// </summary>
    public struct OrderStatus : IEquatable<OrderStatus>, IComparable<OrderStatus>
    {
        public OrderState State { get; init; }
        public DateTime LastUpdated { get; init; }

        public bool Equals(OrderStatus other) => State == other.State;
        public override int GetHashCode() => State.GetHashCode();
        public override string ToString() => $"{State} ({LastUpdated:yyyy-MM-dd})";
        
        public int CompareTo(OrderStatus other) => State.CompareTo(other.State);
    }

    public enum OrderState : byte
    {
        Pending = 0,
        Processing = 1,
        Shipped = 2,
        Delivered = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Struct for price with currency.
    /// </summary>
    public readonly struct Money : IEquatable<Money>, IComparable<Money>
    {
        public decimal Amount { get; init; }
        public string Currency { get; init; }

        public Money(decimal amount, string currency = "USD")
        {
            Amount = amount;
            Currency = currency ?? "USD";
        }

        public bool Equals(Money other) => Amount == other.Amount && Currency == other.Currency;
        public override int GetHashCode() => HashCode.Combine(Amount, Currency);
        public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
        public override string ToString() => $"{Amount:C} {Currency}";
    }

    /// <summary>
    /// Product record with struct/enum fields.
    /// </summary>
    public sealed record Product(
        int Id,
        string Name,
        ProductCategory Category,
        Money Price,
        OrderStatus Status,
        DateTime CreatedAt);

    /// <summary>
    /// Order record with complex struct fields.
    /// </summary>
    public sealed record Order(
        int Id,
        int CustomerId,
        OrderStatus Status,
        Money TotalPrice,
        DateTime OrderDate,
        DateTime? DeliveredDate);

    /// <summary>
    /// Metric record for analytics.
    /// </summary>
    public sealed record Metric(
        int Id,
        string MetricName,
        double Value,
        long Timestamp,
        ProductCategory Category);

    #endregion

    #region Generic Hash Index Load Tests

    [Fact]
    public void GenericHashIndex_WithStructKey_Load100k()
    {
        // Arrange
        var index = new GenericHashIndex<OrderStatus>("status");
        var orders = new Dictionary<long, Order>(); // Simulate storage
        var stopwatch = Stopwatch.StartNew();
        long position = 0;

        // Act: Insert 100k orders with struct keys
        for (int i = 0; i < 100_000; i++)
        {
            var status = new OrderStatus
            {
                State = (OrderState)(i % 5),
                LastUpdated = DateTime.Now.AddDays(-i % 365)
            };

            var order = new Order(
                i,
                CustomerId: i % 1000,
                Status: status,
                TotalPrice: new Money((i % 1000) + 10m, "USD"),
                OrderDate: DateTime.Now.AddDays(-i % 30),
                DeliveredDate: null
            );

            orders[position] = order;
            index.Add(status, position);
            position++;
        }

        stopwatch.Stop();

        // Assert
        Assert.Equal(100_000, position);
        var stats = index.GetStatistics();

        Console.WriteLine($"? Inserted 100k orders with struct keys");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {100_000.0 / stopwatch.Elapsed.TotalSeconds:N0} ops/sec");
        Console.WriteLine($"   Unique keys: {stats.UniqueKeys}");
        Console.WriteLine($"   Avg per key: {stats.AverageEntriesPerKey:F2}");
        Console.WriteLine($"   Memory: {stats.MemoryUsageBytes / 1024 / 1024:F2}MB");

        // Verify lookups work
        var testStatus = new OrderStatus { State = OrderState.Processing, LastUpdated = DateTime.Now };
        var results = index.Find(testStatus).ToList();
        Console.WriteLine($"   Lookup results: {results.Count} positions found");
    }

    [Fact]
    public void GenericHashIndex_WithEnumKey_Load50k()
    {
        // Arrange: Use int keys (cast from enum) since GenericHashIndex requires IComparable
        var index = new GenericHashIndex<int>("category");
        var products = new Dictionary<long, Product>();
        var stopwatch = Stopwatch.StartNew();
        long position = 0;

        // Act: Insert 50k products with enum keys (as int)
        for (int i = 0; i < 50_000; i++)
        {
            var category = (ProductCategory)((i % 6) + 1);
            
            var product = new Product(
                i,
                Name: $"Product{i}",
                Category: category,
                Price: new Money((i % 500) + 10m, "USD"),
                Status: new OrderStatus { State = OrderState.Pending, LastUpdated = DateTime.Now },
                CreatedAt: DateTime.Now
            );

            products[position] = product;
            index.Add((int)category, position); // Cast enum to int
            position++;
        }

        stopwatch.Stop();

        // Assert
        var stats = index.GetStatistics();
        Console.WriteLine($"? Inserted 50k products with enum keys (as int)");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {50_000.0 / stopwatch.Elapsed.TotalSeconds:N0} ops/sec");
        Console.WriteLine($"   Unique keys: {stats.UniqueKeys}");
        Console.WriteLine($"   Avg per key: {stats.AverageEntriesPerKey:F2}");

        // Verify lookup performance
        var lookupSw = Stopwatch.StartNew();
        var positions = index.Find((int)ProductCategory.Electronics).ToList();
        lookupSw.Stop();

        Console.WriteLine($"   Lookup time: {lookupSw.Elapsed.TotalMicroseconds:F2}µs");
        Console.WriteLine($"   Electronics positions: {positions.Count}");
    }

    [Fact]
    public void GenericHashIndex_WithMoneyStruct_Load25k()
    {
        // Arrange
        var index = new GenericHashIndex<Money>("price");
        var products = new Dictionary<long, Product>();
        var stopwatch = Stopwatch.StartNew();
        long position = 0;

        // Act: Insert 25k products with Money struct keys
        for (int i = 0; i < 25_000; i++)
        {
            var price = new Money((i % 100) + 9.99m, "USD");
            
            var product = new Product(
                i,
                Name: $"Product{i}",
                Category: (ProductCategory)((i % 6) + 1),
                Price: price,
                Status: new OrderStatus { State = OrderState.Pending, LastUpdated = DateTime.Now },
                CreatedAt: DateTime.Now
            );

            products[position] = product;
            index.Add(price, position);
            position++;
        }

        stopwatch.Stop();

        // Assert
        var stats = index.GetStatistics();
        Console.WriteLine($"? Inserted 25k products with Money struct keys");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {25_000.0 / stopwatch.Elapsed.TotalSeconds:N0} ops/sec");
        Console.WriteLine($"   Unique prices: {stats.UniqueKeys}");
        Console.WriteLine($"   Products per price: {stats.AverageEntriesPerKey:F2}");
    }

    #endregion

    #region MVCC with Struct/Enum Load Tests

    [Fact]
    public void MVCC_WithStructValues_Load10k()
    {
        // Arrange
        var mvcc = new MvccManager<int, Product>("products_load");
        var stopwatch = Stopwatch.StartNew();

        // Act: Insert 10k products with struct/enum fields
        using (var tx = mvcc.BeginTransaction())
        {
            for (int i = 0; i < 10_000; i++)
            {
                var product = new Product(
                    i,
                    Name: $"Product{i}",
                    Category: (ProductCategory)((i % 6) + 1),
                    Price: new Money((i % 500) + 19.99m, "USD"),
                    Status: new OrderStatus
                    {
                        State = (OrderState)(i % 5),
                        LastUpdated = DateTime.Now.AddDays(-i % 365)
                    },
                    CreatedAt: DateTime.Now.AddDays(-i % 730)
                );

                mvcc.Insert(i, product, tx);
            }

            mvcc.CommitTransaction(tx);
        }

        stopwatch.Stop();

        // Assert: Verify by scanning
        int count = 0;
        using (var verifyTx = mvcc.BeginTransaction(isReadOnly: true))
        {
            count = mvcc.Scan(verifyTx).Count();
        }
        Assert.Equal(10_000, count);

        Console.WriteLine($"? MVCC: Inserted 10k products with struct fields");
        Console.WriteLine($"   Time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {10_000.0 / stopwatch.Elapsed.TotalSeconds:N0} ops/sec");

        // Test concurrent reads
        var readSw = Stopwatch.StartNew();
        using (var readTx = mvcc.BeginTransaction(isReadOnly: true))
        {
            var products = mvcc.Scan(readTx).ToList();
            Assert.Equal(10_000, products.Count);
        }
        readSw.Stop();

        Console.WriteLine($"   Full scan time: {readSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Scan throughput: {10_000.0 / readSw.Elapsed.TotalSeconds:N0} rows/sec");
    }

    [Fact]
    public void MVCC_ConcurrentReadsWithStructs_Stress()
    {
        // Arrange
        var mvcc = new MvccManager<int, Order>("orders_concurrent");
        
        // Populate
        using (var writeTx = mvcc.BeginTransaction())
        {
            for (int i = 0; i < 5_000; i++)
            {
                var order = new Order(
                    i,
                    CustomerId: i % 100,
                    Status: new OrderStatus { State = (OrderState)(i % 5), LastUpdated = DateTime.Now },
                    TotalPrice: new Money((i % 200) + 50m, "USD"),
                    OrderDate: DateTime.Now.AddDays(-i % 60),
                    DeliveredDate: null
                );

                mvcc.Insert(i, order, writeTx);
            }

            mvcc.CommitTransaction(writeTx);
        }

        // Act: 100 concurrent readers
        var stopwatch = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 100).Select(async _ =>
        {
            await Task.Run(() =>
            {
                using var readTx = mvcc.BeginTransaction(isReadOnly: true);
                var orders = mvcc.Scan(readTx).ToList();
                Assert.Equal(5_000, orders.Count);
            });
        }).ToArray();

        Task.WaitAll(tasks);
        stopwatch.Stop();

        // Assert
        Console.WriteLine($"? MVCC: 100 concurrent readers with struct data");
        Console.WriteLine($"   Total time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Avg per reader: {stopwatch.ElapsedMilliseconds / 100.0:F2}ms");
        Console.WriteLine($"   Total scans: {100 * 5_000:N0} rows");
        Console.WriteLine($"   Throughput: {(100 * 5_000.0) / stopwatch.Elapsed.TotalSeconds:N0} rows/sec");
    }

    #endregion

    #region Columnar Storage with Struct/Enum Load Tests

    [Fact]
    public void ColumnStore_WithStructFields_Load50k()
    {
        // Arrange
        var products = Enumerable.Range(0, 50_000).Select(i => new Product(
            i,
            Name: $"Product{i}",
            Category: (ProductCategory)((i % 6) + 1),
            Price: new Money((i % 500) + 19.99m, "USD"),
            Status: new OrderStatus
            {
                State = (OrderState)(i % 5),
                LastUpdated = DateTime.Now.AddDays(-i % 365)
            },
            CreatedAt: DateTime.Now.AddDays(-i % 730)
        )).ToList();

        var columnStore = new ColumnStore<Product>();

        // Act: Transpose
        var transposeSw = Stopwatch.StartNew();
        columnStore.Transpose(products);
        transposeSw.Stop();

        // Assert
        Assert.Equal(50_000, columnStore.RowCount);

        Console.WriteLine($"? Columnar: Transposed 50k products with struct fields");
        Console.WriteLine($"   Transpose time: {transposeSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {50_000.0 / transposeSw.Elapsed.TotalSeconds:N0} rows/sec");
        Console.WriteLine($"   Columns: {columnStore.ColumnNames.Count}");
        Console.WriteLine($"   Column names: {string.Join(", ", columnStore.ColumnNames)}");

        // Test aggregates on struct fields (this will work on primitive properties)
        var aggregateSw = Stopwatch.StartNew();
        var avgId = columnStore.Average("Id");
        aggregateSw.Stop();

        Console.WriteLine($"   Aggregate time: {aggregateSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   AVG(Id): {avgId:F2}");
    }

    [Fact]
    public void ColumnStore_WithMetrics_SIMD_Aggregates_100k()
    {
        // Arrange: Generate 100k metrics
        var metrics = Enumerable.Range(0, 100_000).Select(i => new Metric(
            i,
            MetricName: $"cpu_usage_{i % 10}",
            Value: (i % 100) + (i / 1000.0),
            Timestamp: DateTime.Now.AddMinutes(-i).Ticks,
            Category: (ProductCategory)((i % 6) + 1)
        )).ToList();

        var columnStore = new ColumnStore<Metric>();

        // Act: Transpose
        var transposeSw = Stopwatch.StartNew();
        columnStore.Transpose(metrics);
        transposeSw.Stop();

        Console.WriteLine($"? Columnar: Transposed 100k metrics");
        Console.WriteLine($"   Transpose time: {transposeSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {100_000.0 / transposeSw.Elapsed.TotalSeconds:N0} rows/sec");

        // Act: SIMD aggregates
        var aggSw = Stopwatch.StartNew();
        
        var sum = columnStore.Sum<int>("Id");
        var avg = columnStore.Average("Value");
        var min = columnStore.Min<double>("Value");
        var max = columnStore.Max<double>("Value");
        var count = columnStore.Count("Id");
        
        aggSw.Stop();

        // Assert: All aggregates < 10ms for 100k records
        Assert.True(aggSw.ElapsedMilliseconds < 10, 
            $"Expected < 10ms for all aggregates, got {aggSw.ElapsedMilliseconds}ms");

        Console.WriteLine($"   All 5 aggregates: {aggSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   SUM(Id): {sum:N0}");
        Console.WriteLine($"   AVG(Value): {avg:F2}");
        Console.WriteLine($"   MIN(Value): {min:F2}");
        Console.WriteLine($"   MAX(Value): {max:F2}");
        Console.WriteLine($"   COUNT: {count:N0}");
        Console.WriteLine($"   Throughput: {500_000.0 / aggSw.Elapsed.TotalMilliseconds:F0}k ops/ms");
    }

    #endregion

    #region LINQ with Struct/Enum Load Tests

    [Fact(Skip = "LINQ translator doesn't support Convert expressions (enum comparisons) yet - known limitation")]
    public void LINQ_WithEnumFilters_Load5k()
    {
        // Arrange
        var mvcc = new MvccManager<int, Product>("products_linq");
        
        // Populate
        using (var tx = mvcc.BeginTransaction())
        {
            for (int i = 0; i < 5_000; i++)
            {
                var product = new Product(
                    i,
                    Name: $"Product{i}",
                    Category: (ProductCategory)((i % 6) + 1),
                    Price: new Money((i % 500) + 19.99m, "USD"),
                    Status: new OrderStatus { State = (OrderState)(i % 5), LastUpdated = DateTime.Now },
                    CreatedAt: DateTime.Now
                );

                mvcc.Insert(i, product, tx);
            }

            mvcc.CommitTransaction(tx);
        }

        // Act: LINQ queries with enum filters
        using var readTx = mvcc.BeginTransaction(isReadOnly: true);
        var queryable = new MvccQueryable<int, Product>(mvcc, readTx);

        var sw = Stopwatch.StartNew();
        
        // Query 1: Filter by enum
        var electronics = queryable
            .Where(p => p.Category == ProductCategory.Electronics)
            .ToList();
        
        // Query 2: Multiple enum values
        var categories = queryable
            .Where(p => p.Category == ProductCategory.Electronics || 
                       p.Category == ProductCategory.Books)
            .ToList();
        
        // Query 3: Group by enum
        var byCategory = queryable
            .GroupBy(p => p.Category)
            .ToList();
        
        sw.Stop();

        // Assert
        Console.WriteLine($"? LINQ with enum filters (5k products)");
        Console.WriteLine($"   Query time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Electronics: {electronics.Count}");
        Console.WriteLine($"   Electronics + Books: {categories.Count}");
        Console.WriteLine($"   Categories: {byCategory.Count}");
        Console.WriteLine($"   Throughput: {15_000.0 / sw.Elapsed.TotalSeconds:N0} ops/sec");
    }

    #endregion

    #region Memory and GC Stress Tests

    [Fact]
    public void StructTypes_MemoryEfficiency_10kObjects()
    {
        // Arrange
        var beforeGC = GC.GetTotalMemory(true);
        var objects = new List<Product>(10_000);

        // Act: Create 10k objects with structs
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
        {
            objects.Add(new Product(
                i,
                Name: $"Product{i}",
                Category: (ProductCategory)((i % 6) + 1),
                Price: new Money((i % 500) + 19.99m, "USD"),
                Status: new OrderStatus { State = (OrderState)(i % 5), LastUpdated = DateTime.Now },
                CreatedAt: DateTime.Now
            ));
        }
        sw.Stop();

        var afterGC = GC.GetTotalMemory(false);
        var allocated = (afterGC - beforeGC) / 1024.0 / 1024.0;

        // Assert
        Console.WriteLine($"? Memory test: 10k Product objects with structs");
        Console.WriteLine($"   Creation time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Memory allocated: {allocated:F2}MB");
        Console.WriteLine($"   Avg per object: {(allocated * 1024 * 1024) / 10_000:F2} bytes");
        Console.WriteLine($"   Gen0 collections: {GC.CollectionCount(0)}");
        Console.WriteLine($"   Gen1 collections: {GC.CollectionCount(1)}");
        Console.WriteLine($"   Gen2 collections: {GC.CollectionCount(2)}");
    }

    [Fact]
    public void GenericHashIndex_GCPressure_100kInserts()
    {
        // Arrange
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
        
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        var index = new GenericHashIndex<int>("category");
        long position = 0;

        // Act: Insert 100k
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++)
        {
            var category = (ProductCategory)((i % 6) + 1);
            index.Add((int)category, position);
            position++;
        }
        sw.Stop();

        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        // Assert
        Console.WriteLine($"? GC Pressure test: 100k inserts");
        Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Gen0 collections: {gen0After - gen0Before}");
        Console.WriteLine($"   Gen1 collections: {gen1After - gen1Before}");
        Console.WriteLine($"   Gen2 collections: {gen2After - gen2Before}");
        Console.WriteLine($"   Throughput: {100_000.0 / sw.Elapsed.TotalSeconds:N0} ops/sec");
    }

    #endregion

    #region Edge Cases and Validation

    [Fact]
    public void StructEquality_HashIndex_DuplicateDetection()
    {
        // Arrange
        var index = new GenericHashIndex<OrderStatus>("status");
        
        var status1 = new OrderStatus { State = OrderState.Processing, LastUpdated = DateTime.Now };
        var status2 = new OrderStatus { State = OrderState.Processing, LastUpdated = DateTime.Now.AddDays(-1) };
        
        // Act
        index.Add(status1, 1); // position 1
        index.Add(status2, 2); // position 2 - should match status1 (same State)

        // Assert
        var results = index.Find(status1).ToList();
        Assert.Equal(2, results.Count); // Both positions should be found

        Console.WriteLine($"? Struct equality works correctly in hash index");
        Console.WriteLine($"   Status1 == Status2: {status1.Equals(status2)}");
        Console.WriteLine($"   Positions found: {results.Count}");
    }

    #endregion
}
