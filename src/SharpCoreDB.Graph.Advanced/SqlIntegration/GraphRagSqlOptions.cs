#nullable enable

// <copyright file="GraphRagSqlOptions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Advanced.SqlIntegration;

/// <summary>
/// Options for GRAPH_RAG SQL provider registration.
/// </summary>
public sealed class GraphRagSqlOptions
{
    /// <summary>
    /// Gets or sets the graph edge table name used by GraphRAG engine.
    /// </summary>
    public string GraphTableName { get; set; } = "graph_edges";

    /// <summary>
    /// Gets or sets the embedding table name used by GraphRAG engine.
    /// </summary>
    public string EmbeddingTableName { get; set; } = "document_embeddings";

    /// <summary>
    /// Gets or sets embedding vector dimensions.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 16;

    /// <summary>
    /// Gets or sets optional embedding provider delegate.
    /// </summary>
    public Func<string, float[]>? EmbeddingProvider { get; set; }
}
