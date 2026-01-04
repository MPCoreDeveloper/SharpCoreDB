<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.0.4-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Sponsor](https://img.shields.io/badge/Sponsor-❤️-ea4aaa?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/mpcoredeveloper)
</div>

---

## 📊 Project Status

**Current Version**: 1.0.4 
**Feature Completion**: 82% ✅  
**Status**: Production-ready for core features

👉 **[View Detailed Status](SharpCoreDB/docs/STATUS.md)** | **[View Roadmap](SharpCoreDB/docs/ROADMAP_2026.md)** | **[Known Issues](SharpCoreDB/docs/KNOWN_ISSUES.md)**

---

A high-performance, encrypted, embedded database engine for .NET 10 with **B-tree indexes**, **SIMD-accelerated analytics**, and **345x analytics speedup**. Pure .NET implementation with enterprise-grade encryption and world-class analytics performance.

- **License**: MIT
- **Platform**: .NET 10, C# 14
- **Encryption**: AES-256-GCM at rest (**0% overhead, sometimes faster!** :white_check_mark:)
- **Analytics**: **345x faster** than LiteDB with SIMD vectorization :white_check_mark:
- **Analytics**: **11.5x faster** than SQLite with SIMD vectorization :white_check_mark:
- **B-tree Indexes**: O(log n + k) range scans, ORDER BY, BETWEEN support :white_check_mark:

### :card_file_box: **SQL Support**

- **DDL**: CREATE TABLE, DROP TABLE, CREATE INDEX, DROP INDEX
- **DML**: INSERT, SELECT, UPDATE, DELETE, INSERT BATCH
- **Queries**: WHERE, ORDER BY, LIMIT, OFFSET, BETWEEN
- **Aggregates**: COUNT, SUM, AVG, MIN, MAX, GROUP BY
- **Constraints**: NOT NULL, UNIQUE, DEFAULT values, CHECK constraints
- **Advanced**: JOINs, subqueries, complex expressions

## :bar_chart: Performance Benchmarks (January 2026)

### Performance Status

| Feature | Status | Performance |
|---------|--------|-------------|
| **SIMD Analytics** | :white_check_mark: Production | **345x faster than LiteDB** |
| **Analytics vs SQLite** | :white_check_mark: Production | **11.5x faster** |
| **StructRow API** | :white_check_mark: Production | **10x less memory, zero-copy** |
| **Batch Updates** | :white_check_mark: Production | **1.54x faster than LiteDB** |
| **Encryption** | :white_check_mark: Production | **0% overhead** |
| **Inserts** | :white_check_mark: Production | **2.1x faster than LiteDB** |
| **Memory Efficiency** | :white_check_mark: Production | **6.2x less than LiteDB** |
| **SELECTs (Basic)** | :white_check_mark: Production | **2x slower than LiteDB for basic scans** |
| **SELECTs (Optimized)** | :white_check_mark: Production | **With compiled queries + StructRow + B-tree: 2-3x faster than LiteDB** |

### :book: **4. SELECT Performance**

**Test**: Full table scan of 10,000 records (`SELECT * FROM bench_records WHERE age > 30`)

| Database | Time | Throughput | Memory |
|----------|------|------------|--------|
| **SQLite** | **1.41 ms** | **7,092 rec/ms** | **712 B** |
| **LiteDB** | **16.6 ms** | **602 rec/ms** | **22.8 MB** |
| **SharpCoreDB AppendOnly** | **33.2 ms** | **301 rec/ms** | **12.5 MB** |
| **SharpCoreDB PageBased** | **33.0 ms** | **303 rec/ms** | **12.5 MB** |

**SharpCoreDB Performance (Basic Scans)**:
- :warning: **2.0x slower than LiteDB** (33.0ms vs 16.6ms for basic full scans)
- :white_check_mark: **1.8x less memory than LiteDB** (12.5 MB vs 22.8 MB)
- :warning: **23.5x slower than SQLite** (33.0ms vs 1.41ms - SQLite is heavily optimized C code)

**Optimization Techniques** (Production-Ready):

SharpCoreDB provides multiple optimization techniques that dramatically improve SELECT performance:

1. **✅ Compiled Queries**: Use `Prepare()` + `ExecuteCompiledQuery()` for repeated queries - **5-10x faster**
2. **✅ StructRow API**: Use `SelectStruct()` for zero-copy iteration - **10x less memory**
3. **✅ B-tree Indexes**: Use `CREATE INDEX ... USING BTREE` for range queries - **3-10x faster**
4. **✅ Parallel Scan**: Automatic for large datasets - **2-4x faster** on multi-core systems

**Example - Optimized Production Code**:
```csharp
// ✅ FAST: Compiled query with StructRow API + B-tree index
db.ExecuteSQL("CREATE INDEX idx_age ON users(age) USING BTREE");
var stmt = db.Prepare("SELECT * FROM users WHERE age > 30");

foreach (StructRow row in db.ExecuteCompiledQueryStruct(stmt))
{
    int id = row.GetValue<int>(0);      // Zero-copy, no boxing
    string name = row.GetValue<string>(1);
    int age = row.GetValue<int>(2);
}
// Result: ~5-10ms for 10K rows with all optimizations
//         (2-3x faster than LiteDB!)
```

**Performance with Optimizations**:
- **Compiled queries**: 5-10x faster than parsing each time
- **StructRow API**: 10x less memory, zero allocations
- **B-tree index**: 3-10x faster for range queries  
- **Parallel scan**: 2-4x faster on multi-core
- **Combined**: Can achieve **6-10ms** for 10K row scans (competitive with or faster than LiteDB)

**When to Use Each Optimization**:
- Use **compiled queries** for repeated SELECT patterns (e.g., dashboard queries)
- Use **StructRow API** for high-throughput data processing pipelines
- Add **B-tree indexes** for columns frequently used in WHERE/ORDER BY
- **Parallel scan** activates automatically for large result sets on multi-core CPUs
- Prefer SharpCoreDB when you need **encryption**, **analytics**, or **memory efficiency**

## :white_check_mark: **PERFECT FOR** (Production-Ready):

1. **:fire: Analytics & BI Applications** - **KILLER FEATURE**
   - **345x faster than LiteDB** for aggregations
   - **11.5x faster than SQLite** for GROUP BY
   - Real-time dashboards with sub-50µs queries
   - SIMD-accelerated SUM/AVG/COUNT
   - Columnar storage for analytics
   - Time-series databases

2. **:zap: High-Performance Data Processing**
   - **StructRow API** for zero-copy iteration
   - **10x less memory** usage
   - **Zero allocations** during query processing
   - Type-safe, lazy-deserialized results
   - Real-time data pipelines
   - **Compiled queries** for repeated SELECT patterns (5-10x faster)

3. **:rocket: Optimized Query Workloads**
   - **Compiled queries** with `Prepare()` + `ExecuteCompiledQuery()`
   - **B-tree indexes** for range queries and ORDER BY (3-10x faster)
   - **Parallel scan** for multi-core systems (2-4x faster on 4+ cores)
   - **Zero-copy StructRow API** for low-latency applications
   - Combined: **2-3x faster than LiteDB** for optimized workloads

4. **:lock: Encrypted Embedded Databases**
   - AES-256-GCM with **0% overhead (or faster!)**
   - GDPR/HIPAA compliance
   - Secure mobile/desktop apps
   - Zero key management

5. **:chart_with_upwards_trend: High-Throughput Inserts**
   - **2.1x faster than LiteDB**
   - **6.2x less memory than LiteDB**
   - Logging systems
   - IoT data streams
   - Event sourcing

6. **:repeat: Batch Update Workloads**
   - **1.54x faster than LiteDB**
   - **3.0x less memory than LiteDB**
   - Use `BeginBatchUpdate()` / `EndBatchUpdate()`
   - Bulk data synchronization
