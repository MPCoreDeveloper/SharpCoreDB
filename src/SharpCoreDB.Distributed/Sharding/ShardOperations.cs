// <copyright file="ShardOperations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Sharding;

/// <summary>
/// Provides basic operations for managing database shards.
/// C# 14: Async methods, collection expressions, modern error handling.
/// </summary>
public sealed class ShardOperations
{
    private readonly ShardManager _shardManager;
    private readonly ShardRouter _shardRouter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardOperations"/> class.
    /// </summary>
    /// <param name="shardManager">The shard manager.</param>
    /// <param name="shardRouter">The shard router.</param>
    public ShardOperations(ShardManager shardManager, ShardRouter shardRouter)
    {
        _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
        _shardRouter = shardRouter ?? throw new ArgumentNullException(nameof(shardRouter));
    }

    /// <summary>
    /// Creates a new shard with the specified configuration.
    /// </summary>
    /// <param name="shardId">Unique shard identifier.</param>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="isMaster">Whether this is a master shard.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CreateShardAsync(string shardId, string connectionString, bool isMaster = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Register the shard
        _shardManager.RegisterShard(shardId, connectionString, isMaster);

        // Test the connection
        await TestShardConnectionAsync(shardId, cancellationToken);

        // Initialize the shard (create necessary tables, indexes, etc.)
        await InitializeShardAsync(shardId, cancellationToken);
    }

    /// <summary>
    /// Removes a shard from the cluster.
    /// </summary>
    /// <param name="shardId">The shard identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveShardAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = _shardManager.GetShard(shardId);
        if (shard is null)
        {
            throw new InvalidOperationException($"Shard '{shardId}' not found.");
        }

        // Mark shard as offline
        _shardManager.UpdateShardStatus(shardId, ShardStatus.Offline, "Removing shard");

        // TODO: Implement data migration to other shards if needed
        // await MigrateShardDataAsync(shardId, targetShardId, cancellationToken);

        // Unregister the shard
        _shardManager.UnregisterShard(shardId);
    }

    /// <summary>
    /// Tests connectivity to a shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task TestShardConnectionAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = _shardManager.GetShard(shardId);
        if (shard is null)
        {
            throw new InvalidOperationException($"Shard '{shardId}' not found.");
        }

        try
        {
            // TODO: Implement actual database connection test
            // For now, just simulate a connection test
            await Task.Delay(100, cancellationToken); // Simulate network latency

            _shardManager.UpdateShardStatus(shardId, ShardStatus.Online);
        }
        catch (Exception ex)
        {
            _shardManager.UpdateShardStatus(shardId, ShardStatus.Offline, ex.Message);
            throw new ShardConnectionException($"Failed to connect to shard '{shardId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Initializes a shard with necessary schema and data.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeShardAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = _shardManager.GetShard(shardId);
        if (shard is null)
        {
            throw new InvalidOperationException($"Shard '{shardId}' not found.");
        }

        // TODO: Implement shard initialization
        // - Create shared schema tables
        // - Initialize shard-specific metadata
        // - Set up indexes and constraints

        await Task.Delay(200, cancellationToken); // Simulate initialization time
    }

    /// <summary>
    /// Gets the health status of all shards.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with health status.</returns>
    public async Task<ShardHealthStatus> GetShardHealthAsync(CancellationToken cancellationToken = default)
    {
        var allShards = _shardManager.GetAllShards();
        var healthChecks = new List<Task<ShardHealthCheck>>();

        foreach (var shard in allShards)
        {
            healthChecks.Add(CheckShardHealthAsync(shard.ShardId, cancellationToken));
        }

        var results = await Task.WhenAll(healthChecks);

        return new ShardHealthStatus
        {
            TotalShards = allShards.Count,
            HealthyShards = results.Count(r => r.IsHealthy),
            UnhealthyShards = results.Count(r => !r.IsHealthy),
            ShardHealthChecks = [.. results]
        };
    }

    /// <summary>
    /// Checks the health of a specific shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with health check result.</returns>
    public async Task<ShardHealthCheck> CheckShardHealthAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = _shardManager.GetShard(shardId);
        if (shard is null)
        {
            return new ShardHealthCheck
            {
                ShardId = shardId,
                IsHealthy = false,
                Status = "Not Found",
                LastChecked = DateTimeOffset.UtcNow
            };
        }

        try
        {
            // Test basic connectivity
            await TestShardConnectionAsync(shardId, cancellationToken);

            // TODO: Implement more comprehensive health checks
            // - Query performance metrics
            // - Disk space availability
            // - Replication lag (for replicas)

            return new ShardHealthCheck
            {
                ShardId = shardId,
                IsHealthy = true,
                Status = "Healthy",
                LastChecked = DateTimeOffset.UtcNow,
                ResponseTime = TimeSpan.FromMilliseconds(50) // Simulated
            };
        }
        catch (Exception ex)
        {
            return new ShardHealthCheck
            {
                ShardId = shardId,
                IsHealthy = false,
                Status = $"Unhealthy: {ex.Message}",
                LastChecked = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Balances data across shards by redistributing based on current load.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RebalanceShardsAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement intelligent rebalancing
        // - Analyze current data distribution
        // - Identify overloaded shards
        // - Migrate data to underutilized shards
        // - Update routing tables

        await Task.Delay(1000, cancellationToken); // Simulate rebalancing time
    }

    /// <summary>
    /// Migrates data from one shard to another.
    /// </summary>
    /// <param name="sourceShardId">The source shard identifier.</param>
    /// <param name="targetShardId">The target shard identifier.</param>
    /// <param name="tableName">The table name to migrate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task MigrateTableAsync(string sourceShardId, string targetShardId, string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceShardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetShardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        // TODO: Implement table migration
        // - Export data from source shard
        // - Import data to target shard
        // - Update routing metadata
        // - Verify data integrity

        await Task.Delay(500, cancellationToken); // Simulate migration time
    }

    /// <summary>
    /// Gets statistics about shard operations.
    /// </summary>
    /// <returns>Shard operation statistics.</returns>
    public ShardOperationStats GetOperationStats()
    {
        return new ShardOperationStats
        {
            TotalShards = _shardManager.ShardCount,
            OnlineShards = _shardManager.OnlineShardCount,
            RoutingStats = _shardRouter.GetRoutingStats()
        };
    }
}

/// <summary>
/// Represents the health status of all shards.
/// </summary>
public class ShardHealthStatus
{
    /// <summary>Gets the total number of shards.</summary>
    public int TotalShards { get; init; }

    /// <summary>Gets the number of healthy shards.</summary>
    public int HealthyShards { get; init; }

    /// <summary>Gets the number of unhealthy shards.</summary>
    public int UnhealthyShards { get; init; }

    /// <summary>Gets the individual shard health checks.</summary>
    public IReadOnlyCollection<ShardHealthCheck> ShardHealthChecks { get; init; } = [];
}

/// <summary>
/// Represents the health check result for a single shard.
/// </summary>
public class ShardHealthCheck
{
    /// <summary>Gets the shard identifier.</summary>
    public string ShardId { get; init; } = string.Empty;

    /// <summary>Gets whether the shard is healthy.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Gets the health status description.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the timestamp of the last health check.</summary>
    public DateTimeOffset LastChecked { get; init; }

    /// <summary>Gets the response time for the health check.</summary>
    public TimeSpan? ResponseTime { get; init; }

    /// <summary>Gets the error message if the shard is unhealthy.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Statistics for shard operations.
/// </summary>
public class ShardOperationStats
{
    /// <summary>Gets the total number of configured shards.</summary>
    public int TotalShards { get; init; }

    /// <summary>Gets the number of online shards.</summary>
    public int OnlineShards { get; init; }

    /// <summary>Gets the routing statistics.</summary>
    public ShardRoutingStats RoutingStats { get; init; } = new();
}

/// <summary>
/// Exception thrown when shard connection fails.
/// </summary>
public class ShardConnectionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShardConnectionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ShardConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
