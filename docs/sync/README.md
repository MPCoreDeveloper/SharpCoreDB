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
    BatchSize = 500,
    TombstoneRetentionDays = 30,

    // Change tracking
    EnableAutoTracking = true,
    AutoProvisionScopeTables = true,

    // Reliability
    CommandTimeoutSeconds = 300
};
```

## Integration Testing

### Running Integration Tests

The provider includes comprehensive integration tests that verify end-to-end synchronization:

```bash
# Run all sync integration tests
dotnet test --filter "Integration"

# Run specific test categories
dotnet test --filter "SQLiteRoundtripTests"
dotnet test --filter "EndToEndSyncScenarios" 
dotnet test --filter "SyncPerformanceBenchmarks"
dotnet test --filter "SyncErrorHandlingTests"
```

### Test Coverage

#### SQLite Roundtrip Tests
- ✅ Empty database synchronization
- ✅ Unidirectional data transfer (SharpCoreDB → SQLite, SQLite → SharpCoreDB)
- ✅ Bidirectional merge with conflict resolution
- ✅ Multiple table synchronization
- ✅ Update operations
- ✅ Delete operations (tombstone handling)
- ✅ Large dataset performance (1000+ records)

#### End-to-End Scenarios
- ✅ **Mobile Offline Sync**: App goes offline, makes changes, syncs when online
- ✅ **Multi-Device Conflicts**: Two devices modify same record, conflict resolution
- ✅ **Incremental Sync**: Only changed data transferred after initial sync
- ✅ **Data Migration**: Migrate existing SQLite data to SharpCoreDB
- ✅ **Backup & Restore**: Use sync for database backup/recovery
- ✅ **Collaborative Editing**: Multiple users working on shared data

#### Performance Benchmarks
- ✅ Small dataset (100 records) - < 5 seconds
- ✅ Medium dataset (1000 records) - < 15 seconds  
- ✅ Large dataset (5000 records) - < 30 seconds
- ✅ Incremental sync performance
- ✅ Multi-table synchronization
- ✅ Concurrent sync operations
- ✅ Complex schema handling
- ✅ Memory usage monitoring
- ✅ Cancellation responsiveness

#### Error Handling & Edge Cases
- ✅ Invalid table names
- ✅ Database corruption scenarios
- ✅ File locking conflicts
- ✅ Network interruption simulation
- ✅ Schema mismatches
- ✅ Primary key conflicts
- ✅ Large data types (BLOB, long TEXT)
- ✅ NULL value handling
- ✅ Unicode character preservation
- ✅ Empty table synchronization
- ✅ Very long table/column names
- ✅ Database restart scenarios

### Test Infrastructure

Integration tests use a common base class `SyncIntegrationTestBase` that provides:

- **Database Setup**: Automatic creation of test SharpCoreDB and SQLite databases
- **Data Population**: Helper methods to insert consistent test data
- **Sync Execution**: Wrapper methods for bidirectional synchronization
- **Verification**: Automatic data consistency checking between databases
- **Cleanup**: Automatic disposal and file cleanup

```csharp
public class SyncIntegrationTestBase : IDisposable
{
    protected readonly Database _sharpcoredb;
    protected readonly SQLiteConnection _sqliteConnection;
    
    // Helper methods for test setup, data insertion, sync execution, and verification
}
```

## Troubleshooting

### Common Issues

#### 1. Table Not Found Errors
**Symptom**: `InvalidOperationException: Table 'X' does not exist`
**Solution**: Ensure tables are created in both databases before syncing

#### 2. Primary Key Required
**Symptom**: `InvalidOperationException: Table must have a primary key for sync`
**Solution**: All sync tables must have a PRIMARY KEY column in their CREATE TABLE statement. SharpCoreDB parses inline (`id INTEGER PRIMARY KEY`) and table-level (`PRIMARY KEY(id)`) constraints.
**Note**: Fixed in January 2026 — `SingleFileDatabase` now correctly detects PRIMARY KEY from SQL DDL.

#### 3. Connection String Issues
**Symptom**: Database connection failures
**Solution**: Verify connection strings and database accessibility

#### 4. Performance Issues
**Symptom**: Sync operations taking too long
**Solution**: 
- Use `BatchSize` option for large datasets
- Enable `UseBulkOperations` 
- Consider incremental sync patterns

#### 5. Conflict Resolution
**Symptom**: Unexpected data changes during sync
**Solution**: Configure `ConflictResolutionPolicy` (ServerWins/ClientWins)

### Debug Logging

Enable verbose logging for troubleshooting:

```csharp
var options = new SyncOptions
{
    UseVerboseErrors = true,
    Logger = new SyncLogger
    {
        UseConsole = true,
        Level = LogLevel.Debug
    }
};
```

### Performance Monitoring

Monitor sync performance with built-in metrics:

```csharp
agent.LocalOrchestrator.OnSyncProgress += (args) =>
{
    Console.WriteLine($"Progress: {args.ProgressPercentage}% - {args.Message}");
    Console.WriteLine($"Changes: ↑{args.ChangesAppliedOnClient} ↓{args.ChangesAppliedOnServer}");
};
```

## API Reference

### SharpCoreDBSyncProvider

```csharp
public sealed class SharpCoreDBSyncProvider(string connectionString, SyncProviderOptions? options = null) : CoreProvider
{
    // Properties
    public override string ConnectionString { get; set; }
    public SyncProviderOptions Options { get; }
    
    // Methods
    public override DbConnection CreateConnection();
    public override string GetDatabaseName();
    public override DbDatabaseBuilder GetDatabaseBuilder();
    public override DbScopeBuilder GetScopeBuilder(string scopeName);
    public override DbSyncAdapter GetSyncAdapter(SyncTable table, ScopeInfo scopeInfo);
    public override DbMetadata GetMetadata();
}
```

### SharpCoreDBSyncAdapter

```csharp
public sealed class SharpCoreDBSyncAdapter(SyncTable tableDescription, ScopeInfo scopeInfo) : DbSyncAdapter
{
    // Methods
    public override (DbCommand Command, bool IsBatchCommand) GetCommand(SyncContext context, DbCommandType commandType, SyncFilter? filter);
    public override DbColumnNames GetParsedColumnNames(string columnName);
    public override DbTableBuilder GetTableBuilder();
    public override Task<int> ExecuteBatchCommandAsync(SyncContext context, DbCommand command, Guid senderScopeId, IEnumerable<SyncRow> arrayItems, SyncTable schemaChangesTable, SyncTable failedRows, long? lastTimestamp, DbConnection connection, DbTransaction? transaction);
}
```

## Contributing

### Running Tests

```bash
# Run all tests
dotnet test

# Run integration tests only
dotnet test --filter "Integration"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Adding New Tests

1. Inherit from `SyncIntegrationTestBase` for integration tests
2. Use `CreateTestTableAsync()` to set up schemas
3. Use `InsertTestDataAsync()` for test data
4. Use `PerformBidirectionalSyncAsync()` for sync operations
5. Use `VerifyDataConsistencyAsync()` to check results

### Performance Testing

When adding performance tests:
- Use realistic data sizes
- Include warm-up operations
- Measure memory usage with `GC.GetTotalMemory()`
- Test cancellation scenarios
- Document expected performance bounds

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

**Last Updated:** January 29, 2026
**Version:** 1.0.0-alpha
**Test Status:** 84/84 passing ✅
