// <copyright file="SharpCoreDbSetupHelper.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data.Common;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Provider.YesSql;

/// <summary>
/// Helper class for SharpCoreDB setup and initialization.
/// Provides utilities for database file creation and connection string parsing.
/// </summary>
public static class SharpCoreDbSetupHelper
{
    /// <summary>
    /// Extracts the database file path from a SharpCoreDB connection string.
    /// Uses <see cref="SharpCoreDBConnectionStringBuilder"/> for reliable parsing.
    /// </summary>
    /// <param name="connectionString">The SharpCoreDB connection string.</param>
    /// <returns>The extracted database file path, or empty string if not found.</returns>
    public static string ExtractDatabasePath(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "";
        }

        var builder = new SharpCoreDBConnectionStringBuilder { ConnectionString = connectionString };
        return builder.Path ?? "";
    }

    /// <summary>
    /// Pre-creates the SharpCoreDB database file if it doesn't exist.
    /// This ensures the file is created with proper structure before any initialization.
    /// </summary>
    /// <param name="connectionString">The SharpCoreDB connection string.</param>
    public static void EnsureDatabaseFileExists(string connectionString)
    {
        var dbPath = ExtractDatabasePath(connectionString);
        if (string.IsNullOrEmpty(dbPath))
        {
            return;
        }

        if (File.Exists(dbPath))
        {
            System.Diagnostics.Debug.WriteLine($"Database file already exists: {dbPath}");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"Creating database file: {dbPath}");

            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create the file by opening and closing a connection
            SharpCoreDbConfigurationExtensions.RegisterProviderFactory();
            using var conn = SharpCoreDBProviderFactory.Instance.CreateConnection()
                ?? throw new InvalidOperationException("Failed to create SharpCoreDB connection for database initialization.");
            conn.ConnectionString = connectionString;
            conn.Open();
            conn.Close();

            System.Diagnostics.Debug.WriteLine($"Database file created successfully: {dbPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create database file: {ex.Message}");
        }
    }
}
