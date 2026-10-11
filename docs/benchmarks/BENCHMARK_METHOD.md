# SharpCoreDB Benchmark Methodology
## Comparative Analysis: BLite, Zvec, and SharpCoreDB

**Status:** RFC (Request for Comments)  
**Proposal Date:** 2026-03-03  
**Target:** Issue #56 - Benchmark Suggestion  
**Implementation Window:** Weeks 3-11 (Milestones M2-M4)  
**Deliverables:** 2 published reports + reproducibility artifacts

---

## Executive Summary

This document defines the methodology for comparing SharpCoreDB performance against:
1. **BLite** — .NET-only embedded document/NoSQL database
2. **Zvec** — Alibaba's embedded vector database

The benchmark is designed to be **reproducible, transparent, and fair** by:
- Publishing raw data alongside summaries
- Documenting exact hardware/software/dataset specifications
- Providing runnable benchmark harness code
- Explaining methodology assumptions and caveats
- Separating measured data from interpretation

---

## 1. Benchmark Philosophy

### Core Principles

1. **Transparency:** All data, configurations, and scripts are published.
2. **Reproducibility:** Anyone with the same hardware can rerun benchmarks and validate results.
3. **Fairness:** Each system is tested under conditions that favor neither; optimizations are documented.
4. **Honesty:** Caveats, limitations, and assumptions are explicitly stated.
5. **Utility:** Results answer questions real users ask ("Is it fast enough?").

### What We Measure

- **Throughput:** Queries per second (QPS), events/transactions per second.
- **Latency:** Median (p50), 99th percentile (p99), max.
- **Memory:** Resident set size (RSS), peak usage, per-operation overhead.
- **Resource utilization:** CPU, disk I/O, network (where applicable).
- **Correctness:** Recall (for vector search), consistency guarantees.

### What We Don't Benchmark (Out of Scope)

- **Distributed scenarios:** All three are embedded/local; distributed comparisons don't apply.
- **Advanced features:** Geospatial, full-text search, complex transactions beyond each system's core.
- **Cost:** BLite/Zvec are open-source; SharpCoreDB is open-source; cost is equal (free).

---

## 2. Benchmark Scenarios

### 2.1 BLite Comparison Track (Relational + Document)

**Focus:** CRUD operations, batch inserts, filtered queries, mixed workloads.

#### Scenario B1: Basic CRUD (100K operations)

```
Setup:
  - Create empty database
  - Create schema with 10 columns (mixed types: ID, name, email, age, score, tags, created_at, etc.)

Phases:
  1. INSERT 100K documents (single-threaded)
  2. SELECT by primary key (100K random reads)
  3. SELECT with filter (range query on numeric column, 10K queries)
  4. UPDATE 10K documents (random selection)
  5. DELETE 10K documents (random selection)
  6. Final SELECT all (full table scan)

Metrics:
  - Insert throughput (docs/sec)
  - Read latency (p50, p99)
  - Update throughput
  - Final memory footprint
```

#### Scenario B2: Batch Insert (1M documents)

```
Setup:
  - Same schema as B1

Phases:
  1. BATCH INSERT 1M documents in batches of 1K/5K/10K
  2. Measure per-batch latency
  3. Measure total throughput (docs/sec)
  4. Measure memory growth during bulk load

Metrics:
  - Batch insert throughput (docs/sec)
  - Memory efficiency (bytes per document)
  - Time to complete full load
```

#### Scenario B3: Filtered Query Performance (1M documents)

```
Setup:
  - Pre-populate with 1M documents from B2

Phases:
  1. Index on numeric column (e.g., age: 1-100)
  2. Query with range filter (age > 25 AND age < 75)
  3. Repeat 10K times with random filters
  4. Measure latency distribution

Metrics:
  - Query latency (p50, p99)
  - QPS
  - Index size
```

#### Scenario B4: Mixed Workload (10-minute sustained load)

```
Setup:
  - 500K documents pre-loaded
  - 8 concurrent threads

Workload:
  - 60% read (random key lookups + filtered queries)
  - 30% insert (mixed single + batch)
  - 10% update

Metrics:
  - Overall throughput (ops/sec)
  - Latency percentiles (p50, p95, p99)
  - Tail latency stability
  - Memory during sustained load
  - GC impact (pause times, frequency)
```

---

### 2.2 Zvec Comparison Track (Vector Search)

**Focus:** Index build, top-k query latency, throughput, recall/latency tradeoffs.

#### Scenario Z1: Index Build (1M vectors)

```
Setup:
  - Generate 1M random vectors, dimensions: 768 (typical LLM embedding size)
  - Vector values: float32, normalized cosine distance

Phases:
  1. Measure time to build HNSW index with M=16, ef_construction=200
  2. Measure time to build brute-force index (baseline)
  3. Index size (bytes)
  4. Memory during build

Metrics:
  - Index build time (seconds)
  - Index size (MB)
  - Throughput (vectors indexed per second)
  - Memory peak
```

#### Scenario Z2: Top-K Latency (1M vectors, warm index)

```
Setup:
  - 1M vectors indexed (warm cache)
  - 1000 random query vectors

Phases:
  1. Execute 1000 top-k queries (k=10)
  2. Measure latency per query
  3. Repeat with k=100
  4. Repeat with k=1000 (full recall)

Metrics:
  - Query latency (p50, p99) per k value
  - QPS (queries/sec)
```

#### Scenario Z3: Throughput Under Load (1M vectors, 8 concurrent threads)

```
Setup:
  - 1M vectors indexed
  - 8 concurrent threads each issuing top-k queries

Workload:
  - Each thread runs 100 top-k queries (k=10)
  - Measure time to completion

Metrics:
  - Overall throughput (queries/sec)
  - Per-thread latency (p50, p99)
  - Tail latency stability
```

#### Scenario Z4: Recall vs Latency Tradeoff (1M vectors)

```
Setup:
  - 1M vectors indexed
  - 100 query vectors with ground-truth nearest neighbors (brute-force search)

Phases:
  For each ef_search parameter [10, 50, 100, 200, 500]:
    1. Execute 100 top-k queries
    2. Measure latency (p50, p99)
    3. Compute recall (% of results matching ground-truth)

Metrics:
  - Recall vs latency curve
  - Sweet spot (e.g., 90% recall at < 1ms)
```

#### Scenario Z5: Insert Performance (incremental add to index)

```
Setup:
  - Start with empty index
  - Pre-indexed 100K vectors

Phases:
  1. Insert 900K vectors in batches (10K per batch)
  2. Measure per-batch insert latency
  3. Measure index build time incrementally

Metrics:
  - Insert throughput (vectors/sec)
  - Index rebuild impact (if applicable)
```

---

## 3. Metrics Definition

### 3.1 Latency Metrics

| Metric | Definition | Use Case |
|--------|-----------|----------|
| **p50** | 50th percentile (median) | Typical case |
| **p99** | 99th percentile | Worst-case for 99% of requests |
| **p999** | 99.9th percentile | Outlier detection |
| **Max** | Maximum observed latency | Worst-case scenario |
| **Mean** | Average latency | Aggregate performance |

### 3.2 Throughput Metrics

| Metric | Definition | Use Case |
|--------|-----------|----------|
| **QPS** | Queries per second | Overall throughput |
| **Ops/sec** | Operations per second (mixed workloads) | Aggregate throughput |
| **Docs/sec** | Documents inserted/processed per second | Bulk load performance |

### 3.3 Memory Metrics

| Metric | Definition | Use Case |
|--------|-----------|----------|
| **RSS** | Resident set size | Process memory footprint |
| **Peak** | Maximum RSS during operation | Memory constraints |
| **Per-item** | Memory / (dataset size) | Scaling efficiency |

### 3.4 Correctness Metrics (Vector Search)

| Metric | Definition | Use Case |
|--------|-----------|----------|
| **Recall@K** | % of results matching ground-truth top-k | Accuracy |
| **NDCG** | Normalized discounted cumulative gain | Ranking quality |

---

## 4. Test Harness Specification

### 4.1 Directory Structure

```
tests/benchmarks/
├── README.md                           # Quickstart guide
├── BenchmarkConfig.json                # Centralized configuration
├── run-all.sh / run-all.ps1           # Master runner script
├── SharpCoreDB.Benchmarks/
│   ├── SharpCoreDB.Benchmarks.csproj
│   ├── Program.cs                      # CLI entry point
│   ├── BenchmarkContext.cs             # Shared setup/teardown
│   ├── BLite/
│   │   ├── BliteCrudBenchmark.cs      # B1, B2, B3 scenarios
│   │   └── BliteMixedWorkloadBenchmark.cs
│   └── Zvec/
│       ├── ZvecIndexBuildBenchmark.cs  # Z1, Z2, Z3 scenarios
│       ├── ZvecQueryBenchmark.cs
│       └── ZvecRecallBenchmark.cs
├── harness/
│   ├── dataset-generator.ps1           # Generate test data
│   ├── hardware-profile.ps1            # Capture hardware/OS/runtime info
│   └── report-generator.ps1            # Generate reports from results
├── results/
│   ├── 2026-03-03-run-001/
│   │   ├── raw-data.json               # All measurements
│   │   ├── hardware.json               # Captured environment
│   │   ├── report.md                   # Summary + charts
│   │   └── raw-csv/                    # Per-scenario CSV exports
│   └── 2026-03-03-run-002/
└── docs/
    ├── METHODOLOGY.md                  # This file
    ├── REPRODUCING.md                  # Step-by-step rerun guide
    └── INTERPRETING_RESULTS.md         # Common questions
```

### 4.2 Configuration Schema

```json
{
  "benchmark": {
    "name": "SharpCoreDB vs BLite vs Zvec",
    "date": "2026-03-03T00:00:00Z"
  },
  "scenarios": {
    "blite": {
      "b1_crud": { "enabled": true, "operations": 100000 },
      "b2_batch_insert": { "enabled": true, "documents": 1000000, "batch_size": 5000 },
      "b3_filtered_query": { "enabled": true, "queries": 10000 },
      "b4_mixed_workload": { "enabled": true, "duration_minutes": 10, "threads": 8 }
    },
    "zvec": {
      "z1_index_build": { "enabled": true, "vectors": 1000000, "dimensions": 768 },
      "z2_latency": { "enabled": true, "queries": 1000, "k_values": [10, 100, 1000] },
      "z3_throughput": { "enabled": true, "threads": 8, "queries_per_thread": 100 },
      "z4_recall": { "enabled": true, "query_vectors": 100, "ef_search_range": [10, 50, 100, 200, 500] },
      "z5_insert": { "enabled": true, "vectors": 900000, "batch_size": 10000 }
    }
  },
  "environment": {
    "hardware": {
      "cpu": "Intel Xeon E5-2670",
      "cores": 16,
      "memory_gb": 128,
      "disk": "SSD NVMe"
    },
    "runtime": {
      "dotnet_version": "10.0.0",
      "os": "Windows 11 22H2",
      "os_version_build": "22621.3672"
    }
  },
  "warmup": {
    "enabled": true,
    "iterations": 100,
    "discard_results": true
  },
  "run": {
    "iterations": 1000,
    "threads": 1,
    "timeout_seconds": 300
  },
  "output": {
    "format": "json",
    "csv_export": true,
    "percentiles": [50, 90, 95, 99, 99.9]
  }
}
```

---

## 5. Execution Policy

### 5.1 Warm-Up

- **Purpose:** Stabilize JIT compilation, cache behavior, and system state.
- **Duration:** 100 iterations per scenario.
- **Discarding:** Results from warm-up are not included in final measurements.

### 5.2 Measurement Run

- **Iterations:** 1000 iterations per scenario (or until timeout).
- **Collection:** All latencies, memory samples, and error counts recorded.
- **Percentile calculation:** After run, compute p50, p90, p95, p99, p99.9.

### 5.3 Cooldown

- Explicit GC between scenarios (`GC.Collect()`).
- 5-second pause between scenarios to let system stabilize.

---

## 6. Hardware and Software Matrix

### 6.1 Test Environment (Baseline)

```
CPU:              Intel Core i9-13900K (16 cores, 24 threads)
Memory:           64 GB DDR5
Storage:          1 TB NVMe SSD
OS:               Windows 11 23H2 (Build 22631+)
.NET Runtime:     .NET 10.0 LTS
JVM (for Zvec):   OpenJDK 21 LTS (if Java version runs)
```

### 6.2 Additional Test Runs (Optional)

Users/CI may run on:
- Linux (Ubuntu 24.04 LTS)
- macOS (M3 ARM64)
- Smaller hardware (8 GB RAM, older CPU)

Results from different hardware are **clearly labeled** and **not compared** in the main report.

---

## 7. Data Generation

### 7.1 BLite Test Data

**Schema:**
```json
{
  "id": "integer (auto-increment)",
  "name": "string (random names, 20-100 chars)",
  "email": "string (valid email format)",
  "age": "integer (18-100)",
  "score": "float (0-100)",
  "tags": "array of strings (2-5 tags)",
  "created_at": "datetime (past 365 days)",
  "updated_at": "datetime (after created_at)",
  "is_active": "boolean (70% true)",
  "metadata": "json object (various fields)"
}
```

**Generation:**
- 100K / 1M documents via dataset-generator.ps1
- Seeded with fixed random seed for reproducibility
- Export to JSON-lines format for import

### 7.2 Zvec Test Data

**Vectors:**
- 1M 768-dimensional float32 vectors
- Generated via numpy/random with fixed seed
- Normalized for cosine distance
- Exported to binary format (HDF5 or custom)

**Ground-truth:**
- For recall scenarios: brute-force compute exact top-k for 100 query vectors
- Store as reference in results/

---

## 8. Reporting Format

### 8.1 Report Structure

```markdown
# Benchmark Report: SharpCoreDB vs BLite vs Zvec
**Date:** 2026-03-03  
**Environment:** [Hardware + Runtime details]

## Executive Summary
- Key findings (1-2 sentences per scenario)
- Recommendation (when one system is clearly superior)

## Methodology Note
This report follows [METHODOLOGY.md]. Results are reproducible. See [REPRODUCING.md] for rerun instructions.

## BLite Comparison Track

### Scenario B1: Basic CRUD (100K operations)
| System | Insert (ops/sec) | Read p50 (μs) | Read p99 (μs) | Update (ops/sec) | Final Memory (MB) |
|--------|------------------|---------------|---------------|------------------|------------------|
| BLite  | 45,230           | 125           | 850           | 38,900           | 256               |
| SharpCoreDB | 52,100      | 95            | 620           | 41,200           | 288               |

**Analysis:** SharpCoreDB is 15% faster on inserts, 24% lower read latency.

---

### Scenario B2: Batch Insert (1M documents)
[Chart: Insert throughput vs batch size]
[Table: Memory during bulk load]

---

## Zvec Comparison Track

### Scenario Z1: Index Build (1M vectors, 768D)
[Table: Build time, index size, memory]

---

### Scenario Z4: Recall vs Latency Tradeoff
[Chart: Recall (%) vs latency (ms), both systems]

---

## Caveats and Limitations

1. **BLite:** Tested with latest version (v0.X.Y). Document schema may not reflect real-world use.
2. **Zvec:** Java implementation; JVM warmup may differ from .NET measurements.
3. **Network:** All tests are local/embedded; no network latency.
4. **Concurrency:** BLite uses single-threaded locks; Zvec may handle multi-threading differently.

---

## Next Steps

- Rerun on different hardware to validate generalizability
- Profile CPU/memory with profiler tools to explain differences
- Test larger dataset sizes (10M documents, 10M vectors)

---

**Full raw data:** `tests/benchmarks/` (one directory per run; no raw-data file is checked in for the
2026-03-03 run, so this line names the location rather than linking a missing artefact)  
**Scripts:** `tests/benchmarks/README.md` and `tests/benchmarks/run-all.ps1`
```

### 8.2 Raw Data Format (JSON)

```json
{
  "metadata": {
    "run_date": "2026-03-03T14:30:00Z",
    "hardware": { ... },
    "environment": { ... }
  },
  "scenarios": {
    "blite_b1_crud": {
      "name": "BLite Basic CRUD",
      "phase": "insert",
      "measurements": [
        { "operation": "insert_1", "latency_ms": 0.025, "success": true },
        { "operation": "insert_2", "latency_ms": 0.021, "success": true },
        ...
      ],
      "summary": {
        "count": 100000,
        "throughput_ops_sec": 45230,
        "latency_p50_ms": 0.022,
        "latency_p99_ms": 0.085,
        "memory_mb": 256
      }
    }
  }
}
```

---

## 9. Reproducibility Guide

### 9.1 Exact Rerun

```bash
# Clone repository
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB/tests/benchmarks

# Capture environment
./harness/hardware-profile.ps1 > hardware.json

# Generate test data
./harness/dataset-generator.ps1

# Run all benchmarks
./run-all.ps1

# Generate report
./harness/report-generator.ps1 --results results/latest
```

### 9.2 Expected Output

```
BLite Benchmarks
  B1 CRUD ................................. ✓ 2m 45s
  B2 Batch Insert ......................... ✓ 1m 30s
  B3 Filtered Query ....................... ✓ 45s
  B4 Mixed Workload ....................... ✓ 10m 15s

Zvec Benchmarks
  Z1 Index Build .......................... ✓ 3m 20s
  Z2 Latency .............................. ✓ 1m 10s
  Z3 Throughput ........................... ✓ 2m 5s
  Z4 Recall ............................... ✓ 4m 30s
  Z5 Insert ............................... ✓ 2m 45s

Results written to: results/2026-03-03-run-XXX/
Report: results/2026-03-03-run-XXX/report.md
```

---

## 10. Interpreting Results

### Key Questions

**Q: Why is SharpCoreDB faster at inserts?**  
A: Possible reasons (to be investigated):
  - WAL design favors sequential writes
  - Lock contention in BLite
  - JIT compilation behavior

See [INTERPRETING_RESULTS.md] for deeper analysis.

---

## 11. Publication Plan

### Week 6: Report v1
- Execute all scenarios
- Publish raw data + summary
- CI automation for future reruns

### Week 11: Report v2
- Rerun with additional hardware profiles
- Add FAQ and interpretation guide
- Publish confidence intervals

---

## 12. Glossary

| Term | Definition |
|------|-----------|
| **Throughput** | Operations completed per second |
| **Latency** | Time for single operation |
| **Recall** | % of results matching ground-truth |
| **HNSW** | Hierarchical Navigable Small World (approximate NN index) |
| **Brute-force** | Linear scan (all vectors compared, 100% recall) |
| **p99** | 99th percentile latency (worst-case for 99% of requests) |

---

## Approval Gate (Week 2)

This methodology is approved when:
- [ ] Benchmark scenarios are realistic and measurable.
- [ ] Metrics are clearly defined.
- [ ] Hardware/software environment is documented.
- [ ] Warm-up and run policies are understood.
- [ ] Team agrees on reporting format.

**Date Approved:** _______________  
**Approver:** _______________

---

**Next Step:** Week 3 begins scenario implementation and first benchmark runs.
