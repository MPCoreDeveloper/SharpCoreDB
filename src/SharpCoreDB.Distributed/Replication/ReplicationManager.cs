// <copyright file="ReplicationManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Distributed.Sharding;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Main coordinator for database replication across distributed nodes.
/// C# 14: Primary constructors, async streams, Channel<T> for coordination.
/// </summary>
public sealed class ReplicationManager : IAsyncDisposable
{
    private readonly ShardManager _shardManager;
    private readonly ILogger<ReplicationManager>? _logger;

    private readonly Dictionary<string, ReplicationState> _replicationStates = [];
    private readonly Dictionary<string, Task> _replicationTasks = [];
    private readonly Lock _lock = new();

    private readonly Channel<ReplicationCommand> _commandChannel = Channel.CreateBounded<ReplicationCommand>(100);
    private readonly CancellationTokenSource _cts = new();

    private Task? _commandProcessorTask;
    private bool _isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplicationManager"/> class.
    /// </summary>
    /// <param name="shardManager">The shard manager.</param>
    /// <param name="logger">Optional logger.</param>
    public ReplicationManager(ShardManager shardManager, ILogger<ReplicationManager>? logger = null)
    {
        _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
        _logger = logger;
    }

    /// <summary>
    /// Starts the replication manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
        }

        _logger?.LogInformation("Starting replication manager");

        // Start command processor
        _commandProcessorTask = ProcessCommandsAsync(_cts.Token);

        // Initialize replication for existing master-replica pairs
        await InitializeExistingReplicationsAsync(cancellationToken);

        _logger?.LogInformation("Replication manager started");
    }

    /// <summary>
    /// Stops the replication manager.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
        }

        _logger?.LogInformation("Stopping replication manager");

        _cts.Cancel();

        // Stop all replication tasks
        var stopTasks = new List<Task>();
        lock (_lock)
        {
            foreach (var task in _replicationTasks.Values)
            {
                stopTasks.Add(task);
            }
        }

        await Task.WhenAll(stopTasks);

        // Wait for command processor to stop
        if (_commandProcessorTask is not null)
        {
            await _commandProcessorTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        _logger?.LogInformation("Replication manager stopped");
    }

    /// <summary>
    /// Adds a replica for replication from a master.
    /// </summary>
    /// <param name="masterNodeId">The master node identifier.</param>
    /// <param name="replicaNodeId">The replica node identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AddReplicaAsync(string masterNodeId, string replicaNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaNodeId);

        var command = new ReplicationCommand
        {
            Type = ReplicationCommandType.AddReplica,
            MasterNodeId = masterNodeId,
            ReplicaNodeId = replicaNodeId
        };

        await _commandChannel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <summary>
    /// Removes a replica from replication.
    /// </summary>
    /// <param name="masterNodeId">The master node identifier.</param>
    /// <param name="replicaNodeId">The replica node identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveReplicaAsync(string masterNodeId, string replicaNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaNodeId);

        var command = new ReplicationCommand
        {
            Type = ReplicationCommandType.RemoveReplica,
            MasterNodeId = masterNodeId,
            ReplicaNodeId = replicaNodeId
        };

        await _commandChannel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <summary>
    /// Promotes a replica to master.
    /// </summary>
    /// <param name="replicaNodeId">The replica node to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task PromoteReplicaAsync(string replicaNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaNodeId);

        var command = new ReplicationCommand
        {
            Type = ReplicationCommandType.PromoteReplica,
            ReplicaNodeId = replicaNodeId
        };

        await _commandChannel.Writer.WriteAsync(command, cancellationToken);
    }

    /// <summary>
    /// Gets the replication state for a master-replica pair.
    /// </summary>
    /// <param name="masterNodeId">The master node identifier.</param>
    /// <param name="replicaNodeId">The replica node identifier.</param>
    /// <returns>The replication state, or null if not found.</returns>
    public ReplicationState? GetReplicationState(string masterNodeId, string replicaNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaNodeId);

        var key = CreateReplicationKey(masterNodeId, replicaNodeId);

        lock (_lock)
        {
            return _replicationStates.TryGetValue(key, out var state) ? state : null;
        }
    }

    /// <summary>
    /// Gets all replication states.
    /// </summary>
    /// <returns>Collection of all replication states.</returns>
    public IReadOnlyCollection<ReplicationState> GetAllReplicationStates()
    {
        lock (_lock)
        {
            return [.. _replicationStates.Values];
        }
    }

    /// <summary>
    /// Gets replication statistics for all pairs.
    /// </summary>
    /// <returns>Collection of replication statistics.</returns>
    public IReadOnlyCollection<ReplicationStatistics> GetReplicationStatistics()
    {
        lock (_lock)
        {
            return [.. _replicationStates.Values.Select(s => s.GetStatistics())];
        }
    }

    /// <summary>
    /// Processes replication commands asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessCommandsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var command in _commandChannel.Reader.ReadAllAsync(cancellationToken))
            {
                await ProcessCommandAsync(command, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing replication commands");
        }
    }

    /// <summary>
    /// Processes a single replication command.
    /// </summary>
    /// <param name="command">The command to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessCommandAsync(ReplicationCommand command, CancellationToken cancellationToken)
    {
        try
        {
            switch (command.Type)
            {
                case ReplicationCommandType.AddReplica:
                    await AddReplicaInternalAsync(command.MasterNodeId!, command.ReplicaNodeId!, cancellationToken);
                    break;

                case ReplicationCommandType.RemoveReplica:
                    await RemoveReplicaInternalAsync(command.MasterNodeId!, command.ReplicaNodeId!, cancellationToken);
                    break;

                case ReplicationCommandType.PromoteReplica:
                    await PromoteReplicaInternalAsync(command.ReplicaNodeId!, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing replication command {CommandType}", command.Type);
        }
    }

    /// <summary>
    /// Adds a replica internally.
    /// </summary>
    /// <param name="masterNodeId">The master node identifier.</param>
    /// <param name="replicaNodeId">The replica node identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddReplicaInternalAsync(string masterNodeId, string replicaNodeId, CancellationToken cancellationToken)
    {
        var key = CreateReplicationKey(masterNodeId, replicaNodeId);

        lock (_lock)
        {
            if (_replicationStates.ContainsKey(key))
            {
                _logger?.LogWarning("Replication already exists for {Master} -> {Replica}", masterNodeId, replicaNodeId);
                return;
            }

            var state = new ReplicationState(masterNodeId, replicaNodeId);
            _replicationStates[key] = state;

            // Start replication task
            var replicationTask = StartReplicationAsync(state, cancellationToken);
            _replicationTasks[key] = replicationTask;
        }

        _logger?.LogInformation("Added replica {Replica} for master {Master}", replicaNodeId, masterNodeId);
    }

    /// <summary>
    /// Removes a replica internally.
    /// </summary>
    /// <param name="masterNodeId">The master node identifier.</param>
    /// <param name="replicaNodeId">The replica node identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task RemoveReplicaInternalAsync(string masterNodeId, string replicaNodeId, CancellationToken cancellationToken)
    {
        var key = CreateReplicationKey(masterNodeId, replicaNodeId);

        Task? replicationTask = null;
        lock (_lock)
        {
            if (_replicationStates.Remove(key))
            {
                if (_replicationTasks.Remove(key, out replicationTask))
                {
                    // Cancel the replication task
                    // Note: In a real implementation, we'd need a way to cancel individual tasks
                }
            }
        }

        if (replicationTask is not null)
        {
            try
            {
                await replicationTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("Replication task for {Master} -> {Replica} did not stop within timeout", masterNodeId, replicaNodeId);
            }
        }

        _logger?.LogInformation("Removed replica {Replica} from master {Master}", replicaNodeId, masterNodeId);
    }

    /// <summary>
    /// Promotes a replica to master internally.
    /// </summary>
    /// <param name="replicaNodeId">The replica node to promote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PromoteReplicaInternalAsync(string replicaNodeId, CancellationToken cancellationToken)
    {
        // Find all replication states where this node is a replica
        var statesToUpdate = new List<ReplicationState>();

        lock (_lock)
        {
            foreach (var state in _replicationStates.Values)
            {
                if (state.ReplicaNodeId == replicaNodeId)
                {
                    statesToUpdate.Add(state);
                }
            }
        }

        foreach (var state in statesToUpdate)
        {
            // Stop replication from old master
            await RemoveReplicaInternalAsync(state.MasterNodeId, replicaNodeId, cancellationToken);

            // Update shard manager to reflect role change
            // Note: This would need integration with ShardManager to update roles
            _logger?.LogInformation("Promoted replica {Replica} to master (was replicating from {OldMaster})",
                replicaNodeId, state.MasterNodeId);
        }
    }

    /// <summary>
    /// Starts replication for a master-replica pair.
    /// </summary>
    /// <param name="state">The replication state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the replication operation.</returns>
    private async Task StartReplicationAsync(ReplicationState state, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Starting replication from {Master} to {Replica}",
            state.MasterNodeId, state.ReplicaNodeId);

        try
        {
            state.ChangeState(ReplicationProtocol.ReplicationState.Starting);

            // TODO: Implement actual replication logic
            // - Connect to master
            // - Perform handshake
            // - Start WAL streaming
            // - Handle acknowledgments

            // For now, simulate replication
            state.ChangeState(ReplicationProtocol.ReplicationState.Streaming);

            // Keep the task alive (in real implementation, this would be the streaming loop)
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Replication failed for {Master} -> {Replica}",
                state.MasterNodeId, state.ReplicaNodeId);
            state.RecordFailure(ex.Message);
        }
    }

    /// <summary>
    /// Initializes replication for existing master-replica pairs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task InitializeExistingReplicationsAsync(CancellationToken cancellationToken)
    {
        var masters = _shardManager.GetMasterShards();

        foreach (var master in masters)
        {
            var replicas = _shardManager.GetReplicaShards(master.ShardId);

            foreach (var replica in replicas)
            {
                await AddReplicaInternalAsync(master.ShardId, replica.ShardId, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Creates a unique key for a master-replica pair.
    /// </summary>
    /// <param name="masterNodeId">The master node identifier.</param>
    /// <param name="replicaNodeId">The replica node identifier.</param>
    /// <returns>The replication key.</returns>
    private static string CreateReplicationKey(string masterNodeId, string replicaNodeId)
    {
        return $"{masterNodeId}->{replicaNodeId}";
    }

    /// <summary>
    /// Disposes the replication manager asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
        _commandChannel.Writer.Complete();
    }
}

/// <summary>
/// Replication command types.
/// </summary>
internal enum ReplicationCommandType
{
    AddReplica,
    RemoveReplica,
    PromoteReplica
}

/// <summary>
/// Internal replication command.
/// </summary>
internal class ReplicationCommand
{
    public ReplicationCommandType Type { get; init; }
    public string? MasterNodeId { get; init; }
    public string? ReplicaNodeId { get; init; }
}
