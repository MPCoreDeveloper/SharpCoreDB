// <copyright file="DistanceFunction.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Supported vector distance/similarity functions.
/// </summary>
public enum DistanceFunction
{
    /// <summary>Cosine distance: 1 - cosine_similarity. Range [0, 2]. Lower = more similar.</summary>
    Cosine,

    /// <summary>Euclidean (L2) distance. Range [0, ∞). Lower = more similar.</summary>
    Euclidean,

    /// <summary>Negative dot product. Lower = more similar (higher inner product).</summary>
    DotProduct,
}
