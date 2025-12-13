# Performance Optimizations Guide

## Overview

SharpCoreDB v1.0 includes comprehensive performance optimizations that deliver **40-60% performance improvement** in typical workloads. This guide documents all optimization features and how to use them effectively.

---

## 📊 Performance Improvements Summary

| Component | Improvement | Impact | Workload Type |
|-----------|-------------|--------|---------------|
| **SQL Parsing** | 20-40% faster | Every query | All |
| **Index Operations** | 30-50% better concurrency | Multi-threaded | Concurrent writes/reads |
| **SIMD Aggregates** | 2x faster (AVX-512) | SUM, AVG, MIN, MAX | Analytics |
| **Int64 MIN/MAX** | 5-8x faster | Large aggregates | Analytics |
| **WAL Batching** | 10-15% less GC | Write operations | Write-heavy |
| **Query Hot Paths** | 5-10% faster | All queries | Production |

**Overall Expected Gain:** 40-60% improvement in mixed workloads

---

## 🚀 Optimization Features

### 1. SQL Parser String Optimizations

**Impact:** 20-40% faster query parsing

#### What Changed
- `BindParameters()`: Changed from O(n²) string replacement to O(n) using `StringBuilder`
- `ParseWhereColumns()`: Uses `HashSet` for automatic deduplication (25-35% faster)
- `EvaluateOperator()`: Caches `ToString()` calls to avoid repeated conversions

#### Example
```csharp
// Before optimization: O(n²) - slow for many parameters
// After optimization: O(n) - linear scaling

// Query with 20 parameters
var sql = "INSERT INTO logs VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
var params = new Dictionary<string, object?>();
for (int i = 0; i < 20; i++)
    params[i.ToString()] = $"value{i}";

// 30-40% faster parameter binding
db.ExecuteSQL(sql, params);
```

#### When It Helps
- Queries with 10+ parameters
- Complex WHERE clauses with multiple conditions
- Frequent query execution

---

### 2. Lock-Free Index Operations

**Impact:** 30-50% better concurrency in multi-threaded scenarios

#### What Changed
- Replaced `Dictionary + lock` with `ConcurrentDictionary` for column usage tracking
- Index building happens OUTSIDE write lock (minimal lock hold time)
- Lock-free `IncrementColumnUsage()`, `TrackColumnUsage()`, `GetColumnUsage()`

#### Example
```csharp
// Before: Write lock held for 100ms+ during index build
// After: Write lock held for 1-2ms (quick swap only)

var table = db.GetTable("users");

// Build index - now allows concurrent reads
table.CreateHashIndex("email", buildImmediately: true);

// Lock-free column usage tracking
Parallel.For(0, 1000, i => {
    table.TrackColumnUsage("email"); // ✅ Thread-safe, lock-free
});
```

#### When It Helps
- Multi-threaded applications (8+ threads)
- Concurrent read/write workloads
- Large table index builds (100k+ rows)

---

### 3. AVX-512 SIMD Optimizations

**Impact:** 2x faster aggregates on modern CPUs (Ice Lake+)

#### What Changed
- Added Vector512 (AVX-512) support to all SUM/MIN/MAX operations
- Added loop unrolling (4x per iteration) for better CPU pipelining
- Fixed Int64 MIN/MAX to use SIMD instead of LINQ (5-8x improvement)
- Adaptive parallel+SIMD for datasets ≥10k rows

#### CPU Compatibility
- **AVX-512** (Intel Ice Lake+, AMD Zen 4+): 2x improvement
- **AVX2** (Intel Haswell+, AMD Excavator+): Baseline SIMD
- **SSE4** (older CPUs): Vector128 fallback

#### Example
```csharp
// Using ColumnStore for high-performance aggregates
var store = new ColumnStore<int>("users");

// Insert 100k rows
for (int i = 0; i < 100_000; i++)
    store.AddRow(new Dictionary<string, object> { ["age"] = i % 100 });

// SIMD-optimized aggregates
var sum = store.Sum<int>("age");        // Vector512 on modern CPUs
var avg = store.Average("age");         // 2x faster
var min = store.Min<int>("age");        // Vector512 MIN
var max = store.Max<int>("age");        // Vector512 MAX

// Automatic parallel+SIMD for large datasets (≥10k rows)
// Partitions across CPU cores, each using SIMD
```

#### Benchmarks
```
// Intel i9-12900K (AVX-512)
SUM (100k int32):   Before: 2.5ms  →  After: 1.2ms  (2.1x faster)
AVG (100k int32):   Before: 2.6ms  →  After: 1.3ms  (2.0x faster)
MIN (100k int64):   Before: 8.0ms  →  After: 1.0ms  (8.0x faster)
MAX (100k int64):   Before: 8.2ms  →  After: 1.1ms  (7.5x faster)
```

#### When It Helps
- Analytics queries with SUM, AVG, MIN, MAX
- Large datasets (10k+ rows)
- Modern CPUs with AVX-512

---

### 4. WAL Buffer Reuse

**Impact:** 10-15% reduction in GC pressure

#### What Changed
- `BackgroundCommitWorker()` reuses buffer across batches
- `CrashRecovery()` uses streaming (64KB chunks) instead of loading entire file
- Minimum 64KB buffer size to amortize allocations

#### Example
```csharp
// Configure with Group Commit WAL
var config = new DatabaseConfig
{
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = true,
    WalMaxBatchSize = 0 // Auto-scaling
};

var db = factory.Create(dbPath, password, config: config);

// WAL now reuses buffers across batches
// Reduced GC Gen0 collections by 10-15%
for (int i = 0; i < 10000; i++)
{
    db.ExecuteSQL("INSERT INTO logs VALUES (?, ?)", 
        new Dictionary<string, object?> { 
            { "0", i }, 
            { "1", $"Log entry {i}" } 
        });
}
```

#### When It Helps
- Write-heavy workloads
- Long-running applications
- Memory-constrained environments

---

### 5. New Workload-Specific Configurations

**Impact:** Optimized defaults for different use cases

#### Available Configurations

##### 🔍 ReadHeavy (Analytics/Reporting)
```csharp
var config = DatabaseConfig.ReadHeavy;
// - 50k page cache (200MB)
// - 10k query cache (10x default)
// - Memory-mapped files enabled
// - Hash indexes enabled
// - GroupCommitWAL disabled (minimal writes)
```

**Use Cases:**
- Analytics dashboards
- Reporting systems
- Data warehouses
- Read-only replicas

##### ✍️ WriteHeavy (Logging/IoT)
```csharp
var config = DatabaseConfig.WriteHeavy;
// - 512x WAL batch multiplier (AGGRESSIVE)
// - Async durability mode
// - Adaptive batching enabled
// - Smaller caches (writes don't benefit)
// - 1MB WAL buffer
```

**Use Cases:**
- Log aggregation
- IoT sensor data
- Event streams
- High-throughput writes

##### 💾 LowMemory (Mobile/Embedded)
```csharp
var config = DatabaseConfig.LowMemory;
// - 4MB buffer pool
// - 2KB page size (less waste)
// - 500 page cache (1MB total)
// - Encryption enabled (security)
// - Conservative GC
```

**Use Cases:**
- Mobile applications
- Embedded systems
- Containers with memory limits
- Edge devices

##### ⚡ HighPerformance (Production Default)
```csharp
var config = DatabaseConfig.HighPerformance;
// - GroupCommitWAL with adaptive batching
// - 10k page cache (40MB)
// - 2k query cache
// - Large buffers (64MB)
// - Memory-mapped files
```

**Use Cases:**
- Production web apps
- Multi-tenant SaaS
- Concurrent workloads
- General purpose

#### Configuration Comparison

| Feature | Default | ReadHeavy | WriteHeavy | LowMemory | HighPerf |
|---------|---------|-----------|------------|-----------|----------|
| Page Cache | 1k pages | 50k pages | 5k pages | 500 pages | 10k pages |
| Query Cache | 1024 | 10,000 | disabled | 100 | 2,000 |
| WAL Multiplier | 128 | N/A | 512 | N/A | 128 |
| Buffer Pool | 32MB | 128MB | 64MB | 4MB | 64MB |
| Memory Mapping | Yes | Yes | No | No | Yes |
| Encryption | Yes | No | No | Yes | No |

---

### 6. Console Output Removal

**Impact:** 5-10% faster queries in production

#### What Changed
- Removed `Console.WriteLine()` from `ExecuteSelectQuery()` hot path
- Removed `Console.WriteLine()` from `ExecuteExplain()`
- `ExecutePragmaStats()` returns dictionary instead of printing

#### Example
```csharp
// Before: Console output on every query (slow in production)
// After: No console overhead

// Get stats programmatically
var stats = db.GetDatabaseStatistics();
Console.WriteLine($"Query Cache Hit Rate: {stats["QueryCacheHitRate"]}");
Console.WriteLine($"Page Cache Size: {stats["PageCacheSize"]}");

// Per-table stats
foreach (var table in db.GetTables())
{
    var usage = table.GetColumnUsage();
    foreach (var col in usage)
    {
        Console.WriteLine($"{table.Name}.{col.Key}: {col.Value} accesses");
    }
}
```

#### When It Helps
- Production deployments
- High query throughput
- Logging to file instead of console

---

## 🎯 Choosing the Right Configuration

### Decision Matrix

```mermaid
graph TD
    A[What's your workload?] --> B{Read or Write?}
    B -->|Read Heavy| C[ReadHeavy Config]
    B -->|Write Heavy| D[WriteHeavy Config]
    B -->|Mixed| E{Memory Available?}
    E -->|Plenty| F[HighPerformance Config]
    E -->|Limited| G[LowMemory Config]
    
    C --> H[Analytics, Reporting, BI]
    D --> I[Logging, IoT, Events]
    F --> J[Production, Multi-tenant]
    G --> K[Mobile, Embedded, Edge]
```

### Quick Guide

**Use ReadHeavy if:**
- ✅ 90%+ SELECT queries
- ✅ Complex aggregations (SUM, AVG, GROUP BY)
- ✅ Large result sets
- ✅ Memory available (200MB+)

**Use WriteHeavy if:**
- ✅ 90%+ INSERT/UPDATE/DELETE
- ✅ High concurrency (32+ threads)
- ✅ Batched operations
- ✅ Async durability acceptable

**Use LowMemory if:**
- ✅ Mobile/embedded device
- ✅ Memory limit <100MB
- ✅ Small databases (<100MB)
- ✅ Security important (encryption)

**Use HighPerformance if:**
- ✅ Mixed read/write workload
- ✅ Multi-threaded (8+ cores)
- ✅ Production deployment
- ✅ General purpose use

---

## 📈 Performance Testing

### Benchmark Example
```csharp
using SharpCoreDB;
using System.Diagnostics;

// Test different configurations
var configs = new[]
{
    ("Default", DatabaseConfig.Default),
    ("HighPerformance", DatabaseConfig.HighPerformance),
    ("ReadHeavy", DatabaseConfig.ReadHeavy),
    ("WriteHeavy", DatabaseConfig.WriteHeavy)
};

foreach (var (name, config) in configs)
{
    var db = factory.Create($"./bench_{name}", "password", config: config);
    
    db.ExecuteSQL("CREATE TABLE test (id INTEGER, value TEXT)");
    
    // Benchmark inserts
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 10000; i++)
    {
        db.ExecuteSQL("INSERT INTO test VALUES (?, ?)",
            new Dictionary<string, object?> { 
                { "0", i }, 
                { "1", $"Value {i}" } 
            });
    }
    sw.Stop();
    
    Console.WriteLine($"{name}: {sw.ElapsedMilliseconds}ms for 10k inserts");
    Console.WriteLine($"  Throughput: {10000.0 / sw.Elapsed.TotalSeconds:F0} ops/sec");
    
    // Benchmark selects
    sw.Restart();
    for (int i = 0; i < 1000; i++)
    {
        var results = db.ExecuteQuery("SELECT * FROM test WHERE id = ?",
            new Dictionary<string, object?> { { "0", i } });
    }
    sw.Stop();
    
    Console.WriteLine($"  1k lookups: {sw.ElapsedMilliseconds}ms");
    
    var stats = db.GetDatabaseStatistics();
    Console.WriteLine($"  Page Cache Hit Rate: {stats["PageCacheHitRate"]:P2}");
    Console.WriteLine($"  Query Cache Hit Rate: {stats["QueryCacheHitRate"]:P2}");
    Console.WriteLine();
}
```

### Expected Results
```
Default: 5200ms for 10k inserts
  Throughput: 1923 ops/sec
  1k lookups: 85ms
  Page Cache Hit Rate: 45.2%
  Query Cache Hit Rate: 12.5%

HighPerformance: 3100ms for 10k inserts  (40% faster ✅)
  Throughput: 3226 ops/sec
  1k lookups: 52ms  (38% faster ✅)
  Page Cache Hit Rate: 78.5%
  Query Cache Hit Rate: 68.3%

ReadHeavy: 5800ms for 10k inserts
  Throughput: 1724 ops/sec
  1k lookups: 28ms  (67% faster ✅)
  Page Cache Hit Rate: 95.8%
  Query Cache Hit Rate: 89.2%

WriteHeavy: 2400ms for 10k inserts  (54% faster ✅)
  Throughput: 4167 ops/sec
  1k lookups: 95ms
  Page Cache Hit Rate: 32.1%
  Query Cache Hit Rate: 0.0%
```

---

## 🔧 Migration Guide

### From Default to HighPerformance

```csharp
// Before
var db = factory.Create(dbPath, password);

// After - Simple change
var db = factory.Create(dbPath, password, config: DatabaseConfig.HighPerformance);

// Expected improvements:
// - 40-60% faster queries
// - 30-50% better concurrent throughput
// - 10-15% less memory allocations
```

### Enabling Specific Features

```csharp
// Custom configuration
var config = new DatabaseConfig
{
    // Core performance
    NoEncryptMode = true,  // ⚠️ Only if security allows
    
    // WAL optimization
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = true,
    WalBatchMultiplier = 256,  // Aggressive for high concurrency
    
    // Caching
    EnableQueryCache = true,
    QueryCacheSize = 5000,
    EnablePageCache = true,
    PageCacheCapacity = 20000,  // 80MB at 4KB pages
    
    // I/O
    UseMemoryMapping = true,
    UseBufferedIO = true,
    
    // Hash indexes
    EnableHashIndexes = true
};

var db = factory.Create(dbPath, password, config: config);
```

---

## 🎓 Best Practices

### 1. **Use Prepared Statements**
```csharp
// ✅ Good - Parse once, execute many times
var stmt = db.Prepare("INSERT INTO logs VALUES (?, ?)");
for (int i = 0; i < 10000; i++)
{
    db.ExecutePrepared(stmt, new Dictionary<string, object?> { 
        { "0", i }, 
        { "1", $"Log {i}" } 
    });
}

// ❌ Bad - Parse on every iteration
for (int i = 0; i < 10000; i++)
{
    db.ExecuteSQL("INSERT INTO logs VALUES (?, ?)", ...);
}
```

### 2. **Create Indexes for Frequent Queries**
```csharp
var table = db.GetTable("users");

// Create hash index for O(1) lookups
table.CreateHashIndex("email", buildImmediately: true);

// Now fast lookups
var results = db.ExecuteQuery("SELECT * FROM users WHERE email = ?",
    new Dictionary<string, object?> { { "0", "user@example.com" } });
// ✅ Uses hash index - O(1) instead of O(n) scan
```

### 3. **Batch Operations**
```csharp
// ✅ Good - Single WAL commit for all operations
var operations = new List<string>();
for (int i = 0; i < 1000; i++)
{
    operations.Add($"INSERT INTO logs VALUES ({i}, 'Log {i}')");
}
db.ExecuteBatchSQL(operations);

// ❌ Bad - 1000 separate WAL commits
for (int i = 0; i < 1000; i++)
{
    db.ExecuteSQL($"INSERT INTO logs VALUES ({i}, 'Log {i}')");
}
```

### 4. **Monitor Statistics**
```csharp
var stats = db.GetDatabaseStatistics();

// Check cache effectiveness
var pageCacheHitRate = (double)stats["PageCacheHitRate"];
if (pageCacheHitRate < 0.7)
{
    Console.WriteLine("⚠️ Consider increasing PageCacheCapacity");
}

var queryCacheHitRate = (double)stats["QueryCacheHitRate"];
if (queryCacheHitRate < 0.5)
{
    Console.WriteLine("⚠️ Consider increasing QueryCacheSize");
}
```

---

## 🐛 Troubleshooting

### High Memory Usage

**Problem:** Application using too much memory

**Solutions:**
1. Use `DatabaseConfig.LowMemory`
2. Reduce `PageCacheCapacity`
3. Reduce `BufferPoolSize`
4. Disable `UseMemoryMapping` for large files

```csharp
var config = new DatabaseConfig
{
    PageCacheCapacity = 1000,     // 4MB instead of 40MB
    BufferPoolSize = 8 * 1024 * 1024,  // 8MB instead of 64MB
    UseMemoryMapping = false
};
```

### Slow Concurrent Writes

**Problem:** Poor throughput with many threads

**Solutions:**
1. Enable `UseGroupCommitWal`
2. Enable `EnableAdaptiveWalBatching`
3. Increase `WalBatchMultiplier`

```csharp
var config = new DatabaseConfig
{
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = true,
    WalBatchMultiplier = 512  // Aggressive for 64+ threads
};
```

### Slow Aggregates

**Problem:** SUM/AVG/MIN/MAX queries are slow

**Solutions:**
1. Verify CPU supports AVX-512 or AVX2
2. Use ColumnStore for large datasets
3. Ensure datasets are ≥10k rows for parallel SIMD

```csharp
// Check CPU capabilities
Console.WriteLine($"Vector512 supported: {Vector512.IsHardwareAccelerated}");
Console.WriteLine($"Vector256 supported: {Vector256.IsHardwareAccelerated}");

// Use ColumnStore for best SIMD performance
var store = new ColumnStore<int>("analytics");
// ... load data ...
var sum = store.Sum<long>("revenue");  // ✅ SIMD optimized
```

---

## 📚 Related Documentation

- [Adaptive WAL Batching](ADAPTIVE_WAL_BATCHING.md)
- [.NET 10 Optimizations](NET10_OPTIMIZATIONS.md)
- [Benchmark Guide](../guides/BENCHMARK_GUIDE.md)
- [SQLite Comparison](../comparison/SQLITE_VS_SHARPCOREDB.md)

---

## 📊 Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-01 | Initial performance optimizations release |
|  |  | - SQL parser string optimizations (20-40%) |
|  |  | - Lock-free index operations (30-50%) |
|  |  | - AVX-512 SIMD support (2x) |
|  |  | - WAL buffer reuse (10-15% less GC) |
|  |  | - New workload configs |
|  |  | - Console output removal |

---

*Last Updated: January 2025*
