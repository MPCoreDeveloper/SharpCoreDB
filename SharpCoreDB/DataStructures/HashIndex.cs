// <copyright file="HashIndex.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// High-performance hash-based index for fast WHERE clause lookups.
/// Provides O(1) lookups for equality conditions on indexed columns.
/// .NET 10 optimizations: AggressiveInlining on hot paths for maximum throughput.
/// </summary>
public class HashIndex
{
    private readonly ConcurrentDictionary<object, List<Dictionary<string, object>>> hashMap = new();
    private readonly string columnName;
    private readonly string tableName;

    /// <summary>
    /// Gets the column name this hash index is on.
    /// </summary>
    public string ColumnName => this.columnName;

    /// <summary>
    /// Gets the table name this hash index belongs to.
    /// </summary>
    public string TableName => this.tableName;

    /// <summary>
    /// Gets the number of unique keys in the index.
    /// </summary>
    public int Count => this.hashMap.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashIndex"/> class.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to index.</param>
    public HashIndex(string tableName, string columnName)
    {
        this.tableName = tableName;
        this.columnName = columnName;
    }

    /// <summary>
    /// Adds a row to the hash index.
    /// </summary>
    /// <param name="row">The row data.</param>
    public void Add(Dictionary<string, object> row)
    {
        if (!row.TryGetValue(this.columnName, out var key) || key == null)
        {
            return;
        }

        this.hashMap.AddOrUpdate(
            key,
            _ => new List<Dictionary<string, object>> { row },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(row);
                }

                return existing;
            });
    }

    /// <summary>
    /// Looks up rows by key value in O(1) time.
    /// .NET 10: AggressiveInlining for hot path performance.
    /// </summary>
    /// <param name="key">The key value to search for.</param>
    /// <returns>List of matching rows, or empty list if not found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public List<Dictionary<string, object>> Lookup(object key)
    {
        if (key == null)
        {
            return [];
        }

        if (this.hashMap.TryGetValue(key, out var rows))
        {
            lock (rows)
            {
                return [.. rows];
            }
        }

        return [];
    }

    /// <summary>
    /// Removes a row from the hash index.
    /// </summary>
    /// <param name="row">The row to remove.</param>
    public void Remove(Dictionary<string, object> row)
    {
        if (!row.TryGetValue(this.columnName, out var key) || key == null)
        {
            return;
        }

        if (this.hashMap.TryGetValue(key, out var rows))
        {
            lock (rows)
            {
                rows.Remove(row);
                if (rows.Count == 0)
                {
                    this.hashMap.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Clears all data from the index.
    /// </summary>
    public void Clear()
    {
        this.hashMap.Clear();
    }

    /// <summary>
    /// Rebuilds the index from table data.
    /// </summary>
    /// <param name="rows">All rows from the table.</param>
    public void Rebuild(List<Dictionary<string, object>> rows)
    {
        this.Clear();
        foreach (var row in rows)
        {
            this.Add(row);
        }
    }

    /// <summary>
    /// Checks if the index contains a specific key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the index contains the key.</returns>
    public bool ContainsKey(object key)
    {
        return key != null && this.hashMap.ContainsKey(key);
    }

    /// <summary>
    /// Gets statistics about the hash index.
    /// </summary>
    /// <returns>Tuple containing (unique keys, total rows, average rows per key).</returns>
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey) GetStatistics()
    {
        var uniqueKeys = this.hashMap.Count;
        var totalRows = 0;

        foreach (var kvp in this.hashMap)
        {
            lock (kvp.Value)
            {
                totalRows += kvp.Value.Count;
            }
        }

        var avgRowsPerKey = uniqueKeys > 0 ? (double)totalRows / uniqueKeys : 0;
        return (uniqueKeys, totalRows, avgRowsPerKey);
    }
}
