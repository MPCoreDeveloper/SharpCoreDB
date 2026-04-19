# GRAPH_RAG single SQL statement (v1.7.0)

`GRAPH_RAG` enables vector + graph retrieval in a single SELECT statement.

## Syntax

```sql
SELECT <columns>
FROM <table>
GRAPH_RAG '<natural language question>'
  [LIMIT <n>]
  [WITH SCORE > <threshold>]
  [WITH CONTEXT]
  [TOP_K <n>];
```

## Example

```sql
SELECT *
FROM documents
GRAPH_RAG 'What are the risks of GDPR in AI?'
  LIMIT 5
  WITH SCORE > 0.85
  WITH CONTEXT
  TOP_K 25;
```

## Notes

- `GRAPH_RAG` is a top-level SELECT clause parsed by `EnhancedSqlParser`.
- Execution is delegated to an optional `IGraphRagProvider` implementation.
- Core storage engine, WAL, and file format are unchanged.
- `WITH CONTEXT` requests additional context enrichment fields in returned rows.
- `TOP_K` controls the candidate pool before final `LIMIT` + score filtering.

## Validation rules

- Question must be a non-empty SQL string literal.
- `LIMIT` must be positive.
- `TOP_K` must be positive.
- `WITH SCORE > X` requires `X` between `0` and `1`.

## Integration points

- Core parser/AST: `src/SharpCoreDB/Services`
- Provider contract: `src/SharpCoreDB/Interfaces/IGraphRagProvider.cs`
- Graph.Advanced adapter: `src/SharpCoreDB.Graph.Advanced/SqlIntegration/GraphRagSqlProvider.cs`

## DI registration (Graph.Advanced)

```csharp
var services = new ServiceCollection();
services.AddSharpCoreDB();

services.AddSharpCoreDBGraphRagSql(options =>
{
    options.GraphTableName = "graph_edges";
    options.EmbeddingTableName = "document_embeddings";
    options.EmbeddingDimensions = 384;

    // Optional: inject your embedding provider
    options.EmbeddingProvider = question => MyEmbeddingModel.Embed(question);
});
```

## Projection-aware shaping and aliases

`GRAPH_RAG` results flow through normal SQL projection rules. You can project only required fields and alias them.

```sql
SELECT node_id AS doc_id, score AS relevance, context AS snippet
FROM documents
GRAPH_RAG 'What are the risks of GDPR in AI?'
  LIMIT 5
  WITH SCORE > 0.85
  WITH CONTEXT;
```

Special field mappings in GRAPH_RAG projection:

- `node_id` (also `id`, `nodeId`) → result node identifier
- `score` → combined ranking score
- `context` → enriched context (when `WITH CONTEXT` is used)
