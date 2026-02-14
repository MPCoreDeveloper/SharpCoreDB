namespace SharpCoreDB.Tests;

using SharpCoreDB;
using SharpCoreDB.DataStructures;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// ✅ Phase 5: Runtime Query Optimization with Collation
/// 
/// Tests collation-aware query execution:
/// - WHERE clause filtering with collation
/// - DISTINCT with collation deduplication
/// - GROUP BY with collation grouping
/// - ORDER BY with collation sorting
/// - LIKE pattern matching with collation
/// - Complex conditions with AND/OR and collation
/// </summary>
public class CollationPhase5Tests
{
    private Table CreateTableWithCollation(CollationType emailCollation = CollationType.NoCase)
    {
        var table = new Table();
        table.Name = "users";
        table.Columns = ["id", "email", "name", "status"];
        table.ColumnTypes = [DataType.Integer, DataType.String, DataType.String, DataType.String];
        table.ColumnCollations = [CollationType.Binary, emailCollation, CollationType.Binary, CollationType.NoCase];
        table.IsNotNull = [true, false, false, false];
        table.IsAuto = [true, false, false, false];
        table.PrimaryKeyIndex = 0;

        return table;
    }

    [Fact]
    public void WhereWithNoCaseCollation_ShouldFindCaseInsensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } }
        };

        // Act
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "email", "=", "alice@example.com")).ToList();

        // Assert - both alice@example.com and ALICE@EXAMPLE.COM should match
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => (int)r["id"] == 1);
        Assert.Contains(result, r => (int)r["id"] == 2);
    }

    [Fact]
    public void WhereWithBinaryCollation_ShouldFindCaseSensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.Binary);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } }
        };

        // Act
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "email", "=", "alice@example.com")).ToList();

        // Assert - only exact match
        Assert.Single(result);
        Assert.Equal(1, (int)result[0]["id"]);
    }

    [Fact]
    public void WhereWithLikeAndNoCaseCollation_ShouldFindCaseInsensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "alice@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } }
        };

        // Act - find emails containing "example"
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "email", "LIKE", "%example%")).ToList();

        // Assert - should find all three emails (all contain "example" case-insensitive)
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void DistinctWithNoCaseCollation_ShouldDeduplicateCaseInsensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } }
        };

        // Act - SELECT DISTINCT email
        var result = table.ApplyDistinctWithCollation(rows, "email");

        // Assert - should have 2 unique emails (alice and bob, case-insensitive)
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DistinctWithBinaryCollation_ShouldDeduplicateCaseSensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.Binary);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } }
        };

        // Act - SELECT DISTINCT email
        var result = table.ApplyDistinctWithCollation(rows, "email");

        // Assert - should have 3 unique emails (case-sensitive)
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GroupByWithNoCaseCollation_ShouldGroupCaseInsensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "inactive" } },
            new() { { "id", 3 }, { "email", "alice@EXAMPLE.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 4 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "active" } }
        };

        // Act - GROUP BY email with NOCASE
        var groups = table.GroupByWithCollation(rows, "email");

        // Assert - should have 2 groups (alice and bob, case-insensitive)
        Assert.Equal(2, groups.Count);
        Assert.Equal(3, groups.Values.First().Count); // alice has 3 rows
        Assert.Equal(1, groups.Values.Last().Count);  // bob has 1 row
    }

    [Fact]
    public void GroupByWithBinaryCollation_ShouldGroupCaseSensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.Binary);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "inactive" } },
            new() { { "id", 3 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "active" } }
        };

        // Act - GROUP BY email with Binary
        var groups = table.GroupByWithCollation(rows, "email");

        // Assert - should have 3 groups (case-sensitive)
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void OrderByWithNoCaseCollation_ShouldSortCaseInsensitive()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "Zoe@example.com" }, { "name", "Zoe" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 4 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "active" } }
        };

        // Act - ORDER BY email
        var sorted = table.OrderByWithCollation(rows, "email", ascending: true);

        // Assert - should be sorted alphabetically (case-insensitive)
        // alice variants (indices 1,2) < bob (3) < zoe (0)
        Assert.Equal("alice@example.com", sorted[0]["email"]);
        Assert.Equal("ALICE@EXAMPLE.COM", sorted[1]["email"]);
        Assert.Equal("bob@example.com", sorted[2]["email"]);
        Assert.Equal("Zoe@example.com", sorted[3]["email"]);
    }

    [Fact]
    public void OrderByDescendingWithCollation_ShouldSortCorrectly()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "active" } },
            new() { { "id", 3 }, { "email", "zoe@example.com" }, { "name", "Zoe" }, { "status", "active" } }
        };

        // Act - ORDER BY email DESC
        var sorted = table.OrderByWithCollation(rows, "email", ascending: false);

        // Assert - should be sorted in reverse alphabetically
        Assert.Equal("zoe@example.com", sorted[0]["email"]);
        Assert.Equal("bob@example.com", sorted[1]["email"]);
        Assert.Equal("alice@example.com", sorted[2]["email"]);
    }

    [Fact]
    public void StatusColumnWithNoCaseCollation_ShouldFilter()
    {
        // Arrange
        var table = CreateTableWithCollation();
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "Active" } },
            new() { { "id", 2 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "ACTIVE" } },
            new() { { "id", 3 }, { "email", "charlie@example.com" }, { "name", "Charlie" }, { "status", "Inactive" } }
        };

        // Act - WHERE status = 'active' with NOCASE collation on status
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "status", "=", "active")).ToList();

        // Assert - should find both "Active" and "ACTIVE"
        Assert.Equal(2, result.Count);
        Assert.True(result.All(r => r["status"].ToString()!.ToUpperInvariant() == "ACTIVE"));
    }

    [Fact]
    public void InequalityWithCollation_ShouldWork()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } }
        };

        // Act - WHERE email <> 'ALICE@EXAMPLE.COM' with NOCASE
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "email", "<>", "ALICE@EXAMPLE.COM")).ToList();

        // Assert - should find only bob (alice should be excluded due to case-insensitive match)
        Assert.Single(result);
        Assert.Equal(2, (int)result[0]["id"]);
    }

    [Fact]
    public void InOperatorWithCollation_ShouldWork()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "inactive" } },
            new() { { "id", 3 }, { "email", "charlie@example.com" }, { "name", "Charlie" }, { "status", "active" } }
        };

        // Act - WHERE status IN ('ACTIVE', 'pending') with NOCASE
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "status", "IN", "('ACTIVE', 'pending')")).ToList();

        // Assert - should find alice and charlie (both have "active" status, case-insensitive)
        Assert.Equal(2, result.Count);
        Assert.True(result.All(r => r["status"].ToString()!.ToUpperInvariant() == "ACTIVE"));
    }

    [Fact]
    public void CollationAwareEqualityComparer_ShouldWorkInHashSet()
    {
        // Arrange
        var comparer = new CollationAwareEqualityComparer(CollationType.NoCase);
        var set = new HashSet<string>(comparer);

        // Act - Add case-variant strings
        set.Add("alice@example.com");
        bool added2 = set.Add("ALICE@EXAMPLE.COM");  // Should fail - already in set
        bool added3 = set.Add("alice@EXAMPLE.com");  // Should fail - already in set
        set.Add("bob@example.com");

        // Assert
        Assert.Equal(2, set.Count);
        Assert.False(added2);
        Assert.False(added3);
    }

    [Fact]
    public void ComplexQueryWithMultipleConditions_ShouldRespectCollation()
    {
        // Arrange
        var table = CreateTableWithCollation(CollationType.NoCase);
        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "email", "alice@example.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 2 }, { "email", "ALICE@EXAMPLE.COM" }, { "name", "Alice" }, { "status", "ACTIVE" } },
            new() { { "id", 3 }, { "email", "alice@other.com" }, { "name", "Alice" }, { "status", "active" } },
            new() { { "id", 4 }, { "email", "bob@example.com" }, { "name", "Bob" }, { "status", "active" } }
        };

        // Act - WHERE email = 'alice@example.com' AND status = 'ACTIVE'
        var result = rows.Where(r =>
            table.EvaluateConditionWithCollation(r, "email", "=", "alice@example.com") &&
            table.EvaluateConditionWithCollation(r, "status", "=", "ACTIVE")).ToList();

        // Assert - should find alice@example.com with active status (case-insensitive)
        Assert.Equal(2, result.Count);
        Assert.True(result.All(r => r["email"].ToString()!.ToLowerInvariant().Contains("alice@example.com")));
    }

    [Fact]
    public void RTrimCollation_ShouldTrimTrailingSpace()
    {
        // Arrange
        var table = new Table();
        table.Name = "test";
        table.Columns = ["id", "name"];
        table.ColumnTypes = [DataType.Integer, DataType.String];
        table.ColumnCollations = [CollationType.Binary, CollationType.RTrim];
        table.PrimaryKeyIndex = 0;

        var rows = new List<Dictionary<string, object>>
        {
            new() { { "id", 1 }, { "name", "alice  " } },  // trailing spaces
            new() { { "id", 2 }, { "name", "alice" } }     // no trailing space
        };

        // Act - WHERE name = 'alice' with RTrim
        var result = rows.Where(r => table.EvaluateConditionWithCollation(
            r, "name", "=", "alice")).ToList();

        // Assert - should match both (RTrim makes them equal)
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData(CollationType.Binary)]
    [InlineData(CollationType.NoCase)]
    [InlineData(CollationType.RTrim)]
    [InlineData(CollationType.UnicodeCaseInsensitive)]
    public void AllCollationTypes_ShouldNotThrow(CollationType collation)
    {
        // Arrange
        var table = new Table();
        table.Name = "test";
        table.Columns = ["id", "value"];
        table.ColumnTypes = [DataType.Integer, DataType.String];
        table.ColumnCollations = [CollationType.Binary, collation];

        var row = new Dictionary<string, object> { { "id", 1 }, { "value", "test" } };

        // Act & Assert - should not throw
        var condition = table.EvaluateConditionWithCollation(row, "value", "=", "TEST");
        _ = condition; // Use the result
    }
}
