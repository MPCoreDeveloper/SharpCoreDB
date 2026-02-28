# SharpCoreDB.Distributed

**Distributed Database Extension for SharpCoreDB**

[![NuGet](https://img.shields.io/badge/NuGet-1.4.1-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Distributed)
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

# SharpCoreDB.Distributed v1.4.1

**Enterprise Distributed Database Features**

Multi-master replication, distributed transactions, and automatic sharding - Phase 10 complete with sub-100ms replication latency.

## ✨ What's New in v1.4.1

- ✅ Phase 10.2: Multi-master replication with vector clocks
- ✅ Phase 10.3: Distributed transactions with 2PC protocol
- ✅ Automatic conflict resolution (Last-Write-Wins, Merge, Custom)
- ✅ Horizontal sharding with automatic data distribution
- ✅ <100ms replication latency, 50K writes/sec throughput

## 🚀 Key Features

- **Multi-Master Replication**: Concurrent writes across nodes with vector clocks
- **Distributed Transactions**: 2PC protocol for ACID across shards
- **Horizontal Sharding**: Automatic data distribution and routing
- **Conflict Resolution**: LWW, merge, or custom strategies
- **Failover**: <5 second automatic failover
- **Monitoring**: Real-time replication health metrics

## 📊 Performance

- **Replication Latency**: <100ms across nodes
- **Throughput**: 50K writes/sec across 3 nodes
- **Conflict Resolution**: <100μs per conflict
- **Failover Time**: <5 seconds

## 🎯 Use Cases

- **Distributed Applications**: Multiple data centers, low-latency access
- **High Availability**: Survive node failures transparently
- **Geo-Distribution**: Serve users from closest region
- **Multi-Tenant**: Shard data by tenant for isolation

## 📚 Documentation

- [Distributed Overview](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/distributed/README.md)
- [Replication Guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/distributed/REPLICATION.md)
- [Transactions](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/distributed/TRANSACTIONS.md)
- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Distributed --version 1.4.1
```

**Requires:** SharpCoreDB v1.4.1+

---

**Version:** 1.4.1 | **Status:** ✅ Production Ready | **Phase:** 10 Complete
