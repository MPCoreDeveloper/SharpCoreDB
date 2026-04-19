#nullable enable

// <copyright file="GraphRagSqlProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Advanced.SqlIntegration;

using SharpCoreDB.Graph.Advanced.GraphRAG;
using SharpCoreDB.Interfaces;

/// <summary>
/// SQL integration adapter for SELECT ... GRAPH_RAG clause execution.
/// Bridges core SQL parser/executor with GraphRAG engine in SharpCoreDB.Graph.Advanced.
/// </summary>
public sealed class GraphRagSqlProvider(
    Database database,
    GraphRagEngine graphRagEngine,
    Func<string, float[]>? embeddingProvider = null) : IGraphRagProvider
{
    private readonly Database _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly GraphRagEngine _graphRagEngine = graphRagEngine ?? throw new ArgumentNullException(nameof(graphRagEngine));
    private readonly Func<string, float[]> _embeddingProvider = embeddingProvider ?? CreateDeterministicEmbedding;

    /// <inheritdoc />
    public bool CanExecute(string tableName)
    {
        return !string.IsNullOrWhiteSpace(tableName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Dictionary<string, object>>> ExecuteAsync(
        GraphRagRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.TableName))
            throw new ArgumentException("GRAPH_RAG requires a non-empty table name.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("GRAPH_RAG requires a non-empty question.", nameof(request));

        if (request.Limit <= 0)
            throw new ArgumentException("GRAPH_RAG LIMIT must be greater than zero.", nameof(request));

        if (request.TopK <= 0)
            throw new ArgumentException("GRAPH_RAG TOP_K must be greater than zero.", nameof(request));

        if (request.MinScore is < 0 or > 1)
            throw new ArgumentException("GRAPH_RAG score threshold must be between 0 and 1.", nameof(request));

        var queryEmbedding = _embeddingProvider(request.Question);
        var ranked = await _graphRagEngine.SearchAsync(
            queryEmbedding,
            topK: request.TopK,
            includeCommunities: request.IncludeContext,
            maxHops: request.IncludeContext ? 2 : 0,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var filtered = ranked
            .Where(r => request.MinScore is null || r.CombinedScore >= request.MinScore.Value)
            .Take(request.Limit)
            .ToList();

        List<Dictionary<string, object>> rows = [];
        foreach (var result in filtered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["node_id"] = result.NodeId,
                ["score"] = result.CombinedScore,
                ["semantic_score"] = result.SemanticScore,
                ["topological_score"] = result.TopologicalScore,
                ["community_score"] = result.CommunityScore,
            };

            if (request.IncludeContext)
            {
                row["context"] = result.Context;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static float[] CreateDeterministicEmbedding(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // Lightweight deterministic fallback embedding to keep SQL path functional
        // until a real embedding provider is injected.
        const int dims = 16;
        var vector = new float[dims];
        var seed = question.GetHashCode(StringComparison.Ordinal);
        var random = new Random(seed);

        for (int i = 0; i < dims; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        var mag = Math.Sqrt(vector.Sum(v => v * v));
        if (mag > 0)
        {
            for (int i = 0; i < dims; i++)
            {
                vector[i] /= (float)mag;
            }
        }

        return vector;
    }
}
