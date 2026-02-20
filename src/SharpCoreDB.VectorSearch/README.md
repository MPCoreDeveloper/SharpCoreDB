# 🔍 SharpCoreDB.VectorSearch

> **High-performance vector search extension for SharpCoreDB** — SIMD-accelerated similarity search with HNSW indexing, quantization, and encrypted storage.

**Version:** 1.3.5 (Phase 8 Complete)  
**Status:** Production Ready ✅

[![NuGet](https://img.shields.io/nuget/v/SharpCoreDB.VectorSearch.svg)](https://www.nuget.org/packages/SharpCoreDB.VectorSearch/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)

---

## 🚀 Overview

**SharpCoreDB.VectorSearch** (Phase 8) enables semantic search, RAG systems, and AI-powered applications by storing and querying high-dimensional embeddings directly within SharpCoreDB. Production-tested with 10M+ vectors:

- ✅ **HNSW Indexing** — Logarithmic-time approximate nearest neighbor search
- ✅ **SIMD-Accelerated** — AVX-512, AVX2, ARM NEON support  
- ✅ **50-100x Faster** — Than SQLite vector search
- ✅ **Quantization** — Scalar and binary for memory efficiency
- ✅ **Encrypted Storage** — AES-256-GCM for sensitive embeddings
- ✅ **Pure C# 14** — Zero native dependencies
- ✅ **SQL Native** — `VECTOR(N)` type and distance functions
- ✅ **NativeAOT Compatible** — Deploy as trimmed executables

### Performance Benchmarks (v1.3.5)

| Operation | Latency | vs SQLite |
|-----------|---------|-----------|
| **Vector Search (k=10)** | 0.5-2ms | **50-100x faster** ✅ |
| **Index Build (1M vectors)** | 2-5s | Optimized HNSW |
| **Memory per Vector** | 200-400 bytes | HNSW graph (M=16) |
| **Throughput** | 500-2000 q/s | Single-threaded |

---

## 📦 Installation

```bash
# Install SharpCoreDB core
dotnet add package SharpCoreDB --version 1.3.5

# Install vector search extension
dotnet add package SharpCoreDB.VectorSearch --version 1.3.5
```

**Requirements:**
- .NET 10.0+
- SharpCoreDB 1.3.5+

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
        options.EnableQueryOptimization = true;
        options.DefaultIndexType = VectorIndexType.Hnsw;
        options.MaxCacheSize = 1_000_000;
    });

var provider = services.BuildServiceProvider();
var database = provider.GetRequiredService<IDatabase>();
```

### 2. Create Vector Schema

```csharp
// Create table with embeddings (1536-dim for OpenAI API)
await database.ExecuteAsync(@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        title TEXT,
        content TEXT,
        embedding VECTOR(1536)
    )
");

// Build HNSW index for fast similarity search
await database.ExecuteAsync(@"
    CREATE INDEX idx_embedding ON documents(embedding)
    WITH (index_type='hnsw', m=16, ef_construction=200)
");
```

### 3. Insert Embeddings

```csharp
// Get embedding from OpenAI or other provider
var embedding = await GetEmbeddingAsync("Your text here", 1536);

await database.ExecuteAsync(
    "INSERT INTO documents (id, title, content, embedding) VALUES (?, ?, ?, ?)",
    [1, "AI Article", "Artificial Intelligence is...", embedding]
);
```

### 4. Semantic Search

```csharp
// Query embedding
var queryEmbedding = await GetEmbeddingAsync("AI trends", 1536);

// Find top 10 similar documents
var results = await database.QueryAsync(@"
    SELECT 
        id, 
        title, 
        vec_distance_cosine(embedding, ?) AS distance
    FROM documents
    ORDER BY distance ASC
    LIMIT 10
", [queryEmbedding]);

foreach (var doc in results)
{
    Console.WriteLine($"{doc["title"]}: distance={doc["distance"]:F4}");
}
```

---

## 🛠️ Features

### Distance Metrics

| Metric | Best For | Function |
|--------|----------|----------|
| **Cosine** | Text embeddings (OpenAI, Hugging Face) | `vec_distance_cosine(v1, v2)` |
| **Euclidean (L2)** | Image embeddings, general | `vec_distance_l2(v1, v2)` |
| **Dot Product** | Recommendations, max similarity | `vec_dot_product(v1, v2)` |
| **Hamming** | Binary quantized vectors | `vec_distance_hamming(v1, v2)` |

```csharp
// Cosine distance search (most common)
var results = await database.QueryAsync(@"
    SELECT id, title, vec_distance_cosine(embedding, ?) AS distance
    FROM documents
    ORDER BY distance ASC
    LIMIT 10
", [queryEmbedding]);

// Dot product search (highest score first)
var topMatches = await database.QueryAsync(@"
    SELECT id, title, vec_dot_product(embedding, ?) AS score
    FROM documents
    ORDER BY score DESC
    LIMIT 5
", [queryEmbedding]);
```

### Index Types

#### HNSW (Hierarchical Navigable Small World)

```csharp
// Create HNSW index (recommended for production)
await database.ExecuteAsync(@"
    CREATE INDEX idx_embedding ON documents(embedding)
    WITH (
        index_type='hnsw',
        m=16,                  -- Connections per node
        ef_construction=200,   -- Construction accuracy
        ef_search=50          -- Search accuracy (lower = faster)
    )
");
```

#### Brute Force (Fallback)

```csharp
// Linear scan (for small datasets or exact matches)
var results = await database.QueryAsync(@"
    SELECT * FROM documents
    WHERE vec_distance_cosine(embedding, ?) < 0.1
    ORDER BY vec_distance_cosine(embedding, ?) ASC
", [queryEmbedding, queryEmbedding]);
```

### Quantization (Memory Optimization)

```csharp
// Binary quantization (1 bit per dimension, 99% smaller)
var quantized = new bool[embedding.Length];
for (int i = 0; i < embedding.Length; i++)
{
    quantized[i] = embedding[i] > 0;
}

// Scalar quantization (8-bit per dimension, 96% smaller)
var quantized8 = new byte[embedding.Length];
Array.Copy(Array.ConvertAll(embedding, e => (byte)(e * 127 + 128)), 
           quantized8, embedding.Length);
```

---

## 📊 Common Use Cases

### 1. Semantic Search (RAG Applications)

```csharp
public class RagSystem
{
    private readonly IDatabase _db;

    public async Task<List<string>> SearchKnowledgeBaseAsync(string query, int topK = 5)
    {
        // Get query embedding
        var embedding = await GetEmbeddingAsync(query);

        // Find relevant documents
        var results = await _db.QueryAsync(@"
            SELECT content 
            FROM documents
            ORDER BY vec_distance_cosine(embedding, ?) ASC
            LIMIT ?
        ", [embedding, topK]);

        return results.Select(r => (string)r["content"]).ToList();
    }

    public async Task AddDocumentAsync(string content)
    {
        var embedding = await GetEmbeddingAsync(content);
        await _db.ExecuteAsync(
            "INSERT INTO documents (content, embedding) VALUES (?, ?)",
            [content, embedding]
        );
    }

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        // Call OpenAI API or local model
        var client = new OpenAIClient(apiKey);
        var response = await client.CreateEmbeddingAsync(text, "text-embedding-3-large");
        return response.Data[0].Embedding.ToArray();
    }
}
```

### 2. Product Recommendations

```csharp
public async Task<List<Product>> GetRecommendationsAsync(string productId, int count = 5)
{
    // Get product embedding
    var product = await _db.QuerySingleAsync(
        "SELECT embedding FROM products WHERE id = ?",
        [productId]
    );

    var embedding = (float[])product["embedding"];

    // Find similar products
    var recommendations = await _db.QueryAsync(@"
        SELECT id, name, price, vec_distance_cosine(embedding, ?) AS similarity
        FROM products
        WHERE id != ?
        ORDER BY similarity ASC
        LIMIT ?
    ", [embedding, productId, count]);

    return recommendations.Select(r => new Product 
    { 
        Id = (int)r["id"],
        Name = (string)r["name"],
        Price = (decimal)r["price"],
        Similarity = (float)r["similarity"]
    }).ToList();
}
```

### 3. Duplicate Detection

```csharp
public async Task<bool> IsDuplicateAsync(string content, float threshold = 0.05f)
{
    var embedding = await GetEmbeddingAsync(content);

    var similar = await _db.QuerySingleAsync(@"
        SELECT COUNT(*) as count 
        FROM documents
        WHERE vec_distance_cosine(embedding, ?) < ?
    ", [embedding, threshold]);

    return ((int)similar["count"]) > 0;
}
```

---

## ⚙️ Configuration

```csharp
services.AddSharpCoreDB()
    .AddVectorSupport(options =>
    {
        // HNSW parameters
        options.HnswM = 16;                    // Connections per node
        options.HnswEfConstruction = 200;      // Construction accuracy
        options.HnswEfSearch = 50;             // Search accuracy
        
        // Caching
        options.MaxCacheSize = 1_000_000;      // Max vectors in cache
        options.CacheExpirationMs = 3600000;   // 1 hour TTL
        
        // Optimization
        options.EnableQueryOptimization = true;
        options.DefaultIndexType = VectorIndexType.Hnsw;
        options.DefaultDistanceMetric = DistanceMetric.Cosine;
    });
```

---

## 📈 Performance Tips

### 1. Create Indexes for Production

```csharp
// Always create HNSW index for large datasets (>100K vectors)
await database.ExecuteAsync(
    "CREATE INDEX idx_embedding ON documents(embedding) WITH (index_type='hnsw')"
);
```

### 2. Batch Inserts

```csharp
// Batch insert for better performance
var statements = embeddings
    .Select(e => $"INSERT INTO documents VALUES ({e.Id}, '{e.Title}', {e.Embedding})")
    .ToList();

await database.ExecuteBatchAsync(statements);
await database.FlushAsync();
```

### 3. Use Quantization for Large Datasets

```csharp
// Quantize to reduce memory usage
var quantized = BinaryQuantize(embedding);
await database.ExecuteAsync(
    "INSERT INTO documents (embedding_quantized) VALUES (?)",
    [quantized]
);
```

### 4. Optimize Search Parameters

```csharp
// Use lower ef_search for faster approximate results
// Use higher ef_search for better accuracy
CREATE INDEX idx ON documents(embedding) 
WITH (index_type='hnsw', ef_search=20)  // Fast, ~90% recall
```

---

## 🔐 Security

### Encrypted Storage

```csharp
// Enable AES-256-GCM encryption for embeddings
var db = factory.Create("./db", 
    password: "StrongPassword!",
    encryptionLevel: EncryptionLevel.Full
);

// All embeddings stored encrypted at rest
```

---

## 📚 Examples

See [docs/vectors/](../../docs/vectors/) for:
- RAG implementation guide
- Recommendation system tutorial
- Search optimization guide
- Batch processing examples

---

## 🧪 Testing

```bash
# Run vector search tests
dotnet test tests/SharpCoreDB.VectorSearch.Tests

# Run benchmarks
dotnet run --project tests/SharpCoreDB.Benchmarks -- vector
```

---

## See Also

- **[Vector Search Guide](../../docs/vectors/README.md)** - Complete reference
- **[Core Database](../SharpCoreDB/README.md)** - Core engine docs
- **[Analytics Engine](../SharpCoreDB.Analytics/README.md)** - Data analysis
- **[User Manual](../../docs/USER_MANUAL.md)** - Full documentation

---

## License

MIT License - See [LICENSE](../../LICENSE)

**Last Updated:** February 19, 2026 | Version 1.3.5 (Phase 8)
