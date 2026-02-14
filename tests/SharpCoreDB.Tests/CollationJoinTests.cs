// <copyright file="CollationJoinTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using SharpCoreDB.Execution;
using SharpCoreDB.Interfaces; // ✅ For ITable
using SharpCoreDB.DataStructures; // ✅ For Table, ForeignKeyConstraint
using SharpCoreDB.Services; // ✅ For ColumnDefinition
using Xunit;

/// <summary>
/// Tests for Phase 7: JOIN operations with collation support.
/// Verifies that JOIN conditions respect column collations.
/// ✅ C# 14: Collection expressions, required properties, modern test patterns.
/// </summary>
public sealed class CollationJoinTests
{
    [Fact]
    public void JoinConditionEvaluator_WithBinaryCollation_ShouldBeCaseSensitive()
    {
        // Arrange
        var leftRow = new Dictionary<string, object> { ["users.name"] = "Alice" };
        var rightRow = new Dictionary<string, object> { ["orders.user_name"] = "alice" }; // lowercase
        
        // Create mock tables with Binary collation
        var leftTable = CreateMockTable("users", ["name"], [CollationType.Binary]);
        var rightTable = CreateMockTable("orders", ["user_name"], [CollationType.Binary]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        bool matches = evaluator(leftRow, rightRow);
        
        // Assert
        Assert.False(matches); // Case-sensitive: "Alice" != "alice"
    }
    
    [Fact]
    public void JoinConditionEvaluator_WithNoCaseCollation_ShouldBeCaseInsensitive()
    {
        // Arrange
        var leftRow = new Dictionary<string, object> { ["users.name"] = "Alice" };
        var rightRow = new Dictionary<string, object> { ["orders.user_name"] = "alice" }; // lowercase
        
        // Create mock tables with NoCase collation
        var leftTable = CreateMockTable("users", ["name"], [CollationType.NoCase]);
        var rightTable = CreateMockTable("orders", ["user_name"], [CollationType.NoCase]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        bool matches = evaluator(leftRow, rightRow);
        
        // Assert
        Assert.True(matches); // Case-insensitive: "Alice" == "alice"
    }
    
    [Fact]
    public void JoinConditionEvaluator_WithCollationMismatch_ShouldUseLeftCollation()
    {
        // Arrange
        var leftRow = new Dictionary<string, object> { ["users.name"] = "Alice" };
        var rightRow = new Dictionary<string, object> { ["orders.user_name"] = "alice" };
        
        // Left: NoCase, Right: Binary
        var leftTable = CreateMockTable("users", ["name"], [CollationType.NoCase]);
        var rightTable = CreateMockTable("orders", ["user_name"], [CollationType.Binary]);
        
        List<string> warnings = [];
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable,
            warningCallback: warnings.Add);
        
        // Act
        bool matches = evaluator(leftRow, rightRow);
        
        // Assert
        Assert.True(matches); // Uses left (NoCase): "Alice" == "alice"
        Assert.Single(warnings); // Should emit collation mismatch warning
        Assert.Contains("collation mismatch", warnings[0], StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void ExecuteInnerJoin_WithNoCaseCollation_ShouldMatchCaseInsensitively()
    {
        // Arrange - Test full INNER JOIN execution
        var leftRows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1, ["name"] = "Alice" },
            new() { ["id"] = 2, ["name"] = "Bob" }
        };
        
        var rightRows = new List<Dictionary<string, object>>
        {
            new() { ["user_id"] = 1, ["user_name"] = "alice" }, // lowercase
            new() { ["user_id"] = 2, ["user_name"] = "BOB" }     // uppercase
        };
        
        var leftTable = CreateMockTable("users", ["id", "name"], [CollationType.Binary, CollationType.NoCase]);
        var rightTable = CreateMockTable("orders", ["user_id", "user_name"], [CollationType.Binary, CollationType.NoCase]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        var results = JoinExecutor.ExecuteInnerJoin(
            leftRows, rightRows, "users", "orders", evaluator).ToList();
        
        // Assert
        Assert.Equal(2, results.Count); // Both rows should match despite case differences
        Assert.Contains(results, r => (int)r["users.id"] == 1);
        Assert.Contains(results, r => (int)r["users.id"] == 2);
    }
    
    [Fact]
    public void ExecuteLeftJoin_WithCollation_ShouldPreserveUnmatchedLeftRows()
    {
        // Arrange
        var leftRows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1, ["name"] = "Alice" },
            new() { ["id"] = 2, ["name"] = "Bob" },
            new() { ["id"] = 3, ["name"] = "Charlie" } // No matching order
        };
        
        var rightRows = new List<Dictionary<string, object>>
        {
            new() { ["user_id"] = 1, ["user_name"] = "alice" }
        };
        
        var leftTable = CreateMockTable("users", ["id", "name"], [CollationType.Binary, CollationType.NoCase]);
        var rightTable = CreateMockTable("orders", ["user_id", "user_name"], [CollationType.Binary, CollationType.NoCase]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        var results = JoinExecutor.ExecuteLeftJoin(
            leftRows, rightRows, "users", "orders", evaluator).ToList();
        
        // Assert
        Assert.Equal(3, results.Count); // All left rows should be present
        
        // Alice should have matching order data
        var aliceRow = results.First(r => (int)r["users.id"] == 1);
        Assert.Equal("alice", aliceRow["orders.user_name"]);
        
        // Bob and Charlie should have NULL order data
        var bobRow = results.First(r => (int)r["users.id"] == 2);
        Assert.Equal(DBNull.Value, bobRow["orders.user_name"]);
        
        var charlieRow = results.First(r => (int)r["users.id"] == 3);
        Assert.Equal(DBNull.Value, charlieRow["orders.user_name"]);
    }
    
    [Fact]
    public void ExecuteCrossJoin_ShouldNotRequireCollation()
    {
        // Arrange - CROSS JOIN produces Cartesian product (no ON condition)
        var leftRows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1 },
            new() { ["id"] = 2 }
        };
        
        var rightRows = new List<Dictionary<string, object>>
        {
            new() { ["letter"] = "A" },
            new() { ["letter"] = "B" },
            new() { ["letter"] = "C" }
        };
        
        // Act
        var results = JoinExecutor.ExecuteCrossJoin(
            leftRows, rightRows, "t1", "t2").ToList();
        
        // Assert
        Assert.Equal(6, results.Count); // 2 × 3 = 6 rows
        Assert.All(results, r => 
        {
            Assert.Contains("t1.id", r.Keys);
            Assert.Contains("t2.letter", r.Keys);
        });
    }
    
    [Fact]
    public void ExecuteFullJoin_WithCollation_ShouldPreserveAllUnmatchedRows()
    {
        // Arrange
        var leftRows = new List<Dictionary<string, object>>
        {
            new() { ["id"] = 1, ["name"] = "Alice" },
            new() { ["id"] = 2, ["name"] = "Bob" } // No matching order
        };
        
        var rightRows = new List<Dictionary<string, object>>
        {
            new() { ["user_id"] = 1, ["user_name"] = "alice" },
            new() { ["user_id"] = 3, ["user_name"] = "Charlie" } // No matching user
        };
        
        var leftTable = CreateMockTable("users", ["id", "name"], [CollationType.Binary, CollationType.NoCase]);
        var rightTable = CreateMockTable("orders", ["user_id", "user_name"], [CollationType.Binary, CollationType.NoCase]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        var results = JoinExecutor.ExecuteFullJoin(
            leftRows, rightRows, "users", "orders", evaluator).ToList();
        
        // Assert
        Assert.Equal(3, results.Count); // Alice (matched), Bob (left only), Charlie (right only)
        
        // Alice: Matched row
        var aliceRow = results.First(r => 
            r.TryGetValue("users.id", out var id) && id is int intId && intId == 1);
        Assert.Equal("alice", aliceRow["orders.user_name"]);
        
        // Bob: Unmatched left row (NULL right columns)
        var bobRow = results.First(r => 
            r.TryGetValue("users.id", out var id) && id is int intId && intId == 2);
        Assert.Equal(DBNull.Value, bobRow["orders.user_name"]);
        
        // Charlie: Unmatched right row (NULL left columns)
        var charlieRow = results.First(r => 
            r.TryGetValue("orders.user_name", out var name) && name is string strName && strName == "Charlie");
        Assert.Equal(DBNull.Value, charlieRow["users.name"]);
    }
    
    [Fact]
    public void JoinConditionEvaluator_WithMultiColumnJoin_ShouldRespectAllCollations()
    {
        // Arrange - JOIN on multiple columns with different collations
        var leftRow = new Dictionary<string, object> 
        { 
            ["users.first_name"] = "Alice",
            ["users.last_name"] = "Smith"
        };
        
        var rightRow = new Dictionary<string, object> 
        { 
            ["profiles.first_name"] = "alice", // lowercase
            ["profiles.last_name"] = "SMITH"   // uppercase
        };
        
        // first_name: NoCase, last_name: NoCase
        var leftTable = CreateMockTable("users", ["first_name", "last_name"], 
            [CollationType.NoCase, CollationType.NoCase]);
        var rightTable = CreateMockTable("profiles", ["first_name", "last_name"], 
            [CollationType.NoCase, CollationType.NoCase]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.first_name = profiles.first_name AND users.last_name = profiles.last_name",
            leftAlias: "users",
            rightAlias: "profiles",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        bool matches = evaluator(leftRow, rightRow);
        
        // Assert
        Assert.True(matches); // Both conditions match case-insensitively
    }
    
    [Fact]
    public void JoinConditionEvaluator_WithRTrimCollation_ShouldIgnoreTrailingWhitespace()
    {
        // Arrange
        var leftRow = new Dictionary<string, object> { ["users.name"] = "Alice   " }; // trailing spaces
        var rightRow = new Dictionary<string, object> { ["orders.user_name"] = "Alice" };
        
        var leftTable = CreateMockTable("users", ["name"], [CollationType.RTrim]);
        var rightTable = CreateMockTable("orders", ["user_name"], [CollationType.RTrim]);
        
        var evaluator = JoinConditionEvaluator.CreateEvaluator(
            onClause: "users.name = orders.user_name",
            leftAlias: "users",
            rightAlias: "orders",
            leftTable: leftTable,
            rightTable: rightTable);
        
        // Act
        bool matches = evaluator(leftRow, rightRow);
        
        // Assert
        Assert.True(matches); // RTrim: "Alice   " == "Alice"
    }
    
    // ==================== HELPER METHODS ====================
    
    /// <summary>
    /// Creates a mock ITable for testing collation resolution.
    /// </summary>
    private static Interfaces.ITable CreateMockTable(string name, List<string> columns, List<CollationType> collations)
    {
        return new MockTable
        {
            Name = name,
            Columns = columns,
            ColumnCollations = collations,
            ColumnTypes = Enumerable.Repeat(DataType.String, columns.Count).ToList(),
            DataFile = ":memory:",
            PrimaryKeyIndex = -1,
            IsAuto = Enumerable.Repeat(false, columns.Count).ToList(),
            IsNotNull = Enumerable.Repeat(false, columns.Count).ToList(),
            DefaultValues = Enumerable.Repeat<object?>(null, columns.Count).ToList(),
            UniqueConstraints = [],
            ForeignKeys = []
        };
    }
    
    /// <summary>
    /// Minimal mock ITable implementation for testing.
    /// </summary>
    private sealed class MockTable : Interfaces.ITable
    {
        public required string Name { get; set; }
        public required List<string> Columns { get; init; }
        public required List<DataType> ColumnTypes { get; init; }
        public required string DataFile { get; set; }
        public required int PrimaryKeyIndex { get; init; }
        public required List<bool> IsAuto { get; init; }
        public required List<bool> IsNotNull { get; init; }
        public required List<object?> DefaultValues { get; init; }
        public required List<List<string>> UniqueConstraints { get; init; }
        public required List<ForeignKeyConstraint> ForeignKeys { get; init; }
        public required List<CollationType> ColumnCollations { get; init; }
        public List<string?> ColumnLocaleNames { get; init; } = []; // ✅ Phase 9: Locale support
        
        // Stub implementations - not used in JOIN collation tests
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
