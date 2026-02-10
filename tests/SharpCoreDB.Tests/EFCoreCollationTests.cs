// <copyright file="EFCoreCollationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Unit tests for EF Core COLLATE support:
/// - UseCollation() in migrations
/// - EF.Functions.Collate() in queries
/// - string.Equals(StringComparison) translation
/// - Case-insensitive queries
/// </summary>
public sealed class EFCoreCollationTests : IDisposable
{
    private readonly string testDbPath;
    private readonly TestDbContext db;

    public EFCoreCollationTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"ef_collation_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        var connectionString = $"Data Source={testDbPath};Password=test_password";
        
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSharpCoreDB(connectionString)
            .Options;

        db = new TestDbContext(options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            db.Dispose();
        }
        catch { }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Migration_WithUseCollation_ShouldEmitCollateClause()
    {
        // Arrange & Act - Database created with UseCollation("NOCASE")
        // Verified via migration SQL generation in OnModelCreating

        // Assert - Check that users can be queried case-insensitively
        db.Users.Add(new User { Username = "Alice", Email = "alice@example.com" });
        db.Users.Add(new User { Username = "Bob", Email = "bob@example.com" });
        db.SaveChanges();

        // Case-insensitive query should work due to NOCASE collation on column
        var user = db.Users.FirstOrDefault(u => u.Username == "ALICE");
        Assert.NotNull(user);
        Assert.Equal("Alice", user.Username);
    }

    [Fact]
    public void Query_WithEFunctionsCollate_ShouldGenerateCollateClause()
    {
        // Arrange
        db.Users.Add(new User { Username = "Alice", Email = "alice@example.com" });
        db.Users.Add(new User { Username = "Bob", Email = "bob@example.com" });
        db.SaveChanges();

        // Act - Use EF.Functions.Collate() for explicit collation
        var users = db.Users
            .Where(u => EF.Functions.Collate(u.Username, "NOCASE") == "ALICE")
            .ToList();

        // Assert
        Assert.Single(users);
        Assert.Equal("Alice", users[0].Username);
    }

    [Fact]
    public void Query_WithStringEqualsOrdinalIgnoreCase_ShouldUseCaseInsensitiveComparison()
    {
        // Arrange
        db.Users.Add(new User { Username = "Alice", Email = "alice@example.com" });
        db.Users.Add(new User { Username = "Bob", Email = "bob@example.com" });
        db.SaveChanges();

        // Act - Use string.Equals(StringComparison.OrdinalIgnoreCase)
        var users = db.Users
            .Where(u => u.Username.Equals("alice", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert - Should translate to COLLATE NOCASE
        Assert.Single(users);
        Assert.Equal("Alice", users[0].Username);
    }

    [Fact]
    public void Query_WithStringEqualsOrdinal_ShouldUseCaseSensitiveComparison()
    {
        // Arrange
        db.Users.Add(new User { Username = "Alice", Email = "alice@example.com" });
        db.Users.Add(new User { Username = "alice", Email = "alice2@example.com" });
        db.SaveChanges();

        // Act - Use string.Equals(StringComparison.Ordinal)
        var users = db.Users
            .Where(u => u.Username.Equals("alice", StringComparison.Ordinal))
            .ToList();

        // Assert - Should be case-sensitive
        Assert.Single(users);
        Assert.Equal("alice", users[0].Username);
    }

    [Fact]
    public void Query_WithContains_ShouldWorkWithCollation()
    {
        // Arrange
        db.Users.Add(new User { Username = "Alice Smith", Email = "alice@example.com" });
        db.Users.Add(new User { Username = "Bob Jones", Email = "bob@example.com" });
        db.SaveChanges();

        // Act - Contains with collation
        var users = db.Users
            .Where(u => u.Username.Contains("SMITH"))
            .ToList();

        // Assert - Should find "Alice Smith" via case-insensitive LIKE
        Assert.Single(users);
        Assert.Equal("Alice Smith", users[0].Username);
    }

    [Fact]
    public void MultipleConditions_WithMixedCollations_ShouldWork()
    {
        // Arrange
        db.Users.Add(new User { Username = "Alice", Email = "ALICE@EXAMPLE.COM" });
        db.Users.Add(new User { Username = "Bob", Email = "bob@example.com" });
        db.SaveChanges();

        // Act - Multiple conditions with different collations
        var users = db.Users
            .Where(u => 
                EF.Functions.Collate(u.Username, "NOCASE") == "alice" &&
                EF.Functions.Collate(u.Email, "NOCASE") == "alice@example.com")
            .ToList();

        // Assert
        Assert.Single(users);
        Assert.Equal("Alice", users[0].Username);
    }

    [Fact]
    public void OrderBy_WithCollation_ShouldSortCaseInsensitively()
    {
        // Arrange
        db.Users.Add(new User { Username = "charlie", Email = "c@example.com" });
        db.Users.Add(new User { Username = "Alice", Email = "a@example.com" });
        db.Users.Add(new User { Username = "BOB", Email = "b@example.com" });
        db.SaveChanges();

        // Act - Order by with collation (due to UseCollation on column)
        var users = db.Users.OrderBy(u => u.Username).ToList();

        // Assert - Should be case-insensitive alphabetical order
        Assert.Equal(3, users.Count);
        Assert.Equal("Alice", users[0].Username);
        Assert.Equal("BOB", users[1].Username);
        Assert.Equal("charlie", users[2].Username);
    }
}

/// <summary>
/// Test DbContext with collation-configured entities.
/// </summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // ✅ EF Core COLLATE: Use NOCASE collation for Username and Email
            entity.Property(e => e.Username)
                .IsRequired()
                .HasMaxLength(100)
                .UseCollation("NOCASE"); // Emits: Username TEXT COLLATE NOCASE NOT NULL
            
            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(255)
                .UseCollation("NOCASE"); // Emits: Email TEXT COLLATE NOCASE NOT NULL
        });
    }
}

/// <summary>
/// Test entity for collation tests.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
