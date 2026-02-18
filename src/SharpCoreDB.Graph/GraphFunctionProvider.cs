// <copyright file="GraphFunctionProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Interfaces;

/// <summary>
/// Provides SQL function evaluation for graph traversal functions.
/// </summary>
public sealed class GraphFunctionProvider : ICustomFunctionProvider
{
    private static readonly IReadOnlyList<string> FunctionNames = ["GRAPH_TRAVERSE"];
    private readonly GraphTraversalEngine _traversalEngine;

    public GraphFunctionProvider(GraphTraversalEngine traversalEngine)
    {
        _traversalEngine = traversalEngine ?? throw new ArgumentNullException(nameof(traversalEngine));
    }

    /// <inheritdoc />
    public bool CanHandle(string functionName) => FunctionNames.Contains(functionName);

    /// <inheritdoc />
    public object? Evaluate(string functionName, List<object?> arguments)
    {
        if (!functionName.Equals("GRAPH_TRAVERSE", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"Graph function '{functionName}' is not supported");
        }

        if (arguments.Count < 4)
        {
            throw new ArgumentException("GRAPH_TRAVERSE requires at least 4 arguments");
        }

        if (arguments[0] is not ITable table)
        {
            throw new ArgumentException("GRAPH_TRAVERSE expects the first argument to be an ITable instance");
        }

        var startNodeId = CoerceToLong(arguments[1], "start node");
        var relationshipColumn = CoerceToString(arguments[2], "relationship column");
        var maxDepth = CoerceToInt(arguments[3], "max depth");
        var strategy = arguments.Count >= 5
            ? CoerceToStrategy(arguments[4])
            : GraphTraversalStrategy.Bfs;

        var result = _traversalEngine.Traverse(table, startNodeId, relationshipColumn, maxDepth, strategy);
        return result.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetFunctionNames() => FunctionNames;

    private static long CoerceToLong(object? value, string argumentName)
    {
        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            string stringValue when long.TryParse(stringValue, out var parsed) => parsed,
            _ => throw new ArgumentException($"GRAPH_TRAVERSE {argumentName} must be a numeric value")
        };
    }

    private static int CoerceToInt(object? value, string argumentName)
    {
        return value switch
        {
            int intValue => intValue,
            short shortValue => shortValue,
            long longValue when longValue <= int.MaxValue && longValue >= int.MinValue => (int)longValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => throw new ArgumentException($"GRAPH_TRAVERSE {argumentName} must be an integer value")
        };
    }

    private static string CoerceToString(object? value, string argumentName)
    {
        return value switch
        {
            string stringValue when !string.IsNullOrWhiteSpace(stringValue) => stringValue,
            _ => throw new ArgumentException($"GRAPH_TRAVERSE {argumentName} must be a non-empty string")
        };
    }

    private static GraphTraversalStrategy CoerceToStrategy(object? value)
    {
        return value switch
        {
            GraphTraversalStrategy strategy => strategy,
            int intValue when Enum.IsDefined(typeof(GraphTraversalStrategy), intValue)
                => (GraphTraversalStrategy)intValue,
            short shortValue when Enum.IsDefined(typeof(GraphTraversalStrategy), (int)shortValue)
                => (GraphTraversalStrategy)shortValue,
            string stringValue when stringValue.Equals("BFS", StringComparison.OrdinalIgnoreCase)
                => GraphTraversalStrategy.Bfs,
            string stringValue when stringValue.Equals("DFS", StringComparison.OrdinalIgnoreCase)
                => GraphTraversalStrategy.Dfs,
            string stringValue when stringValue.Equals("BIDIRECTIONAL", StringComparison.OrdinalIgnoreCase)
                => GraphTraversalStrategy.Bidirectional,
            string stringValue when stringValue.Equals("DIJKSTRA", StringComparison.OrdinalIgnoreCase)
                => GraphTraversalStrategy.Dijkstra,
            _ => throw new ArgumentException("GRAPH_TRAVERSE strategy must be BFS, DFS, BIDIRECTIONAL, or DIJKSTRA")
        };
    }
}
