# Comprehensive Database Benchmark Report

**Date**: 2025-12-16 22:15:58
**Platform**: Microsoft Windows NT 10.0.26200.0
**CPU Cores**: 12
**.NET Version**: 10.0.1

## Test Configuration

- Record Count: 10.000
- Query Count: 1.000
- Thread Count (Concurrent Test): 8

## Test 1: Bulk Insert Performance

Inserting 10.000 records in a single transaction:

| Database | Time (ms) | Throughput (rec/sec) | vs SQLite |
|----------|-----------|----------------------|-----------|
| SQLite               |        88 |              113.636 |     1,00x |
| LiteDB               |       344 |               29.069 |     3,91x |
| SharpCore (Enc)      |     2.596 |                3.852 |    29,50x |
| SharpCore (No Enc)   |     2.799 |                3.572 |    31,81x |

## Test 2: Indexed Lookup Performance

Performing 1.000 index lookups:

| Database | Time (ms) | Lookups/sec | Cache Hit Rate |
|----------|-----------|-------------|----------------|
| SQLite               |         7 |     142.857 |            N/A |
| LiteDB               |       166 |       6.024 |            N/A |
| SharpCore            |       247 |       4.048 |            N/A |

## Test 3: Analytical Aggregate Performance

Running SUM/AVG/MIN/MAX on 10.000 records:

| Database | SUM (ms) | AVG (ms) | MIN (ms) | MAX (ms) | Total (ms) |
|----------|----------|----------|----------|----------|------------|
| SQLite               |        2 |        1 |        0 |        0 |          3 |
| LiteDB               |       60 |       57 |       53 |       52 |        222 |
| SharpCore SIMD       |        2 |        0 |        0 |        0 |          2 |

**SIMD Speedup**: 1,50x faster than SQLite

## Test 4: Concurrent Write Performance

Inserting 10.000 records using 8 threads:

| Database | Time (ms) | Throughput (rec/sec) | Scaling Efficiency |
|----------|-----------|----------------------|--------------------|
| LiteDB               |        74 |              135.135 |             13,26x |
| SQLite               |       981 |               10.193 |              1,00x |
| SharpCore            |     2.695 |                3.710 |              0,36x |

## Test 5: Mixed Workload Performance

Workload: 5000 INSERTs + 3000 UPDATEs + 1000 SELECTs

| Database | Time (ms) | Operations/sec |
|----------|-----------|----------------|
| SQLite               |        21 |        428.571 |
| LiteDB               |       450 |         20.000 |
| SharpCore            |     2.705 |          3.327 |

## Test 6: Feature Comparison

| Feature | SQLite | LiteDB | SharpCoreDB |
|---------|--------|--------|-------------|
| **Built-in Encryption** | ❌ No | ❌ No | ✅ AES-256-GCM |
| **Pure .NET** | ❌ No (C lib) | ✅ Yes | ✅ Yes |
| **Hash Indexes (O(1))** | ❌ B-tree only | ❌ B-tree only | ✅ Yes |
| **SIMD Aggregates** | ❌ No | ❌ No | ✅ AVX-512 |
| **Adaptive WAL Batching** | ❌ No | ❌ No | ✅ Yes |
| **Query Cache** | ⚠️ Limited | ❌ No | ✅ Advanced |
| **Page Cache** | ✅ Yes | ✅ Yes | ✅ CLOCK eviction |
| **MVCC** | ⚠️ WAL mode | ❌ No | ✅ Snapshot isolation |
| **Columnar Storage** | ❌ No | ❌ No | ✅ Yes |
| **Modern C# Generics** | ❌ N/A | ⚠️ Limited | ✅ Full support |

