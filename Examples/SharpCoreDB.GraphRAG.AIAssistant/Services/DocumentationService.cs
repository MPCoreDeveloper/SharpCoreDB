using SharpCoreDB.GraphRAG.AIAssistant.Models;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace SharpCoreDB.GraphRAG.AIAssistant.Services;

/// <summary>
/// Service for executing GraphRAG queries on documentation database.
/// Combines vector search (semantic similarity) with graph traversal (relationships).
/// </summary>
public sealed class DocumentationService(IDatabase database, ILogger<DocumentationService> logger)
{
    private readonly IDatabase _db = database;
    private readonly ILogger<DocumentationService> _logger = logger;

    // Scoring weights for hybrid ranking
    private const double VectorWeight = 0.6;
    private const double GraphWeight = 0.3;
    private const double RelationshipWeight = 0.1;

    /// <summary>
    /// Executes GraphRAG query: vector search + graph traversal + hybrid ranking.
    /// </summary>
    /// <param name="question">User's natural language question</param>
    /// <param name="topK">Number of initial vector search candidates</param>
    /// <param name="maxResults">Maximum results to return</param>
    /// <param name="minScore">Minimum combined score threshold (0.0 to 1.0)</param>
    /// <param name="maxDepth">Maximum graph traversal depth (hops)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Ranked list of relevant documents with scores</returns>
    public async Task<List<GraphRAGResult>> SearchAsync(
        string question,
        int topK = 25,
        int maxResults = 5,
        double minScore = 0.0,
        int maxDepth = 2,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var sw = Stopwatch.StartNew();

        _logger.LogInformation("GraphRAG search: '{Question}' (topK={TopK}, maxDepth={MaxDepth})", 
            question, topK, maxDepth);

        // Step 1: Vector search for semantically similar documents
        var vectorResults = await VectorSearchAsync(question, topK, ct);
        _logger.LogDebug("Vector search found {Count} candidates in {Ms}ms", 
            vectorResults.Count, sw.ElapsedMilliseconds);

        if (vectorResults.Count == 0)
        {
            _logger.LogWarning("No vector search results found for: {Question}", question);
            return [];
        }

        // Step 2: Graph traversal to find related documents
        var graphResults = await GraphTraversalAsync(vectorResults, maxDepth, ct);
        _logger.LogDebug("Graph traversal found {Count} additional documents in {Ms}ms", 
            graphResults.Count, sw.ElapsedMilliseconds);

        // Step 3: Combine and rank results
        var combinedResults = CombineAndRank(vectorResults, graphResults);

        // Step 4: Filter by score and limit
        var finalResults = combinedResults
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();

        _logger.LogInformation("GraphRAG completed: {Count} results in {Ms}ms (avg score: {AvgScore:F3})",
            finalResults.Count, sw.ElapsedMilliseconds, 
            finalResults.Count > 0 ? finalResults.Average(r => r.Score) : 0);

        return finalResults;
    }

    /// <summary>
    /// Traditional RAG: vector search only (for comparison)
    /// </summary>
    public async Task<List<GraphRAGResult>> VectorOnlySearchAsync(
        string question,
        int maxResults = 5,
        double minScore = 0.0,
        CancellationToken ct = default)
    {
        var results = await VectorSearchAsync(question, maxResults, ct);

        return results
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    /// <summary>
    /// Performs vector similarity search using embeddings.
    /// </summary>
    private async Task<List<GraphRAGResult>> VectorSearchAsync(
        string query, 
        int topK, 
        CancellationToken ct)
    {
        // Generate embedding for query (simplified - using mock embeddings)
        var queryEmbedding = GenerateEmbedding(query);

        // In production, this would use SharpCoreDB.VectorSearch for efficient similarity search
        // For demo purposes, we'll simulate with direct SQL queries (mock scores based on ID)
        var sql = $@"
            SELECT 
                d.Id,
                d.Title,
                d.Content,
                d.Url
            FROM documentation d
            LIMIT {topK}";

        var rows = _db.ExecuteQuery(sql);

        return rows.Select(row =>
        {
            var id = Convert.ToInt64(row["Id"]);
            // Mock similarity score based on ID (in production, use actual embeddings)
            var mockScore = 0.5 + (id * 0.05);

            return new GraphRAGResult
            {
                DocumentId = id,
                Title = row["Title"].ToString() ?? "",
                Content = row["Content"].ToString() ?? "",
                Score = mockScore,
                VectorSimilarity = mockScore,
                GraphScore = 0.0,
                RetrievalMethod = RetrievalMethod.VectorSearch,
                SourceUrl = row["Url"].ToString()
            };
        }).ToList();
    }

    /// <summary>
    /// Performs graph traversal from seed documents to find related content.
    /// </summary>
    private async Task<List<GraphRAGResult>> GraphTraversalAsync(
        List<GraphRAGResult> seedDocuments,
        int maxDepth,
        CancellationToken ct)
    {
        var relatedDocs = new Dictionary<long, GraphRAGResult>();

        foreach (var seed in seedDocuments)
        {
            // Use SharpCoreDB.Graph for efficient traversal
            var sql = $@"
                SELECT 
                    d.Id,
                    d.Title,
                    d.Content,
                    d.Url,
                    r.relationship_type,
                    r.weight,
                    1 as hop_distance
                FROM documentation d
                INNER JOIN doc_relationships r ON d.Id = r.target_id
                WHERE r.source_id = {seed.DocumentId}
                  AND r.weight > 0.3
                LIMIT 10";

            var rows = _db.ExecuteQuery(sql);

            foreach (var row in rows)
            {
                var docId = Convert.ToInt64(row["Id"]);

                if (relatedDocs.ContainsKey(docId) || seedDocuments.Any(s => s.DocumentId == docId))
                    continue;

                var relationshipType = ParseRelationshipType(row["relationship_type"].ToString() ?? "");
                var weight = Convert.ToDouble(row["weight"]);

                // Calculate graph score based on relationship type and weight
                var graphScore = CalculateGraphScore(relationshipType, weight);

                relatedDocs[docId] = new GraphRAGResult
                {
                    DocumentId = docId,
                    Title = row["Title"].ToString() ?? "",
                    Content = row["Content"].ToString() ?? "",
                    Score = graphScore,
                    VectorSimilarity = 0.0,
                    GraphScore = graphScore,
                    RetrievalMethod = RetrievalMethod.GraphTraversal,
                    SourceUrl = row["Url"].ToString(),
                    RelationshipPath = new RelationshipPath
                    {
                        RelationType = relationshipType,
                        HopDistance = Convert.ToInt32(row["hop_distance"]),
                        SourceDocumentId = seed.DocumentId
                    }
                };
            }
        }

        return [.. relatedDocs.Values];
    }

    /// <summary>
    /// Combines vector and graph results with hybrid scoring.
    /// </summary>
    private List<GraphRAGResult> CombineAndRank(
        List<GraphRAGResult> vectorResults,
        List<GraphRAGResult> graphResults)
    {
        var combined = new Dictionary<long, GraphRAGResult>();

        // Add vector results
        foreach (var result in vectorResults)
        {
            combined[result.DocumentId] = result;
        }

        // Merge graph results
        foreach (var result in graphResults)
        {
            if (combined.TryGetValue(result.DocumentId, out var existing))
            {
                // Document found by both methods - hybrid scoring
                var hybridScore = 
                    (existing.VectorSimilarity * VectorWeight) +
                    (result.GraphScore * GraphWeight) +
                    (result.RelationshipPath?.HopDistance == 1 ? RelationshipWeight : 0);

                combined[result.DocumentId] = new GraphRAGResult
                {
                    DocumentId = existing.DocumentId,
                    Title = existing.Title,
                    Content = existing.Content,
                    Score = hybridScore,
                    VectorSimilarity = existing.VectorSimilarity,
                    GraphScore = result.GraphScore,
                    RetrievalMethod = RetrievalMethod.Hybrid,
                    RelationshipPath = result.RelationshipPath,
                    SourceUrl = existing.SourceUrl
                };
            }
            else
            {
                // Document found only via graph
                combined[result.DocumentId] = result;
            }
        }

        return [.. combined.Values];
    }

    /// <summary>
    /// Calculates graph importance score based on relationship type and weight.
    /// </summary>
    private static double CalculateGraphScore(RelationshipType relationType, double weight)
    {
        // Weight relationship types by importance for context
        var baseScore = relationType switch
        {
            RelationshipType.Prerequisite => 0.9,  // Most important - foundational knowledge
            RelationshipType.DependsOn => 0.85,    // Critical - direct dependencies
            RelationshipType.RelatedTo => 0.7,     // Important - related concepts
            RelationshipType.FollowsFrom => 0.75,  // Useful - next steps
            RelationshipType.SharesAPI => 0.65,    // Helpful - shared tools
            _ => 0.5
        };

        return baseScore * weight;
    }

    /// <summary>
    /// Parses relationship type string to enum.
    /// </summary>
    private static RelationshipType ParseRelationshipType(string typeStr)
    {
        return typeStr.ToLowerInvariant() switch
        {
            "prerequisite" => RelationshipType.Prerequisite,
            "dependson" => RelationshipType.DependsOn,
            "relatedto" => RelationshipType.RelatedTo,
            "followsfrom" => RelationshipType.FollowsFrom,
            "sharesapi" => RelationshipType.SharesAPI,
            _ => RelationshipType.RelatedTo
        };
    }

    /// <summary>
    /// Generates embedding vector for text (mock implementation).
    /// In production, use actual embedding model (OpenAI, Sentence Transformers, etc.)
    /// </summary>
    private static float[] GenerateEmbedding(string text)
    {
        // Mock: simple hash-based embedding for demo
        // Production: Call embedding API or local model
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[384]; // Common embedding dimension

        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        return embedding;
    }

    /// <summary>
    /// Initializes the documentation database schema.
    /// </summary>
    public Task InitializeDatabaseAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing documentation database schema...");

        // Create tables
        _db.ExecuteSQL(@"
            CREATE TABLE IF NOT EXISTS documentation (
                Id BIGINT PRIMARY KEY,
                Title TEXT NOT NULL,
                Content TEXT NOT NULL,
                Category TEXT NOT NULL,
                Tags TEXT NOT NULL,
                Url TEXT NOT NULL,
                DifficultyLevel TEXT NOT NULL,
                ReadingTimeMinutes INT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )");

        _db.ExecuteSQL(@"
            CREATE TABLE IF NOT EXISTS doc_embeddings (
                doc_id BIGINT PRIMARY KEY,
                embedding BLOB NOT NULL,
                similarity_score DOUBLE DEFAULT 0.0
            )");

        _db.ExecuteSQL(@"
            CREATE TABLE IF NOT EXISTS doc_relationships (
                source_id BIGINT NOT NULL,
                target_id BIGINT NOT NULL,
                relationship_type TEXT NOT NULL,
                weight DOUBLE DEFAULT 1.0
            )");

        // Create indexes
        _db.ExecuteSQL(@"
            CREATE INDEX IF NOT EXISTS idx_relationships_source 
            ON doc_relationships(source_id)");

        _db.ExecuteSQL(@"
            CREATE INDEX IF NOT EXISTS idx_relationships_target 
            ON doc_relationships(target_id)");

        _db.Flush();
        _db.ForceSave();

        _logger.LogInformation("Database schema initialized successfully");

        return Task.CompletedTask;
    }
}
