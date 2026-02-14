// <copyright file="FakeVectorTable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch.Tests;

using SharpCoreDB.Interfaces;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Services;

/// <summary>
/// Minimal <see cref="ITable"/> fake for vector search tests.
/// Only <see cref="Select(string?, string?, bool)"/> and schema properties are meaningful;
/// all other members are no-op stubs.
/// </summary>
internal sealed class FakeVectorTable : ITable
{
    private readonly List<Dictionary<string, object>> _rows;

    public FakeVectorTable(int dimensions, int rowCount)
    {
        var rng = new Random(42);
        _rows = new List<Dictionary<string, object>>(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            var vec = new float[dimensions];
            for (int d = 0; d < dimensions; d++)
                vec[d] = (float)rng.NextDouble();
            _rows.Add(new Dictionary<string, object> { ["id"] = (long)i, ["embedding"] = vec });
        }

        Columns = ["id", "embedding"];
        ColumnTypes = [DataType.Integer, DataType.Vector];
    }

    public FakeVectorTable(float[][] vectors)
    {
        _rows = new List<Dictionary<string, object>>(vectors.Length);
        for (int i = 0; i < vectors.Length; i++)
        {
            _rows.Add(new Dictionary<string, object> { ["id"] = (long)i, ["embedding"] = vectors[i] });
        }

        Columns = ["id", "embedding"];
        ColumnTypes = [DataType.Integer, DataType.Vector];
    }

    // ── Schema properties ──
    public string Name { get; set; } = "fake_table";
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

    // ── Data access ──
    public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true)
        => _rows;
    public List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt)
        => _rows;

    // ── Stubs (not used in vector tests) ──
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
