// <copyright file="ConcurrencyTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Identity.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using SharpCoreDB;
using SharpCoreDB.Identity;
using SharpCoreDB.Identity.Entities;
using SharpCoreDB.Identity.Options;
using SharpCoreDB.Identity.Security;
using SharpCoreDB.Identity.Storage;
using SharpCoreDB.Interfaces;

/// <summary>
/// Tests for concurrent identity operations to ensure thread-safety and data integrity.
/// </summary>
public sealed class ConcurrencyTests : IDisposable
{
    private readonly IDatabase _database;
    private readonly SharpCoreDbIdentityService _identityService;
    private readonly string _testDbPath;

    public ConcurrencyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"concurrency_test_{Guid.NewGuid()}.db");
        _database = TestDatabaseHelper.CreateTestDatabase(_testDbPath);

        var options = new SharpCoreIdentityOptions();
        var passwordHasher = new SharpCoreDbPasswordHasher(options);
        var initializer = new IdentityDatabaseInitializer(options);

        _identityService = new SharpCoreDbIdentityService(
            _database,
            passwordHasher,
            initializer,
            options,
            NullLogger<SharpCoreDbIdentityService>.Instance);
    }

    public void Dispose()
    {
        (_database as IDisposable)?.Dispose();
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task CreateUserAsync_ConcurrentCalls_ShouldHandleUniquenessCorrectly()
    {
        // Arrange
        const int concurrentAttempts = 10;
        var tasks = new Task[concurrentAttempts];

        // Act - Try to create users with same username concurrently
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    var user = TestDatabaseHelper.CreateTestUser("concurrent", $"concurrent{index}@example.com");
                    await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
        }

        var results = await Task.WhenAll(tasks.Cast<Task<bool>>());

        // Assert - Only one should succeed
        Assert.Single(results.Where(r => r));
    }

    [Fact]
    public async Task PasswordSignInAsync_ConcurrentLoginAttempts_ShouldMaintainLockoutCount()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("concurrentlogin", "concurrentlogin@example.com");
        user.EmailConfirmed = true;
        user.LockoutEnabled = true;
        await _identityService.CreateUserAsync(user, "CorrectPassword12345!", CancellationToken.None);

        const int concurrentAttempts = 10;
        var tasks = new Task<SharpCoreDB.Identity.Options.SharpCoreSignInResult>[concurrentAttempts];

        // Act - Concurrent failed login attempts
        for (int i = 0; i < concurrentAttempts; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                return await _identityService.PasswordSignInAsync("concurrentlogin", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Should eventually lock out
        Assert.Contains(results, r => r.IsLockedOut);
    }

    [Fact]
    public async Task AddToRoleAsync_ConcurrentRoleAssignments_ShouldNotDuplicate()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("concurrentrole", "role@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        const int concurrentAttempts = 20;
        var tasks = new Task[concurrentAttempts];

        // Act - Try to add same role concurrently
        for (int i = 0; i < concurrentAttempts; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await _identityService.AddToRoleAsync(created.Id, "Admin", CancellationToken.None);
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Role should be assigned only once
        var roles = await _identityService.GetRolesAsync(created.Id, CancellationToken.None);
        Assert.Single(roles);
        Assert.Equal("Admin", roles[0]);
    }

    [Fact]
    public async Task ChangePasswordAsync_ConcurrentChanges_ShouldMaintainConsistency()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("concurrentpassword", "password@example.com");
        var created = await _identityService.CreateUserAsync(user, "InitialPassword12345!", CancellationToken.None);

        const int concurrentAttempts = 5;
        var tasks = new Task<bool>[concurrentAttempts];

        // Act - Try to change password concurrently
        for (int i = 0; i < concurrentAttempts; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                return await _identityService.ChangePasswordAsync(
                created,
                "InitialPassword12345!",
                $"NewPassword{index}12345!",
                CancellationToken.None);
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert - At least one should succeed
        Assert.Contains(results, r => r);

        // Verify database is in consistent state
        var refreshed = await _identityService.FindByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.NotEmpty(refreshed.PasswordHash);
    }

    [Fact]
    public async Task FindByNameAsync_DuringConcurrentCreates_ShouldReturnConsistentResults()
    {
        // Arrange
        const int userCount = 50;
        var createTasks = new Task[userCount];

        // Act - Create multiple users concurrently
        for (int i = 0; i < userCount; i++)
        {
            var index = i;
            createTasks[i] = Task.Run(async () =>
            {
                var user = TestDatabaseHelper.CreateTestUser($"user{index}", $"user{index}@example.com");
                await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);
            });
        }

        await Task.WhenAll(createTasks);

        // Now search for users concurrently
        var searchTasks = new Task<SharpCoreUser?>[userCount];
        for (int i = 0; i < userCount; i++)
        {
            var index = i;
            searchTasks[i] = Task.Run(async () =>
            {
                return await _identityService.FindByNameAsync($"user{index}", CancellationToken.None);
            });
        }

        var searchResults = await Task.WhenAll(searchTasks);

        // Assert - All users should be found
        Assert.All(searchResults, user => Assert.NotNull(user));
    }
}




