// <copyright file="Phase7_JoinCollationBenchmark.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Benchmarks;

using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using SharpCoreDB.Execution;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.DataStructures;

/// <summary>
/// Benchmarks for Phase 7: JOIN operations with collation support.
/// Measures performance impact of collation-aware JOIN comparisons.
/// ✅ C# 14: Collection expressions, primary constructors, required properties.
/// </summary>
[SimpleJob(runtimeMoniker: RuntimeMoniker.Net80)] // ✅ Use Net80 (latest supported by BenchmarkDotNet)
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class Phase7_JoinCollationBenchmark
{
    private List<Dictionary<string, object>> _leftData = null!;
    private List<Dictionary<string, object>> _rightData = null!;
    private MockTable _leftTableBinary = null!;
    private MockTable _leftTableNoCase = null!;
    private MockTable _rightTableBinary = null!;
    private MockTable _rightTableNoCase = null!;
    
    [Params(100, 1000, 10000)]
    public int RowCount { get; set; }
    
    [GlobalSetup]
    public void Setup()
    {
        // Generate test data with mixed case names
        _leftData = [];
        _rightData = [];
        
        for (int i = 0; i < RowCount; i++)
        {
            _leftData.Add(new Dictionary<string, object>
            {
                ["id"] = i,
                ["name"] = i % 2 == 0 ? $"User{i}" : $"user{i}" // Mixed case
            });
            
            _rightData.Add(new Dictionary<string, object>
            {
                ["user_id"] = i,
                ["user_name"] = i % 2 == 0 ? $"user{i}" : $"User{i}" // Opposite case
            });
        }
        
        // Create mock tables with different collations
        _leftTableBinary = new MockTable("users", ["id", "name"], [CollationType.Binary, CollationType.Binary]);
        _leftTableNoCase = new MockTable("users", ["id", "name"], [CollationType.Binary, CollationType.NoCase]);
        _rightTableBinary = new MockTable("orders", ["user_id", "user_name"], [CollationType.Binary, CollationType.Binary]);
        _rightTableNoCase = new MockTable("orders", ["user_id", "user_name"], [CollationType.Binary, CollationType.NoCase]);
    }
    
    [Benchmark(Baseline = true, Description = "Baseline: INNER JOIN without collation (Binary)")]
    public int InnerJoin_Binary()
    {
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: _leftTableBinary,
            rightTable: _rightTableBinary);
        
        var results = JoinExecutor.ExecuteInnerJoin(
            _leftData, _rightData, "users", "orders", evaluator);
        
        return results.Count();
    }
    
    [Benchmark(Description = "INNER JOIN with NoCase collation")]
    public int InnerJoin_NoCase()
    {
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: _leftTableNoCase,
            rightTable: _rightTableNoCase);
        
        var results = JoinExecutor.ExecuteInnerJoin(
            _leftData, _rightData, "users", "orders", evaluator);
        
        return results.Count();
    }
    
    [Benchmark(Description = "LEFT JOIN with NoCase collation")]
    public int LeftJoin_NoCase()
    {
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: _leftTableNoCase,
            rightTable: _rightTableNoCase);
        
        var results = JoinExecutor.ExecuteLeftJoin(
            _leftData, _rightData, "users", "orders", evaluator);
        
        return results.Count();
    }
    
    [Benchmark(Description = "Collation resolution overhead (mismatch warning)")]
    public int CollationResolution_Mismatch()
    {
        List<string> warnings = [];
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: _leftTableNoCase,
            rightTable: _rightTableBinary,
            warningCallback: warnings.Add);
        
        var results = JoinExecutor.ExecuteInnerJoin(
            _leftData, _rightData, "users", "orders", evaluator);
        
        return results.Count();
    }
    
    [Benchmark(Description = "Multi-column JOIN with collation")]
    public int MultiColumnJoin_NoCase()
    {
        // Create tables with multiple string columns
        var leftTable = new MockTable("users", 
            ["first_name", "last_name"], 
            [CollationType.NoCase, CollationType.NoCase]);
        var rightTable = new MockTable("profiles", 
            ["first_name", "last_name"], 
            [CollationType.NoCase, CollationType.NoCase]);
        
        // Generate multi-column data
        var leftMulti = _leftData.Select((row, idx) => new Dictionary<string, object>
        {
            ["first_name"] = $"First{idx}",
            ["last_name"] = $"Last{idx}"
        }).ToList();
        
        var rightMulti = _rightData.Select((row, idx) => new Dictionary<string, object>
        {
            ["first_name"] = idx % 2 == 0 ? $"First{idx}" : $"first{idx}",
            ["last_name"] = idx % 2 == 0 ? $"last{idx}" : $"Last{idx}"
        }).ToList();
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.first_name = profiles.first_name AND users.last_name = profiles.last_name",
            leftAlias: "users",
            rightAlias: "profiles",
            leftTable: leftTable,
            rightTable: rightTable);
        
        var results = JoinExecutor.ExecuteInnerJoin(
            leftMulti, rightMulti, "users", "profiles", evaluator);
        
        return results.Count();
    }
    
    // ==================== MOCK TABLE ====================
    
    /// <summary>
    /// Minimal mock ITable for benchmarking.
    /// </summary>
    private sealed class MockTable(string name, List<string> columns, List<CollationType> collations) : ITable
    {
        public string Name { get; set; } = name;
        public List<string> Columns { get; } = columns;
        public List<CollationType> ColumnCollations { get; } = collations;
        public List<string?> ColumnLocaleNames { get; } = [.. new string?[columns.Count]]; // ✅ Phase 9: Locale support
        public List<DataType> ColumnTypes { get; } = Enumerable.Repeat(DataType.String, columns.Count).ToList();
        public string DataFile { get; set; } = ":memory:";
        public int PrimaryKeyIndex => -1;
        public List<bool> IsAuto => Enumerable.Repeat(false, Columns.Count).ToList();
        public List<bool> IsNotNull => Enumerable.Repeat(false, Columns.Count).ToList();
        public List<object?> DefaultValues => Enumerable.Repeat<object?>(null, Columns.Count).ToList();
        public List<List<string>> UniqueConstraints => [];
        public List<ForeignKeyConstraint> ForeignKeys => [];
        
        // Stub implementations - not used in benchmarks
        public void Insert(Dictionary<string, object> row) => throw new NotImplementedException();
        public long[] InsertBatch(List<Dictionary<string, object>> rows) => throw new NotImplementedException();
        public long[] InsertBatchFromBuffer(ReadOnlySpan<byte> encodedData, int rowCount) => throw new NotImplementedException();
        public List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true) => throw new NotImplementedException();
        public List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt) => throw new NotImplementedException();
        public void Update(string? where, Dictionary<string, object> updates) => throw new NotImplementedException();
        public void Delete(string? where) => throw new NotImplementedException();
        public bool HasHashIndex(string columnName) => false;
        public void CreateHashIndex(string columnName) => throw new NotImplementedException();
        public void CreateHashIndex(string indexName, string columnName) => throw new NotImplementedException();
        public void CreateHashIndex(string indexName, string columnName, bool unique) => throw new NotImplementedException();
        public bool HasBTreeIndex(string columnName) => false;
        public void CreateBTreeIndex(string columnName) => throw new NotImplementedException();
        public void CreateBTreeIndex(string indexName, string columnName) => throw new NotImplementedException();
        public void CreateBTreeIndex(string indexName, string columnName, bool unique) => throw new NotImplementedException();
        public bool RemoveHashIndex(string columnName) => false;
        public void ClearAllIndexes() { }
        public void IncrementColumnUsage(string columnName) { }
        public void TrackColumnUsage(string columnName) { }
        public void TrackAllColumnsUsage() { }
        public void AddColumn(ColumnDefinition columnDef) => throw new NotImplementedException();
        public void Flush() { }
        public long GetCachedRowCount() => 0;
        public void RefreshRowCount() { }
        public IReadOnlyDictionary<string, long> GetColumnUsage() => new Dictionary<string, long>();
        public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName) => null;
        public Table? DeduplicateByPrimaryKey(List<Dictionary<string, object>> results) => null;
        public void InitializeStorageEngine() { }
        public void SetMetadata(string key, object value) { }
        public object? GetMetadata(string key) => null;
        public bool RemoveMetadata(string key) => false;
    }
}
