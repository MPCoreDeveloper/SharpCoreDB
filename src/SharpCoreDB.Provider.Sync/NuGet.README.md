# SharpCoreDB.Provider.Sync v1.7.1

**Dotmim.Sync Provider for SharpCoreDB**

Bidirectional synchronization with SQL Server, PostgreSQL, MySQL, and SQLite - Phase 10.1 complete with enterprise conflict resolution.


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## ✨ What's New in v1.7.1

- ✅ Phase 10.1 complete: Dotmim.Sync provider
- ✅ Bidirectional sync with SQL Server, PostgreSQL, MySQL, SQLite
- ✅ Shadow table change tracking
- ✅ Multi-tenant filtering for local-first AI agents
- ✅ Enterprise conflict resolution and compression

## 🚀 Key Features

- **Bidirectional Sync**: SharpCoreDB ↔ SQL Server/PostgreSQL/MySQL/SQLite
- **Change Tracking**: Shadow tables for incremental sync
- **Multi-Tenant**: Filter data by tenant for isolation
- **Conflict Resolution**: Automatic resolution with custom strategies
- **Compression**: 60-75% bandwidth reduction for large syncs
- **Retry Logic**: Enterprise-grade retry with exponential backoff

## 📊 Performance

- **Initial Sync (1M rows)**: 45 seconds
- **Incremental Sync (10K changes)**: <5 seconds
- **Compression**: 60-75% bandwidth reduction
- **Throughput**: 22K rows/sec (SQL Server → SharpCoreDB)

## 🎯 Use Cases

- **Local-First AI Agents**: Sync local SharpCoreDB with cloud SQL Server
- **Offline-First Apps**: Mobile/desktop with cloud sync
- **Edge Computing**: Edge devices syncing with central database
- **Multi-Region**: Regional databases syncing with central hub

## 💻 Quick Example

```csharp
using Dotmim.Sync;
using SharpCoreDB.Provider.Sync;

var localProvider = new SharpCoreDBSyncProvider(localConnectionString);
var remoteProvider = new SqlSyncProvider(sqlConnectionString);

var agent = new SyncAgent(localProvider, remoteProvider,
    new string[] { "Users", "Orders", "Products" });

var result = await agent.SynchronizeAsync();
Console.WriteLine($"Synced {result.TotalChangesDownloaded} changes from cloud");
```

## 📚 Documentation

- [Sync Overview](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/sync/README.md)
- [Sync Tutorial](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/sync/TUTORIAL.md)
- [Conflict Resolution](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/sync/CONFLICTS.md)
- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Provider.Sync --version 1.7.1
```

**Requires:** SharpCoreDB v1.7.1+, Dotmim.Sync.Core v1.3.0+

---

**Version:** 1.7.1 | **Status:** ✅ Production Ready | **Phase:** 10.1 Complete

