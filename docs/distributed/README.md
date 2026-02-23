# SharpCoreDB.Distributed Documentation

## Overview

SharpCoreDB.Distributed provides enterprise-scale distributed database capabilities for SharpCoreDB, including:

- **Horizontal Sharding** - Distribute data across multiple database instances
- **Multi-Master Replication** - Concurrent writes with automatic synchronization
- **Conflict Resolution** - Automatic resolution of data conflicts
- **Distributed Transactions** - Two-phase commit across multiple nodes
- **Replication Monitoring** - Real-time health monitoring and metrics

## Key Features

### 🔄 Multi-Master Replication
- Vector clock-based causality tracking
- Automatic conflict detection and resolution
- Real-time synchronization between master nodes
- Support for concurrent writes across multiple locations

### 🏗️ Horizontal Sharding
- Automatic data distribution across shards
- Shard-aware query routing
- Dynamic shard addition/removal
- Load balancing and failover support

### ⚖️ Conflict Resolution
- **Last-Write-Wins** - Most recent change takes precedence
- **First-Write-Wins** - Earliest change takes precedence
- **Merge** - Compatible changes are combined
- **Custom** - User-defined resolution logic
- **Manual** - Human intervention required

### 📊 Distributed Transactions
- Two-phase commit protocol implementation
- Transaction recovery from failures
- Cross-shard consistency guarantees
- Timeout and deadlock prevention

## Quick Start

### 1. Basic Multi-Master Setup

```csharp
using SharpCoreDB.Distributed.Sharding;
using SharpCoreDB.Distributed.Replication;

// Configure shards
var shardManager = new ShardManager();
shardManager.RegisterShard("shard1", "Data Source=shard1.db");
shardManager.RegisterShard("shard2", "Data Source=shard2.db");

// Setup conflict resolution
var conflictResolver = new ConflictResolver();

// Create replication manager
var replicationManager = new MultiMasterReplicationManager(shardManager, conflictResolver);

// Register master nodes
await replicationManager.RegisterMasterNodeAsync("node1", "Data Source=node1.db");
await replicationManager.RegisterMasterNodeAsync("node2", "Data Source=node2.db");

// Start replication
await replicationManager.StartAsync();

// Perform distributed operations
await replicationManager.ProcessWriteOperationAsync("node1",
    new WriteOperation("Users", OperationType.Insert, 1,
        new Dictionary<string, object?> { ["Name"] = "Alice", ["Age"] = 28 }));
```

### 2. Distributed Transactions

```csharp
using SharpCoreDB.Distributed.Transactions;

// Create transaction manager
var transactionManager = new DistributedTransactionManager(shardManager);

// Begin distributed transaction
await transactionManager.BeginTransactionAsync("tx-123",
    ["shard1", "shard2"], // Participating shards
    IsolationLevel.ReadCommitted,
    TimeSpan.FromMinutes(5));

// Perform operations across shards
// (Operations would be executed here)

// Prepare for commit (Phase 1)
await transactionManager.PrepareTransactionAsync("tx-123");

// Commit transaction (Phase 2)
await transactionManager.CommitTransactionAsync("tx-123");
```

### 3. Monitoring and Health Checks

```csharp
// Create replication monitor
var monitor = new ReplicationMonitor();
await monitor.RegisterNodeAsync("node1");
await monitor.RegisterNodeAsync("node2");

// Record events
monitor.RecordEvent("node1", ReplicationEventType.WriteOperation);
monitor.RecordSyncLatency("node1", "node2", TimeSpan.FromMilliseconds(150));

// Get health metrics
var metrics = monitor.GetMetrics();
Console.WriteLine($"Healthy: {metrics.HealthStatus == ReplicationHealthStatus.Healthy}");
Console.WriteLine($"Total conflicts: {metrics.TotalConflicts}");
```

## Architecture

### Component Overview

```
┌─────────────────────────────────────────┐
│  Application Layer                      │
│  (Distributed API, Query Routing)       │
├─────────────────────────────────────────┤
│  Replication Layer                      │
│  (Multi-Master Sync, Conflict Resolution│
├─────────────────────────────────────────┤
│  Transaction Layer                      │
│  (2PC Protocol, Recovery)               │
├─────────────────────────────────────────┤
│  Sharding Layer                         │
│  (Data Distribution, Load Balancing)    │
├─────────────────────────────────────────┤
│  Storage Layer                          │
│  (SharpCoreDB Instances)                │
└─────────────────────────────────────────┘
```

### Key Classes

| Class | Purpose |
|-------|---------|
| `ShardManager` | Manages shard metadata and routing |
| `MultiMasterReplicationManager` | Handles multi-master replication |
| `ConflictResolver` | Resolves data conflicts |
| `DistributedTransactionManager` | Manages distributed transactions |
| `ReplicationMonitor` | Monitors replication health |
| `VectorClock` | Tracks causality in distributed systems |

## Configuration

### Shard Configuration

```csharp
var shardManager = new ShardManager();

// Register shards with connection strings
shardManager.RegisterShard("primary", "Data Source=primary.db");
shardManager.RegisterShard("replica1", "Data Source=replica1.db");
shardManager.RegisterShard("replica2", "Data Source=replica2.db");

// Configure shard routing rules
shardManager.AddRoutingRule("Users", ShardByHash("UserId"));
shardManager.AddRoutingRule("Orders", ShardByRange("OrderDate"));
```

### Replication Configuration

```csharp
var options = new ReplicationOptions
{
    ConflictResolutionStrategy = ConflictResolutionStrategy.LastWriteWins,
    SyncInterval = TimeSpan.FromSeconds(30),
    MaxRetries = 3,
    EnableCompression = true,
    BatchSize = 1000
};

var replicationManager = new MultiMasterReplicationManager(shardManager, conflictResolver, options);
```

## Best Practices

### 1. Conflict Resolution Strategy Selection

Choose the appropriate conflict resolution strategy based on your data:

- **Last-Write-Wins**: Good for user profile updates, sensor data
- **Merge**: Suitable for additive operations (counters, sets)
- **Custom**: For business logic-specific resolution
- **Manual**: For critical data requiring human review

### 2. Shard Key Design

- Choose shard keys that distribute data evenly
- Avoid hotspots by using high-cardinality keys
- Consider query patterns for shard key selection
- Plan for shard splitting/growth

### 3. Monitoring Setup

```csharp
// Enable comprehensive monitoring
var monitor = new ReplicationMonitor(logger);
await monitor.StartAsync();

// Set up alerts for critical metrics
monitor.OnHealthDegraded += (nodeId, issues) =>
{
    logger.LogWarning("Replication health degraded for {Node}: {Issues}", nodeId, issues);
    // Send alerts, trigger failover, etc.
};
```

### 4. Performance Optimization

- Use batch operations for bulk sync
- Enable compression for network transfer
- Monitor and tune sync intervals
- Implement connection pooling

## Troubleshooting

### Common Issues

#### High Conflict Rate
- Review conflict resolution strategy
- Check for concurrent updates to same data
- Consider application-level coordination

#### Sync Latency Issues
- Verify network connectivity
- Check system resource usage
- Adjust batch sizes and intervals

#### Transaction Timeouts
- Review transaction scope
- Check for long-running operations
- Adjust timeout values appropriately

### Diagnostic Tools

```csharp
// Get detailed replication status
var status = await replicationManager.GetReplicationStatusAsync();
foreach (var node in status.Nodes)
{
    Console.WriteLine($"Node {node.Id}: {node.Status}, Lag: {node.ReplicationLag}");
}

// Analyze conflict patterns
var conflicts = await monitor.GetConflictAnalysisAsync();
foreach (var pattern in conflicts.FrequentConflicts)
{
    Console.WriteLine($"Frequent conflict: {pattern.Table}.{pattern.Column}");
}
```

## Migration Guide

### From Single-Node to Distributed

1. **Assess Data Distribution Needs**
   - Analyze query patterns
   - Identify shard keys
   - Plan shard topology

2. **Setup Shard Infrastructure**
   ```csharp
   // Create shard manager
   var shardManager = new ShardManager();

   // Register existing database as first shard
   shardManager.RegisterShard("shard1", existingConnectionString);
   ```

3. **Enable Replication**
   ```csharp
   // Add replication capabilities
   var replicationManager = new MultiMasterReplicationManager(shardManager, conflictResolver);
   await replicationManager.StartAsync();
   ```

4. **Migrate Data**
   - Use data migration tools
   - Validate data consistency
   - Update application code

## API Reference

### ShardManager

```csharp
public class ShardManager
{
    void RegisterShard(string shardId, string connectionString);
    void UnregisterShard(string shardId);
    string GetShardForKey(string table, object key);
    IReadOnlyCollection<string> GetAllShardIds();
}
```

### MultiMasterReplicationManager

```csharp
public class MultiMasterReplicationManager
{
    Task RegisterMasterNodeAsync(string nodeId, string connectionString);
    Task ProcessWriteOperationAsync(string nodeId, WriteOperation operation);
    Task<ReplicationStatus> GetReplicationStatusAsync();
}
```

### ConflictResolver

```csharp
public class ConflictResolver
{
    ConflictResolution ResolveConflict(DataConflict conflict, ConflictResolutionStrategy strategy);
    bool CanAutoResolve(DataConflict conflict, ConflictResolutionStrategy strategy);
}
```

## Performance Metrics

| Operation | Performance | Notes |
|-----------|-------------|-------|
| **Conflict Resolution** | <1ms per conflict | Depends on strategy complexity |
| **Vector Clock Update** | <0.1ms | Lightweight causality tracking |
| **Shard Routing** | <0.05ms | Hash-based routing |
| **Transaction Prepare** | 1-10ms | Network-dependent |
| **Replication Sync** | 10-1000ms | Batch size dependent |

## Security Considerations

- Encrypt data in transit between nodes
- Use authentication for node-to-node communication
- Implement access controls for shard operations
- Monitor for unauthorized replication attempts
- Regular security audits of distributed setup

## Support

For issues and questions:
- [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- [Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/tree/master/docs)
- [Contributing Guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/CONTRIBUTING.md)
