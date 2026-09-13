// <copyright file="DiskAnnConfig.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore/vector_diskann.rs GraphParams.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

/// <summary>
/// Configuration for DiskANN-style vector index (munarium-inspired).
/// Focuses on high-recall approximate nearest neighbor search with selective disk access.
/// Parameters tuned for recall vs. latency/memory trade-off (crossover testing recommended).
/// </summary>
public sealed class DiskAnnConfig
{
    /// <summary>
    /// Maximum number of neighbors per node (similar to HNSW M).
    /// Higher values improve recall but increase graph size.
    /// Recommended: 20-100. Default: 32.
    /// </summary>
    public int MaxNeighbors { get; init; } = 32;

    /// <summary>
    /// Search list size during construction (efConstruction equivalent).
    /// Higher values produce better graph quality.
    /// Recommended: 100-500. Default: 200.
    /// </summary>
    public int ConstructionSearchListSize { get; init; } = 200;

    /// <summary>
    /// Query search list size (efSearch equivalent).
    /// Higher values improve recall at query time.
    /// Recommended: 50-400. Default: 100.
    /// </summary>
    public int QuerySearchListSize { get; init; } = 100;

    /// <summary>
    /// Vector dimensionality (must match all indexed vectors).
    /// </summary>
    public int Dimensions { get; init; }

    /// <summary>
    /// Distance function (Cosine, Euclidean, etc.).
    /// </summary>
    public DistanceFunction DistanceFunction { get; init; } = DistanceFunction.Cosine;

    /// <summary>
    /// Threshold for "good enough" recall during crossover testing.
    /// Used in tests to validate against exact flat index.
    /// </summary>
    public double TargetRecall { get; init; } = 0.95;

    /// <summary>
    /// Validates configuration parameters.
    /// </summary>
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Dimensions);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxNeighbors, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(ConstructionSearchListSize, 10);
        ArgumentOutOfRangeException.ThrowIfLessThan(QuerySearchListSize, 10);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(TargetRecall);
    }

    /// <summary>Default high-recall configuration (matches munarium diskann defaults).</summary>
    public static DiskAnnConfig Default(int dimensions) => new()
    {
        Dimensions = dimensions,
        MaxNeighbors = 32,
        ConstructionSearchListSize = 200,
        QuerySearchListSize = 100,
        TargetRecall = 0.95
    };

    /// <summary>Ultra-high recall configuration (slower but >98% recall).</summary>
    public static DiskAnnConfig HighRecall(int dimensions) => new()
    {
        Dimensions = dimensions,
        MaxNeighbors = 64,
        ConstructionSearchListSize = 400,
        QuerySearchListSize = 200,
        TargetRecall = 0.98
    };
}
