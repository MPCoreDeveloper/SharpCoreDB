// <copyright file="ShardManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Sharding;

/// <summary>
/// Manages shard metadata and configuration for distributed SharpCoreDB.
/// C# 14: Primary constructors, collection expressions, modern patterns.
/// </summary>
public sealed class ShardManager
{
    private readonly Dictionary<string, ShardMetadata> _shards = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets the total number of shards.
    /// </summary>
    public int ShardCount => _shards.Count;

    /// <summary>
    /// Gets all shard identifiers.
    /// </summary>
    public IReadOnlyCollection<string> ShardIds => _shards.Keys;

    /// <summary>
    /// Registers a new shard with the manager.
    /// </summary>
    /// <param name="shardId">Unique shard identifier.</param>
    /// <param name="connectionString">Database connection string for the shard.</param>
    /// <param name="isMaster">Whether this is the master shard for replication.</param>
    /// <exception cref="ArgumentException">Thrown when shardId is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when shard already exists.</exception>
    public void RegisterShard(string shardId, string connectionString, bool isMaster = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        lock (_lock)
        {
            if (_shards.ContainsKey(shardId))
            {
                throw new InvalidOperationException($"Shard '{shardId}' already exists.");
            }

            var metadata = new ShardMetadata
            {
                ShardId = shardId,
                ConnectionString = connectionString,
                IsMaster = isMaster,
                Status = ShardStatus.Online,
                CreatedAt = DateTimeOffset.UtcNow,
                LastHeartbeat = DateTimeOffset.UtcNow
            };

            _shards[shardId] = metadata;
        }
    }

    /// <summary>
    /// Unregisters a shard from the manager.
    /// </summary>
    /// <param name="shardId">The shard identifier to remove.</param>
    /// <returns>True if the shard was removed, false if it didn't exist.</returns>
    public bool UnregisterShard(string shardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        lock (_lock)
        {
            return _shards.Remove(shardId);
        }
    }

    /// <summary>
    /// Gets metadata for a specific shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>The shard metadata, or null if not found.</returns>
    public ShardMetadata? GetShard(string shardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        lock (_lock)
        {
            return _shards.TryGetValue(shardId, out var metadata) ? metadata : null;
        }
    }

    /// <summary>
    /// Gets all shard metadata.
    /// </summary>
    /// <returns>Collection of all shard metadata.</returns>
    public IReadOnlyCollection<ShardMetadata> GetAllShards()
    {
        lock (_lock)
        {
            return [.. _shards.Values];
        }
    }

    /// <summary>
    /// Gets all master shards.
    /// </summary>
    /// <returns>Collection of master shard metadata.</returns>
    public IReadOnlyCollection<ShardMetadata> GetMasterShards()
    {
        lock (_lock)
        {
            return [.. _shards.Values.Where(s => s.IsMaster)];
        }
    }

    /// <summary>
    /// Gets all replica shards for a master.
    /// </summary>
    /// <param name="masterShardId">The master shard identifier.</param>
    /// <returns>Collection of replica shard metadata.</returns>
    public IReadOnlyCollection<ShardMetadata> GetReplicaShards(string masterShardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterShardId);

        lock (_lock)
        {
            return [.. _shards.Values.Where(s => !s.IsMaster && s.MasterShardId == masterShardId)];
        }
    }

    /// <summary>
    /// Updates the status of a shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="status">The new status.</param>
    /// <param name="errorMessage">Optional error message for offline status.</param>
    /// <returns>True if the status was updated, false if shard not found.</returns>
    public bool UpdateShardStatus(string shardId, ShardStatus status, string? errorMessage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        lock (_lock)
        {
            if (_shards.TryGetValue(shardId, out var metadata))
            {
                metadata.Status = status;
                metadata.LastHeartbeat = DateTimeOffset.UtcNow;

                if (status == ShardStatus.Offline && !string.IsNullOrEmpty(errorMessage))
                {
                    metadata.LastError = errorMessage;
                }

                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Updates the heartbeat timestamp for a shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>True if the heartbeat was updated, false if shard not found.</returns>
    public bool UpdateHeartbeat(string shardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        lock (_lock)
        {
            if (_shards.TryGetValue(shardId, out var metadata))
            {
                metadata.LastHeartbeat = DateTimeOffset.UtcNow;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets shards that are currently online.
    /// </summary>
    /// <returns>Collection of online shard metadata.</returns>
    public IReadOnlyCollection<ShardMetadata> GetOnlineShards()
    {
        lock (_lock)
        {
            return [.. _shards.Values.Where(s => s.Status == ShardStatus.Online)];
        }
    }

    /// <summary>
    /// Gets shards that are currently offline.
    /// </summary>
    /// <returns>Collection of offline shard metadata.</returns>
    public IReadOnlyCollection<ShardMetadata> GetOfflineShards()
    {
        lock (_lock)
        {
            return [.. _shards.Values.Where(s => s.Status == ShardStatus.Offline)];
        }
    }

    /// <summary>
    /// Checks if a shard exists and is online.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <returns>True if the shard exists and is online.</returns>
    public bool IsShardOnline(string shardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        lock (_lock)
        {
            return _shards.TryGetValue(shardId, out var metadata) &&
                   metadata.Status == ShardStatus.Online;
        }
    }

    /// <summary>
    /// Gets the total number of online shards.
    /// </summary>
    public int OnlineShardCount
    {
        get
        {
            lock (_lock)
            {
                return _shards.Values.Count(s => s.Status == ShardStatus.Online);
            }
        }
    }
}

/// <summary>
/// Represents metadata for a database shard.
/// </summary>
public class ShardMetadata
{
    /// <summary>Gets or sets the unique shard identifier.</summary>
    public required string ShardId { get; set; }

    /// <summary>Gets or sets the database connection string.</summary>
    public required string ConnectionString { get; set; }

    /// <summary>Gets or sets whether this is a master shard.</summary>
    public bool IsMaster { get; set; }

    /// <summary>Gets or sets the master shard ID (for replicas).</summary>
    public string? MasterShardId { get; set; }

    /// <summary>Gets or sets the current shard status.</summary>
    public ShardStatus Status { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the last heartbeat timestamp.</summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>Gets or sets the last error message (if any).</summary>
    public string? LastError { get; set; }

    /// <summary>Gets or sets the shard priority (higher = preferred).</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets the shard capacity (relative weight).</summary>
    public int Capacity { get; set; } = 1;
}

/// <summary>
/// Represents the status of a shard.
/// </summary>
public enum ShardStatus
{
    /// <summary>Shard is online and available.</summary>
    Online,

    /// <summary>Shard is offline or unreachable.</summary>
    Offline,

    /// <summary>Shard is in maintenance mode.</summary>
    Maintenance,

    /// <summary>Shard is being synchronized.</summary>
    Synchronizing
}
