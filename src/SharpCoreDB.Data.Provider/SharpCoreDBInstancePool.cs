// <copyright file="SharpCoreDBInstancePool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Data.Provider;

/// <summary>
/// Provides a thread-safe pool/cache of SharpCoreDB database instances.
/// Multiple connections can share the same database instance, with reference counting
/// to ensure proper cleanup when all connections are closed.
/// This solves the file locking issue where each connection tried to create its own
/// database instance, causing "file is being used by another process" errors.
/// </summary>
internal sealed class SharpCoreDBInstancePool
{
    /// <summary>
    /// Represents a pooled database instance with reference counting.
    /// </summary>
    private sealed class PooledInstance
    {
        public required IDatabase Database { get; init; }
        public required string ConnectionString { get; init; }
        public required string Password { get; init; }
        public int ReferenceCount { get; set; } = 1;
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    private static readonly Lazy<SharpCoreDBInstancePool> _instance = 
        new(() => new SharpCoreDBInstancePool());

    private readonly ConcurrentDictionary<string, PooledInstance> _pool = 
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _lockObject = new();

    /// <summary>
    /// Gets the singleton instance of the pool.
    /// </summary>
    public static SharpCoreDBInstancePool Instance => _instance.Value;

    /// <summary>
    /// Acquires a database instance from the pool.
    /// If an instance exists for the given connection string and password, it is reused
    /// and the reference count is incremented. Otherwise, a new instance is created.
    /// </summary>
    /// <param name="dbPath">Path to the database file or directory.</param>
    /// <param name="password">Database password.</param>
    /// <param name="connectionString">The full connection string (for cache key).</param>
    /// <param name="readOnly">Whether the database should be opened in read-only mode.</param>
    /// <returns>A database instance that must be released via ReleaseInstance.</returns>
    public IDatabase AcquireInstance(string dbPath, string password, string connectionString, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Create a cache key from connection string and password
        var cacheKey = GenerateCacheKey(connectionString, password);

        lock (_lockObject)
        {
            if (_pool.TryGetValue(cacheKey, out var pooled))
            {
                pooled.ReferenceCount++;
                return pooled.Database;
            }

            // Create new instance
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSharpCoreDB();
            var serviceProvider = services.BuildServiceProvider();

            try
            {
                var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
                var database = factory.Create(dbPath, password, isReadOnly: readOnly);

                var instance = new PooledInstance
                {
                    Database = database,
                    ConnectionString = connectionString,
                    Password = password
                };

                _pool.TryAdd(cacheKey, instance);
                return database;
            }
            catch
            {
                if (serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                throw;
            }
        }
    }

    /// <summary>
    /// Releases a reference to a database instance.
    /// When the reference count reaches zero, the instance is disposed and removed from the pool.
    /// </summary>
    /// <param name="connectionString">The connection string (for cache key lookup)</param>
    /// <param name="password">The database password (for cache key lookup)</param>
    public void ReleaseInstance(string connectionString, string password)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var cacheKey = GenerateCacheKey(connectionString, password);

        lock (_lockObject)
        {
            if (_pool.TryGetValue(cacheKey, out var pooled))
            {
                pooled.ReferenceCount--;

                if (pooled.ReferenceCount <= 0)
                {
                    _pool.TryRemove(cacheKey, out _);
                    
                    try
                    {
                        // Attempt to force save and close
                        pooled.Database.ForceSave();
                    }
                    catch
                    {
                        // Suppress errors during cleanup
                    }

                    if (pooled.Database is IDisposable disposable)
                    {
                        try
                        {
                            disposable.Dispose();
                        }
                        catch
                        {
                            // Suppress disposal errors
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Clears all instances from the pool and disposes them.
    /// Should be called during application shutdown.
    /// </summary>
    public void Clear()
    {
        lock (_lockObject)
        {
            foreach (var kvp in _pool)
            {
                try
                {
                    kvp.Value.Database.ForceSave();
                }
                catch
                {
                    // Suppress errors
                }

                if (kvp.Value.Database is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch
                    {
                        // Suppress errors
                    }
                }
            }

            _pool.Clear();
        }
    }

    /// <summary>
    /// Generates a cache key from connection string and password.
    /// Uses a hash to normalize the key and hide sensitive information.
    /// </summary>
    private static string GenerateCacheKey(string connectionString, string password)
    {
        // Create a deterministic key from connection string and password
        var combined = $"{connectionString}|{password}".ToLowerInvariant();
        
        // Use a simple hash to create a normalized key
        unchecked
        {
            var hash = 17;
            foreach (var ch in combined)
            {
                hash = hash * 31 + ch.GetHashCode();
            }
            return $"pool_{hash:X8}";
        }
    }
}
