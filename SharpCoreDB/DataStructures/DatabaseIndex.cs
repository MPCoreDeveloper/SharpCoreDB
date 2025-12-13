// <copyright file="DatabaseIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a database index for fast lookups.
/// </summary>
public class DatabaseIndex
{
    private readonly Dictionary<object, List<int>> indexData = new();
    private readonly string tableName;
    private readonly string columnName;
    private readonly bool isUnique;

    /// <summary>
    /// Gets or sets the name of the index.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets the table name this index belongs to.
    /// </summary>
    public string TableName => this.tableName;

    /// <summary>
    /// Gets the column name this index is on.
    /// </summary>
    public string ColumnName => this.columnName;

    /// <summary>
    /// Gets a value indicating whether gets whether this is a unique index.
    /// </summary>
    public bool IsUnique => this.isUnique;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseIndex"/> class.
    /// </summary>
    /// <param name="name">The index name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="isUnique">Whether this is a unique index.</param>
    public DatabaseIndex(string name, string tableName, string columnName, bool isUnique = false)
    {
        this.Name = name;
        this.tableName = tableName;
        this.columnName = columnName;
        this.isUnique = isUnique;
    }

    /// <summary>
    /// Adds a row to the index.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <param name="rowId">The row identifier.</param>
    public void Add(object key, int rowId)
    {
        if (key == null)
        {
            return;
        }

        if (!this.indexData.ContainsKey(key))
        {
            this.indexData[key] = new List<int>();
        }

        if (this.isUnique && this.indexData[key].Count > 0)
        {
            throw new InvalidOperationException($"Duplicate key value '{key}' violates unique constraint on index '{this.Name}'");
        }

        this.indexData[key].Add(rowId);
    }

    /// <summary>
    /// Removes a row from the index.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <param name="rowId">The row identifier.</param>
    public void Remove(object key, int rowId)
    {
        if (key != null && this.indexData.ContainsKey(key))
        {
            this.indexData[key].Remove(rowId);
            if (this.indexData[key].Count == 0)
            {
                this.indexData.Remove(key);
            }
        }
    }

    /// <summary>
    /// Looks up rows by key value.
    /// </summary>
    /// <param name="key">The key value.</param>
    /// <returns>List of row identifiers.</returns>
    public List<int> Lookup(object key)
    {
        if (key == null || !this.indexData.ContainsKey(key))
        {
            return new List<int>();
        }

        return this.indexData[key];
    }

    /// <summary>
    /// Clears all index data.
    /// </summary>
    public void Clear()
    {
        this.indexData.Clear();
    }

    /// <summary>
    /// Gets the size of the index (number of unique keys).
    /// </summary>
    public int Size => this.indexData.Count;

    /// <summary>
    /// Rebuilds the index from table data.
    /// </summary>
    /// <param name="rows">The table rows with their identifiers.</param>
    public void Rebuild(List<(int RowId, Dictionary<string, object> Row)> rows)
    {
        this.Clear();
        foreach (var (rowId, row) in rows)
        {
            if (row.TryGetValue(this.columnName, out var value) && value != null)
            {
                this.Add(value, rowId);
            }
        }
    }
}
