// <copyright file="IVectorIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Interface for vector similarity search indexes.
/// Implementations must be safe for concurrent reads.
/// </summary>
public interface IVectorIndex : IDisposable
{
    /// <summary>Gets the index type.</summary>
    VectorIndexType IndexType { get; }

    /// <summary>Gets the number of vectors currently in the index.</summary>
    int Count { get; }

    /// <summary>Gets the vector dimensionality this index was created for.</summary>
    int Dimensions { get; }

    /// <summary>Gets the distance function used by this index.</summary>
    DistanceFunction DistanceFunction { get; }

    /// <summary>
    /// Adds a vector to the index.
    /// </summary>
    /// <param name="id">Unique row identifier (storage reference).</param>
    /// <param name="vector">The vector data. Length must equal <see cref="Dimensions"/>.</param>
    void Add(long id, ReadOnlySpan<float> vector);

    /// <summary>
    /// Removes a vector from the index by its row identifier.
    /// </summary>
    /// <param name="id">The row identifier to remove.</param>
    /// <returns>True if the vector was found and removed.</returns>
    bool Remove(long id);

    /// <summary>
    /// Searches for the k nearest neighbors to the query vector.
    /// </summary>
    /// <param name="query">The query vector. Length must equal <see cref="Dimensions"/>.</param>
    /// <param name="k">Maximum number of results to return.</param>
    /// <returns>Results ordered by ascending distance (closest first).</returns>
    IReadOnlyList<VectorSearchResult> Search(ReadOnlySpan<float> query, int k);

    /// <summary>
    /// Removes all vectors from the index.
    /// </summary>
    void Clear();

    /// <summary>
    /// Gets estimated memory usage in bytes.
    /// </summary>
    long EstimatedMemoryBytes { get; }
}

/// <summary>
/// A single result from a vector similarity search.
/// </summary>
/// <param name="Id">The row identifier of the matched vector.</param>
/// <param name="Distance">The computed distance (lower = more similar for all supported metrics).</param>
public readonly record struct VectorSearchResult(long Id, float Distance) : IComparable<VectorSearchResult>
{
    /// <inheritdoc />
    public int CompareTo(VectorSearchResult other) => Distance.CompareTo(other.Distance);
}
