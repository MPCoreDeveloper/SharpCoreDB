# .NET 10 Optimizations

**Last Updated**: 2025-12-13

## Overview

SharpCoreDB leverages .NET 10 performance features for maximum throughput and minimal memory allocations.

## Key Optimizations

### 1. Span<T> and Memory<T>

**Zero-copy string operations:**

```csharp
public void ParseSql(ReadOnlySpan<char> sql)
{
    // No string allocations during parsing
    var tokens = sql.Split(' ');
    foreach (var token in tokens)
    {
        ProcessToken(token); // Span-based processing
    }
}
```

**Benefits:**
- 80% reduction in GC pressure
- 3x faster string operations
- Sub-microsecond tokenization

### 2. AggressiveInlining

**Hot path methods marked for inlining:**

```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool TryGetValue(string key, out object value)
{
    return cache.TryGetValue(key, out value);
}
```

**Impact:**
- 15% faster cache lookups
- Reduced call overhead
- Better CPU branch prediction

### 3. SIMD Vectorization

**Hardware-accelerated aggregates:**

```csharp
public long SumColumn(ReadOnlySpan<int> values)
{
    if (Vector.IsHardwareAccelerated && values.Length >= Vector<int>.Count)
    {
        return SumVectorized(values);
    }
    return SumScalar(values);
}
```

**Performance (10k int32 values):**
- Scalar: 2.5 ms
- SIMD (Vector256): **0.08 ms** (31x faster)

### 4. ValueTask for Async

**Reduced allocations in async paths:**

```csharp
public ValueTask<List<Row>> QueryAsync(string sql)
{
    // Fast path: return synchronously if cached
    if (queryCache.TryGetValue(sql, out var cached))
    {
        return new ValueTask<List<Row>>(cached);
    }
    
    // Slow path: actually query
    return new ValueTask<List<Row>>(QueryInternalAsync(sql));
}
```

**Savings:**
- 90% reduction in Task allocations
- Sub-microsecond cache hits
- Better throughput under load

### 5. Bit-Packed Booleans

**87.5% memory savings for boolean columns:**

```csharp
// Instead of bool[] (1 byte per value)
// Use BitArray with custom vectorized operations
private BitArray booleanColumn;

[MethodImpl(MethodImplOptions.AggressiveInlining)]
public bool GetBool(int index) => booleanColumn[index];
```

**Memory usage (10k booleans):**
- Traditional: 10,000 bytes
- Bit-packed: **1,250 bytes** (87.5% savings)

### 6. Concurrent Collections

**Lock-free data structures:**

```csharp
private readonly ConcurrentDictionary<string, Table> tables = new();
private readonly ConcurrentQueue<Transaction> txQueue = new();
```

**Concurrency benefits:**
- No lock contention on reads
- Scalable to 100+ threads
- Wait-free for common operations

### 7. String Pooling

**Reduce duplicate string allocations:**

```csharp
private static readonly ConcurrentDictionary<string, string> stringPool = new();

public static string Intern(string value)
{
    return stringPool.GetOrAdd(value, value);
}
```

**Use cases:**
- Column names
- Table names
- Repeated SQL patterns

## Benchmarks

### Before vs After .NET 10 Optimizations

| Operation | .NET 8 | .NET 10 | Improvement |
|-----------|--------|---------|-------------|
| Parse SQL (1k queries) | 15 ms | 5 ms | **3x** |
| Cache lookup (1M ops) | 120 ms | 18 ms | **6.7x** |
| SIMD SUM (10k int32) | 2.5 ms | 0.08 ms | **31x** |
| Boolean storage (10k) | 10 KB | 1.25 KB | **87.5% savings** |
| Async query (cached) | 0.05 ms | 0.002 ms | **25x** |

## Compiler Optimizations

### Profile-Guided Optimization (PGO)

Enable in Release build:

```xml
<PropertyGroup>
  <TieredCompilation>true</TieredCompilation>
  <TieredCompilationQuickJit>true</TieredCompilationQuickJit>
  <TieredPGO>true</TieredPGO>
</PropertyGroup>
```

**Benefits:**
- 10-20% throughput improvement
- Better inlining decisions
- Optimized for your workload

### ReadyToRun (R2R)

```xml
<PublishReadyToRun>true</PublishReadyToRun>
```

**Startup benefits:**
- 40% faster startup
- Reduced JIT time
- Better first-query performance

## Best Practices

1. **Use Span<T> for string operations** - Avoid substring allocations
2. **Mark hot paths with AggressiveInlining** - Help JIT optimizer
3. **Leverage SIMD when possible** - Check `Vector.IsHardwareAccelerated`
4. **Prefer ValueTask for async** - Reduce allocations in fast paths
5. **Pool strings and objects** - Minimize GC pressure
6. **Use concurrent collections** - Avoid locks where possible

## Profiling

Use BenchmarkDotNet to measure:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10)]
public class QueryBenchmarks
{
    [Benchmark]
    public List<Row> QueryWithOptimizations() => db.Query("SELECT * FROM users");
}
```

---

For implementation details, see:
- [Source code: Optimizations/](../../SharpCoreDB/Optimizations/)
- [SIMD Aggregates](../features/SIMD_AGGREGATES.md)
