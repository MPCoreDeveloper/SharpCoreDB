# SharpCoreDB — Comparative Benchmark Report

**Date:** 2026-03-06  
**SharpCoreDB Version:** 1.7.0 (.NET 10, C# 14)  
**Test Machine:** Intel i7-10850H (6C/12T), 32 GB RAM, Windows 10, NVMe SSD

---

## Executive Summary

SharpCoreDB was benchmarked against four competing embedded databases across two categories:

| Category | Competitors | SharpCoreDB Result |
|---|---|---|
| **Document CRUD** | SQLite, LiteDB, BLite | 🥇 Fastest INSERT, competitive UPDATE/DELETE |
| **Vector Search** | Zvec (Alibaba, C++) | Competitive for managed .NET; Zvec faster on raw throughput |

**Key finding:** SharpCoreDB delivers the fastest batch INSERT performance of all tested databases (202K ops/sec), while SQLite remains king for point reads and updates. BLite 2.0.2 could not be benchmarked due to severe developer experience issues.

---

## Part 1: Document CRUD Benchmark

### Test Configuration

| Parameter | Value |
|---|---|
| **Inserts** | 100,000 documents (10K batches) |
| **Reads** | 10,000 point queries by name field |
| **Updates** | 10,000 row updates (batched) |
| **Deletes** | 10,000 row deletes (batched) |
| **Schema** | 5 columns: name (TEXT), email (TEXT), age (INTEGER), score (REAL), data (TEXT) |
| **Mode** | All databases: WAL mode, optimal batch settings |

### Results

| Database | Version | INSERT ops/sec | READ ops/sec | UPDATE ops/sec | DELETE ops/sec |
|---|---|---|---|---|---|
| **SharpCoreDB** | 1.7.0 | **202,222** 🥇 | 6,102 | 8,411 | 7,203 |
| **SQLite** | 10.0.3 | 167,363 | **96,724** 🥇 | **252,482** 🥇 | **378,961** 🥇 |
| **LiteDB** | 5.0.21 | 91,845 | 13,317 | 9,218 | 13,907 |
| **BLite** | 2.0.2 | ❌ DNF | ❌ DNF | ❌ DNF | ❌ DNF |

> DNF = Did Not Finish — could not successfully run benchmark (see [BLite Analysis](#blite-202--developer-experience-analysis) below)

### Time Breakdown (seconds)

| Database | INSERT (100K) | READ (10K) | UPDATE (10K) | DELETE (10K) |
|---|---|---|---|---|
| **SharpCoreDB** | 0.49s | 1.64s | 1.19s | 1.39s |
| **SQLite** | 0.60s | 0.10s | 0.04s | 0.03s |
| **LiteDB** | 1.09s | 0.75s | 1.08s | 0.72s |

### Analysis

#### SharpCoreDB — Fastest Batch Inserts

SharpCoreDB's `InsertBatch()` API achieves **202K inserts/sec**, beating SQLite by 21% and LiteDB by 120%. This is powered by:
- SQL-free direct insert path (bypasses SQL parsing entirely)
- Prepared statement caching for repeated schemas
- Batched WAL flushing (single fsync per batch instead of per-row)
- StreamingRowEncoder for zero-allocation bulk inserts

**Trade-off:** Read/Update/Delete performance is behind SQLite. SharpCoreDB's current read path (10K individual `ExecuteSQL` SELECT queries) is bottlenecked by SQL parsing overhead per query. SQLite has 20+ years of B-tree optimization and compiled C native code.

#### SQLite — The Gold Standard for Point Operations

SQLite dominates READ (97K ops/sec), UPDATE (252K ops/sec), and DELETE (379K ops/sec). This is expected — SQLite is a 20+ year C library with:
- Native compiled code (no managed runtime overhead)
- Highly optimized B-tree with page-level caching
- Prepared statement API (parameterized queries avoid reparsing)
- WAL mode with shared memory for concurrent readers

#### LiteDB — Solid .NET Document Database

LiteDB shows balanced performance across all operations. As a pure .NET BSON document database, it's a fair peer comparison to SharpCoreDB:
- INSERT: SharpCoreDB is **2.2x faster** (202K vs 92K)
- READ: LiteDB is **2.2x faster** (13K vs 6K) — LiteDB's FindById uses direct B-tree lookup
- UPDATE: Comparable (9.2K vs 8.4K)
- DELETE: LiteDB is **1.9x faster** (14K vs 7.2K)

**Overall vs LiteDB:** SharpCoreDB wins bulk inserts decisively; LiteDB wins point reads. Both are competitive on updates.

---

### BLite 2.0.2 — Developer Experience Analysis

BLite was intended as a fourth benchmark competitor. It is positioned as a "zero-allocation embedded document database" for .NET with BSON, B-tree indexing, HNSW vector search, and LINQ support. On paper, it's an impressive project.

**However, we were unable to successfully run any BLite benchmarks.** This section documents the issues encountered, because developer experience is just as important as raw performance.

#### What Went Wrong

| # | Issue | Details |
|---|---|---|
| 1 | **Source generator failure** | BLite's `DocumentDbContext` requires a compile-time source generator (`BLite.SourceGenerators`) to emit `InitializeCollections()`. The generator silently produced no output, leaving the `Docs` property `null` at runtime → `NullReferenceException`. |
| 2 | **API documentation mismatch** | The README documents `b.Set("field", value)` for `BsonDocumentBuilder`, but this method does not exist in 2.0.2. Neither do `b["field"]`, `b.Add()`, `b.Write()`, or any other setter we tried. |
| 3 | **`BsonDocument` has no parameterless constructor** | Cannot create a `new BsonDocument()` — the only path is `col.CreateDocument(fields, builder)`, but the builder API is undiscoverable (see #2). |
| 4 | **`BsonDocument` has no indexer** | The README shows `doc.GetString("field")`, `doc.GetInt32("field")`, `doc.Id` — none of these compiled against BLite.Bson 2.0.2. |
| 5 | **No IntelliSense discoverability** | With no parameterless constructors, no working extension methods, and no XML doc comments visible in the NuGet package, the API is essentially undiscoverable without source code access. |
| 6 | **Package structure confusion** | BLite is a meta-package referencing `BLite.Core`, `BLite.Bson`, and `BLite.SourceGenerators`. Adding `BLite.Core` separately alongside `BLite` caused subtle resolution issues. |

#### What We Tried

```
Attempt 1: DocumentDbContext + DocumentCollection<ObjectId, T>  → Source generator silent failure
Attempt 2: BLiteEngine + DynamicCollection + CreateDocument(fields, b => b.Set(...))  → "Set" does not exist
Attempt 3: BLiteEngine + DynamicCollection + CreateDocument(fields, b => { b["key"] = val; })  → No indexer
Attempt 4: BLiteEngine + DynamicCollection + CreateDocument(fields, b => b.Write(...))  → "Write" does not exist
Attempt 5: BLiteEngine + DynamicCollection + new BsonDocument()  → No parameterless constructor
```

#### Verdict

> **BLite may be a capable database engine, but in its 2.0.2 NuGet release, the public API for the schema-less path (`BLiteEngine` + `DynamicCollection`) is not usable without access to the source code.** The documented API does not match the shipped binary. The strongly-typed path (`DocumentDbContext`) requires a source generator that silently fails to produce output.
>
> This is a critical developer experience issue. If a senior .NET developer with AI assistance cannot write a basic Insert → Read → Update → Delete flow after 5+ attempts, the library is not ready for production adoption.

#### Fair Disclosure

- BLite is an active open-source project under development. Version 2.0.2 may have been published ahead of documentation sync.
- The source generator may work correctly in a standalone project (our test was inside a larger solution).
- Future versions may resolve these issues. We recommend re-evaluating when 3.0 ships.
- BLite's feature set (BSON, HNSW, LINQ, zero-allocation) is ambitious and technically interesting.

---

## Part 2: Vector Search Benchmark

### SharpCoreDB HNSW vs Zvec (Alibaba)

Full details: [SHARPCOREDB_VS_ZVEC_COMPARISON.md](SHARPCOREDB_VS_ZVEC_COMPARISON.md)

#### Index Build (128-dimensional vectors)

| Dataset | SharpCoreDB | Zvec (estimated) |
|---|---|---|
| 1K vectors | 2,934 vec/sec | — |
| 10K vectors | 1,676 vec/sec | — |
| 100K vectors | 573 vec/sec | ~10,000+ vec/sec |

#### Search Latency (100K index, K=10)

| Metric | SharpCoreDB | Zvec |
|---|---|---|
| p50 | 0.530ms | <0.5ms (est.) |
| p95 | 0.758ms | — |
| p99 | 0.964ms | — |

#### Search Throughput (100K index, K=10, 10s)

| Threads | SharpCoreDB QPS | Zvec QPS |
|---|---|---|
| 1 thread | 1,815 | — |
| 8 threads | 9,231 | 15,000+ |

#### Key Differentiator: Adaptive SIMD

SharpCoreDB automatically uses the best SIMD instruction set available on the host CPU:

| SIMD Tier | Width | SharpCoreDB | Zvec |
|---|---|---|---|
| AVX-512 | 512-bit (16 floats) | ✅ Auto-detected | ✅ Hard requirement |
| AVX2+FMA | 256-bit (8 floats) | ✅ Auto-detected | ❌ Crashes (`Illegal instruction`) |
| SSE2 | 128-bit (4 floats) | ✅ Auto-detected | ❌ |
| ARM NEON | 128-bit (4 floats) | ✅ Auto-detected | ❌ |
| Scalar | 32-bit | ✅ Fallback | ❌ |

> **Zvec crashed with `Illegal instruction (core dumped)` on our test CPU** (i7-10850H, AVX2) because it hard-requires AVX-512. SharpCoreDB's single binary runs on any CPU and adapts automatically.

---

## Part 3: Overall Comparison Matrix

### CRUD Databases

| Feature | SharpCoreDB | SQLite | LiteDB | BLite |
|---|---|---|---|---|
| **Type** | SQL + Document + Vector | SQL | Document (BSON) | Document (BSON) |
| **Language** | C# (.NET 10) | C (native) | C# (.NET Standard) | C# (.NET 10) |
| **Best at** | Batch INSERT | Point R/U/D | Balanced CRUD | Unknown (untestable) |
| **INSERT 100K** | **202K ops/sec** 🥇 | 167K ops/sec | 92K ops/sec | ❌ |
| **READ 10K** | 6K ops/sec | **97K ops/sec** 🥇 | 13K ops/sec | ❌ |
| **SQL Support** | ✅ Full | ✅ Full | ❌ | ❌ |
| **Vector Search** | ✅ HNSW | ❌ | ❌ | ✅ HNSW |
| **Encryption** | ✅ Built-in | ❌ (extension) | ❌ | ❌ |
| **API Usability** | ✅ Simple | ✅ Standard | ✅ Good | ❌ Undiscoverable |
| **NuGet Package** | ✅ | ✅ | ✅ | ⚠️ Issues |
| **Documentation** | ✅ | ✅ Extensive | ✅ Good | ⚠️ Mismatches |

### Vector Databases

| Feature | SharpCoreDB | Zvec (Alibaba) |
|---|---|---|
| **Type** | Full database + vector | Vector-only |
| **Language** | C# (.NET 10) | C++ |
| **Platform** | Windows + Linux + macOS + ARM | Linux only (AVX-512 only) |
| **QPS (8 threads)** | 9,231 | 15,000+ |
| **Sub-ms latency** | ✅ (p50 = 0.53ms) | ✅ |
| **SQL + Vector** | ✅ Hybrid queries | ❌ |
| **Adaptive SIMD** | ✅ (any CPU) | ❌ (crashes without AVX-512) |
| **Single binary** | ✅ | ❌ |
| **.NET native** | ✅ | ❌ (Python/C++ bridge) |

---

## Conclusions

### When to Use SharpCoreDB

- **Bulk data ingestion** — fastest INSERT of all tested databases (202K ops/sec)
- **Hybrid workloads** — SQL + vector search + encryption in a single embedded database
- **Cross-platform .NET** — runs on Windows, Linux, macOS, ARM with adaptive SIMD
- **Simplicity** — one NuGet package, no native dependencies, no external processes

### When to Use SQLite

- **Read-heavy workloads** — 16x faster point reads than SharpCoreDB
- **Update/Delete-heavy** — 30-50x faster individual row operations
- **Mature ecosystem** — 20+ years of production use, tooling, and documentation

### When to Use LiteDB

- **Balanced .NET document database** — good all-around performance
- **Schema-less BSON** — when you need MongoDB-like document storage embedded in .NET
- **Simple API** — well-documented, easy to pick up

### When NOT to Use BLite (as of 2.0.2)

- **Production use** — public API is not usable from NuGet without source code access
- **Quick prototyping** — documented API does not match shipped binary
- **Any project that values time-to-first-query** — basic CRUD required 5+ failed attempts

---

## Methodology & Reproducibility

### Test Harness

All benchmarks used the same test structure:
1. Create database in temp directory
2. INSERT N documents with identical schema across all databases
3. READ M documents by field match
4. UPDATE M documents
5. DELETE M documents
6. Measure wall-clock time per operation
7. Clean up temp files

### Fairness

| Aspect | Approach |
|---|---|
| **Same schema** | All databases used the same 5-column document structure |
| **Same data** | Identical test data generated for all databases |
| **Optimal API** | Each database used its recommended batch/bulk API |
| **WAL mode** | SQLite: `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL` |
| **Warm start** | All measurements exclude database creation time |
| **No indexing** | No secondary indexes — tests raw storage engine performance |

### Source Code

- Comparative benchmark: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/`
- Zvec benchmark: `tests/benchmarks/zvec_python/` and `tests/benchmarks/QuickZvecTest/`
- Raw results: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/`

---

*Report generated 2026-03-06 by SharpCoreDB Benchmark Suite v1.7.0*  
*All tests run on: .NET 10.0.3, Intel i7-10850H, 32GB RAM, NVMe SSD, Windows 10*
