// <copyright file="IIndexKey.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Generic interface for type-safe index keys.
/// Provides compile-time type checking and better performance than Dictionary-based keys.
/// </summary>
/// <typeparam name="TKey">The type of the index key (must be comparable and have value semantics).</typeparam>
public interface IIndexKey<TKey> where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
{
    /// <summary>
    /// Gets the key value.
    /// </summary>
    TKey Value { get; }
    
    /// <summary>
    /// Gets the column name this key is associated with.
    /// </summary>
    string ColumnName { get; }
    
    /// <summary>
    /// Gets the hash code for the key (optimized for hash-based indexes).
    /// </summary>
    int GetHashCode();
    
    /// <summary>
    /// Compares this key with another for sorting (B-Tree indexes).
    /// </summary>
    int CompareTo(IIndexKey<TKey> other);
}

/// <summary>
/// Generic index interface for type-safe indexes.
/// </summary>
/// <typeparam name="TKey">The type of the index key.</typeparam>
public interface IGenericIndex<TKey> where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
{
    /// <summary>
    /// Gets the column name this index is for.
    /// </summary>
    string ColumnName { get; }
    
    /// <summary>
    /// Gets the index type (Hash, BTree, etc.).
    /// </summary>
    IndexType Type { get; }
    
    /// <summary>
    /// Adds a key-value pair to the index.
    /// </summary>
    void Add(TKey key, long position);
    
    /// <summary>
    /// Finds all positions for a given key (O(1) for hash, O(log n) for B-Tree).
    /// </summary>
    IEnumerable<long> Find(TKey key);
    
    /// <summary>
    /// Finds all positions for keys in a range (B-Tree only).
    /// </summary>
    IEnumerable<long> FindRange(TKey start, TKey end);
    
    /// <summary>
    /// Removes a key-value pair from the index.
    /// </summary>
    bool Remove(TKey key, long position);
    
    /// <summary>
    /// Gets the number of unique keys in the index.
    /// </summary>
    int Count { get; }
    
    /// <summary>
    /// Gets index statistics for query optimization.
    /// </summary>
    IndexStatistics GetStatistics();
}

/// <summary>
/// Index type enumeration.
/// </summary>
public enum IndexType
{
    /// <summary>Hash index - O(1) lookups, no range queries.</summary>
    Hash,
    
    /// <summary>B-Tree index - O(log n) lookups, supports range queries.</summary>
    BTree,
    
    /// <summary>Full-text index - For text search.</summary>
    FullText
}

/// <summary>
/// Index statistics for query optimization.
/// </summary>
public sealed record IndexStatistics
{
    /// <summary>Gets the number of unique keys.</summary>
    public required int UniqueKeys { get; init; }
    
    /// <summary>Gets the total number of entries.</summary>
    public required int TotalEntries { get; init; }
    
    /// <summary>Gets the average entries per key.</summary>
    public required double AverageEntriesPerKey { get; init; }
    
    /// <summary>Gets the selectivity (0-1, higher is better).</summary>
    public required double Selectivity { get; init; }
    
    /// <summary>Gets the memory usage in bytes.</summary>
    public required long MemoryUsageBytes { get; init; }
}
