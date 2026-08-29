# 8. SIMD, Analytics, Vector Search, GraphRAG & Time-Series

> Deep dives: [`docs/Vectors/README.md`](../Vectors/README.md) ·
> [`docs/Vectors/TECHNICAL_SPEC.md`](../Vectors/TECHNICAL_SPEC.md) ·
> [`docs/analytics/README.md`](../analytics/README.md) ·
> [`docs/graphrag/00_START_HERE.md`](../graphrag/00_START_HERE.md) ·
> [`docs/performance/graphrag-performance-tuning.md`](../performance/graphrag-performance-tuning.md)

---

## 8.1 SIMD acceleration model

Every hot numerical path is vectorized via `Vector<T>` / `Vector128/256/512` with runtime
feature detection: AVX2, AVX-512, NEON, with a scalar fallback. No CPU-specific builds and no
crashes on machines without AVX-512. Guarded behind `SIMD_ENABLED` compile-time defines.

| Path | SIMD usage |
|------|-----------|
| Columnar aggregates | `Sum`, `Min`, `Max`, `Avg` over contiguous segments |
| Numeric WHERE filters | batch compares over Integer/Long columns |
| Vector distance kernels | cosine / euclidean / dot over float vectors |
| Time-series codecs | delta + XOR/Gorilla bitpacking loops |
| BINARY collation compares | byte-wise vectorized equality |

## 8.2 Columnar analytics

```sql
CREATE TABLE sales (
  region  TEXT,
  amount  REAL,
  units   INTEGER,
  sold_on DATETIME
) STORAGE = COLUMNAR;

-- ms-scale on millions of rows:
SELECT region, COUNT(*), SUM(amount), AVG(amount), MIN(sold_on), MAX(sold_on)
FROM sales
GROUP BY region;
```

- 100+ aggregate functions: `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`, `STDDEV`, `VARIANCE`,
  `PERCENTILE_CONT`, `PERCENTILE_DISC`, `MEDIAN`, `CORRELATION`, `COVAR_POP/SAMP`, …
- Window functions: `ROW_NUMBER`, `RANK`, `DENSE_RANK`, `NTILE`, `LAG`, `LEAD`,
  `FIRST_VALUE`, `LAST_VALUE`, frames.
- Aggregates over 10M rows in ~2 ms (`GROUP BY` SUM — **~682x faster than SQLite**).
- Tutorial: [`docs/analytics/TUTORIAL.md`](../analytics/TUTORIAL.md)

## 8.3 Vector search

`SharpCoreDB.VectorSearch` provides HNSW indexing with SIMD distance kernels.

```csharp
var index = db.GetVectorIndex("vectors", dimensions: 384);

// Insert embeddings
index.Add(id: 1, new float[] { /* 384 floats */ });

// Search top-K
var hits = index.Search(queryEmbedding, topK: 10);  // p50 ≈ 0.53 ms at 10M+ vectors
```

- Similarity: cosine, euclidean, dot product; `ORDER BY embedding <-> @q` SQL syntax
- Adaptive SIMD (any CPU), validation at 10M+ vectors
- Tuning: `M`, `efConstruction`, `efSearch` — see
  [`docs/Vectors/PERFORMANCE_TUNING.md`](../Vectors/PERFORMANCE_TUNING.md)
- Migration guide (SQLite vector extensions → SharpCoreDB):
  [`docs/migration/SQLITE_VECTORS_TO_SHARPCORE.md`](../migration/SQLITE_VECTORS_TO_SHARPCORE.md)

## 8.4 GraphRAG & graph algorithms

GraphRAG brings graph-aware retrieval to the SQL engine — used by the built-in
`IGraphRagProvider` (DI-cached in v2.0, removing per-query allocations).

| Capability | Detail |
|------------|--------|
| Community detection | Louvain, Label Propagation (LPA) |
| Centrality | degree, betweenness, eigenvector |
| Traversal | BFS, DFS, bidirectional, A* pathfinding |
| Hybrid retrieval | graph + vector + keyword in one query |
| Custom heuristics | plug your own scoring (`docs/graphrag/CUSTOM_HEURISTICS_GUIDE.md`) |

Getting started: [`docs/graphrag/00_START_HERE.md`](../graphrag/00_START_HERE.md) ·
LINQ API: [`docs/graphrag/LINQ_API_GUIDE.md`](../graphrag/LINQ_API_GUIDE.md) ·
Examples: [`docs/examples/graphrag-basic-usage.md`](../examples/graphrag-basic-usage.md)

## 8.5 Time-series

`src/SharpCoreDB/TimeSeries/` provides a complete time-series layer:

- **Bucketed storage** (`BucketManager`) with configurable bucket width
- **Hot/cold tiering** — `BucketTier.Hot/Cold`, archival manager, retention policies
- **Compression codecs** — `Gorilla`, `XOR`, `Delta-of-Delta`
- **Downsampling engine** — min/mean/max per bucket
- **Time-range pushdown** — scans skip buckets outside the query window
