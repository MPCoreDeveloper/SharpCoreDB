// <copyright file="GraphTraversalQueryable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Query;

using SharpCoreDB.Graph;
using SharpCoreDB.Interfaces;
using System.Linq;

/// <summary>
/// Fluent API for configuring graph traversal queries in EF Core.
/// ✅ GraphRAG Phase 5: Advanced traversal configuration with strategy selection.
/// </summary>
/// <typeparam name="TEntity">The entity type being traversed.</typeparam>
public sealed class GraphTraversalQueryable<TEntity> where TEntity : class
{
    private readonly IQueryable<TEntity> _source;
    private readonly long _startNodeId;
    private readonly string _relationshipColumn;
    private readonly int _maxDepth;
    private GraphTraversalStrategy _strategy = GraphTraversalStrategy.Bfs;
    private AStarHeuristic _heuristic = AStarHeuristic.Depth;
    private bool _autoSelectStrategy;
    private GraphStatistics? _statistics;

    /// <summary>
    /// Initializes a new graph traversal queryable.
    /// </summary>
    /// <param name="source">The source queryable.</param>
    /// <param name="startNodeId">Starting node ID.</param>
    /// <param name="relationshipColumn">ROWREF column name.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    internal GraphTraversalQueryable(
        IQueryable<TEntity> source,
        long startNodeId,
        string relationshipColumn,
        int maxDepth)
    {
        _source = source;
        _startNodeId = startNodeId;
        _relationshipColumn = relationshipColumn;
        _maxDepth = maxDepth;
    }

    /// <summary>
    /// Specifies the traversal strategy to use.
    /// </summary>
    /// <param name="strategy">The traversal strategy (BFS, DFS, Bidirectional, Dijkstra, AStar).</param>
    /// <returns>This queryable for chaining.</returns>
    public GraphTraversalQueryable<TEntity> WithStrategy(GraphTraversalStrategy strategy)
    {
        _strategy = strategy;
        _autoSelectStrategy = false;
        return this;
    }

    /// <summary>
    /// Specifies the A* heuristic when using AStar strategy.
    /// </summary>
    /// <param name="heuristic">The heuristic function (Depth or Uniform).</param>
    /// <returns>This queryable for chaining.</returns>
    /// <remarks>
    /// Only applies when strategy is set to AStar.
    /// </remarks>
    public GraphTraversalQueryable<TEntity> WithHeuristic(AStarHeuristic heuristic)
    {
        _heuristic = heuristic;
        return this;
    }

    /// <summary>
    /// Enables automatic strategy selection based on graph characteristics.
    /// Uses TraversalStrategyOptimizer to choose the best strategy.
    /// </summary>
    /// <param name="statistics">Optional graph statistics for optimization.</param>
    /// <returns>This queryable for chaining.</returns>
    public GraphTraversalQueryable<TEntity> WithAutoStrategy(GraphStatistics? statistics = null)
    {
        _autoSelectStrategy = true;
        _statistics = statistics;
        return this;
    }

    /// <summary>
    /// Converts this configuration to a standard IQueryable for execution.
    /// </summary>
    /// <returns>IQueryable of reachable node IDs.</returns>
    public IQueryable<long> AsQueryable()
    {
        var strategy = _strategy;

        // Auto-select strategy if requested
        if (_autoSelectStrategy)
        {
            // TODO: In real implementation, query table statistics
            // For now, use provided statistics or default heuristics
            var stats = _statistics ?? new GraphStatistics(
                totalNodes: 10000,
                totalEdges: 15000,
                estimatedDegree: 1.5);

            var optimizer = new TraversalStrategyOptimizer(
                table: null!, // Will be resolved at query execution time
                _relationshipColumn,
                _maxDepth,
                stats,
                tableRowCount: stats.TotalNodes);

            var recommendation = optimizer.RecommendStrategy();
            strategy = recommendation.RecommendedStrategy;
        }

        return _source.Traverse(_startNodeId, _relationshipColumn, _maxDepth, strategy);
    }

    /// <summary>
    /// Executes the traversal and returns the results.
    /// </summary>
    /// <returns>List of reachable node IDs.</returns>
    public List<long> ToList()
    {
        return AsQueryable().ToList();
    }

    /// <summary>
    /// Executes the traversal asynchronously and returns the results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task with list of reachable node IDs.</returns>
    public async Task<List<long>> ToListAsync(CancellationToken cancellationToken = default)
    {
        var queryable = AsQueryable();
        var results = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(queryable, cancellationToken);
        return results;
    }

    /// <summary>
    /// Gets the configured strategy (for diagnostics/testing).
    /// </summary>
    public GraphTraversalStrategy GetStrategy() => _strategy;

    /// <summary>
    /// Gets the configured heuristic (for diagnostics/testing).
    /// </summary>
    public AStarHeuristic GetHeuristic() => _heuristic;

    /// <summary>
    /// Gets whether auto-strategy selection is enabled (for diagnostics/testing).
    /// </summary>
    public bool IsAutoStrategyEnabled() => _autoSelectStrategy;
}
