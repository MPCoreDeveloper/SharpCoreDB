// <copyright file="FakeGraphTable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

/// <summary>
/// Minimal <see cref="ITable"/> fake for graph traversal tests.
/// </summary>
internal sealed class FakeGraphTable : ITable
{
    private readonly List<Dictionary<string, object>> _rows;
    private readonly string _relationshipColumn;

    public FakeGraphTable(List<Dictionary<string, object>> rows, string relationshipColumn, string? tableName = null)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _relationshipColumn = relationshipColumn ?? throw new ArgumentNullException(nameof(relationshipColumn));
        Name = tableName ?? "graph_table";
        Columns = ["id", _relationshipColumn];
        ColumnTypes = [DataType.Long, DataType.RowRef];
    }

    public string Name { get; set; }
    public List<string> Columns { get; }
    public List<DataType> ColumnTypes { get; }
    public string DataFile { get; set; } = string.Empty;
    public int PrimaryKeyIndex => 0;
    public List<bool> IsAuto { get; } = [false, false];
    public List<bool> IsNotNull { get; } = [false, false];
    public List<object?> DefaultValues { get; } = [null, null];
    public List<ForeignKeyConstraint> ForeignKeys { get; } = [];
    public List<List<string>> UniqueConstraints { get; } = [];
    public List<CollationType> ColumnCollations { get; } = [CollationType.Binary, CollationType.Binary];
    public List<string?> ColumnLocaleNames { get; } = [null, null];

    public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true)
        => Select(where, orderBy, asc, noEncrypt: true);

    public List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt)
    {
        if (string.IsNullOrWhiteSpace(where))
        {
            return _rows;
        }

        var parts = where.Split('=', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return [];
        }

        var column = parts[0].Trim();
        var rawValue = parts[1].Trim().Trim('"', '\'');
        if (!column.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (!long.TryParse(rawValue, System.Globalization.CultureInfo.InvariantCulture, out var key))
        {
            return [];
        }

        return _rows.Where(row => row.TryGetValue("id", out var value) && value is long id && id == key).ToList();
    }

    public void Insert(Dictionary<string, object> row) { }
    public long[] InsertBatch(List<Dictionary<string, object>> rows) => [];
    public long[] InsertBatchFromBuffer(ReadOnlySpan<byte> encodedData, int rowCount) => [];
    public void Update(string? where, Dictionary<string, object> updates) { }
    public void Delete(string? where) { }
    public void CreateHashIndex(string columnName) { }
    public void CreateHashIndex(string indexName, string columnName, bool isUnique = false) { }
    public bool HasHashIndex(string columnName) => false;
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName) => null;
    public void IncrementColumnUsage(string columnName) { }
    public IReadOnlyDictionary<string, long> GetColumnUsage() => new Dictionary<string, long>();
    public void TrackAllColumnsUsage() { }
    public void TrackColumnUsage(string columnName) { }
    public bool RemoveHashIndex(string columnName) => false;
    public void ClearAllIndexes() { }
    public long GetCachedRowCount() => _rows.Count;
    public void RefreshRowCount() { }
    public void CreateBTreeIndex(string columnName) { }
    public void CreateBTreeIndex(string indexName, string columnName, bool isUnique = false) { }
    public bool HasBTreeIndex(string columnName) => false;
    public void Flush() { }
    public void AddColumn(ColumnDefinition columnDef) { }
    public void SetMetadata(string key, object value) { }
    public object? GetMetadata(string key) => null;
    public bool RemoveMetadata(string key) => false;
}
