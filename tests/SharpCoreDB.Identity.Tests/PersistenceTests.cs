// <copyright file="PersistenceTests.cs" company="MPCoreDeveloper">
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
/// Tests for data persistence and database restart scenarios.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _testDbPath;
    private IDatabase? _database;
    private SharpCoreDbIdentityService? _identityService;

    public PersistenceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"persistence_test_{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        (_database as IDisposable)?.Dispose();
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { /* Ignore cleanup errors */ }
        }
    }

    private void InitializeService()
    {
        (_database as IDisposable)?.Dispose();
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

    [Fact]
    public async Task CreateUser_AfterRestart_ShouldPersist()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("persistentuser", "persist@example.com");
        user.FullName = "Persistent User";
        var created = await _identityService!.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        var userId = created.Id;

        // Act - Restart database
        InitializeService();
        var found = await _identityService!.FindByIdAsync(userId, CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("persistentuser", found.UserName);
        Assert.Equal("persist@example.com", found.Email);
        Assert.Equal("Persistent User", found.FullName);
    }

    [Fact]
    public async Task PasswordHash_AfterRestart_ShouldStillValidate()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("hashtest", "hash@example.com");
        var created = await _identityService!.CreateUserAsync(user, "SecurePassword12345!", CancellationToken.None);
        var userId = created.Id;

        // Act - Restart database
        InitializeService();
        var found = await _identityService!.FindByIdAsync(userId, CancellationToken.None);
        Assert.NotNull(found);
        var isValid = await _identityService.CheckPasswordAsync(found, "SecurePassword12345!", CancellationToken.None);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task Roles_AfterRestart_ShouldPersist()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("rolepersist", "role@example.com");
        var created = await _identityService!.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "Admin", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "Moderator", CancellationToken.None);

        // Act - Restart database
        InitializeService();
        var roles = await _identityService!.GetRolesAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.Equal(2, roles.Count);
        Assert.Contains("Admin", roles);
        Assert.Contains("Moderator", roles);
    }

    [Fact]
    public async Task EmailConfirmation_AfterRestart_ShouldPersist()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("emailconfirm", "confirm@example.com");
        var created = await _identityService!.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        var token = await _identityService.GenerateEmailConfirmationTokenAsync(created, CancellationToken.None);
        await _identityService.ConfirmEmailAsync(created.Id, token, CancellationToken.None);

        // Act - Restart database
        InitializeService();
        var found = await _identityService!.FindByIdAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.True(found.EmailConfirmed);
    }

    [Fact]
    public async Task LockoutState_AfterRestart_ShouldPersist()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("lockoutpersist", "lockout@example.com");
        user.EmailConfirmed = true;
        user.LockoutEnabled = true;
        await _identityService!.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Trigger lockout
        for (int i = 0; i < 5; i++)
        {
            await _identityService.PasswordSignInAsync("lockoutpersist", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);
        }

        // Act - Restart database
        InitializeService();
        var result = await _identityService!.PasswordSignInAsync("lockoutpersist", "Password123456!", lockoutOnFailure: true, CancellationToken.None);

        // Assert
        Assert.True(result.IsLockedOut);
    }

    [Fact]
    public async Task MultipleUsers_AfterRestart_AllShouldPersist()
    {
        // Arrange
        InitializeService();
        var userIds = new List<Guid>();

        for (int i = 0; i < 100; i++)
        {
            var user = TestDatabaseHelper.CreateTestUser($"batchuser{i}", $"batch{i}@example.com");
            var created = await _identityService!.CreateUserAsync(user, "Password123456!", CancellationToken.None);
            userIds.Add(created.Id);
        }

        // Act - Restart database
        InitializeService();
        var foundCount = 0;
        foreach (var userId in userIds)
        {
            var found = await _identityService!.FindByIdAsync(userId, CancellationToken.None);
            if (found != null)
            {
                foundCount++;
            }
        }

        // Assert
        Assert.Equal(100, foundCount);
    }

    [Fact]
    public async Task SecurityStamp_AfterRestart_ShouldPersist()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("securitystamp", "stamp@example.com");
        var created = await _identityService!.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        var originalStamp = created.SecurityStamp;

        // Act - Restart database
        InitializeService();
        var found = await _identityService!.FindByIdAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(originalStamp, found.SecurityStamp);
    }

    [Fact]
    public async Task PasswordChange_AfterRestart_ShouldPersist()
    {
        // Arrange
        InitializeService();
        var user = TestDatabaseHelper.CreateTestUser("passwordchange", "change@example.com");
        var created = await _identityService!.CreateUserAsync(user, "OldPassword12345!", CancellationToken.None);
        await _identityService.ChangePasswordAsync(created, "OldPassword12345!", "NewPassword45678!", CancellationToken.None);

        // Act - Restart database
        InitializeService();
        var found = await _identityService!.FindByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(found);

        var oldPasswordValid = await _identityService.CheckPasswordAsync(found, "OldPassword12345!", CancellationToken.None);
        var newPasswordValid = await _identityService.CheckPasswordAsync(found, "NewPassword45678!", CancellationToken.None);

        // Assert
        Assert.False(oldPasswordValid);
        Assert.True(newPasswordValid);
    }
}






