# Migration Guide: v1.0 Performance Optimizations

## Overview

This guide helps you migrate from older SharpCoreDB versions to v1.0 and take advantage of the new performance optimizations.

---

## 🚀 Quick Migration

### Minimal Changes (Immediate 20-30% improvement)

Just change your configuration:

```csharp
// Before
var db = factory.Create(dbPath, password);

// After - Single line change!
var db = factory.Create(dbPath, password, config: DatabaseConfig.HighPerformance);
```

**Expected Improvements:**
- 20-30% faster queries
- 20-40% better concurrent throughput
- 10-15% less memory allocations

---

## 📋 Migration Checklist

### Phase 1: Low-Risk Changes (Recommended for all)

- [ ] Update to `DatabaseConfig.HighPerformance`
- [ ] Enable hash indexes for frequently queried columns
- [ ] Use prepared statements for repeated queries
- [ ] Enable query cache (default: enabled)
- [ ] Enable page cache (default: enabled)

### Phase 2: Medium-Risk Changes (Test thoroughly)

- [ ] Switch to workload-specific config (`ReadHeavy`, `WriteHeavy`, or `LowMemory`)
- [ ] Enable `NoEncryptMode` (only if security allows)
- [ ] Tune cache sizes based on monitoring
- [ ] Enable adaptive WAL batching for concurrent writes

### Phase 3: Advanced Optimization (Production testing required)

- [ ] Benchmark with BenchmarkDotNet
- [ ] Profile memory usage
- [ ] Tune WAL batch multiplier for your workload
- [ ] Optimize page cache capacity
- [ ] Consider ColumnStore for analytics

---

## 🎯 Migration Scenarios

### Scenario 1: Web Application (Multi-tenant SaaS)

**Before:**
```csharp
var db = factory.Create(dbPath, password);
```

**After:**
```csharp
var config = new DatabaseConfig
{
    // Core performance
    NoEncryptMode = true,  // If data is encrypted at network/disk level
    
    // WAL optimization
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = true,
    WalBatchMultiplier = 128,  // 8 cores * 128 = 1024 batch size
    
    // Caching
    EnableQueryCache = true,
    QueryCacheSize = 5000,     // Cache more query plans
    EnablePageCache = true,
    PageCacheCapacity = 20000, // 80MB cache
    
    // I/O
    UseMemoryMapping = true,
    
    // Indexes
    EnableHashIndexes = true
};

var db = factory.Create(dbPath, password, config: config);

// Or use preset
var db = factory.Create(dbPath, password, 
    config: DatabaseConfig.HighPerformance);
```

**Create indexes for hot columns:**
```csharp
var usersTable = db.GetTable("users");
usersTable.CreateHashIndex("email", buildImmediately: true);
usersTable.CreateHashIndex("username", buildImmediately: true);

var ordersTable = db.GetTable("orders");
ordersTable.CreateHashIndex("customer_id", buildImmediately: true);
ordersTable.CreateHashIndex("status", buildImmediately: true);
```

**Expected Improvements:**
- 40-60% faster mixed workload
- 30-50% better concurrent throughput
- 50%+ faster lookups with indexes

---

### Scenario 2: Analytics/Reporting System

**Before:**
```csharp
var db = factory.Create(dbPath, password);

// Slow aggregates
var result = db.ExecuteQuery(
    "SELECT SUM(amount), AVG(amount) FROM transactions WHERE date >= ?",
    new Dictionary<string, object?> { { "0", startDate } });
```

**After:**
```csharp
// Use ReadHeavy preset
var db = factory.Create(dbPath, password, 
    config: DatabaseConfig.ReadHeavy);

// Same queries, but 2x faster with SIMD
var result = db.ExecuteQuery(
    "SELECT SUM(amount), AVG(amount) FROM transactions WHERE date >= ?",
    new Dictionary<string, object?> { { "0", startDate } });

// Or use ColumnStore for even better performance
var store = new ColumnStore<decimal>("transactions");
// ... load data ...
var sum = store.Sum<decimal>("amount");     // SIMD-optimized
var avg = store.Average("amount");          // 2x faster on AVX-512
```

**Expected Improvements:**
- 50-70% faster SELECT queries (large page cache)
- 2x faster aggregates with SIMD
- 10x faster with ColumnStore for analytics

---

### Scenario 3: Logging/IoT System

**Before:**
```csharp
var db = factory.Create(dbPath, password);

// High-frequency writes
Parallel.For(0, 1000, i =>
{
    db.ExecuteSQL("INSERT INTO sensor_data VALUES (?, ?, ?)",
        new Dictionary<string, object?> { 
            { "0", DateTime.UtcNow },
            { "1", sensorId },
            { "2", reading }
        });
});
```

**After:**
```csharp
// Use WriteHeavy preset
var db = factory.Create(dbPath, password, 
    config: DatabaseConfig.WriteHeavy);

// Same code, but 50%+ faster
Parallel.For(0, 1000, i =>
{
    db.ExecuteSQL("INSERT INTO sensor_data VALUES (?, ?, ?)",
        new Dictionary<string, object?> { 
            { "0", DateTime.UtcNow },
            { "1", sensorId },
            { "2", reading }
        });
});

// Or batch for even better performance
var batch = new List<string>();
for (int i = 0; i < 1000; i++)
{
    batch.Add($"INSERT INTO sensor_data VALUES ('{DateTime.UtcNow:O}', {sensorId}, {reading})");
}
db.ExecuteBatchSQL(batch);  // Single WAL commit
```

**Expected Improvements:**
- 50-70% faster concurrent writes
- 60%+ improvement with batching
- 15-25% better at 32+ threads (adaptive WAL)

---

### Scenario 4: Mobile Application

**Before:**
```csharp
var db = factory.Create(dbPath, password);
```

**After:**
```csharp
// Use LowMemory preset
var db = factory.Create(dbPath, password, 
    config: DatabaseConfig.LowMemory);
```

**Expected Changes:**
- Memory usage: 32MB → 10MB (70% reduction)
- Encryption: Enabled (security maintained)
- Performance: 90% of default (acceptable trade-off)

---

## 🔄 Breaking Changes

### None!

All v1.0 optimizations are **100% backward compatible**. Existing code continues to work without changes.

### API Changes

No breaking API changes. All new features are opt-in via configuration.

### Configuration Defaults

| Setting | Old Default | New Default | Impact |
|---------|-------------|-------------|--------|
| `UseGroupCommitWal` | false | false | No change (opt-in) |
| `EnableQueryCache` | true | true | No change |
| `EnablePageCache` | true | true | No change |
| `EnableHashIndexes` | true | true | No change |

**Recommendation:** Use `DatabaseConfig.HighPerformance` for new projects.

---

## 📊 Monitoring Migration Impact

### Before Migration - Collect Baseline

```csharp
var db = factory.Create(dbPath, password);

// Run typical workload
var sw = Stopwatch.StartNew();
RunTypicalWorkload(db);
sw.Stop();

Console.WriteLine($"Baseline: {sw.ElapsedMilliseconds}ms");

var stats = db.GetDatabaseStatistics();
Console.WriteLine($"Page Cache Hit Rate: {stats["PageCacheHitRate"]:P2}");
Console.WriteLine($"Query Cache Hit Rate: {stats["QueryCacheHitRate"]:P2}");
```

### After Migration - Compare

```csharp
var db = factory.Create(dbPath, password, 
    config: DatabaseConfig.HighPerformance);

var sw = Stopwatch.StartNew();
RunTypicalWorkload(db);
sw.Stop();

Console.WriteLine($"Optimized: {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Improvement: {((baselineMs - sw.ElapsedMilliseconds) / (double)baselineMs * 100):F1}%");

var stats = db.GetDatabaseStatistics();
Console.WriteLine($"Page Cache Hit Rate: {stats["PageCacheHitRate"]:P2}");
Console.WriteLine($"Query Cache Hit Rate: {stats["QueryCacheHitRate"]:P2}");
```

### Key Metrics to Track

1. **Throughput** (ops/sec)
   - Target: +40-60% improvement
   
2. **Latency** (ms per operation)
   - Target: -30-50% reduction
   
3. **Cache Hit Rates**
   - Page Cache: >70% (good), >85% (excellent)
   - Query Cache: >50% (good), >75% (excellent)
   
4. **Memory Usage**
   - Should be stable or slightly higher (caching)
   - LowMemory config: 70% reduction
   
5. **GC Collections**
   - Target: -10-15% Gen0 collections

---

## 🐛 Troubleshooting Migration Issues

### Issue: Performance Worse After Migration

**Possible Causes:**
1. Wrong configuration for workload
2. Cache sizes too large (thrashing)
3. Encryption disabled but not needed

**Solutions:**
```csharp
// Try different presets
var configs = new[] {
    DatabaseConfig.Default,
    DatabaseConfig.HighPerformance,
    DatabaseConfig.ReadHeavy,
    DatabaseConfig.WriteHeavy
};

foreach (var config in configs)
{
    var db = factory.Create($"./test_{config.GetType().Name}", password, 
        config: config);
    // Benchmark...
}
```

### Issue: High Memory Usage

**Solution:**
```csharp
var config = new DatabaseConfig
{
    PageCacheCapacity = 1000,     // Reduce from 10k
    QueryCacheSize = 500,          // Reduce from 2k
    BufferPoolSize = 16 * 1024 * 1024,  // Reduce to 16MB
    UseMemoryMapping = false       // Disable mmap
};
```

### Issue: Slow Concurrent Writes

**Solution:**
```csharp
var config = new DatabaseConfig
{
    UseGroupCommitWal = true,           // Enable batching
    EnableAdaptiveWalBatching = true,   // Auto-scale
    WalBatchMultiplier = 512            // Aggressive for high concurrency
};
```

---

## 📚 Additional Resources

- [Performance Optimizations Guide](PERFORMANCE_OPTIMIZATIONS.md) - Complete optimization documentation
- [Benchmark Guide](../guides/BENCHMARK_GUIDE.md) - How to measure improvements
- [Configuration Reference](PERFORMANCE_OPTIMIZATIONS.md#-choosing-the-right-configuration) - Detailed config options

---

## 🎯 Migration Timeline

### Week 1: Preparation
- [ ] Review current performance metrics
- [ ] Identify workload type (read/write/mixed)
- [ ] Set up benchmarking infrastructure
- [ ] Test in development environment

### Week 2: Testing
- [ ] Apply `HighPerformance` config in staging
- [ ] Run load tests
- [ ] Monitor metrics (throughput, latency, memory)
- [ ] Fine-tune cache sizes

### Week 3: Optimization
- [ ] Create indexes for hot columns
- [ ] Convert to prepared statements
- [ ] Batch write operations
- [ ] Measure SIMD effectiveness

### Week 4: Production
- [ ] Deploy to canary servers (10% traffic)
- [ ] Monitor for 48 hours
- [ ] Gradual rollout (25%, 50%, 100%)
- [ ] Final performance validation

---

*Last Updated: January 2026 - v1.0.4 Data Integrity Release*
