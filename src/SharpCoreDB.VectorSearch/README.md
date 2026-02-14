# 🔍 SharpCoreDB.VectorSearch

> **High-performance vector search extension for SharpCoreDB** — SIMD-accelerated similarity search with HNSW indexing, quantization, and encrypted storage.

[![NuGet](https://img.shields.io/nuget/v/SharpCoreDB.VectorSearch.svg)](https://www.nuget.org/packages/SharpCoreDB.VectorSearch/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![Version](https://img.shields.io/badge/Version-1.3.0-green.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB/releases)

---

## 🚀 Overview

**SharpCoreDB.VectorSearch** enables semantic search, similarity matching, and AI/RAG applications by storing and querying high-dimensional embeddings directly within your SharpCoreDB database. It's built for production workloads with:

- ✅ **Pure managed C# 14** — Zero native dependencies
- ✅ **SIMD-accelerated** — AVX-512, AVX2, ARM NEON support
- ✅ **HNSW indexing** — Logarithmic-time approximate nearest neighbor search
- ✅ **Quantization** — Scalar and binary quantization for memory efficiency
- ✅ **Encrypted storage** — AES-256-GCM for sensitive embeddings
- ✅ **NativeAOT compatible** — Deploy as trimmed, self-contained executables
- ✅ **SQL integration** — Native `VECTOR(N)` type and `vec_*()` functions

### Performance Highlights

| Operation | Typical Latency | Notes |
|-----------|----------------|-------|
| **Vector Search (k=10)** | 0.5-2ms | 1M vectors, HNSW index, cosine similarity |
| **Index Build (1M vectors)** | 2-5 seconds | M=16, efConstruction=200 |
| **Memory Overhead** | 200-400 bytes/vector | HNSW graph structure (M=16) |
| **Throughput** | 500-2000 queries/sec | Single-threaded on modern CPU |

*Benchmarks run on AMD Ryzen 9 5950X with 1536-dim vectors. See `tests/SharpCoreDB.Benchmarks/VectorSearchPerformanceBenchmark.cs` for reproducible results.*

---

## 📦 Installation

```bash
# Install SharpCoreDB core (if not already installed)
dotnet add package SharpCoreDB --version 1.3.0

# Install vector search extension
dotnet add package SharpCoreDB.VectorSearch --version 1.3.0
```

**Requirements:**
- .NET 10.0 or later
- SharpCoreDB 1.3.0+
- 64-bit runtime (x64, ARM64)

---

## 🎯 Quick Start

### 1. Register Vector Support

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.VectorSearch;

var services = new ServiceCollection();
services.AddSharpCoreDB()
    .AddVectorSupport(options =>
    {
        options.EnableQueryOptimization = true;  // Auto-select indexes
        options.DefaultIndexType = VectorIndexType.Hnsw;
        options.MaxCacheSize = 1_000_000;       // Cache 1M vectors
    });

var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

using var db = factory.Create("./vector_db", "StrongPassword!");
```

### 2. Create Vector Schema

```csharp
// Create table with VECTOR column
await db.ExecuteSQLAsync(@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        title TEXT,
        content TEXT,
        embedding VECTOR(1536)  -- OpenAI text-embedding-3-large dimensions
    )
");

// Build HNSW index for fast similarity search
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_doc_embedding ON documents(embedding)
    WITH (index_type='hnsw', m=16, ef_construction=200)
");
```

### 3. Insert Vectors

```csharp
// Insert embeddings (e.g., from OpenAI API)
var embedding = new float[1536]; // Your embedding vector
// ... populate embedding from your ML model ...

await db.ExecuteSQLAsync(@"
    INSERT INTO documents (id, title, content, embedding)
    VALUES (?, ?, ?, ?)
", [1, "AI Overview", "Artificial Intelligence is...", embedding]);
```

### 4. Semantic Search

```csharp
// Search for similar documents
var queryEmbedding = new float[1536]; // Query embedding
var k = 10;  // Top-10 results

var results = await db.ExecuteSQLAsync(@"
    SELECT id, title, vec_distance_cosine(embedding, ?) AS similarity
    FROM documents
    ORDER BY similarity ASC
    LIMIT ?
", [queryEmbedding, k]);

foreach (var row in results)
{
    Console.WriteLine($"Document: {row["title"]}, Similarity: {row["similarity"]:F3}");
}
```

---

## 🛠️ Features

### Distance Metrics

Choose the right metric for your embeddings:

| Metric | Use Case | SQL Function |
|--------|----------|--------------|
| **Cosine** | Text embeddings (normalized) | `vec_distance_cosine(v1, v2)` |
| **Euclidean (L2)** | Image embeddings, general purpose | `vec_distance_l2(v1, v2)` |
| **Dot Product** | Recommendation systems, max similarity | `vec_dot_product(v1, v2)` |
| **Hamming** | Binary embeddings | `vec_distance_hamming(v1, v2)` |

```csharp
// Example: Dot product search (higher = more similar)
var results = await db.ExecuteSQLAsync(@"
    SELECT id, title, vec_dot_product(embedding, ?) AS score
    FROM documents
    ORDER BY score DESC
    LIMIT 10
", [queryEmbedding]);
```

### Index Types

#### HNSW (Hierarchical Navigable Small World)

**Best for:** Large datasets (10K+ vectors), fast approximate search

```csharp
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_hnsw ON vectors(embedding)
    WITH (
        index_type='hnsw',
        m=16,               -- Neighbors per layer (higher = more recall, slower build)
        ef_construction=200, -- Build-time beam search width
        ef_search=50        -- Query-time beam search width
    )
");
```

**Tuning Guide:**
- **M=8-16** — Good default (16 for high recall, 8 for faster build)
- **ef_construction=100-400** — Higher = better quality, slower build
- **ef_search=10-100** — Higher = better recall, slower search

#### Flat Index

**Best for:** Small datasets (<1K vectors), exact search

```csharp
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_flat ON vectors(embedding)
    WITH (index_type='flat')
");
```

### Quantization

Reduce memory usage by 4-32x with minimal accuracy loss:

```csharp
// Scalar Quantization (4x reduction: float32 → int8)
var indexManager = provider.GetRequiredService<VectorIndexManager>();
await indexManager.CreateIndexAsync(
    tableName: "documents",
    columnName: "embedding",
    indexType: VectorIndexType.Hnsw,
    quantization: QuantizationType.Scalar
);

// Binary Quantization (32x reduction: float32 → bit)
await indexManager.CreateIndexAsync(
    tableName: "documents",
    columnName: "embedding",
    indexType: VectorIndexType.Hnsw,
    quantization: QuantizationType.Binary
);
```

**Tradeoffs:**
- **Scalar:** ~1-3% recall drop, 4x memory savings
- **Binary:** ~5-10% recall drop, 32x memory savings, best for cosine similarity

### SQL Functions

```sql
-- Distance/similarity functions
vec_distance_cosine(v1, v2)    -- Returns 0-2 (lower = more similar)
vec_distance_l2(v1, v2)        -- Euclidean distance
vec_dot_product(v1, v2)        -- Dot product (higher = more similar)
vec_distance_hamming(v1, v2)   -- Hamming distance (binary vectors)

-- Vector operations
vec_length(v)                  -- Vector L2 norm
vec_normalize(v)               -- Normalize to unit length
vec_add(v1, v2)                -- Element-wise addition
vec_subtract(v1, v2)           -- Element-wise subtraction
vec_multiply(v, scalar)        -- Scalar multiplication

-- Metadata
vec_dimensions(v)              -- Get vector dimensions
```

---

## 📊 Use Cases

### 1. AI/RAG Applications

Store document embeddings for retrieval-augmented generation:

```csharp
// Index knowledge base
var docs = await LoadDocumentsAsync();
foreach (var doc in docs)
{
    var embedding = await GetEmbeddingAsync(doc.Content);  // OpenAI, Ollama, etc.
    await db.ExecuteSQLAsync(@"
        INSERT INTO knowledge_base (id, content, embedding)
        VALUES (?, ?, ?)
    ", [doc.Id, doc.Content, embedding]);
}

// Retrieve context for LLM
var userQuestion = "What is vector search?";
var queryEmbedding = await GetEmbeddingAsync(userQuestion);
var context = await db.ExecuteSQLAsync(@"
    SELECT content
    FROM knowledge_base
    ORDER BY vec_distance_cosine(embedding, ?)
    LIMIT 5
", [queryEmbedding]);

// Send context + question to LLM...
```

### 2. Semantic Search

Search by meaning, not just keywords:

```csharp
// Traditional keyword search (may miss relevant docs)
var results = await db.ExecuteSQLAsync(@"
    SELECT * FROM articles
    WHERE content LIKE '%machine learning%'
");

// Semantic vector search (finds conceptually similar docs)
var queryEmbedding = await GetEmbeddingAsync("machine learning");
var semanticResults = await db.ExecuteSQLAsync(@"
    SELECT id, title, vec_distance_cosine(embedding, ?) AS relevance
    FROM articles
    ORDER BY relevance ASC
    LIMIT 10
", [queryEmbedding]);
```

### 3. Recommendation Systems

Find similar products, users, or content:

```csharp
// Find similar products based on embedding similarity
var productEmbedding = await GetProductEmbeddingAsync(productId);
var recommendations = await db.ExecuteSQLAsync(@"
    SELECT id, name, price, vec_dot_product(embedding, ?) AS score
    FROM products
    WHERE id != ?
    ORDER BY score DESC
    LIMIT 5
", [productEmbedding, productId]);
```

### 4. Image/Audio Similarity

Compare media by their embeddings (e.g., CLIP, Wav2Vec):

```csharp
// Find visually similar images
var imageEmbedding = await GetImageEmbeddingAsync(imagePath);  // CLIP model
var similarImages = await db.ExecuteSQLAsync(@"
    SELECT id, path, vec_distance_l2(embedding, ?) AS distance
    FROM images
    ORDER BY distance ASC
    LIMIT 20
", [imageEmbedding]);
```

---

## 🔐 Security

### Encrypted Vector Storage

All vectors are encrypted at rest using AES-256-GCM when you create an encrypted database:

```csharp
using var db = factory.CreateEncrypted(
    dbPath: "./secure_vectors",
    password: "YourStrongPassword123!",
    options: new DatabaseOptions
    {
        EnableEncryption = true  // Vectors encrypted automatically
    }
);
```

**What's encrypted:**
- ✅ Vector embeddings (VECTOR columns)
- ✅ HNSW graph structure
- ✅ Quantization tables
- ✅ All metadata

---

## ⚡ Performance Tips

### 1. Choose the Right Index

| Dataset Size | Recommended Index | Search Time |
|--------------|-------------------|-------------|
| < 1K vectors | Flat | 0.1-1ms |
| 1K-10K vectors | HNSW (M=8) | 0.2-0.5ms |
| 10K-100K vectors | HNSW (M=16) | 0.5-2ms |
| 100K+ vectors | HNSW (M=16) + Quantization | 1-5ms |

### 2. Tune HNSW Parameters

```csharp
// High recall (slower)
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_high_recall ON vectors(embedding)
    WITH (index_type='hnsw', m=32, ef_construction=400, ef_search=100)
");

// Fast search (lower recall)
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_fast ON vectors(embedding)
    WITH (index_type='hnsw', m=8, ef_construction=100, ef_search=10)
");
```

### 3. Use Quantization for Large Datasets

```csharp
// 1M vectors, 1536 dimensions:
// - Unquantized: ~6GB RAM
// - Scalar:      ~1.5GB RAM (4x reduction)
// - Binary:      ~200MB RAM (32x reduction)

var indexManager = provider.GetRequiredService<VectorIndexManager>();
await indexManager.CreateIndexAsync(
    tableName: "large_embeddings",
    columnName: "embedding",
    indexType: VectorIndexType.Hnsw,
    quantization: QuantizationType.Scalar  // 4x memory savings
);
```

### 4. Batch Operations

```csharp
// ✅ DO: Batch inserts
using var transaction = db.BeginTransaction();
foreach (var doc in documents)
{
    await db.ExecuteSQLAsync(@"
        INSERT INTO documents (id, embedding) VALUES (?, ?)
    ", [doc.Id, doc.Embedding]);
}
transaction.Commit();

// ❌ DON'T: Individual transactions
foreach (var doc in documents)
{
    using var tx = db.BeginTransaction();
    await db.ExecuteSQLAsync("INSERT INTO documents ...");
    tx.Commit();  // Slow!
}
```

---

## 🧪 Testing

Run the included benchmarks to verify performance on your hardware:

```bash
cd tests/SharpCoreDB.Benchmarks
dotnet run -c Release -- --filter *VectorSearch*
```

**Example output:**
```
| Method        | VectorCount | Dimensions | K   | Mean      | Error   | StdDev  | Allocated |
|-------------- |------------ |----------- |---- |----------:|--------:|--------:|----------:|
| HnswSearch    | 100000      | 1536       | 10  | 1.845 ms  | 0.032 ms| 0.028 ms|     2.1 KB|
| FlatSearch    | 100000      | 1536       | 10  | 89.32 ms  | 1.23 ms | 1.15 ms |     2.1 KB|
```

---

## 📚 Documentation

- **[Full Vector Search Guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/Vectors/README.md)** — Complete documentation
- **[Implementation Details](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/Vectors/IMPLEMENTATION_COMPLETE.md)** — Architecture overview
- **[Migration Guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/Vectors/VECTOR_MIGRATION_GUIDE.md)** — Upgrade from older versions
- **[API Reference](https://github.com/MPCoreDeveloper/SharpCoreDB/wiki)** — Full API documentation

---

## 🤝 Contributing

Contributions are welcome! Please see [CONTRIBUTING.md](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/CONTRIBUTING.md) for guidelines.

### Areas for Contribution

- 🚀 Additional distance metrics (Manhattan, Mahalanobis, etc.)
- 🔬 New quantization strategies (product quantization, PQ)
- 📊 Performance benchmarks on different hardware
- 📖 Documentation improvements and examples
- 🐛 Bug reports and fixes

---

## 📄 License

This project is licensed under the **MIT License**. See [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.

---

## 🙏 Acknowledgments

- **HNSW Algorithm:** Based on [Malkov & Yashunin (2018)](https://arxiv.org/abs/1603.09320)
- **SIMD Optimizations:** Inspired by [Faiss](https://github.com/facebookresearch/faiss) and [Qdrant](https://github.com/qdrant/qdrant)
- **C# 14 Features:** Built with modern .NET practices from Microsoft

---

## 📞 Support

- **Issues:** [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions:** [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Email:** [support@sharpcoredb.com](mailto:support@sharpcoredb.com)

---

**Made with ❤️ by [MPCoreDeveloper](https://github.com/MPCoreDeveloper)**
