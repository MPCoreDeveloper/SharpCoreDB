# SharpCoreDB GraphRAG AI Documentation Assistant

**Demonstration of how GraphRAG enhances AI responses by combining semantic search with graph relationships.**

This example shows how to build an intelligent documentation assistant that uses:
- **Vector Search** - Find semantically similar documents
- **Graph Traversal** - Discover related concepts, prerequisites, and dependencies
- **Hybrid Ranking** - Combine signals for optimal context selection
- **AI Integration** - DeepSeek/OpenAI for natural language answers

---

## The Problem with Traditional RAG

**Traditional RAG** (Retrieval Augmented Generation) uses only vector similarity:

```
User: "How do I implement JWT authentication?"

Vector Search finds:
✓ "JWT Authentication Guide" (similarity: 0.94)
✓ "OAuth 2.0 Overview" (similarity: 0.87)
✓ "API Security Best Practices" (similarity: 0.82)

AI Response: Explains JWT but misses critical context!
❌ No mention of prerequisite: User Models & Database
❌ No mention of dependency: HTTP Headers & Cookies
❌ No mention of follow-up: Token Refresh Strategies
```

**Result**: Incomplete answer because the AI didn't get the full context.

---

## The GraphRAG Solution

**GraphRAG** adds graph relationships to vector search:

```
User: "How do I implement JWT authentication?"

Step 1: Vector Search (Semantic Similarity)
✓ "JWT Authentication Guide" (0.94)
✓ "OAuth 2.0 Overview" (0.87)
✓ "API Security Best Practices" (0.82)

Step 2: Graph Traversal (Relationship Discovery)
🔗 "User Models & Database" [DependsOn, 1 hop]
🔗 "HTTP Headers & Cookies" [DependsOn, 1 hop]
🔗 "Role-Based Access Control" [RelatedTo, 2 hops]
🔗 "Token Refresh Strategies" [FollowsFrom, 2 hops]
🔗 "Database Connection Setup" [Prerequisite chain, 2 hops]

Step 3: Hybrid Ranking
Combines vector similarity + graph centrality + relationship weights

AI Response: Complete, actionable answer!
✅ Explains JWT implementation
✅ Mentions prerequisites (User models, DB setup)
✅ Covers dependencies (HTTP headers)
✅ Suggests next steps (Token refresh, RBAC)
```

**Result**: AI gets the full picture and provides better answers!

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                       User Question                         │
│              "How do I implement authentication?"           │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 1: Vector Search (SharpCoreDB.VectorSearch)          │
│  • Generate query embedding                                 │
│  • Find top-K similar documents (cosine similarity)         │
│  • Returns: [(doc1, 0.94), (doc2, 0.87), ...]              │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 2: Graph Traversal (SharpCoreDB.Graph.Advanced)      │
│  • Start from top vector matches                            │
│  • Traverse relationships (BFS, max 2 hops)                 │
│  • Relationship types:                                      │
│    - Prerequisite (foundational knowledge)                  │
│    - DependsOn (direct dependencies)                        │
│    - RelatedTo (adjacent concepts)                          │
│    - FollowsFrom (next steps)                               │
│  • Returns: Additional related documents                    │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 3: Hybrid Ranking                                     │
│  • Combine scores:                                          │
│    - Vector similarity weight: 0.6                          │
│    - Graph centrality weight: 0.3                           │
│    - Relationship type weight: 0.1                          │
│  • Filter by minimum score threshold                        │
│  • Take top N results                                       │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 4: Context Assembly                                   │
│  • Build prompt with all retrieved documents                │
│  • Include document metadata (scores, relationships)        │
│  • Add instructions for citation                            │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 5: AI Generation (DeepSeek/OpenAI)                   │
│  • Send enriched prompt to LLM                              │
│  • Streaming or complete response                           │
│  • Extract source citations                                 │
└────────────────────────┬────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│          AI Answer + Source Citations                       │
└─────────────────────────────────────────────────────────────┘
```

---

## Quick Start

### 1. Install Prerequisites

- .NET 10 SDK
- Visual Studio 2026 or VS Code
- DeepSeek or OpenAI API key (optional, for AI answers)

### 2. Configure API Key (Optional)

Edit `appsettings.json`:

```json
{
  "AI": {
    "Provider": "DeepSeek",
    "ApiKey": "YOUR_DEEPSEEK_API_KEY",
    "Model": "deepseek-chat"
  }
}
```

Or set environment variable:
```bash
export AI__ApiKey="YOUR_API_KEY"
```

**Note:** The demo works without an API key - it will show GraphRAG search results but skip AI answer generation.

### 3. Run the Demo

```bash
cd Examples/SharpCoreDB.GraphRAG.AIAssistant
dotnet run
```

### 4. Try Example Questions

Type `demo` to run the built-in demo, or try these questions:

- "How do I implement JWT authentication?"
- "What do I need to deploy with Docker?"
- "How does OAuth 2.0 work?"
- "What are the security best practices for APIs?"
- "How do I set up database connections?"

---

## Example Output

```
╔══════════════════════════════════════════════════════════════╗
║  SharpCoreDB GraphRAG AI Documentation Assistant             ║
║  Powered by: SharpCoreDB.Graph.Advanced + VectorSearch       ║
╚══════════════════════════════════════════════════════════════╝

✅ Database initialized with 10 articles
✅ Graph contains 24 relationships
✅ Ready to answer questions!

? Your question: How do I implement JWT authentication?

┌─ SEARCHING WITH GRAPHRAG ────────────────────────────────────┐

├─ SEMANTIC SEARCH (Vector Similarity) ───────────────────────┤
Top 5 similar documents:

⭐ JWT Authentication Guide (score: 0.940)
⭐ OAuth 2.0 Overview (score: 0.870)
⭐ API Security Best Practices (score: 0.820)
⭐ User Models and Database Schema (score: 0.750)
⭐ HTTP Headers and Cookie Security (score: 0.720)

├─ GRAPH TRAVERSAL (Relationship Discovery) ──────────────────┤
Related documents via relationships:

🔗 User Models & Database [DependsOn, 1 hop] (score: 0.808)
🔗 HTTP Headers & Cookies [DependsOn, 1 hop] (score: 0.765)
🔗 Role-Based Access Control [RelatedTo, 2 hops] (score: 0.720)
🔗 Token Refresh Strategies [FollowsFrom, 2 hops] (score: 0.855)
🔗 Database Connection Setup [Prerequisite, 2 hops] (score: 0.563)

├─ HYBRID RANKING (Combined Results) ─────────────────────────┤
Final context (8 documents):

1. JWT Authentication Guide (score: 0.940) ⭐
2. Token Refresh Strategies (score: 0.855) 🔗
3. OAuth 2.0 Overview (score: 0.870) ⭐
4. API Security Best Practices (score: 0.820) ⭐
5. User Models & Database (score: 0.808) 🔗
6. HTTP Headers & Cookies (score: 0.765) 🔗
7. Role-Based Access Control (score: 0.720) 🔗
8. Database Connection Setup (score: 0.563) 🔗

├─ AI ANSWER (DeepSeek) ──────────────────────────────────────┤
🤖 Generating answer...

To implement JWT authentication in your application:

1. **Prerequisites** (from context):
   First, ensure you have a User model and database configured [4]. 
   You'll need to understand HTTP headers and cookie handling [5].

2. **Core Implementation** [1]:
   - Install JWT library (System.IdentityModel.Tokens.Jwt)
   - Create token generation endpoint at /auth/login
   - Add JWT validation middleware to protect routes
   - Configure signing key (use strong secret, min 256 bits)

3. **Security Best Practices** [3]:
   - Always use HTTPS to prevent token interception
   - Set short expiration times (15-60 minutes)
   - Implement refresh token rotation [2]
   - Store sensitive data server-side, not in JWT claims

4. **Advanced Topics** (explore next):
   - Role-Based Access Control for fine-grained permissions [6]
   - OAuth 2.0 for third-party integration [2]

Sources: [1] JWT Authentication Guide, [2] Token Refresh Strategies, 
         [3] API Security, [4] User Models, [5] HTTP Headers, 
         [6] RBAC

├─ PERFORMANCE METRICS ───────────────────────────────────────┤
⚡ Total time: 1,254ms
📊 Documents retrieved: 8
🎯 Average relevance score: 0.791

├─ 🆚 COMPARISON: Traditional RAG vs GraphRAG ───────────────┤
Without GraphRAG (vector search only):
  ❌ Missed: User Models & Database (DependsOn)
  ❌ Missed: HTTP Headers & Cookies (DependsOn)
  ❌ Missed: Token Refresh Strategies (FollowsFrom)

With GraphRAG:
  ✅ Found 8 documents total
  ✅ Discovered 5 via graph relationships
  ✅ Complete context with prerequisites and related topics
```

---

## Use Cases for GraphRAG

### ✅ Technical Documentation
- **Problem**: Users search for "deployment" but miss prerequisite topics (database setup, configuration)
- **GraphRAG**: Automatically finds prerequisites and dependencies

### ✅ Knowledge Bases
- **Problem**: Related articles exist but aren't connected by keywords
- **GraphRAG**: Graph relationships reveal adjacent concepts

### ✅ Educational Content
- **Problem**: Tutorials assume prior knowledge without explicit links
- **GraphRAG**: Traverses prerequisite chains to provide complete learning paths

### ✅ API Documentation
- **Problem**: API endpoints depend on authentication, models, configuration
- **GraphRAG**: Discovers all dependencies automatically

### ✅ Troubleshooting Guides
- **Problem**: Solutions require understanding root causes from different articles
- **GraphRAG**: Follows cause-effect relationships to find relevant context

---

## Sample Data

The demo includes **10 technical articles** on:

**Authentication & Security:**
1. JWT Authentication Guide
2. User Models and Database Schema
3. OAuth 2.0 Overview
4. HTTP Headers and Cookie Security
5. Role-Based Access Control (RBAC)
6. Token Refresh Strategies
7. API Security Best Practices

**Deployment & Infrastructure:**
8. Database Connection Setup
9. Docker Deployment Guide
10. Kubernetes Deployment

**Relationship Graph** (24 connections):
- **Prerequisites**: "HTTP Headers" → "JWT Auth" → "API Security"
- **Dependencies**: "JWT Auth" → "User Models", "Database Connection"
- **Related Topics**: "JWT" ↔ "OAuth", "JWT" ↔ "RBAC"
- **Follow-ups**: "JWT" → "Token Refresh"

---

## How It Works: Scoring Algorithm

### Hybrid Score Calculation

```csharp
HybridScore = (VectorSimilarity × 0.6) + (GraphScore × 0.3) + (RelationshipBonus × 0.1)
```

**Example:**

Document found via both vector search **and** graph traversal:
- Vector Similarity: 0.87
- Graph Centrality: 0.75 (high centrality in relationship graph)
- Relationship: "Prerequisite" from 1 hop away

```
Score = (0.87 × 0.6) + (0.75 × 0.3) + (0.1 for prerequisite bonus)
      = 0.522 + 0.225 + 0.10
      = 0.847 (High relevance!)
```

### Relationship Type Weights

```csharp
Prerequisite  → 0.9  // Most important - foundational knowledge
DependsOn     → 0.85 // Critical - direct dependencies
RelatedTo     → 0.7  // Important - related concepts
FollowsFrom   → 0.75 // Useful - next steps
SharesAPI     → 0.65 // Helpful - shared tools
```

---

## Performance Benchmarks

Typical performance on modern hardware (Intel i7, 16GB RAM):

| Operation | Time | Notes |
|---|---|---|
| Vector search (25 candidates) | ~12ms | Cosine similarity on 384-dim embeddings |
| Graph traversal (2 hops, 45 nodes) | ~8ms | BFS with relationship filtering |
| Hybrid ranking | ~2ms | In-memory score calculation |
| AI response (DeepSeek) | ~1,200ms | Network latency + token generation |
| **Total** | **~1,222ms** | Sub-second without AI, <2s with AI |

**Scaling characteristics:**
- Vector search: O(n) linear with document count (can use approximate nearest neighbors for O(log n))
- Graph traversal: O(k × d) where k = branching factor, d = depth
- Typical graph in docs: 2-3 average degree, depth limited to 2 hops → ~10-20 nodes visited

---

## Configuration Options

### `appsettings.json`

```json
{
  "Database": {
    "Path": "./documentation.scdb",
    "Password": "GraphRAG_Demo_2025!"
  },
  "AI": {
    "Provider": "DeepSeek",  // or "OpenAI"
    "ApiKey": "YOUR_API_KEY",
    "Model": "deepseek-chat" // or "gpt-4", "gpt-3.5-turbo"
  },
  "GraphRAG": {
    "TopK": 25,              // Vector search candidates
    "MaxResults": 8,         // Final results to return
    "MaxDepth": 2,           // Graph traversal hops
    "MinScore": 0.0,         // Score threshold filter
    "VectorWeight": 0.6,     // Vector similarity weight
    "GraphWeight": 0.3,      // Graph centrality weight
    "RelationshipWeight": 0.1 // Relationship bonus weight
  }
}
```

---

## Extending the Demo

### Add Your Own Documents

Edit `Data/SampleDocumentation.cs`:

```csharp
new DocumentationArticle
{
    Id = 11,
    Title = "Your Article Title",
    Content = @"Article content here...",
    Category = "YourCategory",
    Tags = ["tag1", "tag2"],
    Url = "/docs/your-article",
    DifficultyLevel = "Intermediate",
    ReadingTimeMinutes = 10
}
```

### Add Relationships

```csharp
new DocumentRelationship
{
    SourceDocId = 11,
    TargetDocId = 1,
    RelationType = RelationshipType.Prerequisite,
    Weight = 0.9
}
```

### Use Real Embeddings

Replace mock embedding generation in `DocumentationService.cs`:

```csharp
private static async Task<float[]> GenerateEmbedding(string text)
{
    // Option 1: OpenAI Embeddings API
    var response = await httpClient.PostAsJsonAsync(
        "https://api.openai.com/v1/embeddings",
        new { input = text, model = "text-embedding-3-small" });
    var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
    return result.Data[0].Embedding;

    // Option 2: Local model (Sentence Transformers via ONNX)
    // See: https://github.com/microsoft/onnxruntime
}
```

---

## Comparison: GraphRAG vs Alternatives

| Feature | Traditional RAG | GraphRAG (This Demo) | Knowledge Graph QA |
|---|---|---|---|
| **Semantic Search** | ✅ Vector embeddings | ✅ Vector embeddings | ⚠️ Often keyword-based |
| **Relationship Discovery** | ❌ No | ✅ Graph traversal | ✅ SPARQL/Cypher queries |
| **Hybrid Ranking** | ❌ Vector only | ✅ Vector + Graph + Relationships | ⚠️ Graph-only scoring |
| **Setup Complexity** | 🟢 Low | 🟡 Medium | 🔴 High (entity extraction, ontology) |
| **Handles Unstructured Text** | ✅ Native | ✅ Native | ❌ Requires preprocessing |
| **Prerequisites Discovery** | ❌ No | ✅ Automatic | ✅ Manual relationships |
| **Performance** | 🟢 Fast (~10ms) | 🟢 Fast (~20ms) | 🟡 Slower (100ms+) |

**TL;DR**: GraphRAG combines the best of vector search (semantic understanding) with graph databases (relationship awareness) without the complexity of full knowledge graph systems.

---

## Troubleshooting

### Issue: "No AI response"

**Solution**: Check your API key in `appsettings.json` or set the `AI__ApiKey` environment variable.

### Issue: "Database not found"

**Solution**: The database is created automatically on first run. Ensure you have write permissions in the app directory.

### Issue: "Graph traversal returns no results"

**Solution**: Check that relationships exist in `SampleDocumentation.cs`. The demo includes 24 pre-defined relationships.

### Issue: "Low relevance scores"

**Solution**: Mock embeddings are used in the demo (for simplicity). For production, use real embedding models (OpenAI, Sentence Transformers, etc.).

---

## Further Reading

- **SharpCoreDB GraphRAG Documentation**: `../../docs/graphrag/00_START_HERE.md`
- **Graph Traversal Strategies**: `../../docs/graphrag/LINQ_API_GUIDE.md`
- **Performance Tuning**: `../../docs/graphrag/QUERY_PLAN_CACHING.md`
- **Metrics & Observability**: `../../docs/graphrag/METRICS_AND_OBSERVABILITY_GUIDE.md`

---

## License

This example is part of the **SharpCoreDB** project and is licensed under the MIT License.

---

**Questions or feedback?** Open an issue on the [SharpCoreDB GitHub repository](https://github.com/MPCoreDeveloper/SharpCoreDB).

**Happy coding! 🚀**
