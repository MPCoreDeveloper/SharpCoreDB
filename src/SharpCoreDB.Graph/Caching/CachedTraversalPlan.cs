// <copyright file="CachedTraversalPlan.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Caching;

using SharpCoreDB.Interfaces;
using System;

/// <summary>
/// Represents a cached traversal plan with metadata.
/// ✅ GraphRAG Phase 5.2: Stores compiled traversal strategy and execution hints.
/// </summary>
public sealed class CachedTraversalPlan
{
    /// <summary>
    /// Initializes a new cached traversal plan.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="strategy">The optimal strategy.</param>
    /// <param name="estimatedCardinality">Estimated result count.</param>
    /// <param name="createdAt">Plan creation timestamp.</param>
    public CachedTraversalPlan(
        TraversalPlanCacheKey key,
        GraphTraversalStrategy strategy,
        long estimatedCardinality,
        DateTime createdAt)
    {
        Key = key;
        Strategy = strategy;
        EstimatedCardinality = estimatedCardinality;
        CreatedAt = createdAt;
        LastAccessedAt = createdAt;
        AccessCount = 0;
    }

    /// <summary>
    /// Gets the cache key.
    /// </summary>
    public TraversalPlanCacheKey Key { get; }

    /// <summary>
    /// Gets the selected traversal strategy.
    /// </summary>
    public GraphTraversalStrategy Strategy { get; }

    /// <summary>
    /// Gets the estimated result cardinality.
    /// </summary>
    public long EstimatedCardinality { get; }

    /// <summary>
    /// Gets the plan creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Gets or sets the last access timestamp.
    /// </summary>
    public DateTime LastAccessedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of times this plan has been accessed.
    /// </summary>
    public int AccessCount { get; set; }

    /// <summary>
    /// Gets the plan age in seconds.
    /// </summary>
    public double AgeInSeconds => (DateTime.Now - CreatedAt).TotalSeconds;

    /// <summary>
    /// Gets the time since last access in seconds.
    /// </summary>
    public double TimeSinceLastAccessSeconds => (DateTime.Now - LastAccessedAt).TotalSeconds;

    /// <summary>
    /// Records an access to this cached plan.
    /// </summary>
    public void RecordAccess()
    {
        LastAccessedAt = DateTime.Now;
        AccessCount++;
    }

    /// <summary>
    /// Determines if the plan is stale based on TTL.
    /// </summary>
    /// <param name="ttlSeconds">Time-to-live in seconds.</param>
    /// <returns>True if the plan has expired.</returns>
    public bool IsStale(double ttlSeconds)
    {
        return AgeInSeconds > ttlSeconds;
    }
}
