# 🔍 SharpCoreDB Vector Search & Storage

> **Status:** 🔵 Design Phase — Implementation Planned  
> **Target Version:** 1.2.0  
> **Module:** `SharpCoreDB.VectorSearch` (optional, separate NuGet)  
> **Requirements:** .NET 10, C# 14  
> **Breaking Changes:** None — 100% backward compatible

---

## What Is Vector Search?

Vector search allows you to store **embeddings** (high-dimensional float arrays) alongside your regular data and search for **semantically similar** items using distance metrics like cosine similarity, Euclidean distance, or dot product.

This is the foundation for:
- **AI/RAG applications** — Find relevant context for LLM prompts
- **Semantic search** — Search by meaning, not just keywords  
- **Recommendation engines** — Find similar products, articles, or users
- **Anomaly detection** — Find items that don't match the pattern
- **Image/audio similarity** — Compare media by their embeddings

### Why SharpCoreDB Vectors?

| Feature | SharpCoreDB | sqlite-vec | pgvector | Chroma |
|---------|------------|------------|----------|--------|
| **Embedded (no server)** | ✅ | ✅ | ❌ | ❌ |
| **Encrypted storage** | ✅ AES-256-GCM | ❌ | ❌ | ❌ |
| **Pure managed code** | ✅ C# 14 | ❌ (C) | ❌ (C) | ❌ (Python) |
| **SIMD acceleration** | ✅ AVX-512/AVX2/NEON | ✅ | ✅ | ✅ |
| **Cross-platform** | ✅ 6 RIDs | ⚠️ | ❌ | ⚠️ |
| **NativeAOT compatible** | ✅ | N/A | N/A | N/A |
| **SQL interface** | ✅ | ✅ | ✅ | ❌ |
| **Scales to RDBMS** | ✅ | ❌ | ✅ | ❌ |
| **Zero dependencies** | ✅ | ✅ | ❌ | ❌ |
| **100% optional** | ✅ | ✅ | ❌ | N/A |

---

## Quick Start

### 1. Install the Package

```bash
# Core database (you already have this)
dotnet add package SharpCoreDB

# Vector search extension (optional)
dotnet add package SharpCoreDB.VectorSearch
```

### 2. Enable Vector Support

```csharp
using SharpCoreDB;
using SharpCoreDB.VectorSearch;

// Register vector support via DI
services.AddSharpCoreDB()
        .AddVectorSupport(options =>
        {
            options.DefaultIndexType = VectorIndexType.Hnsw;
            options.DefaultM = 16;              // HNSW connections per layer
            options.DefaultEfConstruction = 200; // Build-time quality
            options.DefaultEfSearch = 50;        // Query-time quality vs speed
            options.MaxDimensions = 4096;        // Safety limit
        });
```

### 3. Create a Table with Vectors

```sql
-- VECTOR(dimensions) column type stores float32 arrays
CREATE TABLE documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    content TEXT,
    embedding VECTOR(1536)
)
```

### 4. Insert Embeddings

```sql
-- Insert with JSON-encoded float array
INSERT INTO documents (title, content, embedding)
VALUES ('AI Overview', 'Artificial intelligence is...', 
        vec_from_float32('[0.0123, -0.0456, 0.0789, ...]'))
```

```csharp
// Or programmatically with parameters
float[] embedding = await embeddingModel.GenerateAsync("AI Overview");
db.ExecuteSQL(
    "INSERT INTO documents (title, content, embedding) VALUES (@title, @content, @embedding)",
    new Dictionary<string, object?>
    {
        ["@title"] = "AI Overview",
        ["@content"] = "Artificial intelligence is...",
        ["@embedding"] = embedding  // float[] passed directly
    });
```

### 5. Search for Similar Items

```sql
-- Find 10 most similar documents using cosine distance
SELECT id, title, 
       vec_distance_cosine(embedding, vec_from_float32(@query_vec)) AS distance
FROM documents
ORDER BY distance
LIMIT 10
```

```csharp
// Programmatic search with type-safe API
float[] queryEmbedding = await embeddingModel.GenerateAsync("machine learning basics");
var results = db.ExecuteQuery(
    @"SELECT id, title, vec_distance_cosine(embedding, @query) AS distance
      FROM documents 
      ORDER BY distance 
      LIMIT 10",
    new Dictionary<string, object?> { ["@query"] = queryEmbedding });

foreach (var row in results)
{
    Console.WriteLine($"{row["title"]} (distance: {row["distance"]:F4})");
}
```

---

## SQL Reference

### Data Types

| Type | Description | Storage | Example |
|------|------------|---------|---------|
| `VECTOR(N)` | Fixed-dimension float32 vector | BLOB (N × 4 bytes) | `VECTOR(1536)` |

### Functions

| Function | Description | Example |
|----------|------------|---------|
| `vec_distance_cosine(a, b)` | Cosine distance (0 = identical, 2 = opposite) | `ORDER BY vec_distance_cosine(col, @q)` |
| `vec_distance_l2(a, b)` | Euclidean (L2) distance | `ORDER BY vec_distance_l2(col, @q)` |
| `vec_distance_dot(a, b)` | Negative dot product (for max inner product) | `ORDER BY vec_distance_dot(col, @q)` |
| `vec_from_float32(json)` | Parse JSON array to vector | `vec_from_float32('[0.1, 0.2]')` |
| `vec_normalize(v)` | L2-normalize a vector | `vec_normalize(embedding)` |
| `vec_dimensions(v)` | Get dimension count | `vec_dimensions(embedding)` → `1536` |
| `vec_quantize_binary(v)` | Binary quantize (1 bit/dim) | For memory reduction |
| `vec_to_json(v)` | Convert vector to JSON string | For debugging/export |

### Index Types

```sql
-- Exact search (brute-force, best for < 10K vectors)
CREATE VECTOR INDEX idx_emb ON documents(embedding) USING FLAT

-- Approximate search (HNSW, best for 10K - 10M vectors)
CREATE VECTOR INDEX idx_emb ON documents(embedding) 
    USING HNSW(M = 16, ef_construction = 200)

-- Drop index
DROP VECTOR INDEX idx_emb ON documents
```

### PRAGMA Commands

```sql
-- Check vector feature status
PRAGMA vector_info

-- Configure HNSW search quality at runtime
PRAGMA vector_ef_search = 100

-- View index statistics
PRAGMA vector_index_stats('documents', 'embedding')
```

---

## Usage Patterns

### RAG (Retrieval-Augmented Generation)

```csharp
// 1. Store document embeddings
foreach (var doc in documents)
{
    float[] emb = await embeddingModel.GenerateAsync(doc.Content);
    db.ExecuteSQL(
        "INSERT INTO knowledge_base (doc_id, chunk, embedding) VALUES (@id, @chunk, @emb)",
        new() { ["@id"] = doc.Id, ["@chunk"] = doc.Content, ["@emb"] = emb });
}

// 2. User asks a question → find relevant context
float[] questionEmb = await embeddingModel.GenerateAsync(userQuestion);
var context = db.ExecuteQuery(
    @"SELECT chunk, vec_distance_cosine(embedding, @q) AS relevance
      FROM knowledge_base ORDER BY relevance LIMIT 5",
    new() { ["@q"] = questionEmb });

// 3. Send context + question to LLM
var prompt = $"Context:\n{string.Join("\n", context.Select(r => r["chunk"]))}\n\nQuestion: {userQuestion}";
var answer = await llm.CompleteAsync(prompt);
```

### Hybrid Search (Vector + SQL Filters)

```sql
-- Combine vector similarity with traditional WHERE filters
SELECT id, title, vec_distance_cosine(embedding, @query) AS distance
FROM products
WHERE category = 'Electronics' AND price < 100.00
ORDER BY distance
LIMIT 20
```

### Multi-Vector Columns

```sql
-- Tables can have multiple vector columns with different dimensions
CREATE TABLE products (
    id INTEGER PRIMARY KEY,
    name TEXT,
    image_embedding VECTOR(512),    -- Image features (CLIP)
    text_embedding VECTOR(1536),    -- Text description (OpenAI)
    price DECIMAL
)

-- Search by image similarity
SELECT name, price
FROM products
ORDER BY vec_distance_cosine(image_embedding, @image_query)
LIMIT 10
```

---

## Configuration

### Memory Management

Vector search can use significant memory, especially with HNSW indexes. SharpCoreDB provides granular control:

```csharp
services.AddVectorSupport(options =>
{
    // Memory limits (critical for embedded/mobile)
    options.MaxMemoryMB = 256;              // Hard limit for all vector indexes
    options.LazyIndexLoading = true;        // Load HNSW graph on first query
    options.EvictIndexOnMemoryPressure = true; // Release under GC pressure

    // Quantization (reduces memory 4-32x)
    options.DefaultQuantization = QuantizationType.None;  // Full precision
    // options.DefaultQuantization = QuantizationType.Scalar8;  // 4x reduction
    // options.DefaultQuantization = QuantizationType.Binary;    // 32x reduction

    // HNSW tuning
    options.DefaultM = 16;              // Connections per node (16 = good default)
    options.DefaultEfConstruction = 200; // Higher = better recall, slower build
    options.DefaultEfSearch = 50;        // Higher = better recall, slower query
    options.MaxDimensions = 4096;        // Reject vectors larger than this
});
```

### Scaling Tiers

| Tier | Vectors | Memory | Index Type | Use Case |
|------|---------|--------|------------|----------|
| **Embedded** | < 10K | < 50 MB | Flat (exact) | Mobile, IoT, small apps |
| **Standard** | 10K - 1M | 50 - 500 MB | HNSW | Desktop, web apps |
| **Enterprise** | 1M - 10M | 500 MB - 8 GB | HNSW + quantization | Server, enterprise |
| **Distributed** *(future)* | > 10M | Disk-based | DiskANN | Cloud, large-scale |

---

## Performance Expectations

### Distance Computation (SIMD-accelerated)

| Dimensions | Vectors | Exact Search | HNSW Search | Platform |
|------------|---------|-------------|-------------|----------|
| 384 | 10K | ~5 ms | ~0.1 ms | AVX2 (x64) |
| 768 | 100K | ~80 ms | ~0.5 ms | AVX2 (x64) |
| 1536 | 100K | ~160 ms | ~1 ms | AVX2 (x64) |
| 1536 | 1M | ~1.6 s | ~2 ms | AVX2 (x64) |
| 384 | 10K | ~8 ms | ~0.2 ms | NEON (ARM64) |

> **Note:** HNSW search times depend on `ef_search` parameter. Higher values increase recall but also latency.

### Memory Usage

| Dimensions | Vectors | Raw Data | HNSW Index (M=16) | With SQ8 | With Binary |
|------------|---------|----------|-------------------|----------|-------------|
| 384 | 10K | 15 MB | ~25 MB | ~8 MB | ~2 MB |
| 1536 | 100K | 586 MB | ~800 MB | ~250 MB | ~20 MB |
| 1536 | 1M | 5.7 GB | ~8 GB | ~2.5 GB | ~200 MB |

---

## Design Principles

1. **100% Optional** — Vector support is a separate NuGet. No impact on existing users.
2. **Zero External Dependencies** — Pure managed C# with `System.Numerics.Vector<T>` SIMD.
3. **SQL-First** — Standard SQL syntax, no custom operators. Works with existing tools.
4. **Scalable** — Same code path from embedded (10K vectors) to server (10M vectors).
5. **Configurable Memory** — Exact control over memory usage for embedded deployments.
6. **NativeAOT Safe** — No reflection, no dynamic loading. Works with `PublishTieredAot`.
7. **Cross-Platform** — Automatic SIMD: AVX-512 (x64) → AVX2 (x64) → NEON (ARM64) → Scalar fallback.

---

## Compatibility

### Supported Platforms

All platforms supported by SharpCoreDB core:
- Windows x64 / ARM64
- Linux x64 / ARM64 (including Android)
- macOS x64 / ARM64 (including iOS)
- IoT / Embedded (ARM32 with scalar fallback)

### Supported Embedding Models

SharpCoreDB stores any float32 vector. Compatible with all embedding providers:

| Provider | Model | Dimensions | Notes |
|----------|-------|-----------|-------|
| OpenAI | text-embedding-3-small | 1536 | Most popular |
| OpenAI | text-embedding-3-large | 3072 | Highest quality |
| Azure OpenAI | text-embedding-ada-002 | 1536 | Enterprise |
| Ollama | nomic-embed-text | 768 | Local/offline |
| Hugging Face | all-MiniLM-L6-v2 | 384 | Small & fast |
| CLIP | ViT-B/32 | 512 | Image + text |
| Cohere | embed-english-v3 | 1024 | Multilingual |

### Integration with Microsoft.Extensions.AI

```csharp
using Microsoft.Extensions.AI;

// Use the standard IEmbeddingGenerator interface
IEmbeddingGenerator<string, Embedding<float>> generator = /* your provider */;

var embedding = await generator.GenerateVectorAsync("Search query");
var results = db.ExecuteQuery(
    "SELECT * FROM docs ORDER BY vec_distance_cosine(embedding, @q) LIMIT 5",
    new() { ["@q"] = embedding.ToArray() });
```

---

## Roadmap

### Phase 1: Core Vector Storage & Exact Search
- [ ] `VECTOR(N)` column type with dimension validation
- [ ] `vec_from_float32()`, `vec_to_json()` conversion functions
- [ ] `vec_distance_cosine()`, `vec_distance_l2()`, `vec_distance_dot()` with SIMD
- [ ] `vec_normalize()`, `vec_dimensions()` utility functions
- [ ] Binary serialization (float[] ↔ byte[] via MemoryMarshal)
- [ ] Exact (brute-force) search via ORDER BY + LIMIT
- [ ] Parameter binding for float[] vectors

### Phase 2: HNSW Approximate Index
- [ ] Pure managed HNSW implementation
- [ ] `CREATE VECTOR INDEX ... USING HNSW(M, ef_construction)` syntax
- [ ] Thread-safe concurrent reads
- [ ] HNSW graph persistence (serialize/deserialize with DB)
- [ ] Configurable ef_search via PRAGMA

### Phase 3: Memory Optimization & Quantization
- [ ] Scalar quantization (float32 → int8, 4x memory reduction)
- [ ] Binary quantization (float32 → bit, 32x memory reduction)
- [ ] Lazy index loading (load on first query)
- [ ] Memory pressure callbacks (evict indexes under GC pressure)
- [ ] `ArrayPool<float>` for temporary buffers

### Phase 4: Advanced Features
- [ ] IVF (Inverted File) index for large datasets
- [ ] Hybrid search (vector + WHERE filters with pre/post-filtering)
- [ ] Multi-vector columns per table
- [ ] Distance-based range queries (WHERE distance < 0.5)
- [ ] EXPLAIN support for vector queries
- [ ] Index statistics and diagnostics

### Phase 5: Enterprise Scale *(future)*
- [ ] DiskANN for billion-scale datasets
- [ ] Product quantization (PQ)
- [ ] Batch vector operations
- [ ] Async HNSW index building
- [ ] Replication-aware vector indexes

---

## FAQ

**Q: Does this affect my existing database?**  
A: No. Vector support is a separate optional NuGet package. Existing databases, tables, and queries work identically.

**Q: Do I need vector support if I'm not doing AI?**  
A: No. Don't install `SharpCoreDB.VectorSearch` and there is zero overhead.

**Q: Can I use this with encrypted databases?**  
A: Yes. Vectors are stored as BLOBs and benefit from the same AES-256-GCM encryption.

**Q: What about NativeAOT?**  
A: Fully compatible. No reflection, no dynamic assembly loading. SIMD via `System.Numerics.Vector<T>`.

**Q: How does memory scale?**  
A: You have full control. Use exact search for small datasets (< 10K) with minimal memory, or HNSW with configurable memory limits for millions of vectors.

**Q: Can I migrate from sqlite-vec?**  
A: Yes. The SQL function names are compatible (`vec_distance_cosine`, etc.). A migration guide will be provided.

---

*Last updated: 2026-02 | SharpCoreDB Vector Search Design Document*
