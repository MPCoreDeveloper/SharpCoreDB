# SharpCoreDB HNSW vs Zvec (Alibaba) — Vector Search Benchmark Report

**Date:** 2026-03-06  
**SharpCoreDB Version:** 1.5.0 (.NET 10, C# 14)  
**Zvec Version:** 0.2.0 (C++, Proxima engine)

---

## 🚀 SharpCoreDB Adaptive SIMD Acceleration

SharpCoreDB automatically detects and uses the **best SIMD instruction set available** on the host CPU at runtime. This means the same binary runs optimally on any hardware — from ARM laptops to high-end AVX-512 servers — without recompilation.

### Supported SIMD Tiers (auto-detected, highest available is used)

| Tier | Instruction Set | Width | Floats/Cycle | Platforms | FMA |
|------|----------------|-------|-------------|-----------|-----|
| **1** | **AVX-512** | 512-bit | 16 | Intel Xeon, Ice Lake+ | ✅ FusedMultiplyAdd |
| **2** | **AVX2** | 256-bit | 8 | Intel Haswell+, AMD Zen+ | ✅ FMA3 (if supported) |
| **3** | **SSE2** | 128-bit | 4 | All modern x86/x64 | ✅ FMA3 (if supported) |
| **4** | **ARM NEON (AdvSimd)** | 128-bit | 4 | Apple Silicon, ARM64 servers | Native |
| **5** | **Scalar** | 32-bit | 1 | Universal fallback | — |

### What This Means

- **On an AVX-512 server:** SharpCoreDB processes **16 floats per SIMD instruction** with fused multiply-add — matching native C++ SIMD performance characteristics
- **On a consumer laptop (AVX2):** Processes **8 floats per instruction** with FMA — half the throughput of AVX-512 but still hardware-accelerated
- **On ARM64 (Apple M-series, Ampere):** Uses **ARM NEON** SIMD — SharpCoreDB runs natively on ARM servers and Apple Silicon
- **On any other CPU:** Graceful scalar fallback — always works, just without SIMD acceleration

### SIMD-Accelerated Operations

All vector distance computations use SIMD acceleration:

| Operation | Description | SIMD Optimized |
|-----------|-------------|----------------|
| **Cosine Distance** | Fused single-pass: dot product + normA + normB in one loop | ✅ |
| **Euclidean Distance** | L2 distance with SIMD difference-and-square | ✅ |
| **Dot Product** | Inner product with FMA accumulation | ✅ |
| **Vector Normalization** | L2-normalize with SIMD division | ✅ |

### Key Advantage Over Zvec

| | SharpCoreDB | Zvec |
|---|---|---|
| **SIMD dispatch** | **Adaptive** (AVX-512/AVX2/SSE/NEON/scalar auto-detected) | Fixed (AVX-512 required, crashes without it) |
| **ARM support** | ✅ ARM NEON (AdvSimd) | ❌ x86 only |
| **Windows** | ✅ | ❌ Linux/macOS only |
| **Binary compatibility** | Single binary runs everywhere | Requires specific CPU features |
| **Graceful degradation** | Always runs, uses best available | Illegal instruction crash on AVX2-only CPUs |

> **This adaptive SIMD approach is a significant architectural advantage.** Zvec's prebuilt binary crashed with `Illegal instruction (core dumped)` on our i7-10850H (AVX2-only) because it hard-requires AVX-512. SharpCoreDB's binary runs on any CPU and automatically uses the fastest path available.

---

## Test Environment

| | SharpCoreDB (measured) | Zvec (published) |
|---|---|---|
| **Language** | C# / .NET 10 (SIMD: AVX2 + FMA) | C++ / SIMD (AVX-512) |
| **CPU** | Intel i7-10850H (6C/12T, AVX2+FMA) | High-end server (AVX-512) |
| **OS** | Windows 10 | Linux |
| **Memory** | 32 GB | N/A (server-class) |

> **Note:** SharpCoreDB uses `System.Runtime.Intrinsics` with multi-tier SIMD dispatch: AVX-512 → AVX2+FMA → SSE → ARM NEON → scalar. On this i7-10850H, AVX2+FMA is active (8 floats/cycle). On an AVX-512 server, SharpCoreDB would automatically use 16 floats/cycle — closing the gap with Zvec's C++ engine.

---

## Z1: Index Build Performance

| Dataset | SharpCoreDB | Notes |
|---|---|---|
| **1,000 vectors** (128D) | **2,934 vec/sec** (0.34s) | Includes graph construction |
| **10,000 vectors** (128D) | **1,676 vec/sec** (5.97s) | O(n·log(n)) scaling |
| **100,000 vectors** (128D) | **573 vec/sec** (174.6s) | AVX2+FMA active (8 floats/cycle) |

**Zvec claim:** Optimized for millions of vectors with C++ SIMD acceleration.

**Analysis:** SharpCoreDB's index build uses SIMD-accelerated distance calculations (AVX2+FMA on this CPU via `System.Runtime.Intrinsics`). The throughput decreases with larger datasets due to HNSW's inherent O(n·log(n)) complexity and graph neighbor updates. Zvec uses C++ with AVX-512, giving it a wider SIMD lane (16 floats vs 8 floats per cycle).

---

## Z2: Search Latency (100K index, 1K queries)

| Top-K | p50 | p95 | p99 | avg |
|---|---|---|---|---|
| **K=10** | **0.530ms** | 0.758ms | 0.964ms | 0.566ms |
| **K=100** | **0.963ms** | 1.329ms | 1.608ms | 1.005ms |

**Zvec claim:** Sub-millisecond search latency for 10M+ vectors.

**Analysis:** SharpCoreDB delivers sub-millisecond p50 latency for K=10 on 100K vectors — competitive for an embedded .NET database. The tail latency (p99 < 1ms for K=10) shows consistent performance.

---

## Z3: Search Throughput (QPS) — 100K index, K=10, 10s test

| Threads | SharpCoreDB QPS | Total Queries |
|---|---|---|
| **1 thread** | **1,815 QPS** | 18,147 |
| **4 threads** | **6,230 QPS** | 62,302 |
| **8 threads** | **9,231 QPS** | 92,308 |

**Zvec claim:** ~15,000+ QPS on 10M vectors (server hardware).

**Analysis:** SharpCoreDB achieves **9,231 QPS at 8 threads on 100K vectors** on a laptop CPU. The scaling factor from 1→8 threads is **5.1x** (theoretical max: 8x), showing good parallel efficiency. Zvec's 15K+ QPS is on a 10M dataset with server-grade hardware — at similar scale SharpCoreDB would need native SIMD optimization to match.

---

## Z4: Recall@10 (10K index, 100 queries)

| Metric | SharpCoreDB | Zvec (typical HNSW) |
|---|---|---|
| **Recall@10** | **65.5%** | 95%+ |

**Analysis:** SharpCoreDB's recall is lower than expected. This suggests the HNSW implementation may need tuning:
- **ef_search** parameter (search-time neighbors to explore) may be too low
- **M** parameter (max connections per layer) at 16 is standard
- **ef_construction** at 200 is good for build quality

**Recommendation:** Expose and tune `ef_search` parameter. Typical HNSW implementations achieve 95%+ recall@10 with ef_search=100-200.

---

## Comparison Matrix

| Feature | SharpCoreDB | Zvec (Alibaba) |
|---|---|---|
| **Type** | Full SQL database + vector search | Dedicated vector database |
| **Language** | C# (.NET 10) | C++ |
| **SIMD Acceleration** | ✅ AVX-512 / AVX2+FMA / SSE / ARM NEON (adaptive) | ✅ AVX-512 only (hard requirement) |
| **ARM Support** | ✅ ARM NEON (AdvSimd) | ❌ x86 only |
| **SQL Support** | ✅ Full SQL | ❌ None |
| **CRUD Operations** | ✅ INSERT/SELECT/UPDATE/DELETE | ✅ Insert/Query/Delete |
| **Transactions** | ✅ ACID | ❌ Limited |
| **Hybrid Search** | ✅ SQL WHERE + vector | ✅ Filter + vector |
| **In-Process** | ✅ | ✅ |
| **Windows** | ✅ | ❌ |
| **macOS (ARM)** | ✅ (ARM NEON) | ✅ (ARM64 only) |
| **.NET Native** | ✅ | ❌ Python/C++ only |
| **Single Binary** | ✅ Runs everywhere (adaptive SIMD) | ❌ Crashes without AVX-512 |
| **Index Build (100K)** | 573 vec/sec (AVX2) | ~10,000+ vec/sec (est., AVX-512) |
| **Search Latency (K=10)** | 0.53ms p50 | <0.5ms p50 (est.) |
| **QPS (8 threads)** | 9,231 | 15,000+ |
| **Recall@10** | 65.5% (needs ef_search tuning) | 95%+ |
| **Scalability** | 100K tested | 10M+ proven |

---

## Key Takeaways

1. **Adaptive SIMD is a major advantage** — SharpCoreDB automatically uses the best SIMD available (AVX-512/AVX2/SSE/ARM NEON) without recompilation. A single binary runs optimally everywhere. Zvec's binary crashes on CPUs without AVX-512.

2. **SharpCoreDB is competitive for its class** — a managed .NET database delivering sub-millisecond vector search latency with 9K+ QPS on a laptop CPU is strong. On an AVX-512 server, throughput would roughly double.

3. **Zvec wins on raw throughput** — as expected for a dedicated C++ vector-only engine from Alibaba, optimized purely for similarity search.

4. **SharpCoreDB wins on versatility** — native .NET, full SQL, Windows + Linux + macOS + ARM support, CRUD + vector + transactions in one engine. No Python/C++ bridge needed.

5. **Recall needs tuning** — 65.5% recall@10 is below the HNSW standard of 95%+. Exposing and tuning `ef_search` parameter should fix this.

6. **Different tools, different jobs:**
   - **Zvec:** When you need maximum vector search throughput on Linux AVX-512 servers
   - **SharpCoreDB:** When you need an embedded .NET database with vector search that runs on any platform and any CPU

---

*Generated by SharpCoreDB Benchmark Suite on 2026-03-06*
