// <copyright file="DirectColumnAccessTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using SharpCoreDB.DataStructures;
using Xunit;

/// <summary>
/// Tests for IndexedRowData - the core Phase 2.4 optimization.
/// Verifies dual-mode access (by index and by name) and compatibility with Dictionary interface.
/// </summary>
public class DirectColumnAccessTests
{
    private readonly Dictionary<string, int> _testIndices = new()
    {
        ["name"] = 0,
        ["age"] = 1,
        ["email"] = 2,
        ["active"] = 3
    };

    [Fact]
    public void IndexedRowData_CreationWithIndices_Succeeds()
    {
        var row = new IndexedRowData(_testIndices);
        
        Assert.NotNull(row);
        Assert.Equal(4, row.ColumnCount);
    }

    [Fact]
    public void IndexedRowData_AccessByIndex_ReturnsCorrectValue()
    {
        var row = new IndexedRowData(_testIndices);
        
        row[0] = "John";
        row[1] = 30;
        row[2] = "john@example.com";
        row[3] = true;
        
        Assert.Equal("John", row[0]);
        Assert.Equal(30, row[1]);
        Assert.Equal("john@example.com", row[2]);
        Assert.Equal(true, row[3]);
    }

    [Fact]
    public void IndexedRowData_AccessByName_ReturnsCorrectValue()
    {
        var row = new IndexedRowData(_testIndices);
        
        row["name"] = "Alice";
        row["age"] = 25;
        row["email"] = "alice@example.com";
        row["active"] = false;
        
        Assert.Equal("Alice", row["name"]);
        Assert.Equal(25, row["age"]);
        Assert.Equal("alice@example.com", row["email"]);
        Assert.Equal(false, row["active"]);
    }

    [Fact]
    public void IndexedRowData_MixedAccessByIndexAndName_Consistent()
    {
        var row = new IndexedRowData(_testIndices);
        
        row[0] = "Bob";
        row["age"] = 35;
        row[2] = "bob@example.com";
        row["active"] = true;
        
        // Verify consistency - same data accessible both ways
        Assert.Equal("Bob", row[0]);
        Assert.Equal("Bob", row["name"]);
        
        Assert.Equal(35, row[1]);
        Assert.Equal(35, row["age"]);
        
        Assert.Equal("bob@example.com", row[2]);
        Assert.Equal("bob@example.com", row["email"]);
        
        Assert.Equal(true, row[3]);
        Assert.Equal(true, row["active"]);
    }

    [Fact]
    public void IndexedRowData_InvalidIndexAccess_ReturnsNull()
    {
        var row = new IndexedRowData(_testIndices);
        row[0] = "Test";
        
        // Out of bounds access
        Assert.Null(row[-1]);
        Assert.Null(row[10]);
        Assert.Null(row[999]);
    }

    [Fact]
    public void IndexedRowData_InvalidNameAccess_ReturnsNull()
    {
        var row = new IndexedRowData(_testIndices);
        row["name"] = "Test";
        
        // Non-existent column access
        Assert.Null(row["invalid"]);
        Assert.Null(row["phone"]);
        Assert.Null(row["address"]);
    }

    [Fact]
    public void IndexedRowData_NullValues_Stored()
    {
        var row = new IndexedRowData(_testIndices);
        
        row[0] = "John";
        row[1] = null;  // NULL value
        row[2] = "john@example.com";
        
        Assert.Equal("John", row[0]);
        Assert.Null(row[1]);
        Assert.Equal("john@example.com", row[2]);
    }

    [Fact]
    public void IndexedRowData_ToDictionary_ConversionComplete()
    {
        var row = new IndexedRowData(_testIndices);
        row["name"] = "Charlie";
        row["age"] = 40;
        row["email"] = "charlie@example.com";
        row["active"] = true;
        
        var dict = row.ToDictionary();
        
        Assert.Equal(4, dict.Count);
        Assert.Equal("Charlie", dict["name"]);
        Assert.Equal(40, dict["age"]);
        Assert.Equal("charlie@example.com", dict["email"]);
        Assert.Equal(true, dict["active"]);
    }

    [Fact]
    public void IndexedRowData_ToDictionary_SkipsNullValues()
    {
        var row = new IndexedRowData(_testIndices);
        row["name"] = "Diana";
        row["age"] = null;  // Skip this
        row["email"] = "diana@example.com";
        // Don't set active - will be null
        
        var dict = row.ToDictionary();
        
        // Should only contain non-null values
        Assert.DoesNotContain("age", dict.Keys);
        Assert.DoesNotContain("active", dict.Keys);
        Assert.Equal(2, dict.Count);
        Assert.Equal("Diana", dict["name"]);
        Assert.Equal("diana@example.com", dict["email"]);
    }

    [Fact]
    public void IndexedRowData_PopulateFromDictionary_LoadsData()
    {
        var sourceDict = new Dictionary<string, object>
        {
            ["name"] = "Eve",
            ["age"] = 28,
            ["email"] = "eve@example.com",
            ["active"] = true,
            ["extra"] = "ignored"  // Not in indices
        };
        
        var row = new IndexedRowData(_testIndices);
        row.PopulateFromDictionary(sourceDict);
        
        Assert.Equal("Eve", row["name"]);
        Assert.Equal(28, row["age"]);
        Assert.Equal("eve@example.com", row["email"]);
        Assert.Equal(true, row["active"]);
        
        // Extra column should not be loaded
        Assert.Null(row["extra"]);
    }

    [Fact]
    public void IndexedRowData_GetValues_ReturnsSpan()
    {
        var row = new IndexedRowData(_testIndices);
        row[0] = "Frank";
        row[1] = 45;
        row[2] = "frank@example.com";
        row[3] = false;
        
        var span = row.GetValues();
        
        Assert.Equal(4, span.Length);
        Assert.Equal("Frank", span[0]);
        Assert.Equal(45, span[1]);
        Assert.Equal("frank@example.com", span[2]);
        Assert.Equal(false, span[3]);
    }

    [Fact]
    public void IndexedRowData_GetColumnNames_ReturnsInOrder()
    {
        var row = new IndexedRowData(_testIndices);
        
        var names = row.GetColumnNames();
        
        Assert.Equal(4, names.Length);
        Assert.Equal("name", names[0]);
        Assert.Equal("age", names[1]);
        Assert.Equal("email", names[2]);
        Assert.Equal("active", names[3]);
    }

    [Fact]
    public void IndexedRowData_TryGetIndex_FindsColumn()
    {
        var row = new IndexedRowData(_testIndices);
        
        Assert.True(row.TryGetIndex("name", out var index));
        Assert.Equal(0, index);
        
        Assert.True(row.TryGetIndex("age", out index));
        Assert.Equal(1, index);
        
        Assert.False(row.TryGetIndex("invalid", out index));
        Assert.Equal(0, index);  // Default value
    }

    [Fact]
    public void IndexedRowData_GetColumnName_ReturnsName()
    {
        var row = new IndexedRowData(_testIndices);
        
        Assert.Equal("name", row.GetColumnName(0));
        Assert.Equal("age", row.GetColumnName(1));
        Assert.Equal("email", row.GetColumnName(2));
        Assert.Equal("active", row.GetColumnName(3));
        
        Assert.Null(row.GetColumnName(-1));
        Assert.Null(row.GetColumnName(10));
    }

    [Fact]
    public void IndexedRowData_Clear_RemovesAllValues()
    {
        var row = new IndexedRowData(_testIndices);
        row[0] = "Grace";
        row[1] = 32;
        row[2] = "grace@example.com";
        row[3] = true;
        
        row.Clear();
        
        Assert.Null(row[0]);
        Assert.Null(row[1]);
        Assert.Null(row[2]);
        Assert.Null(row[3]);
    }

    [Fact]
    public void IndexedRowData_ToString_IncludesData()
    {
        var row = new IndexedRowData(_testIndices);
        row["name"] = "Henry";
        row["age"] = 50;
        
        var str = row.ToString();
        
        Assert.Contains("Henry", str);
        Assert.Contains("50", str);
        Assert.Contains("IndexedRowData", str);
    }

    [Fact]
    public void IndexedRowData_NullConstructorParameter_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => new IndexedRowData(null!));
    }

    [Fact]
    public void IndexedRowData_NullPopulateSource_ThrowsException()
    {
        var row = new IndexedRowData(_testIndices);
        
        Assert.Throws<ArgumentNullException>(() => row.PopulateFromDictionary(null!));
    }

    [Fact]
    public void IndexedRowData_EmptyIndices_CreatesZeroCapacityRow()
    {
        var emptyIndices = new Dictionary<string, int>();
        var row = new IndexedRowData(emptyIndices);
        
        Assert.Equal(0, row.ColumnCount);
        Assert.Null(row[0]);
        Assert.Null(row["any"]);
    }

    [Fact(Skip = "Performance benchmark: CPU-dependent timing. TODO: Use BenchmarkDotNet for consistent cross-platform measurements.")]
    public void IndexedRowData_PerformanceTest_IndexAccess()
    {
        var row = new IndexedRowData(_testIndices);
        row[0] = "Test";
        row[1] = 123;
        row[2] = "test@example.com";
        row[3] = true;
        
        // Simulate many accesses (as would happen in compiled WHERE clauses)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < 10_000; i++)
        {
            var unused1 = row[0];  // Fast array access
            var unused2 = row[1];
            var unused3 = row[2];
            var unused4 = row[3];
        }
        
        sw.Stop();
        
        // Index access should be extremely fast (< 1ms for 40k accesses)
        Assert.True(sw.ElapsedMilliseconds < 10, 
            $"Index access took {sw.ElapsedMilliseconds}ms - should be < 10ms");
    }
}
