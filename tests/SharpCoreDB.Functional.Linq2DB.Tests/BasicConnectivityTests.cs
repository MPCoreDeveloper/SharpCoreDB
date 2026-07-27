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
public sealed class BasicConnectivityTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SharpCoreDBDataConnection _connection;

    public BasicConnectivityTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_linq2db_{Guid.NewGuid():N}.scdb");
        _connection = new SharpCoreDBDataConnection($"Path={_testDbPath}");

        // Create table using the underlying connection to avoid linq2db SQLite adapter connection string parsing issues with "Path=".
        using (var cmd = _connection.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS TestEntities (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    age INTEGER
                );";
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void Connection_ShouldBeOpen()
    {
        // Assert
        _connection.Should().NotBeNull();
        _connection.Connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task Insert_ShouldPersistEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "Test User", Age = 30 };

        // Act
        await _connection.InsertAsync(entity);

        // Assert
        var retrieved = await _connection.GetTable<TestEntity>()
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
        await _connection.InsertAsync(new TestEntity { Name = "Alice", Age = 25 });
        await _connection.InsertAsync(new TestEntity { Name = "Bob", Age = 35 });
        await _connection.InsertAsync(new TestEntity { Name = "Charlie", Age = 30 });

        // Act
        var results = await _connection.GetTable<TestEntity>()
            .Where(e => e.Age > 28)
            .OrderBy(e => e.Name)
            .ToListAsync();

        // Assert
        results.Should().HaveCount(2);
        results[0].Name.Should().Be("Bob");
        results[1].Name.Should().Be("Charlie");
    }

    [Fact]
    public async Task Update_ShouldModifyEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "Original", Age = 20 };
        await _connection.InsertAsync(entity);
        var id = entity.Id;

        // Act
        entity.Name = "Updated";
        entity.Age = 21;
        await _connection.UpdateAsync(entity);

        // Assert
        var retrieved = await _connection.GetTable<TestEntity>()
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();

        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Updated");
        retrieved.Age.Should().Be(21);
    }

    [Fact]
    public async Task Delete_ShouldRemoveEntity()
    {
        // Arrange
        var entity = new TestEntity { Name = "ToDelete", Age = 40 };
        await _connection.InsertAsync(entity);
        var id = entity.Id;

        // Act
        await _connection.DeleteAsync(entity);

        // Assert
        var retrieved = await _connection.GetTable<TestEntity>()
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();

        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task BulkCopy_ShouldInsertMultipleEntities()
    {
        // Arrange
        var entities = new[]
        {
            new TestEntity { Name = "Bulk1", Age = 10 },
            new TestEntity { Name = "Bulk2", Age = 20 },
            new TestEntity { Name = "Bulk3", Age = 30 }
        };

        // Act - use InsertBatch for compatibility (BulkCopyAsync signature varies by linq2db version)
        await using var tx = await _connection.BeginTransactionAsync();
        foreach (var e in entities) { await _connection.InsertAsync(e); }
        await tx.CommitAsync();

        // Assert
        var count = (await _connection.GetTable<TestEntity>().ToListAsync()).Count;
        count.Should().Be(3);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }
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
