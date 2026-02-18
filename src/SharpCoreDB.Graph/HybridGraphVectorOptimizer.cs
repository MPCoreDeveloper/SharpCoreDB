// <copyright file="HybridGraphVectorOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using System;
using System.Collections.Generic;

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

/// <summary>
/// Hybrid Graph-Vector Query Optimizer.
/// ✅ GraphRAG Phase 3: Combines graph traversal with vector search for RAG pipelines.
/// 
/// Optimizes queries like:
/// SELECT * FROM documents 
/// WHERE doc_id IN (GRAPH_TRAVERSE(knowledge_graph, start, 'references', 3))
/// AND vec_distance_cosine(embedding, query_vec) LESS_THAN 0.2
/// LIMIT 10
/// 
/// Cost-based optimization automatically selects predicate ordering based on:
/// - Estimated graph traversal cardinality (graph fan-out)
/// - Estimated vector search selectivity (similarity threshold)
/// - Index availability
/// </summary>
public sealed class HybridGraphVectorOptimizer
{
    private const double VectorSearchCostPerRow = 0.01; // ~10μs per vector distance calc
    private const double GraphTraversalCostPerRow = 0.001; // ~1μs per ROWREF lookup

    /// <summary>
    /// Analyzes a query to detect graph and vector operations.
    /// Provides optimization recommendations based on cardinality estimation.
    /// </summary>
    /// <param name="hasGraphTraversal">Whether the query contains GRAPH_TRAVERSE().</param>
    /// <param name="hasVectorSearch">Whether the query contains vector distance functions.</param>
    /// <param name="graphMaxDepth">Maximum traversal depth for graph operations.</param>
    /// <param name="tableStats">Optional table statistics for cost estimation.</param>
    /// <returns>An optimized execution plan hint with cost breakdown.</returns>
    public QueryOptimizationHint OptimizeHybridQuery(
        bool hasGraphTraversal,
        bool hasVectorSearch,
        int graphMaxDepth = 3,
        TableStatistics? tableStats = null)
    {
        var hint = new QueryOptimizationHint();

        hint.HasGraphTraversal = hasGraphTraversal;
        hint.HasVectorSearch = hasVectorSearch;

        if (!hasGraphTraversal && !hasVectorSearch)
        {
            hint.RecommendedOrder = ExecutionOrder.Default;
            return hint;
        }

        // Cost-based optimization
        if (hasGraphTraversal && hasVectorSearch)
        {
            var graphCost = EstimateGraphTraversalCost(graphMaxDepth, tableStats);
            var vectorCost = EstimateVectorSearchCost(tableStats);

            hint.GraphTraversalCost = graphCost;
            hint.VectorSearchCost = vectorCost;

            // Execute the more selective filter first
            if (graphCost.SelectivityRatio < vectorCost.SelectivityRatio)
            {
                hint.RecommendedOrder = ExecutionOrder.GraphThenVector;
                hint.RecommendedOrder_Reason = $"Graph reduces {graphCost.SelectivityRatio:P1} of rows (cost: {graphCost.EstimatedCostMs:F3}ms); " +
                    $"apply first, then vector filter ({vectorCost.EstimatedCostMs:F3}ms).";
            }
            else
            {
                hint.RecommendedOrder = ExecutionOrder.VectorThenGraph;
                hint.RecommendedOrder_Reason = $"Vector search reduces {vectorCost.SelectivityRatio:P1} of rows (cost: {vectorCost.EstimatedCostMs:F3}ms); " +
                    $"apply first, then graph filter ({graphCost.EstimatedCostMs:F3}ms).";
            }

            hint.TotalEstimatedCostMs = graphCost.EstimatedCostMs + vectorCost.EstimatedCostMs;
        }
        else if (hasGraphTraversal)
        {
            hint.RecommendedOrder = ExecutionOrder.GraphOnly;
            hint.GraphTraversalCost = EstimateGraphTraversalCost(graphMaxDepth, tableStats);
            hint.TotalEstimatedCostMs = hint.GraphTraversalCost.EstimatedCostMs;
        }
        else if (hasVectorSearch)
        {
            hint.RecommendedOrder = ExecutionOrder.VectorOnly;
            hint.VectorSearchCost = EstimateVectorSearchCost(tableStats);
            hint.TotalEstimatedCostMs = hint.VectorSearchCost.EstimatedCostMs;
        }

        return hint;
    }

    /// <summary>
    /// Estimates the cost of executing graph traversal.
    /// </summary>
    private static OperationCost EstimateGraphTraversalCost(
        int maxDepth,
        TableStatistics? tableStats)
    {
        var cost = new OperationCost();

        // Estimate cardinality: assume exponential growth with depth
        var baseDegree = tableStats?.EstimatedAverageDegree ?? 1.5;
        var estimatedNodes = (long)Math.Pow(baseDegree, maxDepth);
        var tableSize = tableStats?.RowCount ?? 10_000;
        var cardinality = Math.Min(estimatedNodes, tableSize);

        cost.EstimatedCardinality = cardinality;
        cost.SelectivityRatio = (double)cardinality / tableSize;
        cost.EstimatedCostMs = cardinality * GraphTraversalCostPerRow;

        return cost;
    }

    /// <summary>
    /// Estimates the cost of executing vector search.
    /// </summary>
    private static OperationCost EstimateVectorSearchCost(TableStatistics? tableStats)
    {
        var cost = new OperationCost();
        var tableSize = tableStats?.RowCount ?? 10_000;

        // Vector search selectivity defaults to 10% (can be refined)
        var selectivity = 0.1;
        var cardinality = (long)(tableSize * selectivity);

        cost.EstimatedCardinality = cardinality;
        cost.SelectivityRatio = selectivity;
        cost.EstimatedCostMs = tableSize * VectorSearchCostPerRow; // All rows must be scanned

        return cost;
    }
}

/// <summary>
/// Optimization hint for query execution with cost breakdown.
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
    /// Gets or sets the cost of graph traversal execution.
    /// </summary>
    public OperationCost? GraphTraversalCost { get; set; }

    /// <summary>
    /// Gets or sets the cost of vector search execution.
    /// </summary>
    public OperationCost? VectorSearchCost { get; set; }

    /// <summary>
    /// Gets or sets the recommended execution order.
    /// </summary>
    public ExecutionOrder RecommendedOrder { get; set; } = ExecutionOrder.Default;

    /// <summary>
    /// Gets or sets the explanation for the recommended execution order.
    /// </summary>
    public string? RecommendedOrder_Reason { get; set; }

    /// <summary>
    /// Gets or sets the total estimated cost in milliseconds.
    /// </summary>
    public double? TotalEstimatedCostMs { get; set; }
}

/// <summary>
/// Cost estimation for a single operation.
/// </summary>
public class OperationCost
{
    /// <summary>
    /// Gets or sets the estimated number of rows produced.
    /// </summary>
    public long EstimatedCardinality { get; set; }

    /// <summary>
    /// Gets or sets the selectivity ratio (estimated cardinality / total rows).
    /// </summary>
    public double SelectivityRatio { get; set; }

    /// <summary>
    /// Gets or sets the estimated execution cost in milliseconds.
    /// </summary>
    public double EstimatedCostMs { get; set; }
}

/// <summary>
/// Table statistics for cost estimation.
/// </summary>
public class TableStatistics
{
    /// <summary>
    /// Gets or sets the total number of rows in the table.
    /// </summary>
    public long RowCount { get; set; }

    /// <summary>
    /// Gets or sets the estimated average out-degree for graph operations.
    /// </summary>
    public double EstimatedAverageDegree { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets whether a vector index is available.
    /// </summary>
    public bool HasVectorIndex { get; set; }
}
