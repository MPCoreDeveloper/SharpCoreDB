// <copyright file="VectorMemoryInfo.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Diagnostic information about vector index memory usage.
/// </summary>
/// <param name="IndexName">The index name.</param>
/// <param name="IndexType">The index type (Flat or HNSW).</param>
/// <param name="VectorCount">Number of vectors in the index.</param>
/// <param name="Dimensions">Vector dimensionality.</param>
/// <param name="EstimatedMemoryBytes">Estimated memory usage in bytes.</param>
/// <param name="QuantizationType">Active quantization method, if any.</param>
public readonly record struct VectorMemoryInfo(
    string IndexName,
    VectorIndexType IndexType,
    int VectorCount,
    int Dimensions,
    long EstimatedMemoryBytes,
    QuantizationType QuantizationType = QuantizationType.None)
{
    /// <summary>Gets the estimated memory usage in megabytes.</summary>
    public double EstimatedMemoryMB => EstimatedMemoryBytes / (1024.0 * 1024.0);

    /// <summary>Gets the bytes per vector (including index overhead).</summary>
    public double BytesPerVector => VectorCount > 0
        ? (double)EstimatedMemoryBytes / VectorCount
        : 0;
}
