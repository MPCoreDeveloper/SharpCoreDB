// <copyright file="TestDatabaseHelper.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Identity.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Helper class for creating test database instances.
/// </summary>
internal static class TestDatabaseHelper
{
    /// <summary>
    /// Creates a temporary in-memory database for testing.
    /// </summary>
    public static IDatabase CreateTestDatabase(string? filePath = null)
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();

        var databaseFactory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var dbPath = filePath ?? Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");

        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = false, // Disable encryption for faster tests
            CreateImmediately = true,
            AutoVacuum = false,
            DatabaseConfig = DatabaseConfig.Default
        };

        return databaseFactory.CreateWithOptions(dbPath, "TestPassword123!", options);
    }

    /// <summary>
    /// Creates a test user with required fields populated.
    /// </summary>
    public static SharpCoreDB.Identity.Entities.SharpCoreUser CreateTestUser(
        string userName,
        string? email = null,
        string? normalizedUserName = null,
        string? passwordHash = null)
    {
        return new SharpCoreDB.Identity.Entities.SharpCoreUser
        {
            UserName = userName,
            Email = email ?? $"{userName}@example.com",
            NormalizedUserName = normalizedUserName ?? userName.ToUpperInvariant(),
            PasswordHash = passwordHash ?? "dummy-hash"
        };
    }
}
