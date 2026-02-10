// <copyright file="VectorSearchExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

/// <summary>
/// Extension methods to register vector search support with SharpCoreDB.
/// </summary>
public static class VectorSearchExtensions
{
    /// <summary>
    /// Adds vector search support to SharpCoreDB.
    /// Registers VECTOR(N) type handling, vec_* SQL functions, index management,
    /// and the query optimizer for <c>ORDER BY vec_distance_*() LIMIT k</c> acceleration.
    /// </summary>
    /// <param name="services">The service collection (typically after <c>AddSharpCoreDB()</c>).</param>
    /// <param name="configure">Optional configuration callback for vector search options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddVectorSupport(
        this IServiceCollection services,
        Action<VectorSearchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new VectorSearchOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ICustomFunctionProvider, VectorFunctionProvider>();
        services.AddSingleton<ICustomTypeProvider>(sp =>
            new VectorTypeProvider(sp.GetRequiredService<VectorSearchOptions>()));

        // Phase 5.4: Vector index management + query planner optimization
        services.AddSingleton<VectorIndexManager>();
        services.AddSingleton<IVectorQueryOptimizer, VectorQueryOptimizer>();

        return services;
    }
}
