# SharpCoreDB Benchmark Guide

## Overview

This guide shows how to benchmark SharpCoreDB performance and measure the impact of the v1.0 performance optimizations.

---

## 🎯 Quick Start

### Running Built-in Benchmarks

```bash
# Run all benchmarks
dotnet run --project SharpCoreDB.Benchmarks -c Release

# Run specific benchmark
dotnet run --project SharpCoreDB.Benchmarks -c Release -- --filter *Insert*

# With detailed output
dotnet run --project SharpCoreDB.Benchmarks -c Release -- --verbosity detailed
```

---

## 📊 Benchmark Categories

### 1. Write Performance Benchmarks

#### Sequential Inserts
```csharp
using BenchmarkDotNet.Attributes;
using SharpCoreDB;

[MemoryDiagnoser]
public class InsertBenchmarks
{
    private IDatabase _db;
    private DatabaseFactory _factory;
    
    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
    }
    
    [Benchmark(Baseline = true)]
    public void InsertSequential_Default()
    {
        var db = _factory.Create("./bench_default", "password");
        db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
        
        for (int i = 0; i < 10000; i++)
        {
            db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
                new Dictionary<string, object?> { 
                    { "0", i }, 
                    { "1", $"Value {i}" } 
                });
        }
    }
    
    [Benchmark]
    public void InsertSequential_HighPerformance()
    {
        var db = _factory.Create("./bench_highperf", "password", 
            config: DatabaseConfig.HighPerformance);
        db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
        
        for (int i = 0; i < 10000; i++)
        {
            db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
                new Dictionary<string, object?> { 
                    { "0", i }, 
                    { "1", $"Value {i}" } 
                });
        }
    }
    
    [Benchmark]
    public void InsertSequential_WriteHeavy()
    {
        var db = _factory.Create("./bench_writeheavy", "password", 
            config: DatabaseConfig.WriteHeavy);
        db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
        
        for (int i = 0; i < 10000; i++)
        {
            db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
                new Dictionary<string, object?> { 
                    { "0", i }, 
                    { "1", $"Value {i}" } 
                });
        }
    }
}
```

**Expected Results:**
```
| Method                        | Mean     | StdDev  | Ratio | Gen0   | Allocated |
|------------------------------ |---------:|--------:|------:|-------:|----------:|
| InsertSequential_Default      | 5.20 s   | 0.15 s  | 1.00  | 250000 | 380 MB    |
| InsertSequential_HighPerf     | 3.10 s   | 0.08 s  | 0.60  | 180000 | 320 MB    |
| InsertSequential_WriteHeavy   | 2.40 s   | 0.06 s  | 0.46  | 150000 | 280 MB    |
```

#### Concurrent Inserts
```csharp
[Benchmark]
public void InsertConcurrent_HighPerformance()
{
    var db = _factory.Create("./bench_concurrent", "password", 
        config: DatabaseConfig.HighPerformance);
    db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
    
    Parallel.For(0, 10000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
    {
        db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
            new Dictionary<string, object?> { 
                { "0", i }, 
                { "1", $"Value {i}" } 
            });
    });
}
```

### 2. Read Performance Benchmarks

#### Index Lookup
```csharp
[Benchmark]
public void SelectWithIndex()
{
    var db = _factory.Create("./bench_read", "password", 
        config: DatabaseConfig.ReadHeavy);
    db.ExecuteSQL("CREATE TABLE users (id INTEGER, email TEXT)");
    
    // Populate
    for (int i = 0; i < 100000; i++)
    {
        db.ExecuteSQL("INSERT INTO users VALUES (?, ?)",
            new Dictionary<string, object?> { 
                { "0", i }, 
                { "1", $"user{i}@example.com" } 
            });
    }
    
    // Create index
    var table = db.GetTable("users");
    table.CreateHashIndex("email", buildImmediately: true);
    
    // Benchmark lookups
    for (int i = 0; i < 1000; i++)
    {
        var results = db.ExecuteQuery("SELECT * FROM users WHERE email = ?",
            new Dictionary<string, object?> { 
                { "0", $"user{i}@example.com" } 
            });
    }
}
```

### 3. Aggregate Benchmarks

#### SIMD Aggregates
```csharp
using SharpCoreDB.ColumnStorage;

[Benchmark]
public void AggregateSUM_SIMD()
{
    var store = new ColumnStore<int>("test");
    
    // Insert 100k rows
    for (int i = 0; i < 100000; i++)
    {
        store.AddRow(new Dictionary<string, object> { 
            ["value"] = i 
        });
    }
    
    // Benchmark SUM with SIMD
    var sum = store.Sum<long>("value");
}

[Benchmark]
public void AggregateMIN_SIMD()
{
    var store = new ColumnStore<long>("test");
    
    for (int i = 0; i < 100000; i++)
    {
        store.AddRow(new Dictionary<string, object> { 
            ["value"] = (long)i 
        });
    }
    
    var min = store.Min<long>("value");
}
```

**Expected Results (AVX-512):**
```
| Method              | Mean     | Ratio | CPU Instructions |
|---------------------|----------|-------|------------------|
| AggregateSUM_SIMD   | 1.2 ms   | 2.1x  | Vector512        |
| AggregateMIN_SIMD   | 1.0 ms   | 8.0x  | Vector512        |
```

---

## 🔍 Measuring Specific Optimizations

### SQL Parser Optimization

```csharp
[Benchmark(Baseline = true)]
public void BindParameters_10Params()
{
    var sql = "INSERT INTO test VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
    var params = new Dictionary<string, object?>();
    for (int i = 0; i < 10; i++)
        params[i.ToString()] = $"value{i}";
    
    db.ExecuteSQL(sql, params);
}

[Benchmark]
public void BindParameters_50Params()
{
    var sql = "INSERT INTO test VALUES (" + 
        string.Join(", ", Enumerable.Range(0, 50).Select(_ => "?")) + ")";
    var params = new Dictionary<string, object?>();
    for (int i = 0; i < 50; i++)
        params[i.ToString()] = $"value{i}";
    
    db.ExecuteSQL(sql, params);
}
```

### Lock-Free Index Operations

```csharp
[Benchmark]
public void ConcurrentIndexAccess()
{
    var table = db.GetTable("test");
    
    Parallel.For(0, 10000, i =>
    {
        table.TrackColumnUsage($"col{i % 10}");  // ✅ Lock-free
    });
}
```

### WAL Buffer Reuse

```csharp
[Benchmark]
[GcServer(true)]
[MemoryDiagnoser]
public void WAL_BufferReuse()
{
    var db = _factory.Create("./bench_wal", "password",
        config: new DatabaseConfig { 
            UseGroupCommitWal = true 
        });
    
    db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
    
    for (int i = 0; i < 10000; i++)
    {
        db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
            new Dictionary<string, object?> { 
                { "0", i }, 
                { "1", $"Value {i}" } 
            });
    }
}
```

**Expected GC Reduction:**
```
| Metric            | Before  | After   | Improvement |
|-------------------|---------|---------|-------------|
| Gen0 Collections  | 250     | 212     | -15%        |
| Gen1 Collections  | 12      | 10      | -17%        |
| Allocated Memory  | 380 MB  | 320 MB  | -16%        |
```

---

## 📈 Comparing Configurations

### Full Configuration Comparison

```csharp
[Benchmark(Baseline = true)]
public void Config_Default() => RunWorkload(DatabaseConfig.Default);

[Benchmark]
public void Config_HighPerformance() => RunWorkload(DatabaseConfig.HighPerformance);

[Benchmark]
public void Config_ReadHeavy() => RunWorkload(DatabaseConfig.ReadHeavy);

[Benchmark]
public void Config_WriteHeavy() => RunWorkload(DatabaseConfig.WriteHeavy);

[Benchmark]
public void Config_LowMemory() => RunWorkload(DatabaseConfig.LowMemory);

private void RunWorkload(DatabaseConfig config)
{
    var db = _factory.Create($"./bench_{config.GetType().Name}", "password", 
        config: config);
    
    db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
    
    // 5000 inserts
    for (int i = 0; i < 5000; i++)
    {
        db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
            new Dictionary<string, object?> { 
                { "0", i }, 
                { "1", $"Value {i}" } 
            });
    }
    
    // 1000 queries
    for (int i = 0; i < 1000; i++)
    {
        var results = db.ExecuteQuery("SELECT * FROM test WHERE id = ?",
            new Dictionary<string, object?> { { "0", i } });
    }
}
```

---

## 🎛️ Advanced Benchmarking

### CPU-Specific SIMD Testing

```csharp
[Benchmark]
public void SIMD_Detection()
{
    Console.WriteLine($"Vector512 (AVX-512): {Vector512.IsHardwareAccelerated}");
    Console.WriteLine($"Vector256 (AVX2):    {Vector256.IsHardwareAccelerated}");
    Console.WriteLine($"Vector128 (SSE):     {Vector128.IsHardwareAccelerated}");
    
    var store = new ColumnStore<int>("test");
    for (int i = 0; i < 100000; i++)
        store.AddRow(new Dictionary<string, object> { ["val"] = i });
    
    var sum = store.Sum<int>("val");
}
```

### Memory Profiling

```csharp
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class MemoryBenchmarks
{
    [Benchmark]
    public void LowMemory_Config()
    {
        var db = _factory.Create("./bench_lowmem", "password",
            config: DatabaseConfig.LowMemory);
        
        db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
        
        for (int i = 0; i < 10000; i++)
        {
            db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
                new Dictionary<string, object?> { 
                    { "0", i }, 
                    { "1", $"Value {i}" } 
                });
        }
    }
}
```

---

## 📊 Interpreting Results

### Key Metrics

#### Mean Time
- **Lower is better**
- Measures average execution time
- Target: <50ms for 1k operations

#### Ratio
- Compared to baseline
- **<1.0 is faster** than baseline
- Example: 0.60 = 40% faster

#### Allocated Memory
- **Lower is better**
- Measures heap allocations
- Target: <1MB per 1k operations

#### Gen0/Gen1 Collections
- **Lower is better**
- GC pressure indicator
- Target: <10 per 10k operations

### Performance Goals

| Workload Type | Target Throughput | Target Latency |
|---------------|------------------|----------------|
| **Sequential Writes** | >3,000 ops/sec | <0.3ms per op |
| **Concurrent Writes** | >10,000 ops/sec | <1ms per op |
| **Index Lookups** | >50,000 ops/sec | <0.02ms per op |
| **SIMD Aggregates** | >80,000 rows/ms | <1.5ms for 100k |
| **Full Table Scan** | >100,000 rows/ms | <1ms for 100k |

---

## 🐛 Benchmark Troubleshooting

### Inconsistent Results

**Problem:** Benchmark results vary significantly

**Solutions:**
1. Disable Turbo Boost / SpeedStep
2. Close background applications
3. Increase iteration count
4. Use `[WarmupCount(10)]` attribute

```csharp
[WarmupCount(10)]
[IterationCount(20)]
[Benchmark]
public void StableBenchmark()
{
    // Your benchmark code
}
```

### OutOfMemory Exceptions

**Problem:** Benchmarks crash with OOM

**Solutions:**
1. Reduce dataset size
2. Add `[MemoryDiagnoser]` to track allocations
3. Use `LowMemory` configuration
4. Clean up between iterations

```csharp
[IterationCleanup]
public void Cleanup()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}
```

### Slow Benchmarks

**Problem:** Benchmarks take too long to run

**Solutions:**
1. Reduce iteration count
2. Use smaller datasets
3. Run specific benchmarks with `--filter`

```bash
dotnet run -c Release -- --filter *Insert* --job short
```

---

## 📋 Benchmark Checklist

Before running benchmarks:

- [ ] Build in Release mode (`-c Release`)
- [ ] Close unnecessary applications
- [ ] Disable Windows Defender / antivirus
- [ ] Ensure AC power connected (laptops)
- [ ] Wait for system to idle (CPU <10%)
- [ ] Use consistent test data
- [ ] Run warmup iterations
- [ ] Record system specifications

---

## 🔗 Related Documentation

- [Performance Optimizations](PERFORMANCE_OPTIMIZATIONS.md)
- [Adaptive WAL Batching](ADAPTIVE_WAL_BATCHING.md)
- [.NET 10 Optimizations](NET10_OPTIMIZATIONS.md)

---

*Last Updated: January 2025*
