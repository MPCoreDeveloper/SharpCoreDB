// <copyright file="DatabasePool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System.Collections.Concurrent;

/// <summary>
/// Provides connection pooling and reuse of Database instances.
/// Modern C# 14 with primary constructor and collection expressions.
/// </summary>
/// <param name="services">The service provider for dependency injection.</param>
/// <param name="maxPoolSize">Maximum number of pooled connections (default 10).</param>
public sealed class DatabasePool(IServiceProvider services, int maxPoolSize = 10) : IDisposable
{
    private readonly ConcurrentDictionary<string, PooledDatabase> _pool = new();
    private readonly int _maxPoolSize = maxPoolSize;  // ✅ Store parameter as field
    private bool _disposed;

    /// <summary>
    /// Represents a pooled database with reference counting.
    /// Modern C# 14 with init-only setters (required removed for simplicity).
    /// </summary>
    private sealed class PooledDatabase(IDatabase database, string connectionString)
    {
        public IDatabase Database { get; init; } = database;  // ✅ Remove required, use init with default
        public string ConnectionString { get; init; } = connectionString;
        public int ReferenceCount { get; set; }
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the maximum pool size.
    /// </summary>
    public int MaxPoolSize => _maxPoolSize;

    /// <summary>
    /// Gets or creates a database instance from the pool using a connection string.
    /// </summary>
    public IDatabase GetDatabase(string connectionString)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);  // ✅ C# 14: modern throw helper
        
        var builder = new ConnectionStringBuilder(connectionString);
        return GetDatabase(builder.DataSource, builder.Password, builder.ReadOnly);
    }

    /// <summary>
    /// Gets or creates a database instance from the pool.
    /// </summary>
    public IDatabase GetDatabase(string dbPath, string masterPassword, bool isReadOnly = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = $"{dbPath}|{isReadOnly}";

        var pooled = _pool.GetOrAdd(key, _ =>
        {
            var factory = services.GetRequiredService<DatabaseFactory>();
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
    public void ReturnDatabase(IDatabase? database)
    {
        if (_disposed || database is null) return;

        var pooled = _pool.Values.FirstOrDefault(p => p.Database == database);
        if (pooled is not null)
        {
            lock (pooled)
            {
                pooled.ReferenceCount = Math.Max(0, pooled.ReferenceCount - 1);
                pooled.LastUsed = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Clears unused database instances from the pool.
    /// </summary>
    public void ClearIdleConnections(TimeSpan? idleTimeout = null)
    {
        if (_disposed) return;

        var timeout = idleTimeout ?? TimeSpan.FromMinutes(5);
        var cutoff = DateTime.UtcNow - timeout;

        var keysToRemove = _pool
            .Where(kvp =>
            {
                lock (kvp.Value)
                {
                    return kvp.Value.ReferenceCount == 0 && kvp.Value.LastUsed < cutoff;
                }
            })
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _pool.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Gets the current pool size.
    /// </summary>
    public int PoolSize => _pool.Count;

    /// <summary>
    /// Gets statistics about the pool.
    /// </summary>
    public Dictionary<string, int> GetPoolStatistics() => new()  // ✅ C# 14: target-typed new
    {
        ["TotalConnections"] = _pool.Count,
        ["ActiveConnections"] = _pool.Values.Count(p => p.ReferenceCount > 0),
        ["IdleConnections"] = _pool.Values.Count(p => p.ReferenceCount == 0),
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _pool.Clear();
        GC.SuppressFinalize(this);
    }
}
