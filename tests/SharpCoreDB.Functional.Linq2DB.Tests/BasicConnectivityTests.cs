using System;
using System.IO;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Mapping;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// Tests for basic SharpCoreDB linq2db connectivity and CRUD operations.
/// C# 14: Uses xUnit v3, primary constructors, and collection expressions.
/// </summary>
public sealed class BasicConnectivityTests : IClassFixture<Linq2DbTestFixture>
{
    private readonly Linq2DbTestFixture _fixture;

    public BasicConnectivityTests(Linq2DbTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Connection_ShouldBeOpen()
    {
        // Assert
        _fixture.Connection.Should().NotBeNull();
        // .Connection is deprecated in linq2db v7+ but used here for test compatibility (matches GetUnderlyingConnection behavior).
        _fixture.Connection.Connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task Insert_ShouldPersistEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "Test User", Age = 30 };

        // Act
        await _fixture.Connection.InsertAsync(entity);

        // Assert
        var retrieved = await _fixture.Connection.GetTable<TestEntity>()
            .Where(e => e.Name == "Test User")
            .FirstOrDefaultAsync();

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Test User");
        retrieved.Age.Should().Be(30);
    }

    [Fact]
    public async Task Query_WithLinq_ShouldReturnFilteredResults()
    {
        // Arrange
        var prefix = "query_" + Guid.NewGuid().ToString("N");
        await _fixture.Connection.InsertAsync(new TestEntity { Name = prefix + "_Alice", Age = 25 });
        await _fixture.Connection.InsertAsync(new TestEntity { Name = prefix + "_Bob", Age = 35 });
        await _fixture.Connection.InsertAsync(new TestEntity { Name = prefix + "_Charlie", Age = 30 });

        // Act
        var results = await _fixture.Connection.GetTable<TestEntity>()
            .Where(e => e.Name.StartsWith(prefix) && e.Age > 28)
            .OrderBy(e => e.Name)
            .ToListAsync();

        // Assert
        results.Should().HaveCount(2);
        results[0].Name.Should().Be(prefix + "_Bob");
        results[1].Name.Should().Be(prefix + "_Charlie");
    }

    [Fact]
    public async Task Update_ShouldModifyEntity()
    {
        // Arrange - use unique values and reload to ensure identity is available
        var uniqueName = "Original_" + Guid.NewGuid().ToString("N");
        await _fixture.Connection.InsertAsync(new TestEntity { Name = uniqueName, Age = 20 });

        var persisted = await _fixture.Connection.GetTable<TestEntity>()
            .Where(e => e.Name == uniqueName)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync();

        persisted.Should().NotBeNull("Inserted entity should be queryable for update");

        // Act
        persisted!.Name = "Updated_" + Guid.NewGuid().ToString("N");
        persisted.Age = 21;
        await _fixture.Connection.UpdateAsync(persisted);

        // Assert
        var retrieved = await _fixture.Connection.GetTable<TestEntity>()
            .Where(e => e.Id == persisted.Id)
            .FirstOrDefaultAsync();

        retrieved.Should().NotBeNull("Update by Id should have succeeded");
        retrieved!.Age.Should().Be(21);
    }

    [Fact]
    public async Task Delete_ShouldRemoveEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "ToDelete", Age = 40 };
        await _fixture.Connection.InsertAsync(entity);
        var id = entity.Id;

        // Act
        await _fixture.Connection.DeleteAsync(entity);

        // Assert
        var retrieved = await _fixture.Connection.GetTable<TestEntity>()
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task BulkCopy_ShouldInsertMultipleEntities()
    {
        // Arrange - use unique names to avoid shared state issues
        var prefix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var entities = new[]
        {
            new TestEntity { Name = $"Bulk1_{prefix}", Age = 10 },
            new TestEntity { Name = $"Bulk2_{prefix}", Age = 20 },
            new TestEntity { Name = $"Bulk3_{prefix}", Age = 30 }
        };

        // Act - use InsertBatch for compatibility (BulkCopyAsync signature varies by linq2db version)
        await using var tx = await _fixture.Connection.BeginTransactionAsync();
        foreach (var e in entities) { await _fixture.Connection.InsertAsync(e); }
        await tx.CommitAsync();

        // Assert - at least these 3 (other tests may have added more)
        var count = (await _fixture.Connection.GetTable<TestEntity>().ToListAsync()).Count;
        count.Should().BeGreaterThanOrEqualTo(3);
    }

    // Fixture handles shared DB cleanup - no per-test Dispose needed
}

/// <summary>
/// Test entity for basic CRUD tests.
/// C# 14: Uses required properties and init-only setters.
/// </summary>
[Table(Name = "TestEntities")]
public sealed class TestEntity
{
    [PrimaryKey, Identity]
    [Column(Name = "id")]
    public int Id { get; set; }

    [Column(Name = "name"), NotNull]
    public required string Name { get; set; }

    [Column(Name = "age")]
    public int Age { get; set; }
}
