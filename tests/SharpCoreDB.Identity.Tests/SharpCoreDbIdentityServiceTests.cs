// <copyright file="SharpCoreDbIdentityServiceTests.cs" company="MPCoreDeveloper">
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
/// Unit tests for <see cref="SharpCoreDbIdentityService"/> covering user management, authentication, and authorization.
/// </summary>
public sealed class SharpCoreDbIdentityServiceTests : IDisposable
{
    private readonly IDatabase _database;
    private readonly SharpCoreDbIdentityService _identityService;
    private readonly string _testDbPath;

    public SharpCoreDbIdentityServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"identity_test_{Guid.NewGuid()}.db");
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
    public async Task CreateUserAsync_WithValidData_ShouldSucceed()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("testuser", "test@example.com");
        user.FullName = "Test User";

        // Act
        var createdUser = await _identityService.CreateUserAsync(user, "SecurePassword123456!", CancellationToken.None);

        // Assert
        Assert.NotNull(createdUser);
        Assert.NotEqual(Guid.Empty, createdUser.Id);
        Assert.Equal("testuser", createdUser.UserName);
        Assert.NotEmpty(createdUser.PasswordHash);
        Assert.NotEmpty(createdUser.SecurityStamp);
    }

    [Fact]
    public async Task CreateUserAsync_WithDuplicateUserName_ShouldThrow()
    {
        // Arrange
        var user1 = TestDatabaseHelper.CreateTestUser("duplicate", "user1@example.com");
        await _identityService.CreateUserAsync(user1, "Password123456!", CancellationToken.None);

        var user2 = TestDatabaseHelper.CreateTestUser("duplicate", "user2@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _identityService.CreateUserAsync(user2, "Password123456!", CancellationToken.None));
    }

    [Fact]
    public async Task CreateUserAsync_WithDuplicateEmail_ShouldThrow()
    {
        // Arrange
        var user1 = TestDatabaseHelper.CreateTestUser("user1", "duplicate@example.com");
        await _identityService.CreateUserAsync(user1, "Password123456!", CancellationToken.None);

        var user2 = TestDatabaseHelper.CreateTestUser("user2", "duplicate@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _identityService.CreateUserAsync(user2, "Password123456!", CancellationToken.None));
    }

    [Fact]
    public async Task CreateUserAsync_WithWeakPassword_ShouldThrow()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("testuser", "test@example.com");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await _identityService.CreateUserAsync(user, "WeakPassword!!", CancellationToken.None));
    }

    [Fact]
    public async Task FindByIdAsync_WithValidId_ShouldReturnUser()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("findbyid", "find@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var found = await _identityService.FindByIdAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("findbyid", found.UserName);
    }

    [Fact]
    public async Task FindByIdAsync_WithInvalidId_ShouldReturnNull()
    {
        // Act
        var found = await _identityService.FindByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task FindByNameAsync_WithValidUserName_ShouldReturnUser()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("findbyname", "findname@example.com");
        await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var found = await _identityService.FindByNameAsync("findbyname", CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("findbyname", found.UserName);
    }

    [Fact]
    public async Task FindByNameAsync_CaseInsensitive_ShouldReturnUser()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("CaseSensitive", "case@example.com");
        await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var found = await _identityService.FindByNameAsync("casesensitive", CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("CaseSensitive", found.UserName);
    }

    [Fact]
    public async Task FindByEmailAsync_WithValidEmail_ShouldReturnUser()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("emailuser", "email@example.com");
        await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var found = await _identityService.FindByEmailAsync("email@example.com", CancellationToken.None);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("email@example.com", found.Email);
    }

    [Fact]
    public async Task CheckPasswordAsync_WithCorrectPassword_ShouldReturnTrue()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("checkpass", "check@example.com");
        var created = await _identityService.CreateUserAsync(user, "CorrectPassword123456!", CancellationToken.None);

        // Act
        var isValid = await _identityService.CheckPasswordAsync(created, "CorrectPassword123456!", CancellationToken.None);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task CheckPasswordAsync_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("checkfail", "fail@example.com");
        var created = await _identityService.CreateUserAsync(user, "CorrectPassword123456!", CancellationToken.None);

        // Act
        var isValid = await _identityService.CheckPasswordAsync(created, "WrongPassword123!123456!123456!", CancellationToken.None);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_ShouldSucceed()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("changepass", "change@example.com");
        var created = await _identityService.CreateUserAsync(user, "OldPassword123456!", CancellationToken.None);

        // Act
        var result = await _identityService.ChangePasswordAsync(created, "OldPassword123456!", "NewPassword45678!", CancellationToken.None);

        // Assert
        Assert.True(result);

        // Verify new password works
        var refreshed = await _identityService.FindByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(refreshed);
        var newPasswordValid = await _identityService.CheckPasswordAsync(refreshed, "NewPassword45678!", CancellationToken.None);
        Assert.True(newPasswordValid);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithInvalidCurrentPassword_ShouldFail()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("failchange", "failchange@example.com");
        var created = await _identityService.CreateUserAsync(user, "CurrentPassword123456!", CancellationToken.None);

        // Act
        var result = await _identityService.ChangePasswordAsync(created, "WrongPassword123!123456!123456!", "NewPassword45678!", CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AddToRoleAsync_ShouldAssignRole()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("roleuser", "role@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        await _identityService.AddToRoleAsync(created.Id, "Admin", CancellationToken.None);
        var roles = await _identityService.GetRolesAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.Contains("Admin", roles);
    }

    [Fact]
    public async Task AddToRoleAsync_Idempotent_ShouldNotDuplicate()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("idempotent", "idempotent@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        await _identityService.AddToRoleAsync(created.Id, "User", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "User", CancellationToken.None);
        var roles = await _identityService.GetRolesAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.Single(roles);
        Assert.Equal("User", roles[0]);
    }

    [Fact]
    public async Task RemoveFromRoleAsync_ShouldUnassignRole()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("removerole", "remove@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "Moderator", CancellationToken.None);

        // Act
        await _identityService.RemoveFromRoleAsync(created.Id, "Moderator", CancellationToken.None);
        var roles = await _identityService.GetRolesAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.Empty(roles);
    }

    [Fact]
    public async Task GetRolesAsync_WithMultipleRoles_ShouldReturnAll()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("multirole", "multi@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "Admin", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "Moderator", CancellationToken.None);
        await _identityService.AddToRoleAsync(created.Id, "User", CancellationToken.None);

        // Act
        var roles = await _identityService.GetRolesAsync(created.Id, CancellationToken.None);

        // Assert
        Assert.Equal(3, roles.Count);
        Assert.Contains("Admin", roles);
        Assert.Contains("Moderator", roles);
        Assert.Contains("User", roles);
    }

    [Fact]
    public async Task PasswordSignInAsync_WithValidCredentials_ShouldSucceed()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("signin", "signin@example.com");
        user.EmailConfirmed = true;
        user.IsActive = true;
        await _identityService.CreateUserAsync(user, "SignInPassword12345!", CancellationToken.None);

        // Act
        var result = await _identityService.PasswordSignInAsync("signin", "SignInPassword12345!", lockoutOnFailure: false, CancellationToken.None);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.IsLockedOut);
        Assert.False(result.IsNotAllowed);
    }

    [Fact]
    public async Task PasswordSignInAsync_WithInvalidPassword_ShouldFail()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("failsignin", "fail@example.com");
        user.EmailConfirmed = true;
        await _identityService.CreateUserAsync(user, "CorrectPassword12345!", CancellationToken.None);

        // Act
        var result = await _identityService.PasswordSignInAsync("failsignin", "WrongPassword123!123456!123456!12345!", lockoutOnFailure: false, CancellationToken.None);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task PasswordSignInAsync_WithInactiveUser_ShouldNotAllow()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("inactive", "inactive@example.com");
        user.IsActive = false;
        await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var result = await _identityService.PasswordSignInAsync("inactive", "Password123456!", lockoutOnFailure: false, CancellationToken.None);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(result.IsNotAllowed);
    }

    [Fact]
    public async Task GenerateEmailConfirmationTokenAsync_ShouldReturnValidToken()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("tokenuser", "token@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var token = await _identityService.GenerateEmailConfirmationTokenAsync(created, CancellationToken.None);

        // Assert
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithValidToken_ShouldConfirmEmail()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("confirmemail", "confirm@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);
        var token = await _identityService.GenerateEmailConfirmationTokenAsync(created, CancellationToken.None);

        // Act
        var result = await _identityService.ConfirmEmailAsync(created.Id, token, CancellationToken.None);

        // Assert
        Assert.True(result);

        var refreshed = await _identityService.FindByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(refreshed);
        Assert.True(refreshed.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithInvalidToken_ShouldFail()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("invalidtoken", "invalid@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var result = await _identityService.ConfirmEmailAsync(created.Id, "invalid-token", CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GeneratePasswordResetTokenAsync_ShouldReturnValidToken()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("resettoken", "reset@example.com");
        var created = await _identityService.CreateUserAsync(user, "Password123456!", CancellationToken.None);

        // Act
        var token = await _identityService.GeneratePasswordResetTokenAsync(created, CancellationToken.None);

        // Assert
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidToken_ShouldResetPassword()
    {
        // Arrange
        var user = TestDatabaseHelper.CreateTestUser("resetpass", "resetpass@example.com");
        var created = await _identityService.CreateUserAsync(user, "OldPassword123456!", CancellationToken.None);
        var token = await _identityService.GeneratePasswordResetTokenAsync(created, CancellationToken.None);

        // Act
        var result = await _identityService.ResetPasswordAsync(created.Id, token, "NewResetPassword45678!", CancellationToken.None);

        // Assert
        Assert.True(result);

        var refreshed = await _identityService.FindByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(refreshed);
        var newPasswordValid = await _identityService.CheckPasswordAsync(refreshed, "NewResetPassword45678!", CancellationToken.None);
        Assert.True(newPasswordValid);
    }
}





