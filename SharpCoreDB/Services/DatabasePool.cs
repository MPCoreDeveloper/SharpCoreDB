// <copyright file="DatabasePool.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

/// <summary>
/// Provides connection pooling and reuse of Database instances.
/// </summary>
public class DatabasePool : IDisposable
{
    private readonly IServiceProvider services;
    private readonly ConcurrentDictionary<string, PooledDatabase> pool = new();
    private readonly int maxPoolSize;
    private bool disposed = false;

    /// <summary>
    /// Represents a pooled database with reference counting.
    /// </summary>
    private class PooledDatabase
    {
        public IDatabase Database { get; set; }

        public string ConnectionString { get; set; }

        public int ReferenceCount { get; set; }

        public DateTime LastUsed { get; set; }

        public PooledDatabase(IDatabase database, string connectionString)
        {
            this.Database = database;
            this.ConnectionString = connectionString;
            this.ReferenceCount = 0;
            this.LastUsed = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabasePool"/> class.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="maxPoolSize">Maximum number of pooled connections (default 10).</param>
    public DatabasePool(IServiceProvider services, int maxPoolSize = 10)
    {
        this.services = services;
        this.maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Gets or creates a database instance from the pool using a connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>A database instance.</returns>
    public IDatabase GetDatabase(string connectionString)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(DatabasePool));
        }

        var builder = new ConnectionStringBuilder(connectionString);
        return this.GetDatabase(builder.DataSource, builder.Password, builder.ReadOnly);
    }

    /// <summary>
    /// Gets or creates a database instance from the pool.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <param name="isReadOnly">Whether the database is readonly.</param>
    /// <returns>A database instance.</returns>
    public IDatabase GetDatabase(string dbPath, string masterPassword, bool isReadOnly = false)
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(DatabasePool));
        }

        var key = $"{dbPath}|{isReadOnly}";

        var pooled = this.pool.GetOrAdd(key, _ =>
        {
            var factory = this.services.GetRequiredService<DatabaseFactory>();
            var db = factory.Create(dbPath, masterPassword, isReadOnly);
            return new PooledDatabase(db, key);
        });

        lock (pooled)
        {
            pooled.ReferenceCount++;
            pooled.LastUsed = DateTime.UtcNow;
        }

        return pooled.Database;
    }

    /// <summary>
    /// Returns a database instance to the pool (decrements reference count).
    /// </summary>
    /// <param name="database">The database instance.</param>
    public void ReturnDatabase(IDatabase database)
    {
        if (this.disposed || database == null)
        {
            return;
        }

        var pooled = this.pool.Values.FirstOrDefault(p => p.Database == database);
        if (pooled != null)
        {
            lock (pooled)
            {
                pooled.ReferenceCount = Math.Max(0, pooled.ReferenceCount - 1);
                pooled.LastUsed = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Clears unused database instances from the pool (reference count = 0 and idle for more than specified time).
    /// </summary>
    /// <param name="idleTimeout">The idle timeout duration (default 5 minutes).</param>
    public void ClearIdleConnections(TimeSpan? idleTimeout = null)
    {
        if (this.disposed)
        {
            return;
        }

        var timeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        var cutoff = DateTime.UtcNow - timeout;

        var keysToRemove = new List<string>();
        foreach (var kvp in this.pool)
        {
            lock (kvp.Value)
            {
                if (kvp.Value.ReferenceCount == 0 && kvp.Value.LastUsed < cutoff)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            if (this.pool.TryRemove(key, out var pooled))
            {
                // Database instances don't have explicit dispose, but we remove from pool
            }
        }
    }

    /// <summary>
    /// Gets the current pool size.
    /// </summary>
    public int PoolSize => this.pool.Count;

    /// <summary>
    /// Gets statistics about the pool.
    /// </summary>
    /// <returns>A dictionary with pool statistics.</returns>
    public Dictionary<string, int> GetPoolStatistics()
    {
        var stats = new Dictionary<string, int>
        {
            ["TotalConnections"] = this.pool.Count,
            ["ActiveConnections"] = this.pool.Values.Count(p => p.ReferenceCount > 0),
            ["IdleConnections"] = this.pool.Values.Count(p => p.ReferenceCount == 0),
        };

        return stats;
    }

    /// <summary>
    /// Disposes the database pool and clears all connections.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.pool.Clear();
        GC.SuppressFinalize(this);
    }
}
