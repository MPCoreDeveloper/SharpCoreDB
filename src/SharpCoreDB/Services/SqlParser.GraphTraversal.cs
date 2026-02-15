// <copyright file="SqlParser.GraphTraversal.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// GraphRAG execution support for SqlParser.
/// Handles evaluation of GRAPH_TRAVERSE expressions in WHERE clauses.
/// ✅ GraphRAG Phase 1: Core integration for graph traversal queries.
/// NOTE: Graph operations are accessed through IGraphTraversalProvider interface
/// to avoid circular dependencies with SharpCoreDB.Graph package.
/// </summary>
public partial class SqlParser
{
    private IGraphTraversalProvider? _graphTraversalProvider;
    private Dictionary<string, ITable>? _tablesForGraphTraversal;

    /// <summary>
    /// Sets the graph traversal provider for query execution.
    /// Called by query execution context to enable GRAPH_TRAVERSE support.
    /// </summary>
    /// <param name="provider">The graph traversal provider instance.</param>
    /// <param name="tables">The tables dictionary for lookups during traversal.</param>
    public void SetGraphTraversalContext(IGraphTraversalProvider provider, Dictionary<string, ITable> tables)
    {
        _graphTraversalProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        _tablesForGraphTraversal = tables ?? throw new ArgumentNullException(nameof(tables));
    }

    /// <summary>
    /// Evaluates a GRAPH_TRAVERSE expression to return node IDs reachable via graph traversal.
    /// ✅ GraphRAG Phase 1: Core evaluation for index-free graph queries.
    /// </summary>
    /// <param name="traverseNode">The GRAPH_TRAVERSE AST node.</param>
    /// <param name="row">The current row context (for resolving parameter expressions).</param>
    /// <returns>Array of reachable node IDs.</returns>
    internal long[]? EvaluateGraphTraverse(GraphTraverseNode traverseNode, Dictionary<string, object> row)
    {
        if (traverseNode is null || _graphTraversalProvider is null || _tablesForGraphTraversal is null)
        {
            return null;
        }

        try
        {
            // Resolve start node from expression
            var startNodeValue = EvaluateExpressionValue(traverseNode.StartNode, row);
            if (!TryCoerceLong(startNodeValue, out var startNodeId))
            {
                return null;
            }

            // Resolve max depth from expression
            var maxDepthValue = EvaluateExpressionValue(traverseNode.MaxDepth, row);
            if (!TryCoerceInt(maxDepthValue, out var maxDepth))
            {
                return null;
            }

            // Get table
            if (!_tablesForGraphTraversal.TryGetValue(traverseNode.TableName, out var table))
            {
                return null;
            }

            // Parse strategy
            var strategy = traverseNode.Strategy == "DFS" ? GraphTraversalStrategy.Dfs : GraphTraversalStrategy.Bfs;

            // Execute traversal
            var result = _graphTraversalProvider.TraverseAsync(
                table,
                traverseNode.TableName,
                startNodeId,
                traverseNode.RelationshipColumn,
                maxDepth,
                strategy).Result; // Sync wait for now; could be made async in future

            return result.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GRAPH_TRAVERSE evaluation failed: {ex.Message}");
            return null;
        }
    }

    private static bool TryCoerceLong(object? value, out long result)
    {
        result = 0;
        return value switch
        {
            long longValue => (result = longValue) >= 0,
            int intValue => (result = intValue) >= 0,
            short shortValue => (result = shortValue) >= 0,
            string stringValue when long.TryParse(stringValue, out var parsed) => (result = parsed) >= 0,
            _ => false
        };
    }

    private static bool TryCoerceInt(object? value, out int result)
    {
        result = 0;
        return value switch
        {
            int intValue => (result = intValue) >= 0,
            short shortValue => (result = shortValue) >= 0,
            long longValue when longValue >= 0 && longValue <= int.MaxValue => (result = (int)longValue) >= 0,
            string stringValue when int.TryParse(stringValue, out var parsed) => (result = parsed) >= 0,
            _ => false
        };
    }
}
