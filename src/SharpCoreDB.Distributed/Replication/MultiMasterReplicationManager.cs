// <copyright file="MultiMasterReplicationManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SharpCoreDB.Distributed.Sharding;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Manages multi-master replication across multiple database nodes.
/// Supports concurrent writes to multiple masters with automatic conflict resolution.
/// C# 14: Primary constructors, Channel<T> for async coordination, pattern matching.
/// </summary>
public sealed class MultiMasterReplicationManager : IAsyncDisposable
{
    private readonly ShardManager _shardManager;
    private readonly ConflictResolver _conflictResolver;
    private readonly ILogger<MultiMasterReplicationManager>? _logger;

    private readonly Dictionary<string, MasterNode> _masterNodes = [];
    private readonly Dictionary<string, VectorClock> _vectorClocks = [];
    private readonly Lock _stateLock = new();

    private readonly Channel<ReplicationEvent> _eventChannel = Channel.CreateBounded<ReplicationEvent>(1000);
    private readonly CancellationTokenSource _cts = new();

    private Task? _eventProcessorTask;
    private bool _isRunning;

    /// <summary>Gets the number of active master nodes.</summary>
    public int MasterNodeCount => _masterNodes.Count;

    /// <summary>Gets all master node identifiers.</summary>
    public IReadOnlyCollection<string> MasterNodeIds => [.. _masterNodes.Keys];

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiMasterReplicationManager"/> class.
    /// </summary>
    /// <param name="shardManager">The shard manager for node coordination.</param>
    /// <param name="conflictResolver">The conflict resolver for handling conflicts.</param>
    /// <param name="logger">Optional logger for replication events.</param>
    public MultiMasterReplicationManager(
        ShardManager shardManager,
        ConflictResolver conflictResolver,
        ILogger<MultiMasterReplicationManager>? logger = null)
    {
        _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _logger = logger;
    }

    /// <summary>
    /// Starts the multi-master replication manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _eventProcessorTask = ProcessEventsAsync(_cts.Token);

        _logger?.LogInformation("Multi-master replication manager started");
    }

    /// <summary>
    /// Stops the multi-master replication manager.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _cts.Cancel();

        if (_eventProcessorTask is not null)
        {
            try
            {
                await _eventProcessorTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _logger?.LogInformation("Multi-master replication manager stopped");
    }

    /// <summary>
    /// Registers a master node for multi-master replication.
    /// </summary>
    /// <param name="nodeId">The unique node identifier.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="initialClock">The initial vector clock for the node.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RegisterMasterNodeAsync(string nodeId, string connectionString, VectorClock? initialClock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var masterNode = new MasterNode
        {
            NodeId = nodeId,
            ConnectionString = connectionString,
            Status = NodeStatus.Active,
            LastSeen = DateTimeOffset.UtcNow
        };

        lock (_stateLock)
        {
            if (_masterNodes.ContainsKey(nodeId))
            {
                throw new InvalidOperationException($"Master node '{nodeId}' already registered");
            }

            _masterNodes[nodeId] = masterNode;
            _vectorClocks[nodeId] = initialClock ?? new VectorClock();
        }

        await _eventChannel.Writer.WriteAsync(
            new ReplicationEvent(ReplicationEventType.NodeRegistered, nodeId),
            CancellationToken.None).ConfigureAwait(false);

        _logger?.LogInformation("Registered master node {NodeId}", nodeId);
    }

    /// <summary>
    /// Unregisters a master node.
    /// </summary>
    /// <param name="nodeId">The node identifier to unregister.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UnregisterMasterNodeAsync(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_stateLock)
        {
            if (!_masterNodes.Remove(nodeId))
            {
                throw new InvalidOperationException($"Master node '{nodeId}' not found");
            }

            _vectorClocks.Remove(nodeId);
        }

        await _eventChannel.Writer.WriteAsync(
            new ReplicationEvent(ReplicationEventType.NodeUnregistered, nodeId),
            CancellationToken.None).ConfigureAwait(false);

        _logger?.LogInformation("Unregistered master node {NodeId}", nodeId);
    }

    /// <summary>
    /// Processes a write operation from a master node.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <param name="operation">The write operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ProcessWriteOperationAsync(string nodeId, WriteOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(operation);

        // Update vector clock for this node
        VectorClock nodeClock;
        lock (_stateLock)
        {
            if (!_vectorClocks.TryGetValue(nodeId, out nodeClock!))
            {
                throw new InvalidOperationException($"Node '{nodeId}' not registered");
            }

            nodeClock = nodeClock.Increment(nodeId);
            _vectorClocks[nodeId] = nodeClock;
        }

        // Check for conflicts with other nodes
        var conflicts = await DetectConflictsAsync(operation, cancellationToken).ConfigureAwait(false);

        if (conflicts.Any())
        {
            await ResolveConflictsAsync(conflicts, cancellationToken).ConfigureAwait(false);
        }

        // Propagate the operation to other master nodes
        await PropagateOperationAsync(nodeId, operation, nodeClock, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Processed write operation from {NodeId}: {Operation}", nodeId, operation);
    }

    /// <summary>
    /// Gets the current vector clock for a node.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>The vector clock for the node.</returns>
    public VectorClock GetVectorClock(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_stateLock)
        {
            return _vectorClocks.TryGetValue(nodeId, out var clock) ? clock : new VectorClock();
        }
    }

    /// <summary>
    /// Gets the status of all master nodes.
    /// </summary>
    /// <returns>A dictionary of node statuses.</returns>
    public IReadOnlyDictionary<string, NodeStatus> GetNodeStatuses()
    {
        lock (_stateLock)
        {
            return _masterNodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Status);
        }
    }

    /// <summary>
    /// Detects conflicts for a write operation.
    /// </summary>
    /// <param name="operation">The write operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of detected conflicts.</returns>
    private async Task<IReadOnlyList<DataConflict>> DetectConflictsAsync(WriteOperation operation, CancellationToken cancellationToken)
    {
        // In a real implementation, this would check the operation against
        // recent operations from other nodes to detect conflicts
        // For now, return empty list (no conflicts)
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return [];
    }

    /// <summary>
    /// Resolves detected conflicts.
    /// </summary>
    /// <param name="conflicts">The conflicts to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ResolveConflictsAsync(IReadOnlyList<DataConflict> conflicts, CancellationToken cancellationToken)
    {
        foreach (var conflict in conflicts)
        {
            var resolution = _conflictResolver.ResolveConflict(conflict, ConflictResolutionStrategy.LastWriteWins);

            // Apply the resolution
            await ApplyConflictResolutionAsync(resolution, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Resolved conflict for {Table}.{Column}", conflict.TableName, conflict.ColumnName);
        }
    }

    /// <summary>
    /// Applies a conflict resolution.
    /// </summary>
    /// <param name="resolution">The conflict resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ApplyConflictResolutionAsync(ConflictResolution resolution, CancellationToken cancellationToken)
    {
        // In a real implementation, this would apply the resolved value
        // to all affected nodes
        await Task.Delay(25, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Propagates a write operation to other master nodes.
    /// </summary>
    /// <param name="sourceNodeId">The source node identifier.</param>
    /// <param name="operation">The operation to propagate.</param>
    /// <param name="vectorClock">The vector clock for the operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PropagateOperationAsync(string sourceNodeId, WriteOperation operation, VectorClock vectorClock, CancellationToken cancellationToken)
    {
        var targetNodes = _masterNodes.Keys.Where(id => id != sourceNodeId).ToList();

        foreach (var targetNodeId in targetNodes)
        {
            // In a real implementation, this would send the operation
            // to the target node via network communication
            await Task.Delay(50, cancellationToken).ConfigureAwait(false); // Simulate network latency

            _logger?.LogTrace("Propagated operation from {Source} to {Target}", sourceNodeId, targetNodeId);
        }
    }

    /// <summary>
    /// Processes replication events asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var @event in _eventChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessEventAsync(@event, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error processing replication event {EventType} for node {NodeId}",
                        @event.EventType, @event.NodeId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fatal error in replication event processor");
        }
    }

    /// <summary>
    /// Processes a replication event.
    /// </summary>
    /// <param name="event">The event to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task ProcessEventAsync(ReplicationEvent @event, CancellationToken cancellationToken)
    {
        // Handle node registration/unregistration events
        _logger?.LogDebug("Processed replication event {EventType} for node {NodeId}", @event.EventType, @event.NodeId);
        await Task.CompletedTask; // Placeholder
    }

    /// <summary>
    /// Disposes the multi-master replication manager.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _eventChannel.Writer.Complete();
    }
}

/// <summary>
/// Represents a master node in the multi-master replication topology.
/// </summary>
internal class MasterNode
{
    /// <summary>Gets the node identifier.</summary>
    public required string NodeId { get; init; }

    /// <summary>Gets the database connection string.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Gets or sets the node status.</summary>
    public NodeStatus Status { get; set; }

    /// <summary>Gets or sets the last seen timestamp.</summary>
    public DateTimeOffset LastSeen { get; set; }
}

/// <summary>
/// Node status.
/// </summary>
public enum NodeStatus
{
    /// <summary>Node is active and participating.</summary>
    Active,

    /// <summary>Node is temporarily unavailable.</summary>
    Unavailable,

    /// <summary>Node has been removed from the topology.</summary>
    Removed
}

/// <summary>
/// Replication event types.
/// </summary>
public enum ReplicationEventType
{
    /// <summary>Node registered.</summary>
    NodeRegistered,

    /// <summary>Node unregistered.</summary>
    NodeUnregistered,

    /// <summary>Write operation processed.</summary>
    WriteOperation,

    /// <summary>Conflict detected.</summary>
    ConflictDetected,

    /// <summary>Conflict resolved.</summary>
    ConflictResolved
}

/// <summary>
/// Replication event.
/// </summary>
internal class ReplicationEvent
{
    /// <summary>Gets the event type.</summary>
    public ReplicationEventType EventType { get; }

    /// <summary>Gets the node identifier.</summary>
    public string NodeId { get; }

    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplicationEvent"/> class.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="nodeId">The node identifier.</param>
    public ReplicationEvent(ReplicationEventType eventType, string nodeId)
    {
        EventType = eventType;
        NodeId = nodeId;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Write operation.
/// </summary>
public class WriteOperation
{
    /// <summary>Gets the table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Gets the operation type.</summary>
    public required OperationType OperationType { get; init; }

    /// <summary>Gets the primary key.</summary>
    public required object PrimaryKey { get; init; }

    /// <summary>Gets the data values.</summary>
    public required IReadOnlyDictionary<string, object?> Data { get; init; }

    /// <summary>Gets the timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Operation types.
/// </summary>
public enum OperationType
{
    /// <summary>Insert operation.</summary>
    Insert,

    /// <summary>Update operation.</summary>
    Update,

    /// <summary>Delete operation.</summary>
    Delete
}
