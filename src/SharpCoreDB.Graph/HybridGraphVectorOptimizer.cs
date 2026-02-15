// <copyright file="HybridGraphVectorOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Services;

/// <summary>
/// Hybrid Graph-Vector Query Optimizer.
/// ✅ GraphRAG Phase 3: Combines graph traversal with vector search for RAG pipelines.
/// 
/// Optimizes queries like:
/// SELECT * FROM documents 
/// WHERE doc_id IN (GRAPH_TRAVERSE(knowledge_graph, start, 'references', 3))
/// AND vec_distance_cosine(embedding, query_vec) LESS_THAN 0.2
/// LIMIT 10
/// </summary>
public sealed class HybridGraphVectorOptimizer
{
    /// <summary>
    /// Optimizes a query by detecting graph and vector operations and reordering execution.
    /// </summary>
    /// <param name="selectNode">The SELECT statement AST node.</param>
    /// <returns>An optimized execution plan hint.</returns>
    public QueryOptimizationHint OptimizeQuery(SelectNode selectNode)
    {
        ArgumentNullException.ThrowIfNull(selectNode);

        var hint = new QueryOptimizationHint();

        if (selectNode.Where?.Condition is null)
        {
            return hint;
        }

        // Detect graph traversal in WHERE clause
        var hasGraphTraversal = DetectGraphTraversal(selectNode.Where.Condition);
        hint.HasGraphTraversal = hasGraphTraversal;

        // Detect vector similarity in WHERE clause
        var hasVectorSearch = DetectVectorSearch(selectNode.Where.Condition);
        hint.HasVectorSearch = hasVectorSearch;

        // Recommend execution order
        if (hasGraphTraversal && hasVectorSearch)
        {
            // Execute graph traversal first (usually faster cardinality reduction)
            // Then apply vector filtering on the result set
            hint.RecommendedOrder = ExecutionOrder.GraphThenVector;
        }
        else if (hasGraphTraversal)
        {
            hint.RecommendedOrder = ExecutionOrder.GraphOnly;
        }
        else if (hasVectorSearch)
        {
            hint.RecommendedOrder = ExecutionOrder.VectorOnly;
        }

        return hint;
    }

    private static bool DetectGraphTraversal(ExpressionNode expression)
    {
        return expression switch
        {
            GraphTraverseNode => true,
            InExpressionNode inExpr when inExpr.Expression is GraphTraverseNode => true,
            BinaryExpressionNode binary => 
                DetectGraphTraversal(binary.Left) || DetectGraphTraversal(binary.Right),
            _ => false
        };
    }

    private static bool DetectVectorSearch(ExpressionNode expression)
    {
        return expression switch
        {
            FunctionCallNode func when func.FunctionName.StartsWith("VEC_", StringComparison.OrdinalIgnoreCase) => true,
            BinaryExpressionNode binary => 
                DetectVectorSearch(binary.Left) || DetectVectorSearch(binary.Right),
            _ => false
        };
    }
}

/// <summary>
/// Optimization hint for query execution.
/// </summary>
public class QueryOptimizationHint
{
    /// <summary>
    /// Gets or sets whether the query contains graph traversal.
    /// </summary>
    public bool HasGraphTraversal { get; set; }

    /// <summary>
    /// Gets or sets whether the query contains vector similarity search.
    /// </summary>
    public bool HasVectorSearch { get; set; }

    /// <summary>
    /// Gets or sets the recommended execution order.
    /// </summary>
    public ExecutionOrder RecommendedOrder { get; set; } = ExecutionOrder.Default;
}

/// <summary>
/// Recommended execution order for hybrid queries.
/// </summary>
public enum ExecutionOrder
{
    /// <summary>No special optimization.</summary>
    Default = 0,

    /// <summary>Execute graph traversal only.</summary>
    GraphOnly = 1,

    /// <summary>Execute vector search only.</summary>
    VectorOnly = 2,

    /// <summary>Execute graph traversal first, then apply vector filtering.</summary>
    GraphThenVector = 3,

    /// <summary>Execute vector search first, then apply graph filtering.</summary>
    VectorThenGraph = 4
}
