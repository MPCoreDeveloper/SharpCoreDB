# Vector Search Performance Tuning Guide

> **Applies to:** SharpCoreDB.VectorSearch 1.2.0+ | .NET 10 | C# 14

---

## Quick Start: Choosing the Right Index

| Dataset Size | Recommended Index | Recall | Latency |
|---|---|---|---|
| < 10K vectors | `FLAT` | 100% (exact) | < 5ms |
| 10K–1M vectors | `HNSW` (default) | > 95% | < 2ms |
| > 1M vectors | `HNSW` (tuned) | > 90% | < 10ms |

```sql
-- Small dataset: exact search (no index needed, or FLAT)
CREATE VECTOR INDEX idx ON docs(embedding) USING FLAT

-- Large dataset: approximate search
CREATE VECTOR INDEX idx ON docs(embedding) USING HNSW
```

---

## HNSW Parameter Tuning

### Key Parameters

| Parameter | Default | Range | Effect |
|---|---|---|---|
| `M` | 16 | 4–64 | Connections per node. Higher = better recall, more memory |
| `EfConstruction` | 200 | 50–800 | Build-time search width. Higher = better index quality, slower builds |
| `EfSearch` | 50 | 10–500 | Query-time search width. Higher = better recall, slower queries |

### Presets

```csharp
// High recall (> 99%): slower queries, more memory
services.AddVectorSupport(opt => {
    opt.DefaultM = 32;
    opt.DefaultEfConstruction = 400;
    opt.DefaultEfSearch = 200;
});

// Low memory: faster queries, slightly lower recall
services.AddVectorSupport(opt => {
    opt.DefaultM = 8;
    opt.DefaultEfConstruction = 100;
    opt.DefaultEfSearch = 30;
});

// Balanced (default)
services.AddVectorSupport(); // M=16, efConstruction=200, efSearch=50
```

### Tuning Strategy

1. **Start with defaults** — 95%+ recall for most datasets
2. **Measure recall** — compare HNSW results with exact (FLAT) search
3. **Increase `EfSearch`** first if recall is too low (cheapest knob)
4. **Increase `M`** if recall plateaus — requires index rebuild
5. **Increase `EfConstruction`** for better index quality — requires rebuild

---

## Distance Function Selection

| Function | Best For | SQL Name |
|---|---|---|
| Cosine | Normalized embeddings (OpenAI, Cohere) | `vec_distance_cosine` |
| Euclidean (L2) | Raw/unnormalized vectors | `vec_distance_l2` |
| Dot Product | Maximum inner product search | `vec_distance_dot` |

**Tip:** If your embeddings are already L2-normalized (most LLM providers), cosine and dot product give identical rankings. Use cosine for clarity.

---

## Memory Optimization

### Quantization

| Method | Memory Reduction | Recall Impact | Use Case |
|---|---|---|---|
| None (float32) | 1× | Perfect | < 100K vectors |
| Scalar (uint8) | 4× | ~1-3% loss | 100K–1M vectors |
| Binary (1-bit) | 32× | ~5-10% loss | > 1M vectors, first-pass filtering |

### Memory Presets

```csharp
// Embedded/mobile — 50 MB limit
services.AddVectorSupport(opt => {
    opt.MaxMemoryMB = 50;
    opt.LazyIndexLoading = true;
    opt.EvictIndexOnMemoryPressure = true;
});

// Server — unlimited
services.AddVectorSupport(opt => {
    opt.MaxMemoryMB = 0;
    opt.LazyIndexLoading = false;
});
```

### Memory Estimation

Per vector in HNSW index:
- **Vector data:** `dimensions × 4 bytes` (float32)
- **Graph links:** `M × 2 × 8 bytes` per layer (bidirectional long IDs)
- **Overhead:** ~64 bytes per node

**Example:** 100K vectors × 1536 dims × M=16:
- Vector data: 100K × 1536 × 4 = **585 MB**
- Graph links: 100K × 16 × 2 × 8 ≈ **25 MB**
- Total: **~610 MB**

---

## SIMD Acceleration

SharpCoreDB.VectorSearch uses `System.Runtime.Intrinsics` for multi-tier SIMD:

| Tier | Width | Floats/op | Available On |
|---|---|---|---|
| AVX-512 | 512-bit | 16 | Intel Ice Lake+, AMD Zen 4+ |
| AVX2 | 256-bit | 8 | Intel Haswell+ (2013), AMD Zen+ |
| SSE | 128-bit | 4 | All x64 CPUs |
| Scalar | 32-bit | 1 | Universal fallback |

**FMA (Fused Multiply-Add):** Automatically used when available — better throughput and precision for dot product / cosine.

### Verifying SIMD Support

```csharp
using System.Runtime.Intrinsics.X86;

Console.WriteLine($"AVX-512: {Avx512F.IsSupported}");
Console.WriteLine($"AVX2:    {Avx2.IsSupported}");
Console.WriteLine($"FMA:     {Fma.IsSupported}");
Console.WriteLine($"SSE:     {Sse.IsSupported}");
```

---

## Query Optimization

### Pattern: Automatic Index Routing

The query planner detects this pattern and routes to the vector index:

```sql
SELECT id, title, vec_distance_cosine(embedding, @query) AS distance
FROM documents
ORDER BY distance
LIMIT 10
```

**Without index:** Full table scan → compute distance for ALL rows → sort → take 10
**With HNSW index:** Index search → return top 10 pre-sorted → skip scan and sort

### Verify with EXPLAIN

```sql
EXPLAIN SELECT id, vec_distance_cosine(embedding, @query) AS d
FROM documents ORDER BY d LIMIT 10
```

Output:
- `Vector Index Scan (HNSW, count=50000)` — index is being used
- `Full table scan` — no index, falling back to brute force

---

## Best Practices

1. **Build index after bulk inserts** — insert all vectors first, then CREATE VECTOR INDEX
2. **Use `ExecuteBatchSQL`** for bulk inserts — ensures data persists to storage engine
3. **Flush after writes** — call `db.Flush(); db.ForceSave();` before querying
4. **Normalize embeddings** — most LLM embeddings are already normalized; use cosine distance
5. **Benchmark with your data** — recall and latency vary by dataset distribution

---

*Last updated: 2026-02*
