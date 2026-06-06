// <copyright file="LockoutTests.cs" company="MPCoreDeveloper">
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
/// Tests for account lockout functionality.
/// </summary>
public sealed class LockoutTests : IDisposable
{
    private readonly IDatabase _database;
    private readonly SharpCoreDbIdentityService _identityService;
    private readonly SharpCoreIdentityOptions _options;
    private readonly string _testDbPath;

    public LockoutTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"lockout_test_{Guid.NewGuid()}.db");
        _database = TestDatabaseHelper.CreateTestDatabase(_testDbPath);

        _options = new SharpCoreIdentityOptions
        {
            Lockout =
            {
                MaxFailedAccessAttempts = 3,
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5),
                AllowedForNewUsers = true
            }
        };

        var passwordHasher = new SharpCoreDbPasswordHasher(_options);
        var initializer = new IdentityDatabaseInitializer(_options);

        _identityService = new SharpCoreDbIdentityService(
            _database,
            passwordHasher,
            initializer,
            _options,
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
    public async Task PasswordSignInAsync_WithLockoutEnabled_ShouldLockAfterMaxAttempts()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("lockoutuser", "lockout@example.com");
        user.EmailConfirmed = true;
        user.LockoutEnabled = true;
        await _identityService.CreateUserAsync(user, "CorrectPassword123456!", CancellationToken.None);

        // Act - Make multiple failed login attempts
        for (int i = 0; i < _options.Lockout.MaxFailedAccessAttempts; i++)
        {
            await _identityService.PasswordSignInAsync("lockoutuser", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);
        }

        var result = await _identityService.PasswordSignInAsync("lockoutuser", "CorrectPassword123456!", lockoutOnFailure: true, CancellationToken.None);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(result.IsLockedOut);
    }

    [Fact]
    public async Task PasswordSignInAsync_WithLockoutDisabled_ShouldNotLock()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("nolockout", "nolockout@example.com");
        user.EmailConfirmed = true;
        user.LockoutEnabled = false;
        await _identityService.CreateUserAsync(user, "CorrectPassword123456!", CancellationToken.None);

        // Act - Make multiple failed login attempts
        for (int i = 0; i < 10; i++)
        {
            await _identityService.PasswordSignInAsync("nolockout", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);
        }

        var result = await _identityService.PasswordSignInAsync("nolockout", "CorrectPassword123456!", lockoutOnFailure: true, CancellationToken.None);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.IsLockedOut);
    }

    [Fact]
    public async Task PasswordSignInAsync_SuccessfulLogin_ShouldResetFailedCount()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("resetcount", "reset@example.com");
        user.EmailConfirmed = true;
        user.LockoutEnabled = true;
        await _identityService.CreateUserAsync(user, "CorrectPassword123456!", CancellationToken.None);

        // Act - Make some failed attempts, then succeed
        await _identityService.PasswordSignInAsync("resetcount", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);
        await _identityService.PasswordSignInAsync("resetcount", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);

        var successResult = await _identityService.PasswordSignInAsync("resetcount", "CorrectPassword123456!", lockoutOnFailure: true, CancellationToken.None);
        Assert.True(successResult.Succeeded);

        // Now try more failed attempts - should require MaxFailedAccessAttempts again
        for (int i = 0; i < _options.Lockout.MaxFailedAccessAttempts - 1; i++)
        {
            var result = await _identityService.PasswordSignInAsync("resetcount", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);
            Assert.False(result.IsLockedOut);
        }

        var lockResult = await _identityService.PasswordSignInAsync("resetcount", "WrongPassword123!123456!123456!", lockoutOnFailure: true, CancellationToken.None);

        // Assert
        Assert.True(lockResult.IsLockedOut);
    }

    [Fact]
    public async Task PasswordSignInAsync_WithLockoutOnFailureFalse_ShouldNotLock()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("nolockoutoption", "nolockoption@example.com");
        user.EmailConfirmed = true;
        user.LockoutEnabled = true;
        await _identityService.CreateUserAsync(user, "CorrectPassword123456!", CancellationToken.None);

        // Act - Make multiple failed login attempts with lockoutOnFailure = false
        for (int i = 0; i < 10; i++)
        {
            await _identityService.PasswordSignInAsync("nolockoutoption", "WrongPassword123!123456!123456!", lockoutOnFailure: false, CancellationToken.None);
        }

        var result = await _identityService.PasswordSignInAsync("nolockoutoption", "CorrectPassword123456!", lockoutOnFailure: false, CancellationToken.None);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.IsLockedOut);
    }
}




