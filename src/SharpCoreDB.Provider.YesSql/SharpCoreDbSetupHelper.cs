// <copyright file="SharpCoreDbSetupHelper.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data.Common;

namespace SharpCoreDB.Provider.YesSql;

/// <summary>
/// Helper class for SharpCoreDB setup and initialization.
/// Provides utilities for database file creation and connection string parsing.
/// </summary>
public static class SharpCoreDbSetupHelper
{
    /// <summary>
    /// Extracts the database file path from a SharpCoreDB connection string.
    /// Connection string format: Data Source=path;Password=xxx
    /// </summary>
    /// <param name="connectionString">The SharpCoreDB connection string</param>
    /// <returns>The extracted database file path, or empty string if not found</returns>
    public static string ExtractDatabasePath(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "";

        var parts = connectionString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var keyValue = part.Trim().Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
            if (keyValue.Length == 2 && 
                keyValue[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                return keyValue[1].Trim();
            }
        }

        return "";
    }

    /// <summary>
    /// Pre-creates the SharpCoreDB database file if it doesn't exist.
    /// This ensures the file is created with proper structure before any initialization.
    /// </summary>
    /// <param name="connectionString">The SharpCoreDB connection string</param>
    public static void EnsureDatabaseFileExists(string connectionString)
    {
        var dbPath = ExtractDatabasePath(connectionString);
        if (string.IsNullOrEmpty(dbPath))
            return;

        if (File.Exists(dbPath))
        {
            System.Diagnostics.Debug.WriteLine($"Database file already exists: {dbPath}");
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"Creating database file: {dbPath}");
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create the file by opening a connection
            RegisterProviderFactory();
            using (var conn = DbProviderFactories.GetFactory("SharpCoreDB").CreateConnection())
            {
                conn.ConnectionString = connectionString;
                conn.Open();
                conn.Close();
            }
            System.Diagnostics.Debug.WriteLine($"Database file created successfully: {dbPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create database file: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers the SharpCoreDB provider factory with ADO.NET.
    /// This is called automatically but is exposed for manual registration if needed.
    /// </summary>
    private static void RegisterProviderFactory()
    {
        SharpCoreDbConfigurationExtensions.RegisterProviderFactory();
    }
}
