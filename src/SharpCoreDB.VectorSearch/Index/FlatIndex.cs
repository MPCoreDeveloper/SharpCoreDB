// <copyright file="FlatIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// Brute-force exact vector search index. Guarantees 100% recall.
/// Stores all vectors in memory and scans every one on each query.
/// Best for small datasets (&lt; 10K vectors) or when perfect accuracy is required.
/// Thread-safe: concurrent reads via <see cref="ConcurrentDictionary{TKey,TValue}"/>,
/// writes are lock-free for individual operations.
/// </summary>
public sealed class FlatIndex : IVectorIndex
{
    private readonly ConcurrentDictionary<long, float[]> _vectors = new();
    private readonly DistanceFunction _distanceFunction;
    private readonly int _dimensions;

    /// <summary>
    /// Initializes a new <see cref="FlatIndex"/> for vectors with the given dimensionality.
    /// </summary>
    /// <param name="dimensions">Fixed dimension count (must be &gt; 0).</param>
    /// <param name="distanceFunction">Distance metric for similarity comparison.</param>
    public FlatIndex(int dimensions, DistanceFunction distanceFunction = DistanceFunction.Cosine)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        _dimensions = dimensions;
        _distanceFunction = distanceFunction;
    }

    /// <inheritdoc />
    public VectorIndexType IndexType => VectorIndexType.Flat;

    /// <inheritdoc />
    public int Count => _vectors.Count;

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public DistanceFunction DistanceFunction => _distanceFunction;

    /// <inheritdoc />
    public long EstimatedMemoryBytes =>
        _vectors.Count * ((_dimensions * sizeof(float)) + 64L); // 64 bytes overhead per entry estimate

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(long id, ReadOnlySpan<float> vector)
    {
        if (vector.Length != _dimensions)
        {
            throw new ArgumentException(
                $"Vector has {vector.Length} dimensions, index requires {_dimensions}");
        }

        _vectors[id] = vector.ToArray();
    }

    /// <inheritdoc />
    public bool Remove(long id) => _vectors.TryRemove(id, out _);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public IReadOnlyList<VectorSearchResult> Search(ReadOnlySpan<float> query, int k)
    {
        if (query.Length != _dimensions)
        {
            throw new ArgumentException(
                $"Query vector has {query.Length} dimensions, index requires {_dimensions}");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        if (_vectors.IsEmpty)
            return [];

        // Clamp k to actual count
        int effectiveK = Math.Min(k, _vectors.Count);
        var heap = new TopKHeap(effectiveK);

        // Copy query to array so we can use it safely across the enumeration
        float[] queryArr = query.ToArray();

        foreach (var (id, vector) in _vectors)
        {
            float distance = DistanceMetrics.Compute(queryArr, vector, _distanceFunction);
            heap.TryAdd(id, distance);
        }

        return heap.ToSortedArray();
    }

    /// <inheritdoc />
    public void Clear() => _vectors.Clear();

    /// <inheritdoc />
    public void Dispose()
    {
        _vectors.Clear();
    }
}
