# Example GraphRAG workflow with DISKANN (2.1.0)

**Munarium-inspired high-recall vector indexing + hybrid fusion + content-verified artifacts**

This example shows a complete **GraphRAG** workflow with the new `DISKANN` index. It combines:
- `DiskAnnIndex` (high-recall ANN from munarium `vector_diskann.rs`).
- Content verification (`ArtifactManifest` + the `BuildResult` union).
- Hybrid fusion (`HybridFusionAlpha`).
- `SharpCoreDB.EventSourcing` (conditional appends for immutable event streams).

## 1. Setup (C# — .NET 11 RC)

```csharp
using SharpCoreDB;
using SharpCoreDB.VectorSearch;
using SharpCoreDB.VectorSearch.Index;
using SharpCoreDB.VectorSearch.Artifacts;
using SharpCoreDB.Graph.Advanced.GraphRAG;
using SharpCoreDB.EventSourcing;
using System.Text.Json;

var db = Database.Open("graphrag_with_diskann.scdb");

// Register VectorSearch with munarium-inspired options
var vectorOptions = new VectorSearchOptions
{
    UseContentVerifiedArtifacts = true,   // Immutable manifests + SHA256 verification
    HybridFusionAlpha = 0.7f,             // 70% lexical / 30% vector weighting
    MaxMemoryMB = 1024
};

db.AddVectorSupport(vectorOptions);

// Register GraphRAG provider (uses the new DISKANN under the hood)
db.AddSharpCoreDBGraphRagSql(options =>
{
    options.GraphTableName = "knowledge_graph";
    options.EmbeddingTableName = "node_embeddings";
    options.EmbeddingDimensions = 1536;
    options.EmbeddingProvider = nodeId => GenerateEmbeddingForNode(nodeId); // your LLM embedding call
});
```

## 2. Create the tables and the DISKANN index (SQL DDL)

```sql
-- Graph table for relationships (EventSourcing compatible)
CREATE TABLE knowledge_graph (
    node_id INTEGER PRIMARY KEY,
    content TEXT,
    embedding VECTOR(1536),
    metadata TEXT
);

-- Create high-recall DISKANN index (munarium-inspired)
CREATE VECTOR INDEX idx_node_embeddings 
ON knowledge_graph(embedding) 
USING DISKANN WITH (
    max_neighbors = 64,
    construction_list_size = 300,
    query_list_size = 150,
    target_recall = 0.97
);

-- Optional: lexical index for hybrid fusion
CREATE INDEX idx_content ON knowledge_graph(content);
```

## 3. Ingest data through EventSourcing (conditional appends)

```csharp
var eventStore = new SharpCoreDbEventStore(db, "graph_events");

// Conditional append (optimistic concurrency)
var appendResult = await eventStore.TryAppendEventsAsync(
    streamId: "graph_build_001",
    expectedVersion: 5,                    // optimistic concurrency
    entries: new[]
    {
        new EventAppendEntry("NodeAdded", JsonSerializer.SerializeToUtf8Bytes(new { NodeId = 42, Content = "xAI Grok is helpful" })),
        new EventAppendEntry("EmbeddingGenerated", JsonSerializer.SerializeToUtf8Bytes(new { NodeId = 42, Dimensions = 1536 }))
    });

if (!appendResult.Success)
{
    Console.WriteLine($"Conflict at version {appendResult.ActualVersion}. Retry logic here.");
    return;
}

// Projection -> insert into graph table + build verified DISKANN index
var projection = new GraphRagProjection(db);
await projection.ApplyEventsAsync(appendResult.Results);
```

## 4. GraphRAG query with DISKANN + hybrid fusion

```csharp
var graphRagEngine = new GraphRagEngine(db, "knowledge_graph", "node_embeddings", 1536);

var query = "What is the latest on xAI Grok?";

// Semantic search uses DISKANN under the hood + hybrid fusion (VectorSearchOptions.HybridFusionAlpha)
var results = await graphRagEngine.PerformGraphRagSearchAsync(query, topK: 5, maxHops: 2);

foreach (var result in results)
{
    Console.WriteLine($"Node {result.NodeId}: {result.Content} (similarity: {result.SimilarityScore:P2}, verified artifact: {result.Manifest?.ArtifactId})");
}
```

## 5. Verification & Reproducibility (munarium feature)

```csharp
// After index build
var index = db.GetVectorIndex("knowledge_graph", "embedding"); // from VectorIndexManager
var manifest = index.Manifest;

var verifyResult = index.Verify(manifest);

verifyResult switch
{
    BuildResult.Success s => Console.WriteLine($"Index verified - artifact {s.ArtifactId} is immutable and reproducible"),
    BuildResult.VerificationFailed f => Console.WriteLine($"Quarantine index: {f.Reason}"),
    BuildResult.LimitExceeded l => Console.WriteLine($"Scale limit: {l.LimitType}"),
    _ => throw new InvalidOperationException("Exhaustive C# 15 union match required")
};
```

## Why this workflow

- **High recall** thanks to DiskANN (better than plain HNSW on large corpora).
- **Hybrid fusion** (lexical + vector) through `HybridFusionAlpha`.
- **Immutable and verifiable** artifacts (canonical SHA256 + the `BuildResult` union) — a good fit for
  EventSourcing projections and caching.
- **Conditional appends** make concurrent writes to the graph event stream safe.
- Fully native C# 15 / .NET 11, no Rust.

See also:
- `docs/Vectors/DISKANN_SQL_DDL.md`
- `docs/Vectors/MUNARIUM_INSPIRATION_PLAN.md`
- `src/SharpCoreDB.VectorSearch/Index/DiskAnnIndex.cs`
- `src/SharpCoreDB.Graph.Advanced/GraphRAG/VectorSearchIntegration.cs`

This example workflow is copy-paste ready and works with the current EventSourcing package.

## Next steps

The example is deliberately minimal. Natural extensions: a fully runnable demo with a real embedding call,
EventSourcing snapshotting / upcasting, and the write-path work tracked in
`docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md`.