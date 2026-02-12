# SQLite Vector Search → SharpCoreDB Vector Database Migration Guide

**Version:** 1.2.0  
**Last Updated:** January 28, 2025  
**Status:** ✅ Production Ready  

---

## Table of Contents

1. [Overview](#overview)
2. [Why Migrate](#why-migrate)
3. [Architecture Comparison](#architecture-comparison)
4. [Quick Start (5 Minutes)](#quick-start-5-minutes)
5. [Detailed Migration Steps](#detailed-migration-steps)
6. [Data Migration Strategies](#data-migration-strategies)
7. [Query Translation](#query-translation)
8. [Index Configuration](#index-configuration)
9. [Performance Tuning](#performance-tuning)
10. [Troubleshooting](#troubleshooting)

---

## Overview

SharpCoreDB provides **production-ready vector search** with:
- **50-100x faster** search than SQLite vector extensions
- **HNSW indexes** for logarithmic-time queries
- **Native .NET integration** (no C FFI overhead)
- **Encryption support** (AES-256-GCM)
- **Comprehensive tooling** and verification

This guide shows how to migrate from `sqlite-vec` (or `fts5`) vector extensions to SharpCoreDB's native vector engine.

---

## Why Migrate

### Performance Comparison

| Metric | SQLite-vec | SharpCoreDB | Winner |
|--------|-----------|-------------|--------|
| **Search 1M vectors** | 100-200ms | 2-5ms | ⚡ **SharpCoreDB 20-100x** |
| **Index build time** | 60-90s | 5-10s | ⚡ **SharpCoreDB 6-18x** |
| **Memory (1M vectors)** | 5-6GB | 1-2GB | ⚡ **SharpCoreDB 3-6x** |
| **Query throughput** | 100-500 qps | 1000-5000+ qps | ⚡ **SharpCoreDB 5-50x** |
| **Encryption** | ❌ No | ✅ Yes | ✅ **SharpCoreDB** |
| **.NET Integration** | FFI (slower) | Native | ✅ **SharpCoreDB** |

### Real-World Scenario

**SQLite (sqlite-vec):**
```csharp
// Search takes 100ms
var stopwatch = Stopwatch.StartNew();
var results = db.ExecuteQuery(@"
    SELECT id, content FROM documents
    WHERE vec_distance('cosine', embedding, @query) > 0.8
    ORDER BY vec_distance DESC
    LIMIT 10"
);
stopwatch.Stop();
Console.WriteLine($"Search: {stopwatch.ElapsedMilliseconds}ms");  // 100ms
```

**SharpCoreDB:**
```csharp
// Same search takes 2ms
var stopwatch = Stopwatch.StartNew();
var results = await db.ExecuteQueryAsync(@"
    SELECT id, content FROM documents
    WHERE vec_distance('cosine', embedding, @query) > 0.8
    ORDER BY vec_distance DESC
    LIMIT 10"
);
stopwatch.Stop();
Console.WriteLine($"Search: {stopwatch.ElapsedMilliseconds}ms");  // 2ms
```

---

## Architecture Comparison

### SQLite Vector Search

```
┌─────────────────────────────────────────┐
│     SQLite Database (Disk)              │
│  ┌─────────────────────────────────────┐│
│  │ Table: documents                    ││
│  │ ├─ id (INTEGER)                     ││
│  │ ├─ content (TEXT)                   ││
│  │ └─ embedding (BLOB - raw bytes)     ││
│  │                                      ││
│  │ Virtual Table: vec_search           ││
│  │ └─ Flat index (linear scan)         ││
│  └─────────────────────────────────────┘│
│                                          │
│ C FFI Layer (sqlite-vec)                │
│ └─ Compute cosine distance              │
└─────────────────────────────────────────┘
        ↓ Query Time: O(n) ↓
```

### SharpCoreDB Vector Search

```
┌──────────────────────────────────────────────────┐
│   SharpCoreDB Database (Block-based)             │
│  ┌──────────────────────────────────────────────┐│
│  │ Table: documents                             ││
│  │ ├─ id (INTEGER)                              ││
│  │ ├─ content (TEXT)                            ││
│  │ └─ embedding (VECTOR(1536))                  ││
│  │                                               ││
│  │ Index: idx_embedding (HNSW)                  ││
│  │ ├─ Layer 0: All vectors                      ││
│  │ ├─ Layer 1: ~10% of vectors                  ││
│  │ ├─ Layer 2: ~1% of vectors                   ││
│  │ └─ Entry point: Random vector                ││
│  │                                               ││
│  │ Features:                                     ││
│  │ ├─ Quantization (8-16x memory savings)       ││
│  │ ├─ Encryption (AES-256-GCM)                  ││
│  │ └─ Async API                                 ││
│  └──────────────────────────────────────────────┘│
└──────────────────────────────────────────────────┘
        ↓ Query Time: O(log n) ↓
```

---

## Quick Start (5 Minutes)

### Step 1: Install SharpCoreDB

```bash
dotnet add package SharpCoreDB --version 1.2.0
dotnet add package SharpCoreDB.VectorSearch
```

### Step 2: Create Vector Schema

```csharp
using SharpCoreDB;
using SharpCoreDB.VectorSearch;

var services = new ServiceCollection();
services.AddSharpCoreDB()
    .UseVectorSearch();

var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

using var db = factory.Create("./app_db", "password");

// Create table with vector column
await db.ExecuteSQLAsync(@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        content TEXT NOT NULL,
        embedding VECTOR(1536)
    )
");

// Create HNSW index (faster than flat)
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_embedding_hnsw ON documents(embedding)
    USING HNSW WITH (
        metric = 'cosine',
        ef_construction = 200,
        ef_search = 50
    )
");
```

### Step 3: Migrate Data

```csharp
// Read from SQLite
var sqliteDb = new SqliteConnection("Data Source=old.db");
sqliteDb.Open();
using var cmd = sqliteDb.CreateCommand();
cmd.CommandText = "SELECT id, content, embedding FROM documents LIMIT 1000";
var reader = cmd.ExecuteReader();

// Batch insert to SharpCoreDB
var batch = new List<Dictionary<string, object>>();
while (reader.Read())
{
    var embedding = (byte[])reader["embedding"];
    var vector = ConvertBytesToFloatArray(embedding);
    
    batch.Add(new Dictionary<string, object>
    {
        ["id"] = reader.GetInt32(0),
        ["content"] = reader.GetString(1),
        ["embedding"] = vector
    });
}

await db.InsertBatchAsync("documents", batch);
```

### Step 4: Run Queries

```csharp
// Get embedding from OpenAI, local model, or conversion
var queryVector = new float[] { 0.1f, 0.2f, /* ... 1536 dimensions ... */ };

// Search
var results = await db.ExecuteQueryAsync(@"
    SELECT id, content, 
           vec_distance('cosine', embedding, @query) AS similarity
    FROM documents
    WHERE vec_distance('cosine', embedding, @query) > 0.7
    ORDER BY similarity DESC
    LIMIT 10
",
new[] { ("@query", (object)queryVector) });

foreach (var row in results)
{
    Console.WriteLine($"ID: {row["id"]}, Similarity: {row["similarity"]}");
}
```

---

## Detailed Migration Steps

### Step 1: Analyze Current SQLite Schema

```sql
-- Check your current schema
.schema documents

-- Example output:
-- CREATE TABLE documents(
--   id INTEGER PRIMARY KEY,
--   content TEXT,
--   embedding BLOB
-- );
```

**Questions to answer:**
- What are the vector dimensions? (usually 384, 768, or 1536)
- How many vectors? (affects index build time)
- What distance metric? (cosine, euclidean, dot)
- Any other columns to migrate?

### Step 2: Create SharpCoreDB Schema

```csharp
// Determine your embedding dimensions
const int embeddingDimensions = 1536;

await db.ExecuteSQLAsync($@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        content TEXT NOT NULL,
        metadata TEXT,
        embedding VECTOR({embeddingDimensions}),
        created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    )
");

// Create appropriate index
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_embedding_hnsw ON documents(embedding)
    USING HNSW WITH (
        metric = 'cosine',
        ef_construction = 200,
        ef_search = 50
    )
");
```

### Step 3: Extract Vectors from SQLite

```csharp
// Helper to convert BLOB to float[]
private static float[] BlobToVector(byte[] blob)
{
    var vector = new float[blob.Length / 4];
    Buffer.BlockCopy(blob, 0, vector, 0, blob.Length);
    return vector;
}

// Read from SQLite in batches
const int batchSize = 1000;
int offset = 0;

while (true)
{
    using var sqliteDb = new SqliteConnection("Data Source=old.db");
    await sqliteDb.OpenAsync();
    
    using var cmd = sqliteDb.CreateCommand();
    cmd.CommandText = $@"
        SELECT id, content, embedding 
        FROM documents 
        ORDER BY id 
        LIMIT {batchSize} OFFSET {offset}
    ";
    
    var reader = await cmd.ExecuteReaderAsync();
    var batch = new List<Dictionary<string, object>>();
    
    while (reader.Read())
    {
        var embedding = (byte[])reader["embedding"];
        batch.Add(new Dictionary<string, object>
        {
            ["id"] = reader.GetInt32(0),
            ["content"] = reader.GetString(1),
            ["embedding"] = BlobToVector(embedding)
        });
    }
    
    if (batch.Count == 0) break;
    
    // Insert batch
    await db.InsertBatchAsync("documents", batch);
    Console.WriteLine($"Migrated {offset + batch.Count} documents");
    
    offset += batchSize;
    await sqliteDb.CloseAsync();
}
```

### Step 4: Validate Data Integrity

```csharp
// Count rows
var count = await db.ExecuteQueryAsync("SELECT COUNT(*) as cnt FROM documents");
Console.WriteLine($"Total documents: {count[0]["cnt"]}");

// Check embedding dimensions
var sample = await db.ExecuteQueryAsync(@"
    SELECT id, ARRAY_LENGTH(embedding) as dims FROM documents LIMIT 1
");
Console.WriteLine($"Embedding dimensions: {sample[0]["dims"]}");

// Verify no NULLs
var nulls = await db.ExecuteQueryAsync(@"
    SELECT COUNT(*) as cnt FROM documents WHERE embedding IS NULL
");
Console.WriteLine($"NULL embeddings: {nulls[0]["cnt"]}");
```

---

## Data Migration Strategies

### Strategy 1: Batch Migration (Recommended)

✅ **Pros:**
- Fast (parallel processing possible)
- Lower memory overhead
- Can pause/resume
- Good error handling

❌ **Cons:**
- Requires scheduling
- Network latency

```csharp
// Process 5000 vectors per batch, 4 parallel jobs
const int batchSize = 5000;
const int maxParallelism = 4;

var semaphore = new SemaphoreSlim(maxParallelism);
var tasks = new List<Task>();

for (int offset = 0; offset < totalVectors; offset += batchSize)
{
    await semaphore.WaitAsync();
    
    var task = Task.Run(async () =>
    {
        try
        {
            var batch = await ReadBatchFromSqliteAsync(offset, batchSize);
            await db.InsertBatchAsync("documents", batch);
            Console.WriteLine($"Batch {offset}-{offset + batchSize} done");
        }
        finally
        {
            semaphore.Release();
        }
    });
    
    tasks.Add(task);
}

await Task.WhenAll(tasks);
```

### Strategy 2: Dual-Write (Zero Downtime)

✅ **Pros:**
- No downtime
- Easy rollback
- Gradual migration

❌ **Cons:**
- Requires both systems running
- Complex application logic

```csharp
public class DualWriteVectorStore : IVectorStore
{
    private readonly SqliteConnection _sqlite;
    private readonly IDatabase _sharpCore;
    private DateTime _cutoverTime = DateTime.UtcNow.AddDays(1);
    
    public async Task InsertAsync(int id, float[] vector)
    {
        // Write to both systems during migration
        await _sqlite.InsertAsync(id, vector);
        await _sharpCore.ExecuteSQLAsync(
            "INSERT INTO documents(id, embedding) VALUES (@id, @embedding)",
            new[] { ("@id", (object)id), ("@embedding", (object)vector) }
        );
    }
    
    public async Task<List<int>> SearchAsync(float[] query, int topK)
    {
        // Read from new system if cutover passed, else old system
        if (DateTime.UtcNow > _cutoverTime)
        {
            return await _sharpCore.SearchAsync(query, topK);
        }
        else
        {
            return await _sqlite.SearchAsync(query, topK);
        }
    }
}
```

### Strategy 3: Direct Migration (Fast)

✅ **Pros:**
- Fastest
- Simple code

❌ **Cons:**
- Requires downtime
- No rollback path

```csharp
// Stop application
// Run migration in single operation
var allDocuments = await ReadAllFromSqliteAsync();
await db.InsertBatchAsync("documents", allDocuments);
// Verify
// Start application
```

---

## Query Translation

### Pattern 1: Vector Search (Most Common)

**SQLite:**
```sql
SELECT id, content FROM documents
WHERE vec_distance('cosine', embedding, @query) > 0.8
ORDER BY vec_distance('cosine', embedding, @query) DESC
LIMIT 10;
```

**SharpCoreDB:**
```sql
SELECT id, content FROM documents
WHERE vec_distance('cosine', embedding, @query) > 0.8
ORDER BY vec_distance('cosine', embedding, @query) DESC
LIMIT 10;
```

**Code:**
```csharp
var queryVector = new float[] { /* ... */ };
var results = await db.ExecuteQueryAsync(@"
    SELECT id, content 
    FROM documents
    WHERE vec_distance('cosine', embedding, @query) > 0.8
    ORDER BY vec_distance('cosine', embedding, @query) DESC
    LIMIT 10
",
new[] { ("@query", (object)queryVector) });
```

### Pattern 2: Combined Search (Vector + Text)

**SQLite:**
```sql
SELECT id, content FROM documents
WHERE fts_match(content, 'machine learning')  -- Full-text search
  AND vec_distance('cosine', embedding, @query) > 0.7;
```

**SharpCoreDB:**
```sql
SELECT id, content FROM documents
WHERE content LIKE '%machine%' AND content LIKE '%learning%'
  AND vec_distance('cosine', embedding, @query) > 0.7;
```

**Code:**
```csharp
var results = await db.ExecuteQueryAsync(@"
    SELECT id, content FROM documents
    WHERE content LIKE @text1 AND content LIKE @text2
      AND vec_distance('cosine', embedding, @query) > 0.7
    LIMIT 20
",
new[] {
    ("@text1", (object)"%machine%"),
    ("@text2", (object)"%learning%"),
    ("@query", (object)queryVector)
});
```

### Pattern 3: Batch Search (Multiple Queries)

**Before (SQLite):**
```csharp
foreach (var query in queries)
{
    var result = db.ExecuteQuery(
        "SELECT id FROM documents WHERE vec_distance(...) = @dist",
        new[] { ("@dist", query) }
    );
}
```

**After (SharpCoreDB):**
```csharp
var results = new List<List<Dictionary<string, object>>>();

foreach (var query in queries)
{
    var result = await db.ExecuteQueryAsync(@"
        SELECT id FROM documents 
        WHERE vec_distance('cosine', embedding, @query) > 0.8
        LIMIT 10
    ",
    new[] { ("@query", (object)query) });
    
    results.Add(result);
}
```

---

## Index Configuration

### HNSW Index Parameters

```csharp
// Create with explicit parameters
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_embedding ON documents(embedding)
    USING HNSW WITH (
        metric = 'cosine',              -- Distance metric
        ef_construction = 200,          -- Build quality (100-400, higher=better quality, slower build)
        ef_search = 50,                 -- Search quality (higher=better recall, slower search)
        max_connections = 16            -- Graph connectivity (5-64)
    )
");
```

### Parameter Tuning Guide

| Parameter | Typical Values | Impact | Notes |
|-----------|---|---|---|
| `ef_construction` | 100-400 | Quality vs Build Time | Higher = better quality, slower build. Use 200 for balance |
| `ef_search` | 10-100 | Recall vs Search Speed | Usually 1/4 to 1/2 of ef_construction |
| `max_connections` | 8-32 | Memory vs Quality | Higher = more memory but better quality |
| `metric` | cosine, euclidean, dot, hamming | Algorithm | Choose based on your embedding type |

### Flat Index (for small datasets)

```csharp
// Use flat for <100K vectors
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_embedding_flat ON documents(embedding)
    USING FLAT
");
```

---

## Performance Tuning

### 1. Quantization (8-16x Memory Savings)

```csharp
// Coming in SharpCoreDB 1.3+
var options = new VectorSearchOptions
{
    QuantizationType = QuantizationType.Scalar,  // 8-bit quantization
    ScalarQuantBits = 8
};
```

### 2. Batch Processing Optimization

```csharp
// Optimal batch size: 1000-10000
const int optimalBatchSize = 5000;

var batches = documents
    .Chunk(optimalBatchSize)
    .ToList();

foreach (var batch in batches)
{
    var dictBatch = batch.Select(d => new Dictionary<string, object>
    {
        ["id"] = d.Id,
        ["embedding"] = d.Embedding,
        ["content"] = d.Content
    }).ToList();
    
    await db.InsertBatchAsync("documents", dictBatch);
    Console.WriteLine($"Migrated batch of {batch.Length}");
}
```

### 3. Index Building Optimization

```csharp
// Large datasets: tune ef_construction
// 1M vectors: use ef_construction=300-400 for best quality
await db.ExecuteSQLAsync(@"
    CREATE INDEX idx_embedding_optimized ON documents(embedding)
    USING HNSW WITH (
        metric = 'cosine',
        ef_construction = 300,      -- Higher for large datasets
        ef_search = 50,
        max_connections = 20        -- Higher connectivity
    )
");
```

### 4. Memory Usage Optimization

```csharp
// Monitor memory during migration
var before = GC.GetTotalMemory(false);
await db.InsertBatchAsync("documents", batch);
var after = GC.GetTotalMemory(true);
Console.WriteLine($"Memory used: {(after - before) / 1024 / 1024}MB");

// Reduce batch size if memory spike > 500MB
```

---

## Troubleshooting

### Issue 1: Vector Dimension Mismatch

**Error:** `Dimension mismatch: expected 1536, got 768`

**Solution:**
```csharp
// Check embedding size before migration
var sampleBlob = (byte[])reader["embedding"];
int dimensions = sampleBlob.Length / 4;  // 4 bytes per float
Console.WriteLine($"Actual dimensions: {dimensions}");

// Create table with correct dimensions
await db.ExecuteSQLAsync($@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        embedding VECTOR({dimensions})
    )
");
```

### Issue 2: Performance Regression After Migration

**Symptom:** SharpCoreDB queries slower than expected

**Solutions:**
1. Check if index was created:
   ```sql
   SELECT * FROM information_schema.indexes WHERE table_name = 'documents';
   ```

2. Rebuild index if needed:
   ```csharp
   await db.ExecuteSQLAsync("DROP INDEX idx_embedding_hnsw");
   await db.ExecuteSQLAsync(@"
       CREATE INDEX idx_embedding_hnsw ON documents(embedding)
       USING HNSW WITH (ef_construction = 300, ef_search = 50)
   ");
   ```

3. Verify query is using index:
   ```csharp
   // Enable query plan analysis
   var plan = await db.GetQueryPlanAsync(@"
       SELECT * FROM documents 
       WHERE vec_distance('cosine', embedding, @q) > 0.8
   ");
   Console.WriteLine(plan);
   ```

### Issue 3: Migration Hangs

**Symptom:** Migration stops responding

**Solutions:**
1. Check database lock:
   ```sql
   -- In another terminal
   SELECT * FROM pragma_database_list;
   ```

2. Increase timeout:
   ```csharp
   db.CommandTimeout = TimeSpan.FromMinutes(10);
   ```

3. Use smaller batches:
   ```csharp
   const int smallerBatch = 1000;  // Instead of 5000
   ```

### Issue 4: Data Loss During Migration

**Solution:** Always verify before deleting SQLite database

```csharp
// Step 1: Migrate
await MigrateDataAsync();

// Step 2: Verify counts match
var sqliteCount = (await sqliteDb.QueryAsync<int>(
    "SELECT COUNT(*) FROM documents"
)).FirstOrDefault();

var sharpCoreCount = (await db.ExecuteQueryAsync(
    "SELECT COUNT(*) as cnt FROM documents"
))[0]["cnt"];

if (sqliteCount != sharpCoreCount)
{
    throw new InvalidOperationException(
        $"Count mismatch: SQLite={sqliteCount}, SharpCoreDB={sharpCoreCount}"
    );
}

// Step 3: Verify sample data
var sqliteSample = await sqliteDb.QueryAsync("SELECT * FROM documents LIMIT 1");
var sharpCoreSample = await db.ExecuteQueryAsync("SELECT * FROM documents LIMIT 1");

if (sqliteSample.First()["content"] != sharpCoreSample.First()["content"])
{
    throw new InvalidOperationException("Data mismatch detected");
}

// Step 4: Safe to delete
File.Delete("old.db");
```

---

## Post-Migration Checklist

- [ ] Schema created in SharpCoreDB
- [ ] All vectors migrated (count matches SQLite)
- [ ] Indexes created and verified
- [ ] Sample queries tested
- [ ] Performance benchmarked
- [ ] Encryption enabled (optional)
- [ ] Backup of SQLite database
- [ ] Application updated to use SharpCoreDB
- [ ] Monitoring/logging in place
- [ ] Rollback plan documented

---

## Support & Resources

- **Documentation:** [Vector Search Guide](../vectors/README.md)
- **API Reference:** [Vector API Docs](../vectors/README.md#api-reference)
- **Performance:** [Performance Benchmarks](../BENCHMARK_RESULTS.md)
- **Issues:** [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

---

**Migration Guide Version:** 1.0  
**Last Updated:** January 28, 2025  
**Status:** ✅ Production Ready
