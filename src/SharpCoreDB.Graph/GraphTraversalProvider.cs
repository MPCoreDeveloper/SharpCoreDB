// <copyright file="GraphTraversalProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Interfaces;

/// <summary>
/// Graph traversal provider scaffolding for ROWREF-based traversal.
/// </summary>
public sealed class GraphTraversalProvider(GraphSearchOptions options, GraphTraversalEngine traversalEngine) : IGraphTraversalProvider
{
    private readonly GraphSearchOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly GraphTraversalEngine _traversalEngine = traversalEngine ?? throw new ArgumentNullException(nameof(traversalEngine));

    /// <inheritdoc />
    public bool CanTraverse(string tableName, string relationshipColumn, int maxDepth)
        => !string.IsNullOrWhiteSpace(tableName)
            && !string.IsNullOrWhiteSpace(relationshipColumn)
            && maxDepth >= 0
            && maxDepth <= _options.MaxDepthLimit;

    /// <inheritdoc />
    public Task<IReadOnlyCollection<long>> TraverseAsync(
        ITable table,
        string tableName,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (!CanTraverse(tableName, relationshipColumn, maxDepth))
        {
            throw new InvalidOperationException(
                $"Graph traversal is not supported for table '{tableName}' and column '{relationshipColumn}'.")
                ;
        }

        return _traversalEngine.TraverseAsync(
            table,
            startNodeId,
            relationshipColumn,
            maxDepth,
            strategy,
            cancellationToken);
    }
}
