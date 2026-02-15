// <copyright file="GraphSearchOptions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

/// <summary>
/// Configuration options for graph traversal features.
/// </summary>
public sealed class GraphSearchOptions
{
    /// <summary>
    /// Gets the default maximum traversal depth when not specified.
    /// </summary>
    public int DefaultMaxDepth { get; init; } = 3;

    /// <summary>
    /// Gets the maximum allowed traversal depth.
    /// </summary>
    public int MaxDepthLimit { get; init; } = 100;

    /// <summary>
    /// Gets a value indicating whether adjacency caching is enabled.
    /// </summary>
    public bool EnableAdjacencyCache { get; init; } = false;
}
