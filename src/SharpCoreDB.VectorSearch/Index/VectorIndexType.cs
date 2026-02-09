// <copyright file="VectorIndexType.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Supported vector index types for similarity search.
/// </summary>
public enum VectorIndexType
{
    /// <summary>
    /// Brute-force exact search. Guarantees 100% recall.
    /// Best for small datasets (&lt; 10K vectors) or when perfect accuracy is required.
    /// Time complexity: O(N × D) per query where N = vectors, D = dimensions.
    /// </summary>
    Flat,

    /// <summary>
    /// Hierarchical Navigable Small World graph — approximate nearest neighbor.
    /// Typical recall: &gt; 95% with default parameters.
    /// Best for larger datasets (10K–10M vectors) where speed matters.
    /// Time complexity: O(log N × D) per query.
    /// </summary>
    Hnsw,
}
