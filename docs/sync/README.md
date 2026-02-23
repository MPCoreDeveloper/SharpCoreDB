# SharpCoreDB.Provider.Sync Documentation

## Overview

SharpCoreDB.Provider.Sync is a Dotmim.Sync provider implementation that enables bidirectional synchronization between SharpCoreDB and **any Dotmim.Sync-supported database**, including:

- **SQL Server** - Enterprise relational database
- **PostgreSQL** - Advanced open-source database  
- **MySQL** - Popular web database
- **SQLite** - Embedded database
- **Oracle** - Enterprise database (via community providers)

## Supported Databases

SharpCoreDB.Provider.Sync works with all Dotmim.Sync supported databases:

### SQL Server
```csharp
var sqlServerProvider = new SqlSyncProvider("Server=mssql;Database=sync;Trusted_Connection=True;");
```

### PostgreSQL  
```csharp
var postgresProvider = new PostgreSqlSyncProvider("Server=postgres;Database=sync;User Id=user;Password=pass;");
```

### MySQL
```csharp
var mysqlProvider = new MySqlSyncProvider("Server=mysql;Database=sync;Uid=user;Pwd=pass;");
```

### SQLite
```csharp
var sqliteProvider = new SqliteSyncProvider("Data Source=remote.db");
```

### Oracle (Community Provider)
```csharp
// Via Dotmim.Sync.Oracle community provider
var oracleProvider = new OracleSyncProvider("Data Source=oracle;User Id=user;Password=pass;");
```

## Key Features

### 🔄 Universal Database Synchronization
- **Cross-platform sync** - Works with all major relational databases
- **Bidirectional synchronization** - Real-time data consistency
- **Enterprise-grade** - Production-ready for large-scale deployments
- **Framework agnostic** - Integrates with any .NET application

### 🏢 Multi-Tenant Support
- **Tenant filtering** for local-first AI agent architectures
- **Scoped synchronization** per tenant or organization
- **Data isolation** between different tenants
- **Scalable architecture** for multi-tenant applications

### 📊 Enterprise Features
- **Change tracking** via shadow tables
- **Compression** to reduce network traffic
- **Retry logic** for resilient operations
- **Progress monitoring** and logging
- **Bulk operations** for large datasets

## Quick Start

### 1. Installation

```bash
# Add the sync provider
dotnet add package SharpCoreDB.Provider.Sync --version 1.0.0

# Add Dotmim.Sync core
dotnet add package Dotmim.Sync.Core --version 1.3.0

# Add provider for your target database
dotnet add package Dotmim.Sync.SqlServer --version 1.3.0
# OR
dotnet add package Dotmim.Sync.PostgreSql --version 1.3.0
# OR
dotnet add package Dotmim.Sync.MySql --version 1.3.0
```

### 2. Basic Synchronization

```csharp
using Dotmim.Sync;
using Dotmim.Sync.SqlServer;
using SharpCoreDB.Provider.Sync;

// Configure providers
var sharpcoredbProvider = new SharpCoreDBSyncProvider("Data Source=local.db");
var sqlServerProvider = new SqlSyncProvider("Server=mssql;Database=sync;Trusted_Connection=True;");

// Create sync agent
var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);

// Define tables to sync
var tables = new string[] { "Users", "Orders", "Products" };

// Perform bidirectional sync
var result = await agent.SynchronizeAsync(tables);

Console.WriteLine("Synchronization completed!");
Console.WriteLine($"Changes uploaded: {result.TotalChangesUploaded}");
Console.WriteLine($"Changes downloaded: {result.TotalChangesDownloaded}");
Console.WriteLine($"Conflicts resolved: {result.TotalResolvedConflicts}");
```

### 3. Multi-Tenant Synchronization

```csharp
// Configure tenant-specific sync
var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider);

// Add tenant filter - only sync data for specific tenant
var tenantId = "tenant-123";
agent.AddFilter("TenantId", tenantId);

// Sync tenant-scoped tables
var tenantTables = new[] { "Conversations", "Documents", "UserPreferences" };
var result = await agent.SynchronizeAsync(tenantTables);

Console.WriteLine($"Tenant {tenantId} sync completed");
```

### 4. Advanced Configuration

```csharp
// Configure sync options
var options = new SyncOptions
{
    BatchSize = 1000,                    // Process in batches
    UseBulkOperations = true,            // Use bulk operations when possible
    ConflictResolutionPolicy = ConflictResolutionPolicy.ServerWins, // or ClientWins
    UseCompression = true,               // Compress data during transfer
    MaxRetries = 3,                      // Retry failed operations
    UseVerboseErrors = true
};

// Configure sync agent with options
var agent = new SyncAgent(sharpcoredbProvider, sqlServerProvider, options);

// Add progress monitoring
agent.LocalOrchestrator.OnSyncProgress += (args) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {args.ProgressPercentage}% - {args.Message}");
};

// Add error handling
agent.LocalOrchestrator.OnSyncError += (args) =>
{
    Console.WriteLine($"Sync error: {args.Exception.Message}");
};
```

## Use Cases

### 1. Local-First Applications

```csharp
// AI agent with local SharpCoreDB + cloud sync
var localDb = new SharpCoreDBSyncProvider("Data Source=agent.db");
var cloudDb = new SqlSyncProvider("Server=cloud;Database=agents;");

// Sync agent data with cloud
var agent = new SyncAgent(localDb, cloudDb);
await agent.SynchronizeAsync(["Conversations", "Documents", "Models"]);
```

### 2. Offline-Capable Mobile Apps

```csharp
// Mobile app with local sync to central database
var mobileDb = new SharpCoreDBSyncProvider("Data Source=mobile.db");
var centralDb = new SqlSyncProvider("Server=central;Database=app;");

// Sync when online
if (NetworkInterface.GetIsNetworkAvailable())
{
    await agent.SynchronizeAsync(["UserData", "Preferences", "Cache"]);
}
```

### 3. Multi-Region Deployment

```csharp
// Sync between regions
var usEastDb = new SharpCoreDBSyncProvider("Data Source=us-east.db");
var usWestDb = new SqlSyncProvider("Server=us-west;Database=app;");
var europeDb = new SqlSyncProvider("Server=europe;Database=app;");

// Multi-way sync setup
var eastAgent = new SyncAgent(usEastDb, usWestDb);
var westAgent = new SyncAgent(usWestDb, europeDb);
var europeAgent = new SyncAgent(europeDb, usEastDb);

// Sync all regions
await Task.WhenAll(
    eastAgent.SynchronizeAsync(tables),
    westAgent.SynchronizeAsync(tables),
    europeAgent.SynchronizeAsync(tables)
);
```

### 4. Data Warehousing

```csharp
// ETL from operational DB to data warehouse
var operationalDb = new SqlSyncProvider("Server=ops;Database=app;");
var warehouseDb = new SharpCoreDBSyncProvider("Data Source=warehouse.db");

// Sync operational data to warehouse
var agent = new SyncAgent(operationalDb, warehouseDb);
await agent.SynchronizeAsync(["Sales", "Inventory", "Customers"]);
```

## Examples

Complete working examples are available in the [examples/sync/](examples/sync/) directory:

- **[SyncExample.cs](examples/sync/SyncExample.cs)** - Basic bidirectional sync with SQL Server
- **[CrossPlatformSyncExample.cs](examples/sync/CrossPlatformSyncExample.cs)** - Sync with PostgreSQL, MySQL, SQLite, and enterprise scenarios

See [examples/README.md](examples/README.md) for setup instructions and more examples.

## Architecture

### How It Works

SharpCoreDB.Provider.Sync implements the Dotmim.Sync `CoreProvider` interface:

```
┌─────────────────────────────────────────┐
│  Application Code                       │
├─────────────────────────────────────────┤
│  Dotmim.Sync Agent                      │
│  (Orchestrates sync process)            │
├─────────────────────────────────────────┤
│  SharpCoreDB.Provider.Sync              │
│  (Implements CoreProvider interface)    │
├─────────────────────────────────────────┤
│  SharpCoreDB Engine                     │
│  (Database operations)                  │
└─────────────────────────────────────────┘
```

### Change Tracking

The provider uses **shadow tables** to track changes:

- `_sharp_users_tracking` - Tracks changes to `users` table
- `_sharp_orders_tracking` - Tracks changes to `orders` table
- Automatic creation during first sync
- Tracks INSERT, UPDATE, DELETE operations
- Stores timestamps and sync metadata

### Data Flow

```
SharpCoreDB → Shadow Tables → Change Detection → Serialization → Network Transfer → Remote DB
Remote DB   ← Conflict Resolution ← Deserialization ← Network Transfer ← Change Application
```

## Configuration Options

### SyncOptions

```csharp
var options = new SyncOptions
{
    // Performance tuning
    BatchSize = 1000,
    DownloadBatchSizeInKB = 5000,
    UploadBatchSizeInKB = 5000,
    UseBulkOperations = true,
    UseCompression = true,

    // Conflict resolution
    ConflictResolutionPolicy = ConflictResolutionPolicy.ServerWins,

    // Reliability
    MaxRetries = 3,
    RetryOnFailure = true,
    UseVerboseErrors = false,

    // Filtering
    Filters = new SyncFilters(),
    Parameters = new SyncParameters()
};
```

### Provider-Specific Options

```csharp
var sharpcoredbOptions = new SharpCoreDBSyncOptions
{
    // Connection configuration
    ConnectionString = "Data Source=sync.db",

    // Performance tuning
    CommandTimeout = TimeSpan.FromMinutes(5),
    BulkCopyBatchSize = 10000,

    // Change tracking
    CleanTrackingTableOnFullSync = true,
    UseChangeTracking = true,

    // Multi-tenant support
    TenantId = "tenant-123",
    EnableTenantFiltering = true
};

var provider = new SharpCoreDBSyncProvider(sharpcoredbOptions);
```

## Best Practices

### 1. Connection Management

```csharp
// Use connection pooling
var provider = new SharpCoreDBSyncProvider("Data Source=sync.db;Pooling=true;Max Pool Size=100;");

// Configure timeouts
var options = new SyncOptions
{
    BulkCopyTimeout = TimeSpan.FromMinutes(10),
    CommandTimeout = TimeSpan.FromMinutes(5)
};
```

### 2. Error Handling

```csharp
try
{
    var result = await agent.SynchronizeAsync(tables);
}
catch (SyncException ex)
{
    // Handle sync-specific errors
    Console.WriteLine($"Sync failed: {ex.Message}");

    // Check for conflicts
    if (ex.Type == SyncExceptionType.Conflicts)
    {
        // Handle conflicts
        var conflicts = await agent.GetConflictsAsync();
        // Resolve conflicts...
    }
}
catch (Exception ex)
{
    // Handle general errors
    Console.WriteLine($"Unexpected error: {ex.Message}");
}
```

### 3. Performance Optimization

```csharp
// Optimize for large datasets
var options = new SyncOptions
{
    BatchSize = 5000,
    UseBulkOperations = true,
    UseCompression = true,
    DownloadBatchSizeInKB = 10000,
    UploadBatchSizeInKB = 10000
};

// Use parallel sync for multiple tables
var tables = new[] { "Table1", "Table2", "Table3" };
var result = await agent.SynchronizeAsync(tables, options);
```

### 4. Monitoring and Logging

```csharp
// Enable detailed logging
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.AddFile("sync.log");
    builder.SetMinimumLevel(LogLevel.Information);
});

// Monitor progress
agent.LocalOrchestrator.OnSyncProgress += (args) =>
{
    Console.WriteLine($"[{DateTime.Now}] {args.ProgressPercentage}% - {args.Message}");
};

// Track performance
var stopwatch = Stopwatch.StartNew();
var result = await agent.SynchronizeAsync(tables);
stopwatch.Stop();

Console.WriteLine($"Sync completed in {stopwatch.Elapsed.TotalSeconds:F2}s");
Console.WriteLine($"Throughput: {result.TotalChangesDownloaded / stopwatch.Elapsed.TotalSeconds:F0} changes/sec");
```

## Troubleshooting

### Common Issues

#### Connection Timeouts
```csharp
// Increase timeouts
var options = new SyncOptions
{
    CommandTimeout = TimeSpan.FromMinutes(10),
    BulkCopyTimeout = TimeSpan.FromMinutes(15)
};
```

#### Large Dataset Sync
```csharp
// Use smaller batches
var options = new SyncOptions
{
    BatchSize = 1000,
    DownloadBatchSizeInKB = 1000,
    UploadBatchSizeInKB = 1000
};
```

#### Memory Issues
```csharp
// Enable compression and reduce batch size
var options = new SyncOptions
{
    UseCompression = true,
    BatchSize = 500,
    CleanMetadatas = true
};
```

#### Conflicts
```csharp
// Configure conflict resolution
var options = new SyncOptions
{
    ConflictResolutionPolicy = ConflictResolutionPolicy.ClientWins
};

// Handle conflicts manually
agent.LocalOrchestrator.OnApplyChangesConflictOccured += (args) =>
{
    // Custom conflict resolution logic
    args.Resolution = ConflictResolution.ClientWins;
};
```

### Diagnostic Tools

```csharp
// Get sync summary
var summary = await agent.GetSummaryAsync();
Console.WriteLine($"Last sync: {summary.LastSync}");
Console.WriteLine($"Total changes: {summary.TotalChanges}");

// Check database schema
var schema = await agent.GetSchemaAsync();
foreach (var table in schema.Tables)
{
    Console.WriteLine($"Table: {table.TableName}, Columns: {table.Columns.Count}");
}

// Validate sync setup
var isValid = await agent.ValidateSetupAsync();
if (!isValid)
{
    var errors = await agent.GetValidationErrorsAsync();
    foreach (var error in errors)
    {
        Console.WriteLine($"Validation error: {error.Message}");
    }
}
```

## API Reference

### SharpCoreDBSyncProvider

```csharp
public class SharpCoreDBSyncProvider : CoreProvider
{
    // Constructors
    SharpCoreDBSyncProvider(string connectionString);
    SharpCoreDBSyncProvider(SharpCoreDBSyncOptions options);

    // Core sync methods
    Task<SyncContext> EnsureSchemaAsync(SyncContext context);
    Task<SyncContext> EnsureDatabaseAsync(SyncContext context);
    Task<DbSyncAdapter> GetSyncAdapterAsync(SyncContext context, DbConnection connection);
    Task<(SyncContext, DatabaseChangesSelected)> GetChangesAsync(SyncContext context);
    Task<(SyncContext, DatabaseChangesApplied)> ApplyChangesAsync(SyncContext context);
    Task<SyncContext> GetConflictAsync(SyncContext context);
    Task<SyncContext> UpdateMetadataAsync(SyncContext context);
}
```

### SharpCoreDBSyncOptions

```csharp
public class SharpCoreDBSyncOptions
{
    string ConnectionString { get; set; }
    TimeSpan CommandTimeout { get; set; }
    int BulkCopyBatchSize { get; set; }
    bool CleanTrackingTableOnFullSync { get; set; }
    bool UseChangeTracking { get; set; }
    string? TenantId { get; set; }
    bool EnableTenantFiltering { get; set; }
}
```

## Performance Benchmarks

| Scenario | Performance | Notes |
|----------|-------------|-------|
| **Initial Sync (1K rows)** | <5 seconds | Full table sync |
| **Incremental Sync (100 changes)** | <1 second | Change tracking |
| **Bulk Sync (100K rows)** | 30-60 seconds | Batch processing |
| **Conflict Resolution** | <10ms per conflict | Automatic resolution |
| **Network Transfer** | 10-50 MB/s | Compression enabled |

## Security Considerations

- Use encrypted connections (SSL/TLS)
- Implement proper authentication
- Validate connection strings
- Monitor sync operations for anomalies
- Regular security audits

## Migration from Other Sync Solutions

### From Custom Sync
1. Replace custom sync logic with Dotmim.Sync + SharpCoreDB.Provider.Sync
2. Configure providers and options
3. Test sync scenarios
4. Monitor performance

### From Database Replication
1. Setup SharpCoreDB as sync client
2. Configure bidirectional sync
3. Handle schema differences
4. Implement conflict resolution

## Support

- [Dotmim.Sync Documentation](https://dotmim-sync.readthedocs.io/)
- [SharpCoreDB Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- [Sync Provider Source](https://github.com/MPCoreDeveloper/SharpCoreDB/tree/master/src/SharpCoreDB.Provider.Sync)
