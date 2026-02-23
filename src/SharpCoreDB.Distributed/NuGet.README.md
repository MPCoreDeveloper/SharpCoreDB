# SharpCoreDB.Distributed

**Distributed Database Extension for SharpCoreDB**

[![NuGet](https://img.shields.io/badge/NuGet-1.4.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Distributed)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Overview

SharpCoreDB.Distributed extends SharpCoreDB with enterprise-scale distributed capabilities:

- **Horizontal Sharding**: Distribute data across multiple nodes
- **Replication**: Master-slave replication for high availability
- **Backup Strategies**: Automated backup and point-in-time recovery
- **Distributed Queries**: Cross-shard query execution
- **Load Balancing**: Intelligent request routing

## Quick Start

```csharp
using SharpCoreDB.Distributed;

// Configure sharding
var shardConfig = new ShardConfiguration
{
    ShardCount = 4,
    ShardKey = new HashShardKey("UserId"),
    ReplicasPerShard = 2
};

var distributedDb = new DistributedDatabase(shardConfig);

// Create distributed table
await distributedDb.ExecuteAsync(@"
    CREATE TABLE Users (
        UserId INT PRIMARY KEY,
        Name TEXT,
        Email TEXT
    ) SHARDED BY UserId
");

// Data is automatically distributed across shards
await distributedDb.ExecuteAsync(
    "INSERT INTO Users VALUES (1, 'Alice', 'alice@example.com')"
);
```

## Features

### Sharding Strategies
- **Hash Sharding**: Even distribution using hash functions
- **Range Sharding**: Partition by value ranges
- **List Sharding**: Explicit shard assignment

### Replication
- **Master-Slave**: Automatic replication to read replicas
- **Failover**: Automatic promotion of healthy replicas
- **Consistency**: Configurable consistency levels

### Backup & Recovery
- **Incremental Backups**: Efficient backup of changes only
- **Point-in-Time Recovery**: Restore to any point in time
- **Validation**: Automatic backup integrity checking

### Distributed Queries
- **Cross-Shard Joins**: Join data across shard boundaries
- **Aggregation**: Distributed GROUP BY and aggregate functions
- **Transactions**: Two-phase commit across shards

## Architecture

```
┌─────────────────┐    ┌─────────────────┐
│   Shard 1       │    │   Shard 2       │
│ ┌─────────────┐ │    │ ┌─────────────┐ │
│ │ Master DB   │ │    │ │ Master DB   │ │
│ │ └───────────┘ │    │ │ └───────────┘ │
│ │ ┌───────────┐ │    │ │ ┌───────────┐ │
│ │ │ Replica   │ │    │ │ │ Replica   │ │
│ │ └───────────┘ │    │ │ └───────────┘ │
│ └─────────────┘ │    │ └─────────────┘ │
└─────────────────┘    └─────────────────┘
         │                       │
         └───────────────────────┘
              Load Balancer
```

## Performance

- **100+ Shards**: Scale horizontally across multiple nodes
- **Sub-second Queries**: Cross-shard query performance
- **Zero Downtime**: Automatic failover and recovery
- **Minimal Overhead**: <1% performance impact for local operations

## Use Cases

- **High-Volume Applications**: Handle millions of users/transactions
- **Global Services**: Distribute data across geographic regions
- **Analytics Platforms**: Parallel processing across shards
- **IoT Systems**: Scale to handle massive sensor data streams

## Documentation

- [Distributed Architecture Guide](https://github.com/MPCoreDeveloper/SharpCoreDB/docs/distributed/)
- [Sharding Best Practices](https://github.com/MPCoreDeveloper/SharpCoreDB/docs/distributed/sharding.md)
- [Replication Setup](https://github.com/MPCoreDeveloper/SharpCoreDB/docs/distributed/replication.md)

## Requirements

- SharpCoreDB.Core v1.3.5+
- .NET 10.0+
- Network connectivity between shard nodes

## License

MIT License - see [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.
