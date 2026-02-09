// <copyright file="TopKHeap.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Runtime.CompilerServices;

/// <summary>
/// Fixed-capacity max-heap for efficient top-k nearest neighbor selection.
/// Maintains the k smallest distances seen so far.
/// Uses a max-heap so the largest distance (worst candidate) is always at the root,
/// allowing O(log k) replacement when a better candidate is found.
/// Total complexity: O(N log k) instead of O(N log N) for full sort.
/// </summary>
internal sealed class TopKHeap
{
    private readonly VectorSearchResult[] _heap;
    private int _count;

    /// <summary>
    /// Initializes a new <see cref="TopKHeap"/> with fixed capacity k.
    /// </summary>
    /// <param name="k">Maximum number of results to keep (must be &gt; 0).</param>
    internal TopKHeap(int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        _heap = new VectorSearchResult[k];
        _count = 0;
    }

    /// <summary>Gets the number of items currently in the heap.</summary>
    internal int Count => _count;

    /// <summary>Gets the capacity (k).</summary>
    internal int Capacity => _heap.Length;

    /// <summary>
    /// Gets the current worst (largest) distance in the heap.
    /// Returns <see cref="float.MaxValue"/> when the heap is not yet full.
    /// </summary>
    internal float WorstDistance
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count < _heap.Length ? float.MaxValue : _heap[0].Distance;
    }

    /// <summary>
    /// Tries to add a result to the heap. The candidate is accepted if the heap is not full
    /// or if the candidate's distance is smaller than the current worst distance.
    /// </summary>
    /// <param name="id">Row identifier.</param>
    /// <param name="distance">Computed distance to the query vector.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void TryAdd(long id, float distance)
    {
        if (_count < _heap.Length)
        {
            // Heap not full yet — just add
            _heap[_count] = new VectorSearchResult(id, distance);
            _count++;

            if (_count == _heap.Length)
            {
                // Heap just became full — build max-heap
                BuildHeap();
            }
        }
        else if (distance < _heap[0].Distance)
        {
            // Better than worst — replace root and sift down
            _heap[0] = new VectorSearchResult(id, distance);
            SiftDown(0);
        }
    }

    /// <summary>
    /// Returns results sorted by ascending distance (closest first).
    /// </summary>
    internal VectorSearchResult[] ToSortedArray()
    {
        var result = new VectorSearchResult[_count];
        Array.Copy(_heap, result, _count);
        Array.Sort(result);
        return result;
    }

    /// <summary>
    /// Builds a max-heap from the unsorted array (Floyd's algorithm, O(k)).
    /// </summary>
    private void BuildHeap()
    {
        for (int i = (_count / 2) - 1; i >= 0; i--)
        {
            SiftDown(i);
        }
    }

    /// <summary>
    /// Sifts a node down to restore the max-heap property.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SiftDown(int index)
    {
        int count = _count;

        while (true)
        {
            int largest = index;
            int left = (2 * index) + 1;
            int right = (2 * index) + 2;

            if (left < count && _heap[left].Distance > _heap[largest].Distance)
                largest = left;

            if (right < count && _heap[right].Distance > _heap[largest].Distance)
                largest = right;

            if (largest == index)
                break;

            (_heap[index], _heap[largest]) = (_heap[largest], _heap[index]);
            index = largest;
        }
    }
}
