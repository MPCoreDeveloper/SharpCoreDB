#nullable enable

// <copyright file="GraphRagServiceCollectionExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Advanced.SqlIntegration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpCoreDB.Graph.Advanced.GraphRAG;
using SharpCoreDB.Interfaces;

/// <summary>
/// Dependency injection extensions for GRAPH_RAG SQL integration.
/// </summary>
public static class GraphRagServiceCollectionExtensions
{
    /// <summary>
    /// Registers GRAPH_RAG SQL execution support through <see cref="IGraphRagProvider"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Optional options configuration.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSharpCoreDBGraphRagSql(
        this IServiceCollection services,
        Action<GraphRagSqlOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GraphRagSqlOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.GraphTableName))
            throw new ArgumentException("GraphTableName must not be empty.", nameof(configure));

        if (string.IsNullOrWhiteSpace(options.EmbeddingTableName))
            throw new ArgumentException("EmbeddingTableName must not be empty.", nameof(configure));

        if (options.EmbeddingDimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(configure), "EmbeddingDimensions must be greater than zero.");

        services.TryAddSingleton(options);

        services.TryAddSingleton<GraphRagEngine>(sp =>
        {
            var db = sp.GetRequiredService<IDatabase>() as Database
                ?? throw new InvalidOperationException("GRAPH_RAG SQL integration requires concrete Database registration.");
            var opts = sp.GetRequiredService<GraphRagSqlOptions>();
            return new GraphRagEngine(db, opts.GraphTableName, opts.EmbeddingTableName, opts.EmbeddingDimensions);
        });

        services.TryAddSingleton<IGraphRagProvider>(sp =>
        {
            var db = sp.GetRequiredService<IDatabase>() as Database
                ?? throw new InvalidOperationException("GRAPH_RAG SQL integration requires concrete Database registration.");
            var engine = sp.GetRequiredService<GraphRagEngine>();
            var opts = sp.GetRequiredService<GraphRagSqlOptions>();
            return new GraphRagSqlProvider(db, engine, opts.EmbeddingProvider);
        });

        return services;
    }
}
