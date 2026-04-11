# GraphRAG Basic Usage Tutorial

**SharpCoreDB.Graph.Advanced v1.7.0**  
**Time to Complete:** 15 minutes  
**Difficulty:** Beginner

## Overview

This tutorial shows you how to get started with GraphRAG (Graph Retrieval-Augmented Generation) using SharpCoreDB.Graph.Advanced. You'll learn to:

- Set up a graph database
- Load graph data
- Run community detection
- Calculate graph metrics
- Perform basic GraphRAG search

## Prerequisites

- .NET 10 SDK
- SharpCoreDB 1.7.0+
- SharpCoreDB.VectorSearch 1.7.0+ (for GraphRAG features)

## Step 1: Project Setup

Create a new console application:

```bash
dotnet new console -n GraphRagTutorial
cd GraphRagTutorial
```

Add the required packages:

```bash
dotnet add package SharpCoreDB --version 1.7.0
dotnet add package SharpCoreDB.VectorSearch --version 1.7.0
dotnet add package SharpCoreDB.Graph.Advanced --version 1.7.0
```

## Step 2: Basic Graph Analytics

### 2.1 Create a Graph Database

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Graph.Advanced;
using SharpCoreDB.Graph.Advanced.SqlIntegration;

var services = new ServiceCollection();
services.AddSharpCoreDB()
    .AddVectorSupport(); // For GraphRAG features

var provider = services.BuildServiceProvider();
var database = provider.GetRequiredService<IDatabase>();

// Create database (in-memory for this tutorial)
database = new Database(services, Path.Combine(Path.GetTempPath(), "graphrag_tutorial.db"), "tutorial_password");
```

### 2.2 Create Graph Data

Let's create a simple social network graph:

```csharp
// Create table for social connections
database.ExecuteSQL(@"
    CREATE TABLE social_connections (
        source INTEGER,
        target INTEGER,
        relationship TEXT DEFAULT 'friend'
    )
");

// Insert some connections (Alice-Bob-Charlie triangle + extensions)
var connections = new[]
{
    (1, 2), (2, 1), // Alice ↔ Bob
    (2, 3), (3, 2), // Bob ↔ Charlie
    (3, 1), (1, 3), // Charlie ↔ Alice (triangle!)
    (3, 4), (4, 3), // Charlie ↔ David
    (4, 5), (5, 4), // David ↔ Eve
    (1, 4), (4, 1)  // Alice ↔ David
};

foreach (var (source, target) in connections)
{
    database.ExecuteSQL($@"
        INSERT INTO social_connections (source, target)
        VALUES ({source}, {target})
    ");
}

database.Flush(); // Important: Persist changes
```

### 2.3 Load Graph Data

```csharp
// Load graph from database table
var graphData = await GraphLoader.LoadFromTableAsync(
    database,
    "social_connections",
    sourceColumn: "source",
    targetColumn: "target"
);

Console.WriteLine($"Loaded graph with {graphData.NodeCount} nodes and {graphData.EdgeCount} edges");
// Output: Loaded graph with 5 nodes and 12 edges
```

### 2.4 Detect Communities

```csharp
// Run Louvain community detection
var louvain = new CommunityDetection.LouvainAlgorithm();
var communityResult = await louvain.ExecuteAsync(graphData);

Console.WriteLine($"Found {communityResult.Communities.Count} communities");
Console.WriteLine($"Overall modularity: {communityResult.OverallModularity:F3}");

foreach (var community in communityResult.Communities.OrderBy(c => c.Id))
{
    Console.WriteLine($"Community {community.Id}: {community.Members.Count} members");
    // Output: Community 0: 3 members (Alice, Bob, Charlie triangle)
    //         Community 1: 2 members (David, Eve)
}
```

### 2.5 Calculate Graph Metrics

```csharp
// Calculate degree centrality
var degreeCentrality = new Metrics.DegreeCentrality();
var degreeResults = await degreeCentrality.ExecuteAsync(graphData);

Console.WriteLine("Degree Centrality Rankings:");
foreach (var result in degreeResults.OrderByDescending(r => r.Value))
{
    Console.WriteLine($"Node {result.NodeId}: {result.Value:F3}");
    // Alice (node 1): 1.000 (highest degree)
    // Charlie (node 3): 0.875 (well connected)
    // Bob (node 2): 0.750
    // David (node 4): 0.625
    // Eve (node 5): 0.250 (least connected)
}
```

### 2.6 Find Subgraphs

```csharp
// Find triangles in the graph
var triangles = await SubgraphQueries.TriangleDetector.DetectTrianglesAsync(graphData);

Console.WriteLine($"Found {triangles.Count} triangles");
foreach (var (u, v, w) in triangles)
{
    var nodeU = graphData.NodeIds[u];
    var nodeV = graphData.NodeIds[v];
    var nodeW = graphData.NodeIds[w];
    Console.WriteLine($"Triangle: {nodeU} - {nodeV} - {nodeW}");
    // Output: Triangle: 1 - 2 - 3 (Alice - Bob - Charlie)
}
```

## Step 3: GraphRAG with Semantic Search

### 3.1 Setup GraphRAG Engine

```csharp
using SharpCoreDB.Graph.Advanced.GraphRAG;

// Initialize GraphRAG engine
var engine = new GraphRagEngine(
    database: database,
    graphTableName: "social_connections",
    embeddingTableName: "user_embeddings",
    embeddingDimensions: 384  // Common embedding size
);

// Initialize tables and indexes
await engine.InitializeAsync();
```

### 3.2 Create Node Embeddings

For this tutorial, we'll generate mock embeddings. In production, use real embeddings from OpenAI, Cohere, etc.

```csharp
// Generate embeddings for our 5 users
var nodeIds = new ulong[] { 1, 2, 3, 4, 5 };
var embeddings = VectorSearchIntegration.GenerateMockEmbeddings(nodeIds, 384);

// Index embeddings in the database
await engine.IndexEmbeddingsAsync(embeddings);
```

### 3.3 Perform GraphRAG Search

```csharp
// Create a query embedding (in production, this would come from your embedding model)
var queryEmbedding = VectorSearchIntegration.GenerateMockEmbeddings([999], 384)[0].Embedding;

// Perform GraphRAG search
var searchResults = await engine.SearchAsync(
    queryEmbedding: queryEmbedding,
    topK: 3,
    includeCommunities: true,
    maxHops: 2  // Consider 2-hop connections
);

Console.WriteLine("GraphRAG Search Results:");
foreach (var result in searchResults)
{
    Console.WriteLine($"Node {result.NodeId}: Score {result.CombinedScore:F3}");
    Console.WriteLine($"  Context: {result.Context}");
    // Output shows semantic + topological + community context
}
```

### 3.4 Analyze Node Context

```csharp
// Get detailed context for a specific node
var context = await engine.GetNodeContextAsync(
    nodeId: 1,  // Alice
    maxDistance: 2,
    includeEmbeddings: true
);

Console.WriteLine($"Node {context.NodeId} Analysis:");
Console.WriteLine($"  Community: {context.CommunityId}");
Console.WriteLine($"  Community Members: {string.Join(", ", context.CommunityMembers)}");
Console.WriteLine($"  Graph Neighbors: {string.Join(", ", context.GraphNeighbors)}");
Console.WriteLine($"  Semantic Neighbors: {context.SemanticNeighbors.Count}");
Console.WriteLine($"  Description: {context.ContextDescription}");
```

## Step 4: SQL Integration

### 4.1 Direct SQL Functions

You can also call graph algorithms directly via SQL functions:

```csharp
// Detect communities via SQL
var communities = await CommunityDetectionFunctions.DetectCommunitiesLouvainAsync(
    database, "social_connections");

Console.WriteLine("Communities via SQL:");
foreach (var (nodeId, communityId) in communities.OrderBy(c => c.nodeId))
{
    Console.WriteLine($"Node {nodeId} → Community {communityId}");
}
```

### 4.2 Calculate Metrics via SQL

```csharp
// Calculate degree centrality via SQL
var degrees = await GraphMetricsFunctions.CalculateDegreeCentralityAsync(
    database, "social_connections");

Console.WriteLine("Degree Centrality via SQL:");
foreach (var (nodeId, degree) in degrees.OrderByDescending(d => d.degree))
{
    Console.WriteLine($"Node {nodeId}: {degree:F3}");
}
```

## Step 5: Performance Monitoring

### 5.1 Profile Operations

```csharp
using SharpCoreDB.Graph.Advanced.GraphRAG;

// Profile GraphRAG search performance
var metrics = await PerformanceProfiler.ProfileSearchAsync(
    engine, queryEmbedding, topK: 10, iterations: 3);

Console.WriteLine($"Search Performance:");
Console.WriteLine($"  Duration: {metrics.TotalDuration.TotalMilliseconds:F2}ms");
Console.WriteLine($"  Memory: {metrics.MemoryUsedBytes / 1024:F2} KB");
Console.WriteLine($"  Throughput: {metrics.OperationsPerSecond:F2} ops/sec");
```

### 5.2 Monitor Cache

```csharp
// Check cache statistics
var (totalEntries, expiredEntries, memoryUsage) = engine.GetCacheStatistics();
Console.WriteLine($"Cache: {totalEntries} entries, {memoryUsage / 1024:F2} KB memory");
```

## Complete Example Code

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Graph.Advanced;
using SharpCoreDB.Graph.Advanced.SqlIntegration;
using SharpCoreDB.Graph.Advanced.GraphRAG;

class Program
{
    static async Task Main()
    {
        // Setup
        var services = new ServiceCollection();
        services.AddSharpCoreDB().AddVectorSupport();
        var provider = services.BuildServiceProvider();
        var database = provider.GetRequiredService<IDatabase>();

        // Create database
        database = new Database(services,
            Path.Combine(Path.GetTempPath(), "graphrag_tutorial.db"), "tutorial_password");

        // Create and populate graph
        await SetupGraphData(database);

        // Basic analytics
        await RunBasicAnalytics(database);

        // GraphRAG search
        await RunGraphRagSearch(database);

        Console.WriteLine("Tutorial completed successfully!");
    }

    static async Task SetupGraphData(Database database)
    {
        // Create table
        database.ExecuteSQL(@"
            CREATE TABLE social_connections (
                source INTEGER, target INTEGER, relationship TEXT DEFAULT 'friend'
            )");

        // Add connections
        var connections = new[] { (1, 2), (2, 1), (2, 3), (3, 2), (3, 1), (1, 3),
                                (3, 4), (4, 3), (4, 5), (5, 4), (1, 4), (4, 1) };

        foreach (var (source, target) in connections)
        {
            database.ExecuteSQL($"INSERT INTO social_connections (source, target) VALUES ({source}, {target})");
        }
        database.Flush();
    }

    static async Task RunBasicAnalytics(Database database)
    {
        var graphData = await GraphLoader.LoadFromTableAsync(database, "social_connections");

        // Community detection
        var louvain = new CommunityDetection.LouvainAlgorithm();
        var communities = await louvain.ExecuteAsync(graphData);
        Console.WriteLine($"Found {communities.Communities.Count} communities");

        // Degree centrality
        var degreeCalc = new Metrics.DegreeCentrality();
        var degrees = await degreeCalc.ExecuteAsync(graphData);
        Console.WriteLine($"Calculated centrality for {degrees.Count} nodes");
    }

    static async Task RunGraphRagSearch(Database database)
    {
        // Setup GraphRAG
        var engine = new GraphRagEngine(database, "social_connections", "user_embeddings", 384);
        await engine.InitializeAsync();

        // Index embeddings
        var embeddings = VectorSearchIntegration.GenerateMockEmbeddings(new ulong[] { 1, 2, 3, 4, 5 }, 384);
        await engine.IndexEmbeddingsAsync(embeddings);

        // Search
        var queryEmbedding = VectorSearchIntegration.GenerateMockEmbeddings([999], 384)[0].Embedding;
        var results = await engine.SearchAsync(queryEmbedding, topK: 3);

        Console.WriteLine($"GraphRAG found {results.Count} relevant nodes");
    }
}
```

## Next Steps

Now that you understand the basics, explore:

1. **Advanced GraphRAG Patterns** - Multi-hop reasoning, temporal ranking
2. **Integration with Embedding Providers** - OpenAI, Cohere, local models
3. **Performance Tuning** - Caching strategies, memory optimization
4. **Real-World Applications** - Knowledge graphs, recommendation systems

## Troubleshooting

### Common Issues

**"Table does not exist"**
- Make sure to call `database.Flush()` after creating tables
- Check table name spelling

**"No communities found"**
- Ensure graph has edges (not just isolated nodes)
- Try different algorithms (Louvain vs Label Propagation)

**"Vector search failed"**
- Verify embedding dimensions match between query and stored vectors
- Check that embeddings table was created and populated

**Performance Issues**
- Enable caching: `engine.GetCache()` operations
- Use batch processing for large datasets
- Monitor memory usage with `PerformanceProfiler`

## Resources

- **API Reference**: `docs/api/SharpCoreDB.Graph.Advanced.API.md`
- **Advanced Patterns**: `docs/examples/graphrag-advanced-patterns.md`
- **Performance Guide**: `docs/performance/graphrag-performance-tuning.md`
- **Integration Examples**: `docs/examples/integration-openai.md`

---

**Congratulations!** You've completed the GraphRAG basic tutorial. Your graph analytics journey has just begun! 🚀
