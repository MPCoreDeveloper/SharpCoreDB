# OpenAI Integration Guide for GraphRAG

**SharpCoreDB.Graph.Advanced v2.0.0**  
**Integration:** OpenAI Embeddings API  
**Time to Complete:** 20 minutes

## Overview

This guide shows how to integrate OpenAI's embedding models with GraphRAG for semantic search. You'll learn to generate embeddings, index them in SharpCoreDB, and perform GraphRAG searches with real semantic understanding.

## Prerequisites

- OpenAI API key with sufficient credits
- SharpCoreDB.Graph.Advanced 2.0.0+
- SharpCoreDB.VectorSearch 1.3.5+
- .NET 11 SDK

## Step 1: Setup OpenAI Client

### Install Dependencies

```bash
dotnet add package System.Net.Http.Json
dotnet add package Microsoft.Extensions.Http
```

### Create OpenAI Embedding Provider

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;

public class OpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _dimensions;

    public OpenAiEmbeddingProvider(string apiKey, string model = "text-embedding-3-small", int dimensions = 1536)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _apiKey = apiKey;
        _model = model;
        _dimensions = dimensions;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var request = new OpenAiEmbeddingRequest
        {
            Input = text,
            Model = _model,
            Dimensions = _dimensions
        };

        var response = await _httpClient.PostAsJsonAsync(
            "https://api.openai.com/v1/embeddings", request, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(ct);
        return result!.Data[0].Embedding;
    }

    public async Task<Dictionary<ulong, float[]>> GenerateEmbeddingsBatchAsync(
        Dictionary<ulong, string> nodeTexts, CancellationToken ct = default)
    {
        var results = new Dictionary<ulong, float[]>();
        var batchSize = 100; // OpenAI limit for text-embedding-3-small

        foreach (var batch in nodeTexts.Chunk(batchSize))
        {
            var batchTexts = batch.Select(kvp => kvp.Value).ToArray();
            var embeddings = await GenerateEmbeddingsAsync(batchTexts, ct);

            for (int i = 0; i < batch.Length; i++)
            {
                results[batch[i].Key] = embeddings[i];
            }

            // Rate limiting: 1 request per second for free tier
            await Task.Delay(1100, ct);
        }

        return results;
    }

    private async Task<float[][]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        var request = new OpenAiEmbeddingRequest
        {
            Input = texts,
            Model = _model,
            Dimensions = _dimensions
        };

        var response = await _httpClient.PostAsJsonAsync(
            "https://api.openai.com/v1/embeddings", request, ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(ct);
        return result!.Data.Select(d => d.Embedding).ToArray();
    }
}

// Request/Response models
public record OpenAiEmbeddingRequest(
    [property: JsonPropertyName("input")] object Input,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("dimensions")] int? Dimensions = null
);

public record OpenAiEmbeddingResponse(
    [property: JsonPropertyName("data")] List<EmbeddingData> Data,
    [property: JsonPropertyName("usage")] UsageInfo Usage
);

public record EmbeddingData(
    [property: JsonPropertyName("embedding")] float[] Embedding,
    [property: JsonPropertyName("index")] int Index
);

public record UsageInfo(
    [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
    [property: JsonPropertyName("total_tokens")] int TotalTokens
);

// Interface for embedding providers
public interface IEmbeddingProvider
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
    Task<Dictionary<ulong, float[]>> GenerateEmbeddingsBatchAsync(
        Dictionary<ulong, string> nodeTexts, CancellationToken ct = default);
}
```

## Step 2: Setup GraphRAG with OpenAI

### Initialize Database and Engine

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Graph.Advanced.GraphRAG;

var services = new ServiceCollection();
services.AddSharpCoreDB()
    .AddVectorSupport(); // Required for vector search

var provider = services.BuildServiceProvider();
var database = provider.GetRequiredService<IDatabase>();

// Create database
database = new Database(services,
    Path.Combine(Path.GetTempPath(), "graphrag_openai.db"), "tutorial_password");

// Initialize GraphRAG engine
var engine = new GraphRagEngine(
    database: database,
    graphTableName: "knowledge_graph",
    embeddingTableName: "node_embeddings",
    embeddingDimensions: 1536  // text-embedding-3-large
);

// Setup tables and indexes
await engine.InitializeAsync();
```

### Create Knowledge Graph Data

```csharp
// Create graph table
database.ExecuteSQL(@"
    CREATE TABLE knowledge_graph (
        source INTEGER,
        target INTEGER,
        relationship TEXT DEFAULT 'related_to'
    )
");

// Create knowledge nodes table (for content)
database.ExecuteSQL(@"
    CREATE TABLE knowledge_nodes (
        id INTEGER PRIMARY KEY,
        title TEXT,
        content TEXT,
        category TEXT
    )
");

// Insert sample knowledge graph
var knowledgeData = new[]
{
    // AI Concepts
    (1, "Artificial Intelligence", "AI is the simulation of human intelligence in machines", "ai"),
    (2, "Machine Learning", "ML is a subset of AI that enables systems to learn from data", "ai"),
    (3, "Deep Learning", "DL uses neural networks with multiple layers", "ai"),
    (4, "Neural Networks", "NNs are computing systems inspired by biological brains", "ai"),

    // Graph Theory
    (5, "Graph Theory", "Mathematical study of graphs and networks", "math"),
    (6, "Graph Algorithms", "Algorithms for processing graph structures", "cs"),
    (7, "Community Detection", "Finding groups in networks", "cs"),
    (8, "Centrality Measures", "Quantifying node importance in graphs", "math"),

    // Relationships
    (1, 2, "contains"), (2, 3, "uses"), (3, 4, "based_on"),  // AI hierarchy
    (5, 6, "enables"), (6, 7, "includes"), (6, 8, "includes"), // Graph applications
    (7, 1, "used_in"), (8, 1, "used_in") // Cross-domain connections
};

foreach (var (id, title, content, category) in knowledgeData)
{
    database.ExecuteSQL($@"
        INSERT INTO knowledge_nodes (id, title, content, category)
        VALUES ({id}, '{title.Replace("'", "''")}', '{content.Replace("'", "''")}', '{category}')");
}

// Insert graph relationships
var relationships = new[]
{
    (1, 2), (2, 3), (3, 4),  // AI chain
    (5, 6), (6, 7), (6, 8),  // Graph chain
    (7, 1), (8, 1)           // Cross connections
};

foreach (var (source, target) in relationships)
{
    database.ExecuteSQL($@"
        INSERT INTO knowledge_graph (source, target)
        VALUES ({source}, {target})");
}

database.Flush();
```

## Step 3: Generate and Index Embeddings

### Generate Embeddings for Knowledge Nodes

```csharp
// Initialize OpenAI provider
var embeddingProvider = new OpenAiEmbeddingProvider(
    apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "your-api-key-here",
    model: "text-embedding-3-large",  // 1536 dimensions
    dimensions: 1536
);

// Get node content for embedding
var nodeContents = new Dictionary<ulong, string>();
var nodes = database.ExecuteQuery("SELECT id, title, content FROM knowledge_nodes");

foreach (var node in nodes)
{
    var id = Convert.ToUInt64(node["id"]);
    var title = node["title"]?.ToString() ?? "";
    var content = node["content"]?.ToString() ?? "";

    // Combine title and content for richer embeddings
    var textForEmbedding = $"{title}: {content}";
    nodeContents[id] = textForEmbedding;
}

Console.WriteLine($"Generating embeddings for {nodeContents.Count} nodes...");

// Generate embeddings (with rate limiting)
var embeddings = await embeddingProvider.GenerateEmbeddingsBatchAsync(nodeContents);

Console.WriteLine($"Generated {embeddings.Count} embeddings");

// Convert to NodeEmbedding format
var nodeEmbeddings = embeddings
    .Select(kvp => new VectorSearchIntegration.NodeEmbedding(kvp.Key, kvp.Value))
    .ToList();

// Index in GraphRAG
await engine.IndexEmbeddingsAsync(nodeEmbeddings);

Console.WriteLine("Embeddings indexed successfully!");
```

## Step 4: Perform GraphRAG Search

### Basic Semantic Search

```csharp
// Search for AI-related concepts
var query = "artificial intelligence and machine learning";
var queryEmbedding = await embeddingProvider.GenerateEmbeddingAsync(query);

var results = await engine.SearchAsync(
    queryEmbedding: queryEmbedding,
    topK: 5,
    includeCommunities: true
);

Console.WriteLine($"Search results for: '{query}'");
foreach (var result in results)
{
    var nodeInfo = database.ExecuteQuery($"SELECT title, category FROM knowledge_nodes WHERE id = {result.NodeId}").First();
    Console.WriteLine($"• {nodeInfo["title"]} (Score: {result.CombinedScore:F3})");
    Console.WriteLine($"  Context: {result.Context}");
    Console.WriteLine($"  Category: {nodeInfo["category"]}");
    Console.WriteLine();
}
```

### Advanced Search with Multi-Hop Reasoning

```csharp
// Search with multi-hop reasoning
var advancedResults = await engine.SearchAsync(
    queryEmbedding: queryEmbedding,
    topK: 8,
    includeCommunities: true,
    maxHops: 2,  // Consider 2nd-degree connections
    rankingWeights: (semantic: 0.5, topological: 0.3, community: 0.2)
);

Console.WriteLine("Advanced search with multi-hop reasoning:");
foreach (var result in advancedResults)
{
    var context = await engine.GetNodeContextAsync(result.NodeId, maxDistance: 2);
    Console.WriteLine($"Node {result.NodeId}: {context.ContextDescription}");

    if (context.SemanticNeighbors.Count > 0)
    {
        Console.WriteLine("  Most similar: " +
            $"{context.SemanticNeighbors.First().nodeId} " +
            $"(similarity: {context.SemanticNeighbors.First().similarity:F3})");
    }

    Console.WriteLine($"  Community: {context.CommunityId} ({context.CommunityMembers.Count} members)");
    Console.WriteLine();
}
```

## Step 5: Performance Monitoring

### Track API Usage and Costs

```csharp
public class OpenAiUsageTracker
{
    private int _totalTokens = 0;
    private int _totalRequests = 0;
    private decimal _estimatedCost = 0;

    public void TrackUsage(UsageInfo usage, string model)
    {
        _totalTokens += usage.TotalTokens;
        _totalRequests++;

        // Cost calculation (as of 2024 pricing)
        var costPer1K = model.Contains("large") ? 0.00013m : 0.00002m;
        _estimatedCost += (usage.TotalTokens / 1000m) * costPer1K;
    }

    public void PrintReport()
    {
        Console.WriteLine("OpenAI Usage Report:");
        Console.WriteLine($"  Total Requests: {_totalRequests}");
        Console.WriteLine($"  Total Tokens: {_totalTokens}");
        Console.WriteLine($"  Estimated Cost: ${_estimatedCost:F4}");
        Console.WriteLine($"  Avg Tokens/Request: {_totalTokens / (double)_totalRequests:F1}");
    }
}

// Usage tracking wrapper
public class TrackedOpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly OpenAiEmbeddingProvider _inner;
    private readonly OpenAiUsageTracker _tracker;

    public TrackedOpenAiEmbeddingProvider(string apiKey, OpenAiUsageTracker tracker)
    {
        _inner = new OpenAiEmbeddingProvider(apiKey);
        _tracker = tracker;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        // This would need to be modified to capture usage from individual requests
        // For simplicity, we'll track in batch operations
        return await _inner.GenerateEmbeddingAsync(text, ct);
    }

    public async Task<Dictionary<ulong, float[]>> GenerateEmbeddingsBatchAsync(
        Dictionary<ulong, string> nodeTexts, CancellationToken ct = default)
    {
        var results = await _inner.GenerateEmbeddingsBatchAsync(nodeTexts, ct);

        // Track usage (simplified - you'd need to capture from actual API responses)
        _tracker.TrackUsage(new UsageInfo(PromptTokens: nodeTexts.Count * 5, TotalTokens: nodeTexts.Count * 10),
                          "text-embedding-3-large");

        return results;
    }
}
```

### Monitor GraphRAG Performance

```csharp
// Profile search performance
var performanceMetrics = await PerformanceProfiler.ProfileSearchAsync(
    engine, queryEmbedding, topK: 5, iterations: 3);

Console.WriteLine("GraphRAG Performance:");
Console.WriteLine($"  Average Search Time: {performanceMetrics.TotalDuration.TotalMilliseconds:F2}ms");
Console.WriteLine($"  Memory Usage: {performanceMetrics.MemoryUsedBytes / 1024:F2} KB");
Console.WriteLine($"  Throughput: {performanceMetrics.OperationsPerSecond:F2} ops/sec");

// Generate comprehensive benchmark report
var benchmarks = await PerformanceProfiler.RunComprehensiveBenchmarkAsync(engine, queryEmbedding);
var report = PerformanceProfiler.GeneratePerformanceReport(benchmarks);
Console.WriteLine("\nBenchmark Report:");
Console.WriteLine(report);
```

## Step 6: Production Considerations

### Error Handling and Retry Logic

```csharp
public class ResilientOpenAiEmbeddingProvider : IEmbeddingProvider
{
    private readonly OpenAiEmbeddingProvider _inner;
    private readonly ILogger _logger;

    public ResilientOpenAiEmbeddingProvider(OpenAiEmbeddingProvider inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        return await retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                return await _inner.GenerateEmbeddingAsync(text, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Rate limited by OpenAI, retrying...");
                throw; // Let retry policy handle it
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "OpenAI API error");
                throw;
            }
        });
    }

    public async Task<Dictionary<ulong, float[]>> GenerateEmbeddingsBatchAsync(
        Dictionary<ulong, string> nodeTexts, CancellationToken ct = default)
    {
        // Implement batch-specific retry logic
        // Split large batches if needed
        // Handle partial failures
        return await _inner.GenerateEmbeddingsBatchAsync(nodeTexts, ct);
    }
}
```

### Caching Embeddings

```csharp
public class CachedEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider _inner;
    private readonly Dictionary<string, float[]> _cache = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(text, out var cached))
            {
                return cached;
            }

            var embedding = await _inner.GenerateEmbeddingAsync(text, ct);
            _cache[text] = embedding;
            return embedding;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Dictionary<ulong, float[]>> GenerateEmbeddingsBatchAsync(
        Dictionary<ulong, string> nodeTexts, CancellationToken ct = default)
    {
        var results = new Dictionary<ulong, float[]>();
        var uncached = new Dictionary<ulong, string>();

        // Check cache first
        await _semaphore.WaitAsync(ct);
        try
        {
            foreach (var (id, text) in nodeTexts)
            {
                if (_cache.TryGetValue(text, out var cached))
                {
                    results[id] = cached;
                }
                else
                {
                    uncached[id] = text;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }

        // Generate uncached embeddings
        if (uncached.Count > 0)
        {
            var newEmbeddings = await _inner.GenerateEmbeddingsBatchAsync(uncached, ct);

            await _semaphore.WaitAsync(ct);
            try
            {
                foreach (var (id, embedding) in newEmbeddings)
                {
                    var text = uncached[id];
                    _cache[text] = embedding;
                    results[id] = embedding;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        return results;
    }
}
```

## Complete Example Application

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Graph.Advanced.GraphRAG;
using Polly;

class Program
{
    static async Task Main()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.WriteLine("Please set OPENAI_API_KEY environment variable");
            return;
        }

        // Setup services
        var services = new ServiceCollection();
        services.AddSharpCoreDB().AddVectorSupport();
        var provider = services.BuildServiceProvider();

        // Initialize components
        var database = new Database(provider, "graphrag_openai.db", "password");
        var usageTracker = new OpenAiUsageTracker();

        var embeddingProvider = new CachedEmbeddingProvider(
            new ResilientOpenAiEmbeddingProvider(
                new OpenAiEmbeddingProvider(apiKey), new LoggerFactory().CreateLogger("OpenAI")));

        var engine = new GraphRagEngine(database, "knowledge_graph", "embeddings", 1536);
        await engine.InitializeAsync();

        // Setup knowledge base
        await SetupKnowledgeBase(database);

        // Generate and index embeddings
        await IndexKnowledgeBase(database, engine, embeddingProvider, usageTracker);

        // Interactive search
        await RunInteractiveSearch(engine, embeddingProvider, usageTracker);
    }

    static async Task SetupKnowledgeBase(Database database)
    {
        // Create tables and insert data (as shown in Step 2)
        // ... implementation ...
    }

    static async Task IndexKnowledgeBase(Database database, GraphRagEngine engine,
        IEmbeddingProvider embeddingProvider, OpenAiUsageTracker tracker)
    {
        // Generate and index embeddings (as shown in Step 3)
        // ... implementation ...
    }

    static async Task RunInteractiveSearch(GraphRagEngine engine,
        IEmbeddingProvider embeddingProvider, OpenAiUsageTracker tracker)
    {
        while (true)
        {
            Console.Write("Enter search query (or 'quit'): ");
            var query = Console.ReadLine();
            if (query?.ToLower() == "quit") break;

            var queryEmbedding = await embeddingProvider.GenerateEmbeddingAsync(query);
            var results = await engine.SearchAsync(queryEmbedding, topK: 5);

            Console.WriteLine($"\nSearch results for: '{query}'");
            foreach (var result in results)
            {
                Console.WriteLine($"• Node {result.NodeId}: Score {result.CombinedScore:F3}");
                Console.WriteLine($"  Context: {result.Context}");
            }

            tracker.PrintReport();
            Console.WriteLine();
        }
    }
}
```

## Cost Optimization

### Model Selection

| Model | Dimensions | Cost per 1K tokens | Use Case |
|-------|------------|-------------------|----------|
| `text-embedding-3-small` | 1536 | $0.02 | Good balance |
| `text-embedding-3-large` | 3072 | $0.13 | Maximum accuracy |
| `text-embedding-ada-002` | 1536 | $0.10 | Legacy |

### Batching Strategies

```csharp
// Optimal batch sizes for different models
var batchSizes = new Dictionary<string, int>
{
    ["text-embedding-3-small"] = 100,   // 100 texts per request
    ["text-embedding-3-large"] = 50,    // 50 texts per request (slower)
    ["text-embedding-ada-002"] = 50     // 50 texts per request
};

// Rate limits (requests per minute)
var rateLimits = new Dictionary<string, int>
{
    ["text-embedding-3-small"] = 60,    // 60 RPM
    ["text-embedding-3-large"] = 30,    // 30 RPM
    ["text-embedding-ada-002"] = 30     // 30 RPM
};
```

## Troubleshooting

### Common Issues

**Rate Limiting**
```
Error: 429 Too Many Requests
Solution: Implement exponential backoff and reduce batch sizes
```

**Invalid API Key**
```
Error: 401 Unauthorized
Solution: Check API key and billing status
```

**Embedding Dimension Mismatch**
```
Error: Vector dimension mismatch
Solution: Ensure all embeddings use the same dimensions
```

**High Costs**
```
Issue: Unexpected high API costs
Solution: Implement caching, monitor usage, use smaller models for testing
```

### Monitoring API Health

```csharp
public class OpenAiHealthMonitor
{
    private readonly HttpClient _httpClient;
    private DateTime _lastHealthyCheck = DateTime.MinValue;

    public async Task<bool> IsHealthyAsync()
    {
        if (DateTime.UtcNow - _lastHealthyCheck < TimeSpan.FromMinutes(5))
        {
            return true; // Cache health checks
        }

        try
        {
            // Simple health check - models endpoint doesn't require authentication
            var response = await _httpClient.GetAsync("https://api.openai.com/v1/models");
            if (response.IsSuccessStatusCode)
            {
                _lastHealthyCheck = DateTime.UtcNow;
                return true;
            }
        }
        catch
        {
            // Network or service issues
        }

        return false;
    }
}
```

## Conclusion

OpenAI integration brings powerful semantic understanding to GraphRAG. Key benefits:

- **High-Quality Embeddings**: State-of-the-art semantic representations
- **Easy Integration**: Simple REST API with comprehensive .NET client
- **Cost Effective**: Reasonable pricing with batch processing
- **Production Ready**: Rate limiting, error handling, and monitoring

The combination of OpenAI embeddings with GraphRAG's graph-aware ranking provides search results that understand both semantic meaning and relational context.

---

**For basic GraphRAG usage, see:** `docs/examples/graphrag-basic-usage.md`  
**For performance tuning, see:** `docs/performance/graphrag-performance-tuning.md`  
**For API reference, see:** `docs/api/SharpCoreDB.Graph.Advanced.API.md`
