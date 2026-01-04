// <copyright file="CompiledQueryBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks comparing compiled query execution vs regular parsing.
/// Target: 5-10x speedup for repeated SELECT queries.
/// Goal: 1000 identical SELECTs in less than 8ms total.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(BenchmarkConfig))]
public class CompiledQueryBenchmark
{
    private class BenchmarkConfig : ManualConfig
    {
        public BenchmarkConfig()
        {
            AddLogger(BenchmarkDotNet.Loggers.ConsoleLogger.Default);
        }
    }

    private IDatabase _db = null!;
    private PreparedStatement _compiledStmt = null!;
    private string _sql = null!;
    private string _dbPath = null!;

    [Params(100, 1000, 5000)]
    public int QueryCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"compiled_query_bench_{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        
        _db = factory.Create(_dbPath, "bench123");

        // Create test table with enough data
        _db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, customer TEXT, amount DECIMAL, status TEXT)");
        
        for (int i = 1; i <= 1000; i++)
        {
            var status = i % 3 == 0 ? "completed" : "pending";
            _db.ExecuteSQL($"INSERT INTO orders VALUES ({i}, 'Customer{i}', {100 + i}, '{status}')");
        }

        // Prepare SQL and compiled statement
        _sql = "SELECT id, customer, amount FROM orders WHERE status = 'completed' AND amount > 500";
        _compiledStmt = _db.Prepare(_sql);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dbPath))
        {
            Directory.Delete(_dbPath, true);
        }
    }

    [Benchmark(Baseline = true)]
    public List<List<Dictionary<string, object>>> RegularQuery_RepeatedParsing()
    {
        var results = new List<List<Dictionary<string, object>>>();
        
        for (int i = 0; i < QueryCount; i++)
        {
            results.Add(_db.ExecuteQuery(_sql));
        }
        
        return results;
    }

    [Benchmark]
    public List<List<Dictionary<string, object>>> CompiledQuery_ZeroParsing()
    {
        var results = new List<List<Dictionary<string, object>>>();
        
        for (int i = 0; i < QueryCount; i++)
        {
            results.Add(_db.ExecuteCompiledQuery(_compiledStmt));
        }
        
        return results;
    }

    [Benchmark]
    public List<List<Dictionary<string, object>>> CompiledQuery_Parameterized()
    {
        var results = new List<List<Dictionary<string, object>>>();
        var parameters = new Dictionary<string, object?> { { "status", "completed" } };
        
        for (int i = 0; i < QueryCount; i++)
        {
            results.Add(_db.ExecuteCompiledQuery(_compiledStmt, parameters));
        }
        
        return results;
    }
}

/// <summary>
/// Benchmarks for simple vs complex queries with compilation.
/// </summary>
[MemoryDiagnoser]
public class CompiledQueryComplexityBenchmark
{
    private IDatabase _db = null!;
    private PreparedStatement _simpleStmt = null!;
    private PreparedStatement _complexStmt = null!;
    private PreparedStatement _aggregateStmt = null!;
    private string _dbPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"compiled_complexity_bench_{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        
        _db = factory.Create(_dbPath, "bench123");

        // Create test table
        _db.ExecuteSQL("CREATE TABLE sales (id INTEGER, product TEXT, quantity INTEGER, price DECIMAL, region TEXT)");
        
        for (int i = 1; i <= 5000; i++)
        {
            var region = i % 3 == 0 ? "North" : i % 3 == 1 ? "South" : "East";
            _db.ExecuteSQL($"INSERT INTO sales VALUES ({i}, 'Product{i % 50}', {i % 100}, {10 + (i % 20)}, '{region}')");
        }

        // Prepare queries
        _simpleStmt = _db.Prepare("SELECT * FROM sales WHERE region = 'North'");
        _complexStmt = _db.Prepare("SELECT product, quantity FROM sales WHERE region = 'North' AND quantity > 50 ORDER BY quantity DESC");
        _aggregateStmt = _db.Prepare("SELECT product, SUM(quantity) FROM sales WHERE region = 'North' GROUP BY product");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dbPath))
        {
            Directory.Delete(_dbPath, true);
        }
    }

    [Benchmark]
    public List<Dictionary<string, object>> SimpleSelect_Compiled()
    {
        return _db.ExecuteCompiledQuery(_simpleStmt);
    }

    [Benchmark]
    public List<Dictionary<string, object>> ComplexWhere_Compiled()
    {
        return _db.ExecuteCompiledQuery(_complexStmt);
    }

    [Benchmark(Baseline = true)]
    public List<Dictionary<string, object>> SimpleSelect_Regular()
    {
        return _db.ExecuteQuery("SELECT * FROM sales WHERE region = 'North'");
    }

    [Benchmark]
    public List<Dictionary<string, object>> ComplexWhere_Regular()
    {
        return _db.ExecuteQuery("SELECT product, quantity FROM sales WHERE region = 'North' AND quantity > 50 ORDER BY quantity DESC");
    }
}

/// <summary>
/// Benchmark for the target scenario: 1000 identical SELECTs in less than 8ms.
/// </summary>
[MemoryDiagnoser]
public class CompiledQuery1000SelectsBenchmark
{
    private IDatabase _db = null!;
    private PreparedStatement _compiledStmt = null!;
    private string _sql = null!;
    private string _dbPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"compiled_1000_bench_{Guid.NewGuid()}");
        
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        
        _db = factory.Create(_dbPath, "bench123");

        // Small table for fastest possible queries
        _db.ExecuteSQL("CREATE TABLE fast_table (id INTEGER, value INTEGER)");
        for (int i = 1; i <= 100; i++)
        {
            _db.ExecuteSQL($"INSERT INTO fast_table VALUES ({i}, {i * 10})");
        }

        _sql = "SELECT * FROM fast_table WHERE value > 500";
        _compiledStmt = _db.Prepare(_sql);
        
        // Warm-up
        _ = _db.ExecuteCompiledQuery(_compiledStmt);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dbPath))
        {
            Directory.Delete(_dbPath, true);
        }
    }

    [Benchmark]
    [Arguments(1000)]
    public void Execute1000CompiledQueries(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = _db.ExecuteCompiledQuery(_compiledStmt);
        }
    }

    [Benchmark]
    [Arguments(1000)]
    public void Execute1000RegularQueries(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = _db.ExecuteQuery(_sql);
        }
    }
}
