// <copyright file="IIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for indexing data, using B-tree for fast lookups.
/// </summary>
public interface IIndex<TKey, TValue>
    where TKey : IComparable<TKey>
{
    /// <summary>
    /// Inserts a key-value pair into the index.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    void Insert(TKey key, TValue value);

    /// <summary>
    /// Searches for a value by key.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>True if found, and the value.</returns>
    (bool Found, TValue? Value) Search(TKey key);

    /// <summary>
    /// Deletes a key-value pair from the index.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>True if the key was found and deleted, false otherwise.</returns>
    bool Delete(TKey key);
}
