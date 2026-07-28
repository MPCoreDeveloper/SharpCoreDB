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
public sealed class FunctionalApiTests : IClassFixture<Linq2DbTestFixture>
{
    private readonly Linq2DbTestFixture _fixture;

    public FunctionalApiTests(Linq2DbTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindOneAsync_WhenExists_ShouldReturnSome()
    {
        // Arrange
        await _fixture.Connection.InsertAsync(new User { Email = "test@example.com", IsActive = true });

        // Act
        var result = await _fixture.FunctionalDb.FindOneAsync<User>(u => u.Email == "test@example.com");

        // Assert
        result.IsSome.Should().BeTrue();
        result.IfSome(user => user.Email.Should().Be("test@example.com"));
    }

    [Fact]
    public async Task FindOneAsync_WhenNotExists_ShouldReturnNone()
    {
        // Act
        var result = await _fixture.FunctionalDb.FindOneAsync<User>(u => u.Email == "nonexistent@example.com");

        // Assert
        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task InsertAsync_WhenSuccessful_ShouldReturnFinSucc()
    {
        // Arrange
        var user = new User { Email = "new@example.com", IsActive = true };

        // Act
        var result = await _fixture.FunctionalDb.InsertAsync(user);

        // Assert
        result.IsSucc.Should().BeTrue();

        var retrieved = await _fixture.FunctionalDb.FindOneAsync<User>(u => u.Email == "new@example.com");
        retrieved.IsSome.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_WithPredicate_ShouldReturnSeq()
    {
        // Arrange - use unique emails to avoid shared state pollution
        var active1 = "active1_" + Guid.NewGuid().ToString("N") + "@test.com";
        var inactive = "inactive_" + Guid.NewGuid().ToString("N") + "@test.com";
        var active2 = "active2_" + Guid.NewGuid().ToString("N") + "@test.com";

        await _fixture.Connection.InsertAsync(new User { Email = active1, IsActive = true });
        await _fixture.Connection.InsertAsync(new User { Email = inactive, IsActive = false });
        await _fixture.Connection.InsertAsync(new User { Email = active2, IsActive = true });

        // Act
        var result = await _fixture.FunctionalDb.QueryAsync<User>(u => u.IsActive);

        // Assert - at least 2 (other tests may add more active users)
        result.Count().Should().BeGreaterThanOrEqualTo(2);
        foreach (var u in result) u.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task QueryAsync_WithQueryBuilder_ShouldApplyTransformations()
    {
        // Arrange
        await _fixture.Connection.InsertAsync(new User { Email = "user1@test.com", IsActive = true });
        await _fixture.Connection.InsertAsync(new User { Email = "user2@test.com", IsActive = true });
        await _fixture.Connection.InsertAsync(new User { Email = "user3@test.com", IsActive = false });

        // Act
        var result = await _fixture.FunctionalDb.QueryAsync<User>(q => q
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
        await _fixture.Connection.InsertAsync(user);
        var id = user.Id;

        // Act
        user.Email = "updated@example.com";
        var result = await _fixture.FunctionalDb.UpdateAsync(user);

        // Assert
        result.IsSucc.Should().BeTrue();

        var retrieved = await _fixture.FunctionalDb.FindOneAsync<User>(u => u.Id == id);
        retrieved.IfSome(u => u.Email.Should().Be("updated@example.com"));
    }

    [Fact]
    public async Task DeleteAsync_WhenSuccessful_ShouldReturnFinSucc()
    {
        // Arrange
        var user = new User { Email = "todelete@example.com", IsActive = true };
        await _fixture.Connection.InsertAsync(user);
        var id = user.Id;

        // Act
        var result = await _fixture.FunctionalDb.DeleteAsync(user);

        // Assert
        result.IsSucc.Should().BeTrue();

        var retrieved = await _fixture.FunctionalDb.FindOneAsync<User>(u => u.Id == id);
        retrieved.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteWhereAsync_ShouldReturnDeletedCount()
    {
        // Arrange - use unique emails to avoid interference from other tests in shared fixture
        var delete1 = "delete1_" + Guid.NewGuid().ToString("N") + "@test.com";
        var delete2 = "delete2_" + Guid.NewGuid().ToString("N") + "@test.com";
        var keep = "keep_" + Guid.NewGuid().ToString("N") + "@test.com";

        await _fixture.Connection.InsertAsync(new User { Email = delete1, IsActive = false });
        await _fixture.Connection.InsertAsync(new User { Email = delete2, IsActive = false });
        await _fixture.Connection.InsertAsync(new User { Email = keep, IsActive = true });

        // Act
        var result = await _fixture.FunctionalDb.DeleteWhereAsync<User>(u => !u.IsActive);

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().BeGreaterThanOrEqualTo(2));

        var remaining = await _fixture.FunctionalDb.GetAllAsync<User>();
        remaining.Count().Should().BeGreaterThan(0); // at least the keep record (other tests may have added more)
    }

    [Fact]
    public async Task CountAsync_ShouldReturnCorrectCount()
    {
        // Arrange - use unique emails
        var email1 = "count1_" + Guid.NewGuid().ToString("N") + "@test.com";
        var email2 = "count2_" + Guid.NewGuid().ToString("N") + "@test.com";

        await _fixture.Connection.InsertAsync(new User { Email = email1, IsActive = true });
        await _fixture.Connection.InsertAsync(new User { Email = email2, IsActive = true });

        // Act
        var result = await _fixture.FunctionalDb.CountAsync<User>();

        // Assert - at least these 2 (other tests may have added more in shared fixture)
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().BeGreaterThanOrEqualTo(2));
    }

    [Fact]
    public async Task ExistsAsync_WhenExists_ShouldReturnTrue()
    {
        // Arrange
        await _fixture.Connection.InsertAsync(new User { Email = "exists@test.com", IsActive = true });

        // Act
        var result = await _fixture.FunctionalDb.ExistsAsync<User>(u => u.Email == "exists@test.com");

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(exists => exists.Should().BeTrue());
    }

    [Fact]
    public async Task TransactionAsync_WhenSuccessful_ShouldCommit()
    {
        // Act - use unique emails
        var tx1 = "tx1_" + Guid.NewGuid().ToString("N") + "@test.com";
        var tx2 = "tx2_" + Guid.NewGuid().ToString("N") + "@test.com";

        var result = await _fixture.FunctionalDb.TransactionAsync(async () =>
        {
            await _fixture.Connection.InsertAsync(new User { Email = tx1, IsActive = true });
            await _fixture.Connection.InsertAsync(new User { Email = tx2, IsActive = true });
            return 2;
        });

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().BeGreaterThanOrEqualTo(2));

        var users = await _fixture.FunctionalDb.GetAllAsync<User>();
        users.Count().Should().BeGreaterThanOrEqualTo(2); // at least these 2
    }

    [Fact]
    public async Task InsertBatchAsync_ShouldInsertMultiple()
    {
        // Arrange - use unique emails
        var batch1 = "batch1_" + Guid.NewGuid().ToString("N") + "@test.com";
        var batch2 = "batch2_" + Guid.NewGuid().ToString("N") + "@test.com";
        var batch3 = "batch3_" + Guid.NewGuid().ToString("N") + "@test.com";

        var users = new[]
        {
            new User { Email = batch1, IsActive = true },
            new User { Email = batch2, IsActive = true },
            new User { Email = batch3, IsActive = false }
        };

        // Act
        var result = await _fixture.FunctionalDb.InsertBatchAsync(users);

        // Assert
        result.IsSucc.Should().BeTrue();
        result.IfSucc(count => count.Should().Be(3));

        var all = await _fixture.FunctionalDb.GetAllAsync<User>();
        all.Count().Should().BeGreaterThanOrEqualTo(3);
    }

    // Fixture handles shared DB cleanup - no per-test Dispose needed
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
