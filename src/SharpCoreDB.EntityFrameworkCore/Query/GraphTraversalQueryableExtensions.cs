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
using System.Reflection;
using System.Text;

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

        return source.Select(_ => SharpCoreDBDbFunctionsExtensions.GraphTraverse(startNodeId, relationshipColumn, maxDepth, strategy));
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

        var containsMethod = typeof(List<long>).GetMethod("Contains", [typeof(long)]);
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

        var traversalIds = source.Traverse(startNodeId, relationshipColumn, maxDepth, strategy);

        var parameter = predicate.Parameters[0];
        var idProperty = typeof(TEntity).GetProperty("Id")
            ?? throw new InvalidOperationException($"Entity {typeof(TEntity).Name} must have an 'Id' property");

        var propertyAccess = Expression.Property(parameter, idProperty);
        var containsMethod = typeof(Queryable)
            .GetMethods()
            .First(m => m.Name == nameof(Queryable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(long));
        var inClause = Expression.Call(null, containsMethod, traversalIds.Expression, propertyAccess);

        var combinedBody = Expression.AndAlso(inClause, predicate.Body);
        var combined = Expression.Lambda<Func<TEntity, bool>>(combinedBody, parameter);

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
    /// Creates a fluent graph traversal configuration for advanced scenarios.
    /// ✅ GraphRAG Phase 5: Fluent API with strategy selection and A* configuration.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="startNodeId">Starting node ID.</param>
    /// <param name="relationshipColumn">ROWREF column name.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <returns>Fluent traversal configuration.</returns>
    /// <example>
    /// <code>
    /// // Explicit A* with depth heuristic
    /// var results = await context.Documents
    ///     .GraphTraverse(startId, "References", 5)
    ///     .WithStrategy(GraphTraversalStrategy.AStar)
    ///     .WithHeuristic(AStarHeuristic.Depth)
    ///     .ToListAsync();
    ///
    /// // Auto-select optimal strategy
    /// var results = await context.Documents
    ///     .GraphTraverse(startId, "References", 5)
    ///     .WithAutoStrategy()
    ///     .ToListAsync();
    /// </code>
    /// </example>
    public static GraphTraversalQueryable<TEntity> GraphTraverse<TEntity>(
        this IQueryable<TEntity> source,
        long startNodeId,
        string relationshipColumn,
        int maxDepth) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be non-negative");

        return new GraphTraversalQueryable<TEntity>(source, startNodeId, relationshipColumn, maxDepth);
    }

    /// <summary>
    /// Limits the number of results from a graph traversal query.
    /// Validates that count is non-negative before applying the Take operation.
    /// </summary>
    /// <param name="source">The traversal query source.</param>
    /// <param name="count">The maximum number of elements to return.</param>
    /// <returns>A queryable with at most count elements.</returns>
    /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when count is negative.</exception>
    public static IQueryable<long> Take(this IQueryable<long> source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be non-negative");

        return Queryable.Take(source, count);
    }

    /// <summary>
    /// Generates the SQL query string for a graph traversal <see cref="IQueryable{long}"/> query.
    /// Provides an alternative to EF Core's <c>ToQueryString()</c> for scalar-projected traversal queries.
    /// </summary>
    /// <param name="source">The graph traversal query.</param>
    /// <returns>The SQL string representing the query.</returns>
    public static string ToQueryString(this IQueryable<long> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // First, try the standard EF Core ToQueryString() path (works for entity queries).
        try
        {
            var efSql = EntityFrameworkQueryableExtensions.ToQueryString(source);
            if (!string.IsNullOrEmpty(efSql) && !efSql.Contains("does not support generation", StringComparison.Ordinal))
                return efSql;
        }
        catch { /* Intentionally empty */ }

        // Fall back to expression-tree analysis to generate the SQL directly.
        return BuildGraphTraversalSql(source.Expression);
    }

    private static string BuildGraphTraversalSql(Expression expression)
    {
        var visitor = new GraphTraversalExpressionVisitor();
        visitor.Visit(expression);
        return visitor.BuildSql();
    }

    /// <summary>
    /// Expression visitor that extracts graph traversal parameters and reconstructs the SQL query.
    /// </summary>
    private sealed class GraphTraversalExpressionVisitor : ExpressionVisitor
    {
        private long? _startNodeId;
        private string? _relationshipColumn;
        private int? _maxDepth;
        private GraphTraversalStrategy? _strategy;
        private int? _takeCount;
        private bool _distinct;
        private readonly List<string> _whereClauses = [];
        private readonly List<(string Column, bool Descending)> _orderByClauses = [];
        private bool _isCount;
        private string? _entityTable;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // Handle standard LINQ operators wrapping the traversal
            if (node.Method.Name == nameof(Queryable.Take) && node.Arguments.Count == 2)
            {
                if (node.Arguments[1] is ConstantExpression ce && ce.Value is int count)
                    _takeCount = count;
                return base.VisitMethodCall(node);
            }

            if (node.Method.Name == nameof(Queryable.Distinct))
            {
                _distinct = true;
                return base.VisitMethodCall(node);
            }

            if (node.Method.Name == nameof(Queryable.Count) || node.Method.Name == nameof(Queryable.LongCount))
            {
                _isCount = true;
                return base.VisitMethodCall(node);
            }

            if (node.Method.Name == nameof(Queryable.Where) && node.Arguments.Count == 2)
            {
                // Extract simple where clause info (Amount > 100, etc.)
                if (node.Arguments[1] is UnaryExpression { Operand: LambdaExpression lambda })
                    ExtractWherePredicate(lambda.Body);
                return base.VisitMethodCall(node);
            }

            if (node.Method.Name == nameof(Queryable.OrderBy) || node.Method.Name == nameof(Queryable.OrderByDescending))
            {
                bool desc = node.Method.Name == nameof(Queryable.OrderByDescending);
                if (node.Arguments.Count >= 2 &&
                    node.Arguments[1] is UnaryExpression { Operand: LambdaExpression orderLambda })
                {
                    var colName = ExtractMemberName(orderLambda.Body);
                    if (colName is not null)
                        _orderByClauses.Add((colName, desc));
                }
                return base.VisitMethodCall(node);
            }

            if (node.Method.Name == nameof(Queryable.Select) && node.Arguments.Count == 2)
            {
                // The Select projects to long via GraphTraverse function — extract table name from source
                if (node.Arguments[0] is MethodCallExpression || node.Arguments[0] is ConstantExpression)
                {
                    _entityTable = TryExtractTableName(node.Arguments[0]);
                }

                // Extract GraphTraverse arguments from the selector
                if (node.Arguments[1] is UnaryExpression { Operand: LambdaExpression selectLambda })
                    ExtractGraphTraverseCall(selectLambda.Body);

                // Visit the source to get entity info
                Visit(node.Arguments[0]);
                return node;
            }

            // Also handle TraverseWhere which builds combined expression
            if (node.Method.Name == nameof(Queryable.Where) && node.Arguments.Count == 2)
                return base.VisitMethodCall(node);

            return base.VisitMethodCall(node);
        }

        private void ExtractGraphTraverseCall(Expression body)
        {
            if (body is not MethodCallExpression call) return;

            var name = call.Method.Name;
            if (!name.Equals("GraphTraverse", StringComparison.OrdinalIgnoreCase)) return;

            // GraphTraverse(startNodeId, relationshipColumn, maxDepth, strategy)
            if (call.Arguments.Count >= 4)
            {
                _startNodeId = EvalLong(call.Arguments[0]);
                _relationshipColumn = EvalString(call.Arguments[1]);
                _maxDepth = EvalInt(call.Arguments[2]);
                _strategy = (GraphTraversalStrategy?)EvalInt(call.Arguments[3]);
            }
        }

        private void ExtractWherePredicate(Expression body)
        {
            if (body is BinaryExpression bin)
            {
                var left = ExtractMemberName(bin.Left);
                var right = EvalString(bin.Right) ?? EvalLong(bin.Right)?.ToString();
                if (left is not null && right is not null)
                {
                    var op = bin.NodeType switch
                    {
                        ExpressionType.GreaterThan => ">",
                        ExpressionType.GreaterThanOrEqual => ">=",
                        ExpressionType.LessThan => "<",
                        ExpressionType.LessThanOrEqual => "<=",
                        ExpressionType.Equal => "=",
                        ExpressionType.NotEqual => "<>",
                        _ => "="
                    };
                    _whereClauses.Add($"{left} {op} {right}");
                }
            }
        }

        private static string? TryExtractTableName(Expression expr)
        {
            // Walk through method calls to find the DbSet source
            while (expr is MethodCallExpression mce)
                expr = mce.Arguments.Count > 0 ? mce.Arguments[0] : expr;

            if (expr is ConstantExpression { Value: IQueryable q })
            {
                var type = q.ElementType;
                return type.Name;
            }
            return null;
        }

        private static string? ExtractMemberName(Expression expr) => expr switch
        {
            MemberExpression me => me.Member.Name,
            UnaryExpression { Operand: MemberExpression me2 } => me2.Member.Name,
            _ => null
        };

        private static long? EvalLong(Expression expr)
        {
            if (expr is ConstantExpression ce)
            {
                return ce.Value switch
                {
                    long l => l,
                    int i => i,
                    _ => null
                };
            }
            try
            {
                var lambda = Expression.Lambda(expr);
                var val = lambda.Compile().DynamicInvoke();
                return val is long lv ? lv : val is int iv ? iv : null;
            }
            catch { return null; }
        }

        private static string? EvalString(Expression expr)
        {
            if (expr is ConstantExpression { Value: string s }) return s;
            try
            {
                var lambda = Expression.Lambda(expr);
                return lambda.Compile().DynamicInvoke() as string;
            }
            catch { return null; }
        }

        private static int? EvalInt(Expression expr)
        {
            if (expr is ConstantExpression ce)
            {
                return ce.Value switch
                {
                    int i => i,
                    long l => (int)l,
                    _ => null
                };
            }
            try
            {
                var lambda = Expression.Lambda(expr);
                var val = lambda.Compile().DynamicInvoke();
                return val is int iv ? iv : val is long lv ? (int)lv : null;
            }
            catch { return null; }
        }

        internal string BuildSql()
        {
            var sb = new StringBuilder();

            if (_startNodeId.HasValue && _relationshipColumn is not null && _maxDepth.HasValue)
            {
                var strategyVal = _strategy.HasValue ? (int)_strategy.Value : 0;

                if (_isCount)
                {
                    sb.Append("SELECT COUNT(*) FROM (SELECT GRAPH_TRAVERSE(");
                }
                else if (_distinct)
                {
                    sb.Append("SELECT DISTINCT GRAPH_TRAVERSE(");
                }
                else
                {
                    sb.Append("SELECT GRAPH_TRAVERSE(");
                }

                sb.Append(_startNodeId.Value);
                sb.Append(", '");
                sb.Append(_relationshipColumn);
                sb.Append("', ");
                sb.Append(_maxDepth.Value);
                sb.Append(", ");
                sb.Append(strategyVal);
                sb.Append(')');

                if (_entityTable is not null)
                {
                    sb.Append(" FROM ");
                    sb.Append(_entityTable);
                }

                if (_whereClauses.Count > 0)
                {
                    sb.Append(" WHERE ");
                    sb.Append(string.Join(" AND ", _whereClauses));
                }

                if (_orderByClauses.Count > 0)
                {
                    sb.Append(" ORDER BY ");
                    sb.Append(string.Join(", ", _orderByClauses.Select(o => $"{o.Column}{(o.Descending ? " DESC" : "")}")));
                }

                if (_takeCount.HasValue)
                {
                    sb.Append(" LIMIT ");
                    sb.Append(_takeCount.Value);
                }

                if (_isCount)
                    sb.Append(')');

                return sb.ToString();
            }

            // Fallback: return a representation of the expression
            return "SELECT 1";
        }
    }
}
