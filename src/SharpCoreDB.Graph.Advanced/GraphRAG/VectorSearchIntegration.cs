#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.VectorSearch;

namespace SharpCoreDB.Graph.Advanced.GraphRAG;

/// <summary>
/// Vector search integration for GraphRAG semantic similarity.
/// Provides real semantic search capabilities using SharpCoreDB.VectorSearch.
/// </summary>
public static class VectorSearchIntegration
{
    private static readonly ConcurrentDictionary<string, List<NodeEmbedding>> EmbeddingCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Represents a node with its embedding vector.
    /// </summary>
    public readonly record struct NodeEmbedding(ulong NodeId, float[] Embedding);

    /// <summary>
    /// Creates a vector-enabled table for storing node embeddings.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">Name of the table to create.</param>
    /// <param name="embeddingDimensions">Dimensions of the embedding vectors.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task CreateEmbeddingTableAsync(
        Database database,
        string tableName,
        int embeddingDimensions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        if (embeddingDimensions <= 0) throw new ArgumentOutOfRangeException(nameof(embeddingDimensions));

        // Create table with vector column
        var createTableSql = $@"
            CREATE TABLE {tableName} (
                node_id INTEGER PRIMARY KEY,
                content TEXT,
                embedding VECTOR({embeddingDimensions})
            )";

        database.ExecuteSQL(createTableSql);

        // Create HNSW index for fast similarity search
        var createIndexSql = $@"
            CREATE INDEX idx_{tableName}_embedding 
            ON {tableName}(embedding) 
            WITH (index_type='hnsw', m=16, ef_construction=200)";

        database.ExecuteSQL(createIndexSql);
        EmbeddingCache.TryAdd(tableName, []);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Inserts node embeddings into the vector table.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">Name of the embedding table.</param>
    /// <param name="embeddings">Node embeddings to insert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task InsertEmbeddingsAsync(
        Database database,
        string tableName,
        IEnumerable<NodeEmbedding> embeddings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(embeddings);

        var embeddingList = embeddings.ToList();
        List<string> statements = [];
        foreach (var embedding in embeddingList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vectorString = $"[{string.Join(",", embedding.Embedding.Select(value => value.ToString(CultureInfo.InvariantCulture)))}]";
            var content = $"Node {embedding.NodeId}";

            statements.Add($"INSERT INTO {tableName} (node_id, content, embedding) VALUES ({embedding.NodeId}, '{content}', {vectorString})");
        }

        database.ExecuteBatchSQL(statements);
        EmbeddingCache[tableName] = embeddingList;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs semantic similarity search using vector embeddings.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">Name of the embedding table.</param>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="topK">Number of top results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of (nodeId, similarityScore) pairs.</returns>
    public static Task<List<(ulong nodeId, double similarityScore)>> SemanticSimilaritySearchAsync(
        Database database,
        string tableName,
        float[] queryEmbedding,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));

        if (EmbeddingCache.TryGetValue(tableName, out var cachedEmbeddings) && cachedEmbeddings.Count > 0)
        {
            var cachedResults = cachedEmbeddings
                .Select(embedding =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var similarityScore = Math.Clamp(1.0 - DistanceMetrics.CosineDistance(embedding.Embedding, queryEmbedding), 0.0, 1.0);
                    return (embedding.NodeId, similarityScore);
                })
                .OrderByDescending(result => result.Item2)
                .Take(topK)
                .Select(result => (nodeId: result.NodeId, similarityScore: result.Item2))
                .ToList();

            return Task.FromResult(cachedResults);
        }

        var rows = database.ExecuteQuery($"SELECT node_id, embedding FROM {tableName}", []);
        var similarityResults = rows
            .Select(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeIdKey = row.Keys.FirstOrDefault(key => string.Equals(key, "node_id", StringComparison.OrdinalIgnoreCase))
                    ?? row.Keys.First();
                var embeddingKey = row.Keys.FirstOrDefault(key => string.Equals(key, "embedding", StringComparison.OrdinalIgnoreCase))
                    ?? row.Keys.Last();
                var embeddingText = Convert.ToString(row[embeddingKey], CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("Embedding value was null.");
                var storedEmbedding = ParseEmbeddingText(embeddingText);
                var similarityScore = Math.Clamp(1.0 - DistanceMetrics.CosineDistance(storedEmbedding, queryEmbedding), 0.0, 1.0);

                return (
                    nodeId: Convert.ToUInt64(row[nodeIdKey], CultureInfo.InvariantCulture),
                    similarityScore
                );
            })
            .OrderByDescending(result => result.similarityScore)
            .Take(topK)
            .ToList();

        return Task.FromResult(similarityResults);
    }

    /// <summary>
    /// Generates mock embeddings for testing (in production, use real embedding models).
    /// </summary>
    /// <param name="nodeIds">Node IDs to generate embeddings for.</param>
    /// <param name="dimensions">Embedding dimensions.</param>
    /// <param name="seed">Random seed for reproducible results.</param>
    /// <returns>List of node embeddings.</returns>
    public static List<NodeEmbedding> GenerateMockEmbeddings(
        IEnumerable<ulong> nodeIds,
        int dimensions,
        int seed = 42)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));

        // Random is used to generate deterministic mock/test data, not secrets.
        var random = new Random(seed); // NOSONAR:S2245
        var embeddings = new List<NodeEmbedding>();

        foreach (var nodeId in nodeIds)
        {
            var embedding = new float[dimensions];
            for (int i = 0; i < dimensions; i++)
            {
                // Generate normalized random vectors
                embedding[i] = (float)(random.NextDouble() * 2 - 1);
            }

            // Normalize the vector
            var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
            for (int i = 0; i < dimensions; i++)
            {
                embedding[i] /= (float)magnitude;
            }

            embeddings.Add(new NodeEmbedding(nodeId, embedding));
        }

        return embeddings;
    }

    /// <summary>
    /// Parses vector text returned by SharpCoreDB into a float array.
    /// </summary>
    private static float[] ParseEmbeddingText(string embeddingText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingText);

        var trimmed = embeddingText.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return [];
        }

        return trimmed
            .Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => float.Parse(value.Trim('[', ']'), CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>
    /// Computes semantic distance between two embeddings.
    /// </summary>
    /// <param name="embedding1">First embedding.</param>
    /// <param name="embedding2">Second embedding.</param>
    /// <returns>Cosine similarity (1.0 = identical, 0.0 = orthogonal, -1.0 = opposite).</returns>
    public static double ComputeSemanticSimilarity(float[] embedding1, float[] embedding2)
    {
        ArgumentNullException.ThrowIfNull(embedding1);
        ArgumentNullException.ThrowIfNull(embedding2);

        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException("Embeddings must have the same dimensions");
        }

        // Cosine similarity: dot product / (magnitude1 * magnitude2)
        var dotProduct = embedding1.Zip(embedding2, (a, b) => a * b).Sum();
        var magnitude1 = Math.Sqrt(embedding1.Sum(x => x * x));
        var magnitude2 = Math.Sqrt(embedding2.Sum(x => x * x));

        if (magnitude1 == 0 || magnitude2 == 0)
        {
            return 0.0; // Handle zero vectors
        }

        return dotProduct / (magnitude1 * magnitude2);
    }

    /// <summary>
    /// Validates that a table has the required vector columns.
    /// </summary>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">Name of the table to validate.</param>
    /// <returns>True if table is properly configured for vector search.</returns>
    public static bool ValidateVectorTable(Database database, string tableName)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        try
        {
            // Check if table exists and has embedding column
            var testQuery = $"SELECT node_id, embedding FROM {tableName} LIMIT 1";
            database.ExecuteQuery(testQuery, []);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
