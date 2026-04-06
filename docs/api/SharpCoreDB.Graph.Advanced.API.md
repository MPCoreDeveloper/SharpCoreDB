# SharpCoreDB.Graph.Advanced API Reference

**Version:** 1.7.0 (Phase 12 Complete)  
**Target Framework:** .NET 10  
**Language:** C# 14

## Overview

SharpCoreDB.Graph.Advanced provides advanced graph analytics capabilities including community detection, centrality metrics, subgraph queries, and GraphRAG (Graph Retrieval-Augmented Generation) functionality.

## Core Concepts

### GraphData Structure
```csharp
public readonly record struct GraphData(
    ulong[] NodeIds,
    int[][] AdjacencyList,
    float[][]? EdgeWeights = null,
    bool IsDirected = false
);
```

- **NodeIds**: Array of unique node identifiers
- **AdjacencyList**: Adjacency list representation (index-based)
- **EdgeWeights**: Optional edge weights for weighted graphs
- **IsDirected**: Whether the graph is directed

### Execution Metrics
```csharp
public sealed class ExecutionMetrics
{
    public required TimeSpan Duration { get; init; }
    public int Iterations { get; init; }
    public long PeakMemoryBytes { get; init; }
    public Dictionary<string, object> CustomMetrics { get; init; } = [];
}
```

## Community Detection

### Louvain Algorithm
```csharp
/// <summary>
/// Detects communities using modularity optimization.
/// Time Complexity: O(n log n) - Quasi-linear
/// Space Complexity: O(n + m) - Linear
/// </summary>
public sealed class LouvainAlgorithm : IGraphAlgorithm<CommunityResult>
{
    public string AlgorithmName => "Louvain";
    public async Task<CommunityResult> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

**Usage:**
```csharp
var algorithm = new LouvainAlgorithm();
var result = await algorithm.ExecuteAsync(graphData);
Console.WriteLine($"Found {result.Communities.Count} communities");
```

### Label Propagation
```csharp
/// <summary>
/// Community detection via label propagation.
/// Time Complexity: O(d * m) - Depends on degree and edges
/// Space Complexity: O(n) - Linear
/// </summary>
public sealed class LabelPropagationAlgorithm : IGraphAlgorithm<CommunityResult>
{
    public string AlgorithmName => "Label Propagation";
    public async Task<CommunityResult> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

### Connected Components
```csharp
/// <summary>
/// Finds weakly connected components.
/// Time Complexity: O(n + m) - Linear
/// Space Complexity: O(n) - Linear
/// </summary>
public sealed class ConnectedComponentsAlgorithm : IGraphAlgorithm<CommunityResult>
{
    public string AlgorithmName => "Connected Components";
    public async Task<CommunityResult> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

### CommunityResult Structure
```csharp
public readonly record struct CommunityResult(
    List<Community> Communities,
    double OverallModularity
);

public readonly record struct Community(
    ulong Id,
    List<ulong> Members,
    double Modularity
);
```

## Graph Metrics

### Centrality Measures

#### Degree Centrality
```csharp
/// <summary>
/// Calculates normalized degree centrality.
/// Time Complexity: O(n) - Linear
/// </summary>
public sealed class DegreeCentrality : IGraphAlgorithm<List<GraphMetricResult>>
{
    public string AlgorithmName => "Degree Centrality";
    public async Task<List<GraphMetricResult>> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

#### Betweenness Centrality
```csharp
/// <summary>
/// Calculates betweenness centrality using Brandes' algorithm.
/// Time Complexity: O(n * m) - Quadratic
/// Space Complexity: O(n + m) - Linear
/// </summary>
public sealed class BetweennessCentrality : IGraphAlgorithm<List<GraphMetricResult>>
{
    public string AlgorithmName => "Betweenness Centrality";
    public async Task<List<GraphMetricResult>> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

#### Closeness Centrality
```csharp
/// <summary>
/// Calculates closeness centrality.
/// Time Complexity: O(n * (n + m)) - Quadratic
/// </summary>
public sealed class ClosenessCentrality : IGraphAlgorithm<List<GraphMetricResult>>
{
    public string AlgorithmName => "Closeness Centrality";
    public async Task<List<GraphMetricResult>> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

#### Eigenvector Centrality
```csharp
/// <summary>
/// Calculates eigenvector centrality using power iteration.
/// Time Complexity: O(k * m) - k iterations
/// Space Complexity: O(n) - Linear
/// </summary>
public sealed class EigenvectorCentrality : IGraphAlgorithm<List<GraphMetricResult>>
{
    public string AlgorithmName => "Eigenvector Centrality";
    public async Task<List<GraphMetricResult>> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

### Clustering Coefficient
```csharp
/// <summary>
/// Calculates local clustering coefficient.
/// Time Complexity: O(n * d²) - Depends on average degree
/// </summary>
public sealed class ClusteringCoefficient : IGraphAlgorithm<List<GraphMetricResult>>
{
    public string AlgorithmName => "Clustering Coefficient";
    public async Task<List<GraphMetricResult>> ExecuteAsync(GraphData graphData, CancellationToken ct = default);
    public ExecutionMetrics? LastExecutionMetrics { get; }
}
```

### GraphMetricResult Structure
```csharp
public readonly record struct GraphMetricResult(
    ulong NodeId,
    double Value,
    string MetricType
);
```

## Subgraph Queries

### K-Core Decomposition
```csharp
/// <summary>
/// Finds k-core decomposition of the graph.
/// Time Complexity: O(n + m) - Linear
/// </summary>
public static class KCoreDecomposition
{
    public static async Task<(int[] kCore, Dictionary<int, List<int>> cores)> DecomposeAsync(
        GraphData graphData, CancellationToken ct = default);
}
```

### Clique Detection
```csharp
/// <summary>
/// Finds all maximal cliques using Bron-Kerbosch algorithm.
/// Time Complexity: Exponential in worst case
/// </summary>
public static class CliqueDetector
{
    public static async Task<List<List<int>>> FindMaximalCliquesAsync(
        GraphData graphData, int minSize = 3, CancellationToken ct = default);
}
```

### Triangle Detection
```csharp
/// <summary>
/// Finds all triangles using node-iterator approach.
/// Time Complexity: O(m^{3/2}) - Sub-quadratic
/// </summary>
public static class TriangleDetector
{
    public static async Task<List<(int u, int v, int w)>> DetectTrianglesAsync(
        GraphData graphData, CancellationToken ct = default);
}
```

## SQL Integration

### GraphLoader
```csharp
/// <summary>
/// Loads graph data from SharpCoreDB tables.
/// </summary>
public static class GraphLoader
{
    public static async Task<GraphData> LoadFromTableAsync(
        Database database,
        string tableName,
        string sourceColumn = "source",
        string targetColumn = "target",
        string? weightColumn = null,
        bool directed = false,
        CancellationToken cancellationToken = default);

    public static async Task<GraphData> LoadFromRowRefTableAsync(
        Database database,
        string tableName,
        string sourceRefColumn = "source_ref",
        string targetRefColumn = "target_ref",
        bool directed = false,
        CancellationToken cancellationToken = default);

    public static bool ValidateGraphTable(
        Database database,
        string tableName,
        string sourceColumn = "source",
        string targetColumn = "target",
        string? weightColumn = null);
}
```

### Community Detection SQL Functions
```csharp
/// <summary>
/// SQL function implementations for community detection.
/// </summary>
public static class CommunityDetectionFunctions
{
    public static async Task<List<(ulong nodeId, ulong communityId)>> DetectCommunitiesLouvainAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, ulong communityId)>> DetectCommunitiesLPAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, ulong communityId)>> GetConnectedComponentsAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static List<ulong> GetCommunityMembers(
        Database database, string tableName, ulong communityId,
        string sourceColumn = "source", string targetColumn = "target");

    public static int GetCommunitySize(
        Database database, string tableName, ulong communityId,
        string sourceColumn = "source", string targetColumn = "target");

    public static double GetCommunityDensity(
        Database database, string tableName, ulong communityId,
        string sourceColumn = "source", string targetColumn = "target");
}
```

### Graph Metrics SQL Functions
```csharp
/// <summary>
/// SQL function implementations for graph metrics.
/// </summary>
public static class GraphMetricsFunctions
{
    public static async Task<List<(ulong nodeId, double centrality)>> CalculateBetweennessCentralityAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, double centrality)>> CalculateClosenessCentralityAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, double centrality)>> CalculateEigenvectorCentralityAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, double coefficient)>> CalculateClusteringCoefficientAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<double> CalculateGlobalClusteringCoefficientAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, double degree)>> CalculateDegreeCentralityAsync(
        Database database, string tableName, string sourceColumn = "source",
        string targetColumn = "target", CancellationToken cancellationToken = default);
}
```

### Subgraph SQL Functions
```csharp
/// <summary>
/// SQL function implementations for sub-graph queries.
/// </summary>
public static class SubgraphFunctions
{
    public static async Task<List<(ulong nodeId, ulong edgeFrom, ulong edgeTo, int distance)>> ExtractSubgraphAsync(
        Database database, string tableName, ulong rootNode, int maxDepth,
        string sourceColumn = "source", string targetColumn = "target",
        CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, int k)>> GetKCoreAsync(
        Database database, string tableName, int k,
        string sourceColumn = "source", string targetColumn = "target",
        CancellationToken cancellationToken = default);

    public static async Task<List<(int cliqueId, int memberCount, string membersJson)>> FindCliquesAsync(
        Database database, string tableName, int minSize,
        string sourceColumn = "source", string targetColumn = "target",
        CancellationToken cancellationToken = default);

    public static async Task<List<(int triangleId, ulong node1, ulong node2, ulong node3)>> FindTrianglesAsync(
        Database database, string tableName,
        string sourceColumn = "source", string targetColumn = "target",
        CancellationToken cancellationToken = default);
}
```

## GraphRAG Enhancement

### Vector Search Integration
```csharp
/// <summary>
/// Vector search integration for GraphRAG semantic similarity.
/// </summary>
public static class VectorSearchIntegration
{
    public readonly record struct NodeEmbedding(ulong NodeId, float[] Embedding);

    public static async Task CreateEmbeddingTableAsync(
        Database database, string tableName, int embeddingDimensions,
        CancellationToken cancellationToken = default);

    public static async Task InsertEmbeddingsAsync(
        Database database, string tableName, IEnumerable<NodeEmbedding> embeddings,
        CancellationToken cancellationToken = default);

    public static async Task<List<(ulong nodeId, double similarityScore)>> SemanticSimilaritySearchAsync(
        Database database, string tableName, float[] queryEmbedding, int topK = 10,
        CancellationToken cancellationToken = default);

    public static List<NodeEmbedding> GenerateMockEmbeddings(
        IEnumerable<ulong> nodeIds, int dimensions, int seed = 42);

    public static double ComputeSemanticSimilarity(float[] embedding1, float[] embedding2);

    public static bool ValidateVectorTable(Database database, string tableName);
}
```

### Enhanced Ranking
```csharp
/// <summary>
/// Enhanced ranking algorithms combining semantic, topological, and community factors.
/// </summary>
public static class EnhancedRanking
{
    public readonly record struct RankedResult(
        ulong NodeId,
        double SemanticScore,
        double TopologicalScore,
        double CommunityScore,
        double CombinedScore,
        string Context
    );

    public static List<RankedResult> RankResults(
        List<(ulong nodeId, double semanticScore)> semanticResults,
        GraphData graphData,
        List<(ulong nodeId, ulong communityId)> communities,
        ulong? queryNode = null,
        (double semantic, double topological, double community) weights = default);

    public static List<RankedResult> RankWithMultiHop(
        List<(ulong nodeId, double semanticScore)> semanticResults,
        GraphData graphData,
        List<(ulong nodeId, ulong communityId)> communities,
        int maxHops, ulong queryNode);

    public static List<(ulong nodeId, double score, string temporalContext)> RankWithTemporal(
        List<(ulong nodeId, double semanticScore)> semanticResults,
        Dictionary<ulong, (DateTime lastAccessed, int accessCount)> temporalData,
        double recencyWeight = 0.3, double frequencyWeight = 0.2);
}
```

### Result Cache
```csharp
/// <summary>
/// Intelligent caching for community detection and metrics results.
/// </summary>
public class ResultCache
{
    public async Task<T> GetOrComputeAsync<T>(
        string cacheKey, Func<CancellationToken, Task<T>> computeFunc,
        TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    public List<(ulong nodeId, ulong communityId)>? GetCommunities(string tableName, string algorithm);
    public void CacheCommunities(string tableName, string algorithm,
        List<(ulong nodeId, ulong communityId)> communities, TimeSpan? ttl = null);

    public List<(ulong nodeId, double value)>? GetMetrics(string tableName, string metricType);
    public void CacheMetrics(string tableName, string metricType,
        List<(ulong nodeId, double value)> metrics, TimeSpan? ttl = null);

    public void Clear();
    public int CleanupExpired();
    public (int totalEntries, int expiredEntries, long memoryUsage) GetStatistics();
}
```

### GraphRAG Engine
```csharp
/// <summary>
/// Main GraphRAG engine combining vector search, community detection, and enhanced ranking.
/// </summary>
public class GraphRagEngine
{
    public GraphRagEngine(Database database, string graphTableName,
        string embeddingTableName, int embeddingDimensions);

    public async Task<List<EnhancedRanking.RankedResult>> SearchAsync(
        float[] queryEmbedding, int topK = 10, bool includeCommunities = true,
        int maxHops = 0, (double semantic, double topological, double community)? rankingWeights = null,
        CancellationToken cancellationToken = default);

    public async Task<NodeContext> GetNodeContextAsync(
        ulong nodeId, int maxDistance = 2, bool includeEmbeddings = true,
        CancellationToken cancellationToken = default);

    public async Task InitializeAsync(CancellationToken cancellationToken = default);
    public async Task IndexEmbeddingsAsync(IEnumerable<VectorSearchIntegration.NodeEmbedding> nodeEmbeddings,
        CancellationToken cancellationToken = default);

    public (int totalEntries, int expiredEntries, long memoryUsage) GetCacheStatistics();
    public void ClearCache();
}

public readonly record struct NodeContext(
    ulong NodeId,
    ulong CommunityId,
    List<ulong> CommunityMembers,
    List<ulong> GraphNeighbors,
    List<(ulong nodeId, double similarity)> SemanticNeighbors,
    string ContextDescription
);
```

## Performance Profiling

### Performance Profiler
```csharp
/// <summary>
/// Performance profiling and memory optimization utilities.
/// </summary>
public static class PerformanceProfiler
{
    public readonly record struct PerformanceMetrics(
        TimeSpan TotalDuration,
        long MemoryUsedBytes,
        int NodesProcessed,
        int EdgesProcessed,
        double OperationsPerSecond,
        string OperationName
    );

    public static async Task<PerformanceMetrics> ProfileSearchAsync(
        GraphRagEngine engine, float[] queryEmbedding, int topK = 10, int iterations = 5,
        CancellationToken cancellationToken = default);

    public static async Task<PerformanceMetrics> ProfileCommunityDetectionAsync(
        Database database, string tableName, string algorithm = "louvain", int iterations = 3,
        CancellationToken cancellationToken = default);

    public static async Task<PerformanceMetrics> ProfileVectorSearchAsync(
        Database database, string tableName, float[] queryEmbedding, int topK = 10, int iterations = 5,
        CancellationToken cancellationToken = default);

    public static async Task<Dictionary<string, PerformanceMetrics>> RunComprehensiveBenchmarkAsync(
        GraphRagEngine engine, float[] queryEmbedding, CancellationToken cancellationToken = default);

    public static string GeneratePerformanceReport(Dictionary<string, PerformanceMetrics> results);
}
```

### Memory Optimization
```csharp
/// <summary>
/// Memory optimization utilities for large graph operations.
/// </summary>
public static class MemoryOptimizer
{
    public static async Task OptimizeMemoryAsync(
        Func<CancellationToken, Task> action, bool forceGC = true,
        CancellationToken cancellationToken = default);

    public static async Task ProcessInBatchesAsync<T>(
        IEnumerable<T> items, int batchSize,
        Func<List<T>, CancellationToken, Task> processor,
        CancellationToken cancellationToken = default);

    public static class MemoryEstimator
    {
        public static long EstimateGraphMemory(GraphData graph);
        public static long EstimateEmbeddingMemory(int nodeCount, int dimensions);
        public static long EstimateCommunityMemory(int nodeCount);
    }
}
```

## Error Handling

All methods follow consistent error handling patterns:

- **ArgumentNullException**: Thrown for null required parameters
- **ArgumentException**: Thrown for invalid parameter values
- **InvalidOperationException**: Thrown for invalid operations
- **OperationCanceledException**: Thrown when operations are cancelled

## Threading and Concurrency

- All async methods support `CancellationToken`
- Vector search uses SIMD acceleration when available
- Community detection algorithms are thread-safe
- Result caching uses concurrent data structures

## Performance Characteristics

| Operation | Complexity | Typical Performance | Memory Usage |
|-----------|------------|---------------------|--------------|
| Graph Loading | O(n + m) | < 50ms (1K nodes) | O(n + m) |
| Community Detection | O(n log n) | < 100ms (1K nodes) | O(n) |
| Centrality Calculation | O(n * m) | < 200ms (1K nodes) | O(n) |
| Vector Search | O(log n) | < 12ms (1K vectors) | O(1) |
| GraphRAG Search | O(query + rank) | < 45ms end-to-end | O(k) |

## Version History

- **1.7.0**: Complete GraphRAG implementation with vector search integration
- **1.0.0**: Initial graph analytics algorithms (community detection, metrics, subgraphs)
- **0.1.0**: Foundation algorithms and data structures

---

**For usage examples and tutorials, see the documentation in the `docs/` directory.**
