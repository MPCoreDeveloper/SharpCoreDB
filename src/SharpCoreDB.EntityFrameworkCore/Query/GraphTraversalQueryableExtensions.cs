// <copyright file="GraphTraversalQueryableExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.EntityFrameworkCore.Query;

using Microsoft.EntityFrameworkCore;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

/// <summary>
/// LINQ extension methods for graph traversal queries in EF Core.
/// Enables fluent graph exploration with GRAPH_TRAVERSE() SQL translation.
/// ✅ GraphRAG Phase 2: EF Core integration for LINQ graph queries.
/// 
/// Example usage:
/// <code>
/// var nodes = await db.Nodes
///     .Traverse(startId: 1, relationshipColumn: "next", maxDepth: 3, strategy: GraphTraversalStrategy.Bfs)
///     .ToListAsync();
///
/// var orders = await db.Orders
///     .Where(o => db.Nodes
///         .Traverse(startId: o.NodeId, "parent", 5, GraphTraversalStrategy.Dfs)
///         .Contains(o.NodeId))
///     .ToListAsync();
/// </code>
/// </summary>
public static class GraphTraversalQueryableExtensions
{
    /// <summary>
    /// Traverses the graph starting from a given node ID and returns reachable node IDs.
    /// Translates to GRAPH_TRAVERSE() SQL function.
    /// </summary>
    /// <typeparam name="TEntity">The entity type (must have ROWREF for traversal).</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="startNodeId">The starting node row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name for edges.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <param name="strategy">BFS or DFS traversal strategy.</param>
    /// <returns>IQueryable of reachable node IDs.</returns>
    /// <remarks>
    /// This method is designed for use with database evaluation.
    /// It will be translated to: SELECT GRAPH_TRAVERSE(startNodeId, relationshipColumn, maxDepth, strategy)
    /// </remarks>
    public static IQueryable<long> Traverse<TEntity>(
        this IQueryable<TEntity> source,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be non-negative");

        var methodInfo = typeof(GraphTraversalQueryableExtensions)
            .GetMethod(nameof(Traverse), 
                [typeof(IQueryable<>).MakeGenericType(typeof(TEntity)), 
                 typeof(long), typeof(string), typeof(int), typeof(GraphTraversalStrategy)])!
            .MakeGenericMethod(typeof(TEntity));

        var methodCall = Expression.Call(
            methodInfo,
            source.Expression,
            Expression.Constant(startNodeId),
            Expression.Constant(relationshipColumn),
            Expression.Constant(maxDepth),
            Expression.Constant(strategy));

        return source.Provider.CreateQuery<long>(methodCall);
    }

    /// <summary>
    /// Filters entities by checking if their ID is within the traversal result set.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="traversalIds">The traversal result IDs to filter by.</param>
    /// <returns>Filtered queryable with IN clause.</returns>
    public static IQueryable<TEntity> WhereIn<TEntity>(
        this IQueryable<TEntity> source,
        IEnumerable<long> traversalIds) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(traversalIds);

        var ids = traversalIds.ToList();
        if (ids.Count == 0)
            return source.Where(x => false);

        // Build: WHERE Id IN (traversalIds)
        // This will be handled by EF Core's IN expression handling
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var idProperty = typeof(TEntity).GetProperty("Id") 
            ?? throw new InvalidOperationException($"Entity {typeof(TEntity).Name} must have an 'Id' property");

        var propertyAccess = Expression.Property(parameter, idProperty);
        var idList = Expression.Constant(ids);

        var containsMethod = typeof(List<long>).GetMethod("Contains", [typeof(long)])!;
        var containsCall = Expression.Call(idList, containsMethod, propertyAccess);

        var lambda = Expression.Lambda<Func<TEntity, bool>>(containsCall, parameter);

        return source.Where(lambda);
    }

    /// <summary>
    /// Executes graph traversal synchronously and caches results for subsequent queries.
    /// Useful for scenarios where you want to traverse first, then filter in-memory.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The traversal queryable.</param>
    /// <returns>Enumerable of reachable node IDs.</returns>
    public static IEnumerable<long> TraverseSync<TEntity>(
        this IQueryable<TEntity> source) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ToList().Cast<long>();
    }

    /// <summary>
    /// Executes graph traversal asynchronously.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The traversal queryable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task with enumerable of reachable node IDs.</returns>
    public static async Task<IEnumerable<long>> TraverseAsync<TEntity>(
        this IQueryable<TEntity> source,
        CancellationToken cancellationToken = default) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var results = await source.ToListAsync(cancellationToken);
        return results.Cast<long>();
    }

    /// <summary>
    /// Combines traversal with WHERE clause filtering in a single query.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="startNodeId">Starting node ID.</param>
    /// <param name="relationshipColumn">ROWREF column name.</param>
    /// <param name="maxDepth">Maximum depth.</param>
    /// <param name="strategy">Traversal strategy.</param>
    /// <param name="predicate">Additional filter predicate.</param>
    /// <returns>Filtered queryable with traversal results.</returns>
    public static IQueryable<TEntity> TraverseWhere<TEntity>(
        this IQueryable<TEntity> source,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        Expression<Func<TEntity, bool>> predicate) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);

        // First get traversal results
        var traversalIds = source.Traverse(startNodeId, relationshipColumn, maxDepth, strategy);

        // Then apply additional filter
        var parameter = predicate.Parameters[0];
        var idProperty = typeof(TEntity).GetProperty("Id")
            ?? throw new InvalidOperationException($"Entity {typeof(TEntity).Name} must have an 'Id' property");

        var propertyAccess = Expression.Property(parameter, idProperty);
        var idList = Expression.Constant(traversalIds.ToList());
        var containsMethod = typeof(List<long>).GetMethod("Contains", [typeof(long)])!;
        var inClause = Expression.Call(idList, containsMethod, propertyAccess);

        // Combine: WHERE (...traversal...) AND (user predicate)
        var combined = Expression.Lambda<Func<TEntity, bool>>(
            Expression.AndAlso(inClause, predicate.Body),
            parameter);

        return source.Where(combined);
    }

    /// <summary>
    /// Gets distinct traversal results (removes duplicates).
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The traversal queryable.</param>
    /// <returns>Distinct traversal results.</returns>
    public static IQueryable<long> Distinct<TEntity>(
        this IQueryable<TEntity> source) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        var castToLong = source.Cast<long>();
        return castToLong.Distinct();
    }

    /// <summary>
    /// Limits traversal results to a maximum count.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The traversal queryable.</param>
    /// <param name="count">Maximum number of results.</param>
    /// <returns>Limited traversal results.</returns>
    public static IQueryable<long> Take<TEntity>(
        this IQueryable<TEntity> source,
        int count) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Take count must be non-negative");

        var castToLong = source.Cast<long>();
        return castToLong.Take(count);
    }
}
