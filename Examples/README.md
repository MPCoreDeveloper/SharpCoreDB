# SharpCoreDB Examples

This directory contains practical examples for using SharpCoreDB in different scenarios.

## 📁 Directory Structure

```
examples/
├── CQRS/                          # CQRS examples
│   └── OrderManagement.CqrsDemo/  # Explicit CQRS Order Management demo
│       ├── Program.cs
│       ├── OrderCqrsDemo.cs
│       └── README.md
├── EventSourcing/                 # Event Sourcing examples
│   └── OrderManagement/           # Order Management System demo
│       ├── Program.cs
│       ├── OrderAggregate.cs
│       ├── OrderEvents.cs
│       └── README.md
├── sync/                          # Synchronization examples
│   ├── SyncExample.cs             # Basic Dotmim.Sync integration
│   └── CrossPlatformSyncExample.cs # Cross-platform sync (SQL Server, PostgreSQL, etc.)
└── README.md                      # This file
```

---

## 🎯 CQRS Examples

### Order Management - Explicit CQRS Demo
**Location:** `CQRS/OrderManagement.CqrsDemo/`  
**Status:** ✅ Complete  
**Level:** Intermediate

**Demonstrates:**
- Explicit command-side and query-side separation
- Command handlers via `SharpCoreDB.CQRS`
- Separate write model and read model
- Read projector updates after command processing
- Clear CQRS vs Event Sourcing comparison in output and README

**Features:**
- ✅ In-memory command dispatcher
- ✅ In-memory write repository
- ✅ In-memory read store + projector
- ✅ Query service on the read model
- ✅ Side-by-side explanation of differences with Event Sourcing

**How to run:**
```bash
cd examples/CQRS/OrderManagement.CqrsDemo
dotnet run
```

**See:** [CQRS Demo README](CQRS/OrderManagement.CqrsDemo/README.md) for detailed explanation.

---

## 🎯 Event Sourcing Examples

### Order Management System
**Location:** `EventSourcing/OrderManagement/`  
**Status:** ✅ Complete  
**Level:** Intermediate

**Demonstrates:**
- Complete event sourcing pattern with SharpCoreDB.EventSourcing
- Order aggregate with lifecycle management
- Event replay and state reconstruction
- Global event feed for projections
- Point-in-time queries (temporal queries)
- Per-stream sequence tracking

**Features:**
- ✅ Event-driven aggregate design
- ✅ Command/Event pattern
- ✅ Immutable events
- ✅ Complete audit trail
- ✅ Event versioning
- ✅ 5 demo scenarios

**How to run:**
```bash
cd examples/EventSourcing/OrderManagement
dotnet run
```

**See:** [Order Management README](EventSourcing/OrderManagement/README.md) for detailed explanation.

---

## 🔄 Synchronization Examples

### Basic Synchronization
**File:** `sync/SyncExample.cs`

Shows how to synchronize SharpCoreDB with SQL Server via Dotmim.Sync:

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using SharpCoreDB.Provider.Sync;

// Configure SharpCoreDB as the local provider
var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");

// Configure SQL Server as the remote provider
var sqlServerProvider = new SqlSyncProvider("Server=mssql;Database=sync;Trusted_Connection=True;");

// Create sync agent
var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);

// Define tables to sync
var tables = new string[] { "Users", "Orders", "Products" };

// Run bidirectional sync
var result = await agent.SynchronizeAsync(tables);

Console.WriteLine($"Updated: ↑{result.TotalChangesUploaded} ↓{result.TotalChangesDownloaded}");
```

### Cross-Platform Synchronization
**File:** `sync/CrossPlatformSyncExample.cs`

Demonstrates synchronization with multiple database types:

```csharp
// Sync with PostgreSQL
var postgresProvider = new PostgreSqlSyncProvider("Server=postgres;Database=sync;User Id=user;Password=pass;");
var pgAgent = new SyncAgent(sharpcoredbProvider, postgresProvider);
await pgAgent.SynchronizeAsync(tables);

// Sync with MySQL
var mysqlProvider = new MySqlSyncProvider("Server=mysql;Database=sync;Uid=user;Pwd=pass;");
var mysqlAgent = new SyncAgent(sharpcoredbProvider, mysqlProvider);
await mysqlAgent.SynchronizeAsync(tables);
```

## 🚀 How to Run

### 1. Install Dependencies

```bash
# For basic sync
dotnet add package SharpCoreDB.Provider.Sync
dotnet add package Dotmim.Sync.Core
dotnet add package Dotmim.Sync.SqlServer

# For cross-platform sync
dotnet add package Dotmim.Sync.PostgreSql
dotnet add package Dotmim.Sync.MySql
dotnet add package Dotmim.Sync.Sqlite
```

### 2. Create Example Project

```bash
# Create new console project
dotnet new console -n SharpCoreDBSyncExample
cd SharpCoreDBSyncExample

# Add dependencies
dotnet add package SharpCoreDB.Provider.Sync
dotnet add package Dotmim.Sync.Core
dotnet add package Dotmim.Sync.SqlServer

# Copy example code
# (Copy code from sync/SyncExample.cs)
```

### 3. Run

```bash
dotnet run
```

## 🎯 Use Cases

### 1. Local-First Architecture (AI Agents)
- SharpCoreDB for local storage
- Sync with cloud database for backup/sharing
- Offline-first capability

### 2. IoT Edge Computing
- SharpCoreDB on edge devices
- Periodic sync with central database
- Offline data buffering

### 3. Mobile Apps
- Local SharpCoreDB database
- Sync with backend on connectivity
- Conflict resolution for offline changes

### 4. Enterprise Data Warehousing
- Operational data in SharpCoreDB
- Sync with central data warehouse
- Support ETL processes

## 📋 Requirements

- **.NET 10** or higher
- **SharpCoreDB** v1.4.0+
- **Dotmim.Sync** v1.3.0+
- Database-specific providers (SQL Server, PostgreSQL, etc.)

## 🔧 Configuration

### Connection Strings

```csharp
// SharpCoreDB (local)
"Data Source=local.db"

// SQL Server
"Server=mssql;Database=sync;Trusted_Connection=True;"

// PostgreSQL
"Server=postgres;Database=sync;User Id=user;Password=pass;"

// MySQL
"Server=mysql;Database=sync;Uid=user;Pwd=pass;"
```

### Sync Options

```csharp
var options = new SyncOptions
{
    BatchSize = 1000,                    // Process in batches
    UseBulkOperations = true,            // Use bulk operations
    ConflictResolutionPolicy = ConflictResolutionPolicy.ServerWins,
    UseCompression = true,               // Compress data
    MaxRetries = 3                       // Retry logic
};
```

## 🐛 Troubleshooting

### Common Problems

#### Connection Timeouts
```csharp
// Increase timeouts
var options = new SyncOptions
{
    CommandTimeout = TimeSpan.FromMinutes(10),
    BulkCopyTimeout = TimeSpan.FromMinutes(15)
};
```

#### Large Datasets
```csharp
// Use smaller batches
var options = new SyncOptions
{
    BatchSize = 500,
    UseCompression = true
};
```

#### Conflicts
```csharp
// Configure conflict resolution
var options = new SyncOptions
{
    ConflictResolutionPolicy = ConflictResolutionPolicy.ClientWins
};
```

## 📚 More Information

- **[Dotmim.Sync Documentation](https://dotmim-sync.readthedocs.io/)**
- **[SharpCoreDB Sync Guide](docs/sync/README.md)**
- **[Distributed Features](docs/distributed/README.md)**

## 🤝 Contributing

Feel free to add more examples! Use the same structure:

1. Create a subdirectory for the use case (`examples/[category]/`)
2. Add the `[ExampleName].cs` file
3. Update this README
4. Test the example

**Examples belong in `examples/`, not in `src/`!**
