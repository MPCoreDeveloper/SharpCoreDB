// <copyright file="FlatIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Runtime.CompilerServices;

/// <summary>
/// Brute-force exact vector search index. Guarantees 100% recall and is the <b>correctness
/// oracle</b> every approximate engine is measured against: an approximate index whose recall
/// nobody can compute is an approximate index nobody can promote.
/// </summary>
/// <remarks>
/// Storage is a contiguous struct-of-arrays (<c>float[]</c> + <c>long[]</c>) rather than one
/// <c>float[]</c> per vector: a scan touches one linear buffer, so it is cache-friendly and
/// allocation-free. Queries capture an immutable <see cref="Snapshot"/> reference (a volatile
/// publish), so reads are lock-free and never observe a half-written row. Removals leave a
/// tombstone so a concurrent scan never sees a shifted slot.
/// </remarks>
public sealed class FlatIndex : IVectorIndex
{
    private const long Tombstone = long.MinValue;

    private readonly int _dimensions;
    private readonly DistanceFunction _distanceFunction;
    private readonly Lock _writeLock = new();
    private readonly Dictionary<long, int> _idToSlot = new();

    private float[] _data;
    private long[] _ids;
    private int _capacity;
    private int _length;
    private int _liveCount;

    private volatile Snapshot _snapshot;

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

        _capacity = 4;
        _data = new float[_capacity * dimensions];
        _ids = new long[_capacity];
        _snapshot = new Snapshot(_data, _ids, 0, 0);
    }

    /// <inheritdoc />
    public VectorIndexType IndexType => VectorIndexType.Flat;

    /// <inheritdoc />
    public int Count => _snapshot.LiveCount;

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public DistanceFunction DistanceFunction => _distanceFunction;

    /// <inheritdoc />
    public long EstimatedMemoryBytes => ((long)_capacity * _dimensions * sizeof(float)) + ((long)_capacity * sizeof(long)) + 128;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The vector has the wrong dimensionality or holds a non-finite value.</exception>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(long id, ReadOnlySpan<float> vector)
    {
        if (vector.Length != _dimensions)
        {
            throw new ArgumentException(
                $"Vector has {vector.Length} dimensions, index requires {_dimensions}");
        }

        if (!IsFinite(vector))
        {
            // A NaN would make every comparison false and quietly sort the vector to wherever the
            // algorithm happens to leave it.
            throw new ArgumentException($"Vector for id {id} holds a non-finite value");
        }

        lock (_writeLock)
        {
            if (_idToSlot.TryGetValue(id, out int existing))
            {
                // Overwrite in place, then re-publish so a reader that captured the new snapshot
                // sees the fully written row (the volatile write is a release barrier).
                vector.CopyTo(_data.AsSpan(existing * _dimensions, _dimensions));
                _snapshot = new Snapshot(_data, _ids, _length, _liveCount);
                return;
            }

            if (_length == _capacity)
            {
                Grow();
            }

            int slot = _length;
            vector.CopyTo(_data.AsSpan(slot * _dimensions, _dimensions));
            _ids[slot] = id;
            _idToSlot[id] = slot;
            _length++;
            _liveCount++;

            _snapshot = new Snapshot(_data, _ids, _length, _liveCount);
        }
    }

    /// <inheritdoc />
    public bool Remove(long id)
    {
        lock (_writeLock)
        {
            if (!_idToSlot.Remove(id, out int slot))
            {
                return false;
            }

            _ids[slot] = Tombstone;
            _liveCount--;
            _snapshot = new Snapshot(_data, _ids, _length, _liveCount);
            return true;
        }
    }

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

        // One consistent snapshot for the whole scan: lock-free reads, no re-allocation per query.
        Snapshot snap = _snapshot;
        int length = snap.Length;
        if (length == 0 || snap.LiveCount == 0)
        {
            return [];
        }

        int effectiveK = Math.Min(k, snap.LiveCount);
        var heap = new TopKHeap(effectiveK);

        float[] data = snap.Data;
        long[] ids = snap.Ids;
        int dims = _dimensions;
        DistanceFunction metric = _distanceFunction;

        for (int slot = 0; slot < length; slot++)
        {
            long id = ids[slot];
            if (id == Tombstone)
            {
                continue;
            }

            float distance = DistanceMetrics.Compute(query, data.AsSpan(slot * dims, dims), metric);
            heap.TryAdd(id, distance);
        }

        var results = heap.ToSortedArray();

        // Deterministic total order: distance first, id as the tie-break, so two runs agree.
        Array.Sort(results, static (a, b) =>
        {
            int byDistance = a.Distance.CompareTo(b.Distance);
            return byDistance != 0 ? byDistance : a.Id.CompareTo(b.Id);
        });

        return results;
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_writeLock)
        {
            _idToSlot.Clear();
            _capacity = 4;
            _data = new float[_capacity * _dimensions];
            _ids = new long[_capacity];
            _length = 0;
            _liveCount = 0;
            _snapshot = new Snapshot(_data, _ids, 0, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose() => Clear();

    private static bool IsFinite(ReadOnlySpan<float> vector)
    {
        for (int i = 0; i < vector.Length; i++)
        {
            if (!float.IsFinite(vector[i]))
            {
                return false;
            }
        }

        return true;
    }

    private void Grow()
    {
        int newCapacity = _capacity * 2;
        var newData = new float[newCapacity * _dimensions];
        var newIds = new long[newCapacity];
        Array.Copy(_data, newData, (long)_length * _dimensions);
        Array.Copy(_ids, newIds, _length);
        _data = newData;
        _ids = newIds;
        _capacity = newCapacity;
    }

    /// <summary>An immutable, consistently-published view of the store.</summary>
    private sealed record Snapshot(float[] Data, long[] Ids, int Length, int LiveCount);
}
