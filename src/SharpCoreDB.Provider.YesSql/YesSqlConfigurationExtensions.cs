// <copyright file="SharpCoreDbYesSqlExtensions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using YesSql;
using YesSql.Provider.Sqlite;

namespace SharpCoreDB.Provider.YesSql;

/// <summary>
/// Extension methods for configuring YesSql to use SharpCoreDB.
/// Uses the Sqlite provider as base but with SharpCoreDB ADO.NET provider underneath.
/// Compatible with OrchardCore CMS and .NET 10.
/// </summary>
public static class SharpCoreDbConfigurationExtensions
{
    private static readonly object _storeInitLock = new();
    private static IStore? _store;
    private static bool _isInitialized;
    private static Exception? _lastInitError;

    /// <summary>
    /// Adds YesSql with SharpCoreDB as the database provider.
    /// This is the primary integration point for OrchardCore.
    /// 
    /// CRITICAL: Uses Lazy<IStore> to defer store initialization until first use.
    /// This allows DI setup to complete without errors on fresh databases where
    /// schema tables don't exist yet. The store is initialized lazily when first accessed.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SharpCoreDB connection string (Data Source=path;Password=xxx).</param>
    /// <param name="tablePrefix">Optional table prefix for multi-tenancy (default: null).</param>
    /// <param name="isolationLevel">Transaction isolation level (default: ReadCommitted).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddYesSqlWithSharpCoreDB(
        this IServiceCollection services,
        string connectionString,
        string? tablePrefix = null,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Register SharpCoreDB provider factory
        RegisterProviderFactory();

        // Register store configuration
        services.AddSingleton(sp => CreateConfiguration(connectionString, tablePrefix, isolationLevel));
        
        // CRITICAL FIX: Use Lazy<IStore> to defer initialization until first use
        // This prevents the "table doesn't exist" error during DI setup
        services.AddSingleton<Lazy<IStore>>(sp => new Lazy<IStore>(() => GetOrCreateStore(sp)));
        
        // Register IStore as a factory that accesses the Lazy<IStore>.Value
        // This ensures the store is only created when first accessed, not during DI setup
        services.AddSingleton<IStore>(sp => sp.GetRequiredService<Lazy<IStore>>().Value);

        return services;
    }

    /// <summary>
    /// Gets or creates the YesSql Store with lazy initialization.
    /// This handles both fresh databases (where schema doesn't exist) and configured databases.
    /// </summary>
    private static IStore GetOrCreateStore(IServiceProvider sp)
    {
        // If already successfully initialized, return cached store
        if (_store != null && _isInitialized)
        {
            return _store;
        }

        lock (_storeInitLock)
        {
            // Double-check after acquiring lock
            if (_store != null && _isInitialized)
            {
                return _store;
            }

            var config = sp.GetRequiredService<Configuration>();

            try
            {
                // Attempt to create and initialize the store
                var storeTask = StoreFactory.CreateAndInitializeAsync(config);
                _store = storeTask.GetAwaiter().GetResult();
                _isInitialized = true;
                _lastInitError = null;
                return _store;
            }
            catch (Exception ex)
            {
                // Store initialization failed. On fresh databases, this is expected.
                // Cache the error and re-throw so the caller can handle it.
                System.Diagnostics.Debug.WriteLine($"Store initialization failed: {ex.Message}");
                
                _lastInitError = ex;
                _isInitialized = false;
                _store = null;
                
                // Re-throw and let Program.cs error handling deal with it
                throw;
            }
        }
    }

    /// <summary>
    /// Configures YesSql to use SharpCoreDB as the underlying database.
    /// Uses Sqlite-compatible SQL syntax but routes all operations to SharpCoreDB.
    /// </summary>
    /// <param name="configuration">The YesSql configuration.</param>
    /// <param name="connectionString">SharpCoreDB connection string.</param>
    /// <param name="tablePrefix">Optional table prefix.</param>
    /// <param name="isolationLevel">Transaction isolation level.</param>
    /// <returns>The configuration for chaining.</returns>
    public static IConfiguration UseSharpCoreDB(
        this IConfiguration configuration,
        string connectionString,
        string? tablePrefix = null,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Register provider factory
        RegisterProviderFactory();

        // Set configuration properties directly
        // YesSql Configuration exposes these as public properties
        if (configuration is Configuration config)
        {
            // Create connection factory with connection string
            var connectionFactory = new SharpCoreDbConnectionFactory(connectionString);
            config.ConnectionFactory = connectionFactory;
            
            // Use Sqlite SQL dialect from YesSql.Provider.Sqlite - SharpCoreDB is Sqlite-compatible
            // This ensures all SQL generation uses Sqlite syntax that SharpCoreDB understands:
            // - CREATE TABLE IF NOT EXISTS
            // - INTEGER PRIMARY KEY for auto-increment
            // - LIMIT/OFFSET pagination
            // - last_insert_rowid() function
            // - Double-quote identifiers ("table"."column")
            //
            // Note: The dialect Name property returns "SQLite" but this is cosmetic.
            // All SQL behavior is 100% compatible with SharpCoreDB.
            config.SqlDialect = new SqliteDialect();
            
            // Set multi-tenancy table prefix
            config.TablePrefix = tablePrefix;
            
            // Set transaction isolation level
            config.IsolationLevel = isolationLevel;
        }

        return configuration;
    }

    /// <summary>
    /// Registers the SharpCoreDB provider factory with ADO.NET.
    /// This is called automatically but can be called manually at startup.
    /// Thread-safe and idempotent - multiple calls are safe.
    /// </summary>
    public static void RegisterProviderFactory()
    {
        try
        {
            // Check if already registered
            var existing = DbProviderFactories.GetFactory("SharpCoreDB");
            if (existing != null)
            {
                return; // Already registered
            }
        }
        catch (ArgumentException)
        {
            // Not registered yet, continue
        }

        // Register the factory
        DbProviderFactories.RegisterFactory(
            "SharpCoreDB",
            SharpCoreDbProviderFactory.Instance);
    }

    /// <summary>
    /// Creates and configures a YesSql Configuration for SharpCoreDB.
    /// </summary>
    private static Configuration CreateConfiguration(
        string connectionString,
        string? tablePrefix,
        IsolationLevel isolationLevel)
    {
        var config = new Configuration();
        config.UseSharpCoreDB(connectionString, tablePrefix, isolationLevel);
        return config;
    }
}
