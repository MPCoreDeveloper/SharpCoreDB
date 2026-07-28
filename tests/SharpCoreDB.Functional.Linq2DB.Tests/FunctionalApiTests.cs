using System;
using System.IO;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Mapping;
using Xunit;
using static SharpCoreDB.Functional.Prelude;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// Tests for functional API wrapper with Option/Fin/Seq return types.
/// C# 14: Railway-oriented programming patterns with modern C# syntax.
/// </summary>
public sealed class FunctionalApiTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SharpCoreDBDataConnection _connection;
    private readonly FunctionalLinq2DbContext _functionalDb;

    public FunctionalApiTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_functional_{Guid.NewGuid():N}.scdb");
        // Use Data Source= format compatible with Microsoft.Data.Sqlite / linq2db SQLite provider
        _connection = new SharpCoreDBDataConnection($"Data Source={_testDbPath}");
        _functionalDb = new FunctionalLinq2DbContext(_connection);

        // Create table via underlying connection (avoids linq2db SQLite adapter "Path=" parsing issues)
        using (var cmd = _connection.GetUnderlyingConnection().CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    email TEXT NOT NULL,
                    is_active INTEGER
                );";
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task FindOneAsync_WhenExists_ShouldReturnSome()
    {
        // Arrange
        await _connection.InsertAsync(new User { Email = "test@example.com", IsActive = true });

        // Act
        var result = await _functionalDb.FindOneAsync<User>(u => u.Email == "test@example.com");

        // Assert
        result.IsSome.Should().BeTrue();
        result.IfSome(user => user.Email.Should().Be("test@example.com"));
    }

    [Fact]
    public async Task FindOneAsync_WhenNotExists_ShouldReturnNone()
    {
        // Act
        var result = await _functionalDb.FindOneAsync<User>(u => u.Email == "nonexistent@example.com");

        // Assert
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task InsertAsync_WhenSuccessful_ShouldReturnFinSucc()
    {
        // Arrange
        var user = new User { Email = "new@example.com", IsActive = true };

        // Act
        var result = await _functionalDb.InsertAsync(user);

        // Assert
        result.IsSucc.Should().BeTrue();

        var retrieved = await _functionalDb.FindOneAsync<User>(u => u.Email == "new@example.com");
        retrieved.IsSome.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_WithPredicate_ShouldReturnSeq()
    {
        // Arrange
        await _connection.InsertAsync(new User { Email = "active1@test.com", IsActive = true });
        await _connection.InsertAsync(new User { Email = "inactive@test.com", IsActive = false });
        await _connection.InsertAsync(new User { Email = "active2@test.com", IsActive = true });

        // Act
        var result = await _functionalDb.QueryAsync<User>(u => u.IsActive);

        // Assert
        result.Count().Should().Be(2);
        foreach (var u in result) u.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_WithQueryBuilder_ShouldApplyTransformations()
    {
        // Arrange
        await _connection.InsertAsync(new User { Email = "user1@test.com", IsActive = true });
        await _connection.InsertAsync(new User { Email = "user2@test.com", IsActive = true });
        await _connection.InsertAsync(new User { Email = "user3@test.com", IsActive = false });

        // Act
        var result = await _functionalDb.QueryAsync<User>(q => q
            .Where(u => u.IsActive)
            .OrderBy(u => u.Email)
            .Take(1));

        // Assert
        result.Count().Should().Be(1);
        result[0].Email.Should().Be("user1@test.com");
    }

    [Fact]
    public async Task UpdateAsync_WhenSuccessful_ShouldReturnFinSucc()
    {
        // Arrange
        var user = new User { Email = "original@example.com", IsActive = true };
        await _connection.InsertAsync(user);
        var id = user.Id;

        // Act
        user.Email = "updated@example.com";
        var result = await _functionalDb.UpdateAsync(user);

        // Assert
        result.IsSucc.Should().BeTrue();

        var retrieved = await _functionalDb.FindOneAsync<User>(u => u.Id == id);
        retrieved.IfSome(u => u.Email.Should().Be("updated@example.com"));
    }

    [Fact]
    public async Task DeleteAsync_WhenSuccessful_ShouldReturnFinSucc()
    {
        // Arrange
        var user = new User { Email = "todelete@example.com", IsActive = true };
        await _connection.InsertAsync(user);
        var id = user.Id;

        // Act
        var result = await _functionalDb.DeleteAsync(user);

        // Assert
        result.IsSucc.Should().BeTrue();

        var retrieved = await _functionalDb.FindOneAsync<User>(u => u.Id == id);
        retrieved.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteWhereAsync_ShouldReturnDeletedCount()
    {
        // Arrange
        await _connection.InsertAsync(new User { Email = "delete1@test.com", IsActive = false });
        await _connection.InsertAsync(new User { Email = "delete2@test.com", IsActive = false });
        await _connection.InsertAsync(new User { Email = "keep@test.com", IsActive = true });

        // Act
        var result = await _functionalDb.DeleteWhereAsync<User>(u => !u.IsActive);

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().Be(2));

        var remaining = await _functionalDb.GetAllAsync<User>();
        remaining.Count().Should().Be(1);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await _connection.InsertAsync(new User { Email = "count1@test.com", IsActive = true });
        await _connection.InsertAsync(new User { Email = "count2@test.com", IsActive = true });

        // Act
        var result = await _functionalDb.CountAsync<User>();

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().Be(2));
    }

    [Fact]
    public async Task ExistsAsync_WhenExists_ShouldReturnTrue()
    {
        // Arrange
        await _connection.InsertAsync(new User { Email = "exists@test.com", IsActive = true });

        // Act
        var result = await _functionalDb.ExistsAsync<User>(u => u.Email == "exists@test.com");

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(exists => exists.Should().BeTrue());
    }

    [Fact]
    public async Task TransactionAsync_WhenSuccessful_ShouldCommit()
    {
        // Act
        var result = await _functionalDb.TransactionAsync(async () =>
        {
            await _connection.InsertAsync(new User { Email = "tx1@test.com", IsActive = true });
            await _connection.InsertAsync(new User { Email = "tx2@test.com", IsActive = true });
            return 2;
        });

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().Be(2));

        var users = await _functionalDb.GetAllAsync<User>();
        users.Count().Should().Be(2);
    }

    [Fact]
    public async Task InsertBatchAsync_ShouldInsertMultiple()
    {
        // Arrange
        var users = new[]
        {
            new User { Email = "batch1@test.com", IsActive = true },
            new User { Email = "batch2@test.com", IsActive = true },
            new User { Email = "batch3@test.com", IsActive = false }
        };

        // Act
        var result = await _functionalDb.InsertBatchAsync(users);

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().Be(3));

        var all = await _functionalDb.GetAllAsync<User>();
        all.Count().Should().Be(3);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        // Add retry for Windows file locking (common with SQLite/linq2db connections not fully released)
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
                break;
            }
            catch (IOException) when (i < 4)
            {
                System.Threading.Thread.Sleep(100); // brief backoff
            }
        }
    }
}

[Table(Name = "Users")]
public sealed class User
{
    [PrimaryKey, Identity]
    [Column(Name = "id")]
    public int Id { get; set; }

    [Column(Name = "email"), NotNull]
    public required string Email { get; set; }

    [Column(Name = "is_active")]
    public bool IsActive { get; set; }
}
