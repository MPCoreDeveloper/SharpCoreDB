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
    private static readonly Lock _storeInitLock = new();
    private static volatile IStore? _store;
    private static volatile bool _isInitialized;

    /// <summary>
    /// Adds YesSql with SharpCoreDB as the database provider.
    /// This is the primary integration point for OrchardCore.
    /// 
    /// Uses <see cref="Lazy{T}"/> to defer store initialization until first use.
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

        RegisterProviderFactory();

        services.AddSingleton(sp => CreateConfiguration(connectionString, tablePrefix, isolationLevel));

        // Use Lazy<IStore> to defer initialization until first use.
        // This prevents "table doesn't exist" errors during DI setup on fresh databases.
        services.AddSingleton<Lazy<IStore>>(sp => new Lazy<IStore>(() => GetOrCreateStore(sp)));
        services.AddSingleton<IStore>(sp => sp.GetRequiredService<Lazy<IStore>>().Value);

        return services;
    }

    /// <summary>
    /// Gets or creates the YesSql Store with lazy initialization.
    /// Handles both fresh databases (where schema doesn't exist) and configured databases.
    /// </summary>
    private static IStore GetOrCreateStore(IServiceProvider sp)
    {
        if (_store is not null && _isInitialized)
        {
            return _store;
        }

        lock (_storeInitLock)
        {
            if (_store is not null && _isInitialized)
            {
                return _store;
            }

            var config = sp.GetRequiredService<Configuration>();

            // StoreFactory.CreateAndInitializeAsync is the only API available.
            // We must block here because IStore registration is synchronous in DI.
            var store = StoreFactory.CreateAndInitializeAsync(config).GetAwaiter().GetResult();
            _store = store;
            _isInitialized = true;
            return store;
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

        RegisterProviderFactory();

        // YesSql Configuration exposes these as public properties
        if (configuration is Configuration config)
        {
            var connectionFactory = new SharpCoreDbConnectionFactory(connectionString);
            config.ConnectionFactory = connectionFactory;

            // Use Sqlite SQL dialect — SharpCoreDB is Sqlite-compatible:
            // CREATE TABLE IF NOT EXISTS, INTEGER PRIMARY KEY, LIMIT/OFFSET,
            // last_insert_rowid(), double-quote identifiers
            config.SqlDialect = new SqliteDialect();

            config.TablePrefix = tablePrefix;
            config.IsolationLevel = isolationLevel;
        }

        return configuration;
    }

    /// <summary>
    /// Registers the SharpCoreDB provider factory with ADO.NET.
    /// Thread-safe and idempotent — multiple calls are safe.
    /// </summary>
    public static void RegisterProviderFactory()
    {
        if (DbProviderFactories.TryGetFactory("SharpCoreDB", out _))
        {
            return;
        }

        DbProviderFactories.RegisterFactory(
            "SharpCoreDB",
            SharpCoreDbProviderFactory.Instance);
    }

    /// <summary>
    /// Resets the cached store instance. Intended for testing scenarios only.
    /// </summary>
    internal static void ResetStore()
    {
        lock (_storeInitLock)
        {
            _store = null;
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Creates and configures a YesSql <see cref="Configuration"/> for SharpCoreDB.
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
