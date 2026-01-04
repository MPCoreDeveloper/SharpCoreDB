// <copyright file="DatabaseExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;

/// <summary>
/// Extension methods for configuring SharpCoreDB services.
/// Modern C# 14 with improved service registration patterns.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Adds SharpCoreDB services to the service collection.
    /// </summary>
    public static IServiceCollection AddSharpCoreDB(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);  // ✅ C# 14
        
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddTransient<DatabaseFactory>();
        services.AddSingleton<SharpCoreDB.Services.WalManager>();
        
        return services;
    }
}

/// <summary>
/// Factory for creating Database instances with dependency injection.
/// Modern C# 14 primary constructor pattern with enhanced storage mode support.
/// </summary>
/// <param name="services">The service provider for dependency injection.</param>
public class DatabaseFactory(IServiceProvider services)
{
    /// <summary>
    /// Creates a new Database instance (legacy method, backward compatible).
    /// </summary>
    /// <param name="dbPath">Database path (directory or .scdb file)</param>
    /// <param name="masterPassword">Master password for encryption</param>
    /// <param name="isReadOnly">Whether database is read-only</param>
    /// <param name="config">Optional database configuration (legacy)</param>
    /// <param name="securityConfig">Security configuration (kept for API compatibility)</param>
    /// <returns>Database instance</returns>
    public IDatabase Create(
        string dbPath, 
        string masterPassword, 
        bool isReadOnly = false, 
        DatabaseConfig? config = null, 
        SecurityConfig? securityConfig = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        
        // Auto-detect storage mode by file extension
        var options = DetectStorageMode(dbPath, config);
        
        return CreateWithOptions(dbPath, masterPassword, options);
    }

    /// <summary>
    /// Creates a new Database instance with DatabaseOptions (new API).
    /// Supports both directory and single-file storage modes.
    /// </summary>
    /// <param name="dbPath">Database path (directory or .scdb file)</param>
    /// <param name="masterPassword">Master password for encryption</param>
    /// <param name="options">Database options with storage mode</param>
    /// <returns>Database instance</returns>
    public IDatabase CreateWithOptions(string dbPath, string masterPassword, DatabaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(masterPassword);
        ArgumentNullException.ThrowIfNull(options);
        
        options.Validate();

        // Auto-detect storage mode if not explicitly set
        if (dbPath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
        {
            options.StorageMode = StorageMode.SingleFile;
        }

        return options.StorageMode switch
        {
            StorageMode.SingleFile => CreateSingleFileDatabase(dbPath, masterPassword, options),
            StorageMode.Directory => CreateDirectoryDatabase(dbPath, masterPassword, options),
            _ => throw new ArgumentException($"Invalid storage mode: {options.StorageMode}")
        };
    }

    /// <summary>
    /// Creates a database using legacy directory-based storage.
    /// </summary>
    private IDatabase CreateDirectoryDatabase(string dbPath, string masterPassword, DatabaseOptions options)
    {
        // For now, use existing Database class (will be refactored to use DirectoryStorageProvider)
        var config = options.DatabaseConfig ?? DatabaseConfig.Default;
        return new Database(services, dbPath, masterPassword, false, config);
    }

    /// <summary>
    /// Creates a database using new single-file storage (.scdb format).
    /// </summary>
    private static IDatabase CreateSingleFileDatabase(string dbPath, string masterPassword, DatabaseOptions options)
    {
        // NOTE: This will be integrated once Database class is refactored to use IStorageProvider
        // For now, return placeholder that uses SingleFileStorageProvider
        _ = dbPath;
        _ = masterPassword;
        _ = options;
        
        throw new NotImplementedException(
            "Single-file storage mode is not yet integrated with Database class. " +
            "Use StorageMode.Directory for backward compatibility. " +
            "Integration planned for next phase.");
    }

    /// <summary>
    /// Auto-detects storage mode from file path and creates appropriate options.
    /// </summary>
    private static DatabaseOptions DetectStorageMode(string dbPath, DatabaseConfig? config)
    {
        var isSingleFile = dbPath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase) ||
                           File.Exists(dbPath) && !Directory.Exists(dbPath);

        var options = isSingleFile
            ? DatabaseOptions.CreateSingleFileDefault()
            : DatabaseOptions.CreateDirectoryDefault();

        // Apply legacy config if provided
        if (config != null)
        {
            options.DatabaseConfig = config;
            options.EnableMemoryMapping = config.UseMemoryMapping;
            options.WalBufferSizePages = config.WalBufferSize / options.PageSize;
        }

        return options;
    }
}
