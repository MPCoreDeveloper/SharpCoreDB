// <copyright file="BatchOperationList.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using System.Buffers;
using System.Collections;

/// <summary>
/// A growable list for the batch dispatcher's per-table operation buckets, backed by an array rented
/// from <see cref="ArrayPool{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Why it exists (session 79, plan §5.5 (ii) residual <c>parse</c> stage). A bucket holds one operation
/// per statement, and <see cref="List{T}"/> pays for that with its growth: the <b>sum</b> of the arrays
/// doubling produces is ~2× the final capacity, which the classifier's stage probe measured at
/// <b>131 B per statement</b> — 23 % of the whole <c>parse</c> stage and the second-largest piece in it
/// after the per-statement updates dictionary. The same bytes are also copied through each doubling
/// (~1,3 MB per 10.000-statement batch). Renting the array instead removes both: the pool hands back an
/// already-sized array, so a steady-state batch allocates nothing here and copies nothing beyond the
/// one-time grow of the first batch that needed that size.
/// </para>
/// <para>
/// The array is returned with <c>clearArray: true</c>: the elements are tuples holding strings and
/// dictionaries, and leaving them in a pooled array would keep them alive for whoever rents it next.
/// <see cref="Release"/> is therefore not optional — the executor calls it in a <c>finally</c>.
/// </para>
/// </remarks>
/// <typeparam name="T">The operation type the bucket carries.</typeparam>
internal sealed class BatchOperationList<T> : IReadOnlyList<T>
{
    /// <summary>
    /// Upper bound for the initial capacity a classifier asks for. The hint removes the doubling for the
    /// batch being classified; a batch above this cap still grows through the pool, which allocates only the
    /// first time each size is touched.
    /// </summary>
    internal const int MaxInitialCapacity = 32_768;

    private T[] _items;
    private int _count;

    /// <summary>Initializes an empty bucket.</summary>
    /// <param name="initialCapacity">
    /// Optional capacity hint. Pass the batch's statement count when it is known: the first rent is then the
    /// array the bucket ends up needing, instead of the eleven the doubling would take. It is only a hint —
    /// <see cref="Add"/> grows past it.
    /// </param>
    public BatchOperationList(int initialCapacity = 0)
    {
        _items = initialCapacity > 0
            ? ArrayPool<T>.Shared.Rent(Math.Min(initialCapacity, MaxInitialCapacity))
            : [];
    }

    /// <inheritdoc/>
    public int Count => _count;

    /// <inheritdoc/>
    public T this[int index] => _items[index];

    /// <summary>Appends an operation to the bucket.</summary>
    /// <param name="item">The operation to append.</param>
    public void Add(T item)
    {
        if (_count == _items.Length)
        {
            Grow();
        }

        _items[_count++] = item;
    }

    /// <summary>
    /// Empties the bucket and returns its backing array to the pool. Every bucket built by a
    /// classification pass must be released once its batch has executed.
    /// </summary>
    public void Release()
    {
        ReturnBackingArray();
        _count = 0;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _items[i];
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Grow()
    {
        int count = _count;
        var next = ArrayPool<T>.Shared.Rent(_items.Length == 0 ? 16 : _items.Length * 2);
        Array.Copy(_items, next, count);
        ReturnBackingArray();
        _items = next;
        _count = count;
    }

    private void ReturnBackingArray()
    {
        if (_items.Length == 0)
        {
            return;
        }

        var items = _items;
        _items = [];
        ArrayPool<T>.Shared.Return(items, clearArray: true);
    }
}
