// <copyright file="HnswConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Configuration for an HNSW (Hierarchical Navigable Small World) vector index.
/// These parameters control the trade-off between recall, speed, and memory usage.
/// </summary>
public sealed class HnswConfig
{
    /// <summary>
    /// Maximum number of bidirectional connections per node per layer.
    /// Higher M improves recall and reduces search hops but increases memory.
    /// Recommended: 12–48. Default: 16.
    /// Memory per node ≈ M × 2 × 8 bytes (int64 neighbor IDs) × avg layers.
    /// </summary>
    public int M { get; init; } = 16;

    /// <summary>
    /// Maximum connections at layer 0 (the densest layer).
    /// Defaults to 2 × <see cref="M"/> per the original HNSW paper.
    /// </summary>
    public int MaxM0 { get; init; }

    /// <summary>
    /// Search width during index construction. Higher values produce a better graph
    /// at the cost of slower inserts.
    /// Recommended: 100–400. Default: 200.
    /// </summary>
    public int EfConstruction { get; init; } = 200;

    /// <summary>
    /// Search width at query time. Higher values improve recall at the cost of
    /// query latency. Must be ≥ k (number of results requested).
    /// Recommended: 20–500. Default: 50.
    /// </summary>
    public int EfSearch { get; init; } = 50;

    /// <summary>
    /// Vector dimensionality. All vectors in this index must have exactly this many elements.
    /// </summary>
    public int Dimensions { get; init; }

    /// <summary>
    /// Distance function used for similarity comparison.
    /// </summary>
    public DistanceFunction DistanceFunction { get; init; } = DistanceFunction.Cosine;

    /// <summary>
    /// Level generation probability multiplier: 1 / ln(M).
    /// Controls how many layers the graph has. Lower values produce fewer layers (shallower graph).
    /// Computed automatically from <see cref="M"/> when left at 0.
    /// </summary>
    public double LevelMultiplier { get; init; }

    /// <summary>
    /// Gets the effective MaxM0 (defaults to 2 × M when not explicitly set).
    /// </summary>
    internal int EffectiveMaxM0 => MaxM0 > 0 ? MaxM0 : M * 2;

    /// <summary>
    /// Gets the effective level multiplier (defaults to 1/ln(M) when not explicitly set).
    /// </summary>
    internal double EffectiveLevelMultiplier => LevelMultiplier > 0
        ? LevelMultiplier
        : 1.0 / Math.Log(Math.Max(M, 2));

    /// <summary>
    /// Validates configuration and throws if any parameters are out of range.
    /// </summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Dimensions);
        ArgumentOutOfRangeException.ThrowIfLessThan(M, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(EfConstruction, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(EfSearch, 1);
    }

    /// <summary>Default configuration for general-purpose use.</summary>
    public static HnswConfig Default(int dimensions) => new()
    {
        Dimensions = dimensions,
        M = 16,
        EfConstruction = 200,
        EfSearch = 50,
    };

    /// <summary>High recall configuration (better accuracy, slower queries).</summary>
    public static HnswConfig HighRecall(int dimensions) => new()
    {
        Dimensions = dimensions,
        M = 32,
        EfConstruction = 400,
        EfSearch = 200,
    };

    /// <summary>Low memory configuration (less memory, lower recall).</summary>
    public static HnswConfig LowMemory(int dimensions) => new()
    {
        Dimensions = dimensions,
        M = 8,
        EfConstruction = 100,
        EfSearch = 30,
    };
}
