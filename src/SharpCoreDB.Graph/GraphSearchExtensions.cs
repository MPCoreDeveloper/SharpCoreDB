// <copyright file="GraphSearchExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

/// <summary>
/// Extension methods to register graph traversal support with SharpCoreDB.
/// </summary>
public static class GraphSearchExtensions
{
    /// <summary>
    /// Adds graph traversal support to SharpCoreDB.
    /// Registers ROWREF graph traversal services and SQL function providers.
    /// </summary>
    /// <param name="services">The service collection (typically after <c>AddSharpCoreDB()</c>).</param>
    /// <param name="configure">Optional configuration callback for graph options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGraphSupport(
        this IServiceCollection services,
        Action<GraphSearchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new GraphSearchOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<GraphTraversalEngine>();
        services.AddSingleton<ICustomFunctionProvider, GraphFunctionProvider>();
        services.AddSingleton<IGraphTraversalProvider, GraphTraversalProvider>();

        return services;
    }
}
