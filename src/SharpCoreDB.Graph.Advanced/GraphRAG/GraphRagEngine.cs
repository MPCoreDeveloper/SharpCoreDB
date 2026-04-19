#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.Graph.Advanced.SqlIntegration;

namespace SharpCoreDB.Graph.Advanced.GraphRAG;

/// <summary>
/// Main GraphRAG engine that combines vector search, community detection,
/// and enhanced ranking for semantic search with graph context.
/// </summary>
/// <remarks>
/// The engine is designed as the maintained orchestration point for the
/// <c>SharpCoreDB.Graph.Advanced</c> GraphRAG workflow. It coordinates
/// embedding search, graph loading, cached community analysis, and ranking.
/// </remarks>
public class GraphRagEngine
{
    private readonly Database _database;
    private readonly ResultCache _cache;
    private readonly string _graphTableName;
    private readonly string _embeddingTableName;
    private readonly int _embeddingDimensions;

    /// <summary>
    /// Initializes a new instance of the GraphRAG engine.
    /// </summary>
    /// <param name="database">The SharpCoreDB database instance.</param>
    /// <param name="graphTableName">Name of the table containing graph edges.</param>
    /// <param name="embeddingTableName">Name of the table containing node embeddings.</param>
    /// <param name="embeddingDimensions">Dimensions of the embedding vectors.</param>
    public GraphRagEngine(
        Database database,
        string graphTableName,
        string embeddingTableName,
        int embeddingDimensions)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(graphTableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingTableName);
        if (embeddingDimensions <= 0) throw new ArgumentOutOfRangeException(nameof(embeddingDimensions));

        _database = database;
        _cache = new ResultCache();
        _graphTableName = graphTableName;
        _embeddingTableName = embeddingTableName;
        _embeddingDimensions = embeddingDimensions;
    }

    /// <summary>
    /// Performs comprehensive GraphRAG search combining semantic similarity with graph context.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="topK">Number of top results to return.</param>
    /// <param name="includeCommunities">Whether to include community analysis.</param>
    /// <param name="maxHops">Maximum hops for multi-hop ranking (0 = disable).</param>
    /// <param name="rankingWeights">Weights for combining semantic, topological, and community scores.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked search results with comprehensive context.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryEmbedding"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="topK"/> is less than or equal to zero.</exception>
    public async Task<List<EnhancedRanking.RankedResult>> SearchAsync(
        float[] queryEmbedding,
        int topK = 10,
        bool includeCommunities = true,
        int maxHops = 0,
        (double semantic, double topological, double community)? rankingWeights = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));

        // Step 1: Perform semantic similarity search
        var semanticResults = await VectorSearchIntegration.SemanticSimilaritySearchAsync(
            _database, _embeddingTableName, queryEmbedding, topK * 2, cancellationToken); // Get more for ranking

        if (semanticResults.Count == 0)
        {
            return [];
        }

        // Step 2: Load graph data
        var graphData = await GraphLoader.LoadFromTableAsync(_database, _graphTableName,
            cancellationToken: cancellationToken);

        // Step 3: Get community information (cached)
        List<(ulong nodeId, ulong communityId)> communities = [];
        if (includeCommunities)
        {
            communities = await _cache.GetOrComputeCommunitiesAsync(
                _graphTableName,
                "louvain",
                async ct => await CommunityDetectionFunctions.DetectCommunitiesLouvainAsync(
                    _database, _graphTableName, cancellationToken: ct),
                ttl: TimeSpan.FromMinutes(30),
                cancellationToken: cancellationToken);
        }

        // Step 4: Apply enhanced ranking
        var weights = rankingWeights ?? (semantic: 0.5, topological: 0.3, community: 0.2);

        List<EnhancedRanking.RankedResult> rankedResults;

        if (maxHops > 0 && semanticResults.Count > 0)
        {
            // Use multi-hop ranking
            var queryNode = semanticResults[0].nodeId; // Use top semantic result as query node
            rankedResults = EnhancedRanking.RankWithMultiHop(
                semanticResults, graphData, communities, maxHops, queryNode);
        }
        else
        {
            // Use standard ranking
            rankedResults = EnhancedRanking.RankResults(
                semanticResults, graphData, communities, weights: weights);
        }

        // Step 5: Return top K results
        return rankedResults.Take(topK).ToList();
    }

    /// <summary>
    /// Gets semantic context for a specific node including community and neighborhood information.
    /// </summary>
    /// <param name="nodeId">The node to analyze.</param>
    /// <param name="maxDistance">Maximum graph distance to consider.</param>
    /// <param name="includeEmbeddings">Whether to include semantic similarity to neighbors.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Comprehensive context information for the node.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxDistance"/> is negative.</exception>
    public async Task<NodeContext> GetNodeContextAsync(
        ulong nodeId,
        int maxDistance = 2,
        bool includeEmbeddings = true,
        CancellationToken cancellationToken = default)
    {
        if (maxDistance < 0) throw new ArgumentOutOfRangeException(nameof(maxDistance));

        // Get community information
        var communities = await _cache.GetOrComputeCommunitiesAsync(
            _graphTableName,
            "louvain",
            async ct => await CommunityDetectionFunctions.DetectCommunitiesLouvainAsync(
                _database, _graphTableName, cancellationToken: ct),
            cancellationToken: cancellationToken);

        // Get community context
        var communityContext = await GraphRagFunctions.CommunitySematicContextAsync(
            _database, nodeId, _graphTableName, maxDistance, cancellationToken: cancellationToken);

        // Get node community
        var nodeCommunity = communities.FirstOrDefault(c => c.nodeId == nodeId).communityId;

        // Get semantic neighbors if requested
        List<(ulong nodeId, double similarity)> semanticNeighbors = [];
        if (includeEmbeddings)
        {
            // Get the node's embedding
            var nodeEmbeddingQuery = $"SELECT embedding FROM {_embeddingTableName} WHERE node_id = {nodeId}";
            var embeddingResult = _database.ExecuteQuery(nodeEmbeddingQuery, []);

            if (embeddingResult.Count > 0)
            {
                // For now, use mock embedding - in production, extract from result
                var mockEmbedding = VectorSearchIntegration.GenerateMockEmbeddings([nodeId], _embeddingDimensions)[0].Embedding;

                semanticNeighbors = await VectorSearchIntegration.SemanticSimilaritySearchAsync(
                    _database, _embeddingTableName, mockEmbedding, topK: 10, cancellationToken: cancellationToken);
            }
        }

        return new NodeContext(
            NodeId: nodeId,
            CommunityId: nodeCommunity,
            CommunityMembers: communities.Where(c => c.communityId == nodeCommunity).Select(c => c.nodeId).ToList(),
            GraphNeighbors: communityContext.Select(c => c.relatedNodeId).ToList(),
            SemanticNeighbors: semanticNeighbors,
            ContextDescription: GenerateNodeDescription(nodeId, nodeCommunity, communityContext.Count, semanticNeighbors.Count)
        );
    }

    /// <summary>
    /// Initializes the GraphRAG system by creating necessary tables and indexes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when initialization finishes.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the configured graph table is missing or invalid.</exception>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Create embedding table
        await VectorSearchIntegration.CreateEmbeddingTableAsync(
            _database, _embeddingTableName, _embeddingDimensions, cancellationToken);

        // Validate graph table exists
        if (!GraphLoader.ValidateGraphTable(_database, _graphTableName))
        {
            throw new InvalidOperationException($"Graph table '{_graphTableName}' does not exist or is not properly configured");
        }
    }

    /// <summary>
    /// Indexes nodes with their embeddings for semantic search.
    /// </summary>
    /// <param name="nodeEmbeddings">Node embeddings to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the embeddings are persisted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="nodeEmbeddings"/> is null.</exception>
    public async Task IndexEmbeddingsAsync(
        IEnumerable<VectorSearchIntegration.NodeEmbedding> nodeEmbeddings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeEmbeddings);

        await VectorSearchIntegration.InsertEmbeddingsAsync(
            _database, _embeddingTableName, nodeEmbeddings, cancellationToken);
    }

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    /// <returns>Cache statistics.</returns>
    public (int totalEntries, int expiredEntries, long memoryUsage) GetCacheStatistics()
    {
        return _cache.GetStatistics();
    }

    /// <summary>
    /// Clears all cached results.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Generates a descriptive string for a node's context.
    /// </summary>
    private static string GenerateNodeDescription(
        ulong nodeId,
        ulong communityId,
        int graphNeighbors,
        int semanticNeighbors)
    {
        var parts = new List<string>
        {
            $"Node {nodeId}",
            $"Community {communityId}",
            $"{graphNeighbors} graph neighbors",
            $"{semanticNeighbors} semantic neighbors"
        };

        return string.Join(", ", parts);
    }
}

/// <summary>
/// Comprehensive context information for a node.
/// </summary>
/// <param name="NodeId">The node identifier for the context result.</param>
/// <param name="CommunityId">The detected community identifier for the node.</param>
/// <param name="CommunityMembers">Nodes that belong to the detected community.</param>
/// <param name="GraphNeighbors">Neighboring nodes discovered through graph traversal.</param>
/// <param name="SemanticNeighbors">Neighboring nodes ranked by semantic similarity.</param>
/// <param name="ContextDescription">Human-readable summary of the collected context.</param>
public readonly record struct NodeContext(
    ulong NodeId,
    ulong CommunityId,
    List<ulong> CommunityMembers,
    List<ulong> GraphNeighbors,
    List<(ulong nodeId, double similarity)> SemanticNeighbors,
    string ContextDescription
);
