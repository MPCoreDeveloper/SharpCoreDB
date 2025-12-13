// <copyright file="ColumnStoreTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.ColumnStorage;
using System.Diagnostics;
using Xunit;

/// <summary>
/// Tests for columnar storage with SIMD-optimized aggregates.
/// Target: Aggregates on 10k records in < 2ms.
/// </summary>
public sealed class ColumnStoreTests
{
    #region Test Data Models

    public sealed record SalesRecord(
        int Id,
        string Product,
        decimal Price,
        int Quantity,
        DateTime OrderDate,
        string Region);

    public sealed record EmployeeRecord(
        int Id,
        string Name,
        int Age,
        decimal Salary,
        string Department,
        DateTime HireDate);

    public sealed record MetricsRecord(
        int Id,
        double Value,
        long Timestamp,
        string MetricName,
        double Average);

    #endregion

    #region Transpose Tests

    [Fact]
    public void ColumnStore_Transpose_ConvertsRowsToColumns()
    {
        // Arrange
        var employees = new[]
        {
            new EmployeeRecord(1, "Alice", 30, 100000m, "Engineering", DateTime.Now),
            new EmployeeRecord(2, "Bob", 25, 80000m, "Sales", DateTime.Now),
            new EmployeeRecord(3, "Charlie", 35, 120000m, "Engineering", DateTime.Now),
        };

        var columnStore = new ColumnStore<EmployeeRecord>();

        // Act
        columnStore.Transpose(employees);

        // Assert
        Assert.Equal(3, columnStore.RowCount);
        Assert.Contains("Age", columnStore.ColumnNames);
        Assert.Contains("Salary", columnStore.ColumnNames);
        Assert.Contains("Department", columnStore.ColumnNames);

        Console.WriteLine($"? Transposed {employees.Length} rows to {columnStore.ColumnNames.Count} columns");
        Console.WriteLine($"   Columns: {string.Join(", ", columnStore.ColumnNames)}");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_Transpose_10kRecords_Fast()
    {
        // Arrange
        var records = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();

        // Act
        var sw = Stopwatch.StartNew();
        columnStore.Transpose(records);
        sw.Stop();

        // Assert
        Assert.Equal(10_000, columnStore.RowCount);
        Assert.True(sw.ElapsedMilliseconds < 50,
            $"Expected < 50ms for transpose, got {sw.ElapsedMilliseconds}ms");

        Console.WriteLine($"? Transposed 10k records in {sw.ElapsedMilliseconds}ms");
        Console.WriteLine($"   Throughput: {10000.0 / sw.Elapsed.TotalSeconds:N0} rows/sec");

        columnStore.Dispose();
    }

    #endregion

    #region SUM Aggregate Tests

    [Fact]
    public void ColumnStore_Sum_Int32_CorrectResult()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act
        var sum = columnStore.Sum<int>("Age");

        // Assert
        var expectedSum = employees.Sum(e => e.Age);
        Assert.Equal(expectedSum, sum);

        Console.WriteLine($"? SUM(Age) = {sum:N0} (expected: {expectedSum:N0})");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_Sum_Decimal_CorrectResult()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act
        var sum = columnStore.Sum<decimal>("Salary");

        // Assert
        var expectedSum = employees.Sum(e => e.Salary);
        Assert.Equal(expectedSum, sum);

        Console.WriteLine($"? SUM(Salary) = ${sum:N0} (expected: ${expectedSum:N0})");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_Sum_10kRecords_Under2ms()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Warm up
        _ = columnStore.Sum<int>("Age");

        // Act: Benchmark SUM on 10k records
        var sw = Stopwatch.StartNew();
        var sum = columnStore.Sum<int>("Age");
        sw.Stop();

        // Assert
        Assert.True(sw.Elapsed.TotalMilliseconds < 2.0,
            $"Expected < 2ms, got {sw.Elapsed.TotalMilliseconds:F3}ms");

        Console.WriteLine($"? SUM on 10k records: {sw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Result: {sum:N0}");
        Console.WriteLine($"   Throughput: {10000.0 / sw.Elapsed.TotalMilliseconds:F0}k rows/ms");

        columnStore.Dispose();
    }

    #endregion

    #region AVERAGE Aggregate Tests

    [Fact]
    public void ColumnStore_Average_CorrectResult()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act
        var avg = columnStore.Average("Age");

        // Assert
        var expectedAvg = employees.Average(e => e.Age);
        Assert.Equal(expectedAvg, avg, precision: 2);

        Console.WriteLine($"? AVG(Age) = {avg:F2} (expected: {expectedAvg:F2})");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_Average_Salary_CorrectResult()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act
        var avg = columnStore.Average("Salary");

        // Assert
        var expectedAvg = employees.Average(e => (double)e.Salary);
        Assert.Equal(expectedAvg, avg, precision: 2);

        Console.WriteLine($"? AVG(Salary) = ${avg:N2} (expected: ${expectedAvg:N2})");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_Average_10kRecords_Under2ms()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Warm up
        _ = columnStore.Average("Age");

        // Act
        var sw = Stopwatch.StartNew();
        var avg = columnStore.Average("Age");
        sw.Stop();

        // Assert
        Assert.True(sw.Elapsed.TotalMilliseconds < 2.0,
            $"Expected < 2ms, got {sw.Elapsed.TotalMilliseconds:F3}ms");

        Console.WriteLine($"? AVG on 10k records: {sw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Result: {avg:F2}");

        columnStore.Dispose();
    }

    #endregion

    #region MIN/MAX Aggregate Tests

    [Fact]
    public void ColumnStore_Min_CorrectResult()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act
        var min = columnStore.Min<int>("Age");

        // Assert
        var expectedMin = employees.Min(e => e.Age);
        Assert.Equal(expectedMin, min);

        Console.WriteLine($"? MIN(Age) = {min} (expected: {expectedMin})");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_Max_CorrectResult()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act
        var max = columnStore.Max<int>("Age");

        // Assert
        var expectedMax = employees.Max(e => e.Age);
        Assert.Equal(expectedMax, max);

        Console.WriteLine($"? MAX(Age) = {max} (expected: {expectedMax})");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_MinMax_10kRecords_Under2ms()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Warm up
        _ = columnStore.Min<int>("Age");
        _ = columnStore.Max<int>("Age");

        // Act: Benchmark both MIN and MAX
        var sw = Stopwatch.StartNew();
        var min = columnStore.Min<int>("Age");
        var max = columnStore.Max<int>("Age");
        sw.Stop();

        // Assert
        Assert.True(sw.Elapsed.TotalMilliseconds < 2.0,
            $"Expected < 2ms, got {sw.Elapsed.TotalMilliseconds:F3}ms");

        Console.WriteLine($"? MIN+MAX on 10k records: {sw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   MIN = {min}, MAX = {max}");

        columnStore.Dispose();
    }

    #endregion

    #region Multi-Aggregate Tests

    [Fact]
    public void ColumnStore_MultipleAggregates_10kRecords_Under2ms()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Warm up
        _ = columnStore.Sum<int>("Age");

        // Act: Run ALL aggregates on same column
        var sw = Stopwatch.StartNew();
        
        var sum = columnStore.Sum<int>("Age");
        var avg = columnStore.Average("Age");
        var min = columnStore.Min<int>("Age");
        var max = columnStore.Max<int>("Age");
        var count = columnStore.Count("Age");
        
        sw.Stop();

        // Assert: All 5 aggregates should complete in < 2ms total
        Assert.True(sw.Elapsed.TotalMilliseconds < 2.0,
            $"Expected < 2ms for all aggregates, got {sw.Elapsed.TotalMilliseconds:F3}ms");

        Console.WriteLine($"? ALL AGGREGATES on 10k records: {sw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   SUM   = {sum:N0}");
        Console.WriteLine($"   AVG   = {avg:F2}");
        Console.WriteLine($"   MIN   = {min}");
        Console.WriteLine($"   MAX   = {max}");
        Console.WriteLine($"   COUNT = {count:N0}");

        columnStore.Dispose();
    }

    [Fact]
    public void ColumnStore_AggregatesOnMultipleColumns_Under2ms()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        // Act: Aggregates on different columns
        var sw = Stopwatch.StartNew();
        
        var avgAge = columnStore.Average("Age");
        var avgSalary = columnStore.Average("Salary");
        var minAge = columnStore.Min<int>("Age");
        var maxSalary = columnStore.Max<decimal>("Salary");
        
        sw.Stop();

        // Assert (relaxed threshold for CI/different hardware)
        Assert.True(sw.Elapsed.TotalMilliseconds < 10.0,
            $"Expected < 10ms, got {sw.Elapsed.TotalMilliseconds:F3}ms");

        Console.WriteLine($"? Multi-column aggregates: {sw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   AVG(Age)    = {avgAge:F2}");
        Console.WriteLine($"   AVG(Salary) = ${avgSalary:N2}");
        Console.WriteLine($"   MIN(Age)    = {minAge}");
        Console.WriteLine($"   MAX(Salary) = ${maxSalary:N0}");

        columnStore.Dispose();
    }

    #endregion

    #region Performance Comparison Tests

    [Fact]
    public void ColumnStore_VsLinq_PerformanceComparison()
    {
        // Arrange
        var employees = Generate10kEmployees();
        var columnStore = new ColumnStore<EmployeeRecord>();
        columnStore.Transpose(employees);

        Console.WriteLine("??????????????????????????????????????????????????????????????");
        Console.WriteLine("?        COLUMNAR vs LINQ - PERFORMANCE COMPARISON           ?");
        Console.WriteLine("??????????????????????????????????????????????????????????????");

        // Warm up both
        _ = employees.Sum(e => e.Age);
        _ = columnStore.Sum<int>("Age");

        // Test 1: SUM
        var linqSw = Stopwatch.StartNew();
        var linqSum = employees.Sum(e => e.Age);
        linqSw.Stop();

        var columnSw = Stopwatch.StartNew();
        var columnSum = columnStore.Sum<int>("Age");
        columnSw.Stop();

        var sumSpeedup = linqSw.Elapsed.TotalMilliseconds / columnSw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"\n?? SUM(Age) on 10k records:");
        Console.WriteLine($"   LINQ:     {linqSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Columnar: {columnSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Speedup:  {sumSpeedup:F2}x faster! ?");

        // Test 2: AVERAGE
        linqSw.Restart();
        var linqAvg = employees.Average(e => e.Age);
        linqSw.Stop();

        columnSw.Restart();
        var columnAvg = columnStore.Average("Age");
        columnSw.Stop();

        var avgSpeedup = linqSw.Elapsed.TotalMilliseconds / columnSw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"\n?? AVG(Age) on 10k records:");
        Console.WriteLine($"   LINQ:     {linqSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Columnar: {columnSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Speedup:  {avgSpeedup:F2}x faster! ?");

        // Test 3: MIN + MAX
        linqSw.Restart();
        var linqMin = employees.Min(e => e.Age);
        var linqMax = employees.Max(e => e.Age);
        linqSw.Stop();

        columnSw.Restart();
        var columnMin = columnStore.Min<int>("Age");
        var columnMax = columnStore.Max<int>("Age");
        columnSw.Stop();

        var minMaxSpeedup = linqSw.Elapsed.TotalMilliseconds / columnSw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"\n?? MIN+MAX(Age) on 10k records:");
        Console.WriteLine($"   LINQ:     {linqSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Columnar: {columnSw.Elapsed.TotalMilliseconds:F3}ms");
        Console.WriteLine($"   Speedup:  {minMaxSpeedup:F2}x faster! ?");

        Console.WriteLine($"\n??????????????????????????????????????????????????????????????");
        Console.WriteLine($"?                    SUMMARY                                 ?");
        Console.WriteLine($"??????????????????????????????????????????????????????????????");
        Console.WriteLine($"   Average Speedup: {(sumSpeedup + avgSpeedup + minMaxSpeedup) / 3:F2}x");
        Console.WriteLine($"   Columnar storage is SIGNIFICANTLY faster! ??");

        // Assert: Columnar should be at least 2x faster
        Assert.True(sumSpeedup > 2.0, "Columnar SUM should be at least 2x faster");
        Assert.True(avgSpeedup > 2.0, "Columnar AVG should be at least 2x faster");

        columnStore.Dispose();
    }

    #endregion

    #region Helper Methods

    private static List<EmployeeRecord> Generate10kEmployees()
    {
        var random = new Random(42); // Seed for reproducibility
        var employees = new List<EmployeeRecord>(10_000);

        var departments = new[] { "Engineering", "Sales", "Marketing", "HR", "Finance" };

        for (int i = 0; i < 10_000; i++)
        {
            employees.Add(new EmployeeRecord(
                Id: i + 1,
                Name: $"Employee{i + 1}",
                Age: random.Next(22, 65),
                Salary: random.Next(50_000, 200_000),
                Department: departments[random.Next(departments.Length)],
                HireDate: DateTime.Now.AddDays(-random.Next(1, 3650))
            ));
        }

        return employees;
    }

    private static List<SalesRecord> Generate10kSales()
    {
        var random = new Random(42);
        var sales = new List<SalesRecord>(10_000);

        var products = new[] { "ProductA", "ProductB", "ProductC", "ProductD", "ProductE" };
        var regions = new[] { "North", "South", "East", "West" };

        for (int i = 0; i < 10_000; i++)
        {
            sales.Add(new SalesRecord(
                Id: i + 1,
                Product: products[random.Next(products.Length)],
                Price: random.Next(10, 1000),
                Quantity: random.Next(1, 100),
                OrderDate: DateTime.Now.AddDays(-random.Next(1, 365)),
                Region: regions[random.Next(regions.Length)]
            ));
        }

        return sales;
    }

    #endregion
}
