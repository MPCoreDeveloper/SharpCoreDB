// <copyright file="IGraphRagProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

using SharpCoreDB.Services;

/// <summary>
/// Optional extension point for GRAPH_RAG SQL clause execution.
/// Registered by Graph.Advanced package to keep core storage/query engine decoupled.
/// </summary>
public interface IGraphRagProvider
{
    /// <summary>
    /// Determines whether this provider can execute GRAPH_RAG against the target table.
    /// </summary>
    /// <param name="tableName">Target table name.</param>
    /// <returns>True when provider can execute request.</returns>
    bool CanExecute(string tableName);

    /// <summary>
    /// Executes a parsed GRAPH_RAG request and returns rows compatible with SQL result-set pipeline.
    /// </summary>
    /// <param name="request">GraphRAG request model.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result rows.</returns>
    Task<IReadOnlyList<Dictionary<string, object>>> ExecuteAsync(
        GraphRagRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// GraphRAG request model passed from SQL parser/executor to provider implementation.
/// </summary>
/// <param name="TableName">Primary table name from FROM clause.</param>
/// <param name="Question">Natural-language question prompt.</param>
/// <param name="Columns">Selected SQL columns.</param>
/// <param name="Limit">Limit hint.</param>
/// <param name="TopK">Candidate pool size hint.</param>
/// <param name="MinScore">Optional minimum score threshold.</param>
/// <param name="IncludeContext">Whether provider should enrich rows with context.</param>
public sealed record GraphRagRequest(
    string TableName,
    string Question,
    IReadOnlyList<string> Columns,
    int Limit,
    int TopK,
    double? MinScore,
    bool IncludeContext);
