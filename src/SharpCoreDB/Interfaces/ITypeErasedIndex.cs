// <copyright file="ITypeErasedIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Non-generic view over a type-safe index (<see cref="IGenericIndex{TKey}"/>).
/// Lets callers that only know a column's runtime <c>DataType</c> drive an index — add a row
/// position, run a range scan, read statistics — without reflecting on the index type.
/// Every index type this assembly constructs implements it, so a call site that used
/// <c>GetType().GetMethod("Add")</c> (or <c>"FindRange"</c>) can call the interface member instead.
/// </summary>
/// <remarks>
/// Keys are passed as <see cref="object"/> because the generic argument is unknown at the call site.
/// A key must already be converted to the index's key type (see <c>Table.ConvertValueForBTreeKey</c>);
/// a mismatched key throws, which is the same failure mode the previous reflective dispatch had.
/// Members are intentionally limited to what type-erased call sites need — the typed surface stays on
/// <see cref="IGenericIndex{TKey}"/>.
/// </remarks>
public interface ITypeErasedIndex
{
    /// <summary>
    /// Gets the column name this index covers.
    /// </summary>
    string ColumnName { get; }

    /// <summary>
    /// Gets the number of indexed entries.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Adds a key/position pair to the index.
    /// </summary>
    /// <param name="key">The key, already converted to the index's key type.</param>
    /// <param name="position">The storage position of the row.</param>
    void Add(object? key, long position);

    /// <summary>
    /// Finds all positions whose key falls inside <paramref name="start"/>..<paramref name="end"/> (inclusive).
    /// Index types that cannot scan ranges (hash indexes) fall back to a linear filter.
    /// </summary>
    /// <param name="start">The inclusive lower bound, converted to the index's key type.</param>
    /// <param name="end">The inclusive upper bound, converted to the index's key type.</param>
    /// <returns>The matching storage positions.</returns>
    IEnumerable<long> FindRange(object? start, object? end);

    /// <summary>
    /// Gets counters describing this index for query optimization.
    /// </summary>
    /// <returns>The index statistics.</returns>
    IndexStatistics GetStatistics();
}
