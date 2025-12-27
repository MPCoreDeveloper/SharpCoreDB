<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB.Serilog.Sinks
  
  **High-Performance Serilog Sink for SharpCoreDB**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.0.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Serilog.Sinks)
  [![Serilog](https://img.shields.io/badge/Serilog-4.2.0-purple.svg)](https://serilog.net/)
  
</div>

---

A Serilog sink for SharpCoreDB, optimized for efficient batch logging with built-in encryption support.

## Features

* **PeriodicBatchingSink**: Efficient batching of log events
* **Automatic table creation**: Creates the Logs table automatically
* **AppendOnly engine**: Maximum write speed for logs
* **Encryption support**: Fully encrypted logs with AES-256-GCM
* **Async/await**: Fully asynchronous operations
* **Configurable**: All options customizable via extension methods
* **Batch updates**: Uses SharpCoreDB's batch update API for better performance
* **Error handling**: Automatic rollback on errors

## Installation

```bash
dotnet add package SharpCoreDB.Serilog.Sinks
```

## Quick Start

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        path: "logs.scdb",
        password: "myPassword",
        autoCreateTable: true)
    .CreateLogger();

Log.Information("Hello, SharpCoreDB!");
Log.Error(new Exception("Test"), "Something went wrong");
Log.CloseAndFlush();
```

## Usage Examples

### Basic Usage

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        path: "logs.scdb",
        password: "myPassword",
        autoCreateTable: true)
    .CreateLogger();

Log.Information("This is a test log message");
Log.Warning("This is a warning with {Property}", "value");
Log.Error(new Exception("Test exception"), "An error occurred");

Log.CloseAndFlush();
```

### With Existing Database Instance

```csharp
using Serilog;
using SharpCoreDB;
using SharpCoreDB.Serilog.Sinks;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
var database = factory.Create("logs.scdb", "myPassword");

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        database: database,
        tableName: "ApplicationLogs",
        restrictedToMinimumLevel: LogEventLevel.Information)
    .CreateLogger();

Log.Information("Logging with existing database");
Log.CloseAndFlush();
```

### Full Configuration

```csharp
using Serilog;
using Serilog.Events;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        path: "logs.scdb",
        password: "securePassword123",
        tableName: "Logs",
        restrictedToMinimumLevel: LogEventLevel.Information,
        batchPostingLimit: 100,
        period: TimeSpan.FromSeconds(5),
        autoCreateTable: true,
        storageEngine: "AppendOnly")
    .CreateLogger();

Log.Information("Configured logging");
Log.CloseAndFlush();
```

### With Options Object

```csharp
using Serilog;
using Serilog.Events;
using SharpCoreDB.Serilog.Sinks;

var options = new SharpCoreDBSinkOptions
{
    Path = "logs.scdb",
    Password = "myPassword",
    TableName = "Logs",
    RestrictedToMinimumLevel = LogEventLevel.Information,
    BatchPostingLimit = 50,
    Period = TimeSpan.FromSeconds(2),
    AutoCreateTable = true,
    StorageEngine = "AppendOnly"
};

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(options)
    .CreateLogger();

Log.Information("Logging with options");
Log.CloseAndFlush();
```

### ASP.NET Core Integration

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.SharpCoreDB(
            path: "logs/webapp.scdb",
            password: context.Configuration["Logging:SharpCoreDB:Password"] ?? "default",
            tableName: "Logs",
            restrictedToMinimumLevel: LogEventLevel.Information);
});

var app = builder.Build();

// Create indexes on startup for better query performance
var services = app.Services.CreateScope().ServiceProvider;
var factory = services.GetRequiredService<DatabaseFactory>();
var db = factory.Create("logs/webapp.scdb", 
    app.Configuration["Logging:SharpCoreDB:Password"] ?? "default");

try
{
    db.ExecuteSQL("CREATE INDEX idx_logs_timestamp ON Logs (Timestamp)");
    db.ExecuteSQL("CREATE INDEX idx_logs_level_timestamp ON Logs (Level, Timestamp)");
}
catch { }

app.UseSerilogRequestLogging();

app.MapGet("/", (ILogger<Program> logger) =>
{
    logger.LogInformation("Homepage accessed");
    return "Hello World!";
});

app.Run();
```

### Structured Logging

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(path: "structured.scdb", password: "test")
    .CreateLogger();

// Structured properties are stored in Properties column as JSON
Log.Information("User {UserId} performed {Action} at {Timestamp}", 
    123, "Login", DateTime.UtcNow);

Log.Information("Order {OrderId} processed: {Items} items, total {Total:C}", 
    "ORD-2024-001", 5, 99.99m);

// With custom properties object
var context = new 
{ 
    UserId = 456, 
    SessionId = "abc123", 
    IpAddress = "192.168.1.1",
    UserAgent = "Mozilla/5.0"
};

Log.Information("Request received {@Context}", context);
Log.CloseAndFlush();
```

## Database Schema

The sink automatically creates a table with the following structure:

```sql
CREATE TABLE Logs (
    Id ULID AUTO PRIMARY KEY,
    Timestamp DATETIME,
    Level TEXT,
    Message TEXT,
    Exception TEXT,
    Properties TEXT
) ENGINE=AppendOnly
```

### Columns

* **Id**: ULID (Universally Unique Lexicographically Sortable Identifier) - automatically generated
  * Contains timestamp, so sortable by creation time
  * 128-bit unique ID
  * Better than INTEGER AUTOINCREMENT for distributed systems
* **Timestamp**: UTC timestamp of the log event
* **Level**: Log level (Verbose, Debug, Information, Warning, Error, Fatal)
* **Message**: The rendered log message
* **Exception**: Exception details (if present)
* **Properties**: JSON object with all log properties

## Configuration Options

### LoggerConfigurationExtensions

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `database` | `IDatabase` | - | Existing database instance |
| `path` | `string` | - | Path to .scdb file |
| `password` | `string` | - | Encryption password |
| `tableName` | `string` | `"Logs"` | Name of the logs table |
| `restrictedToMinimumLevel` | `LogEventLevel` | `Verbose` | Minimum log level |
| `batchPostingLimit` | `int` | `50` | Max events per batch |
| `period` | `TimeSpan?` | `2 sec` | Interval between batches |
| `autoCreateTable` | `bool` | `true` | Automatically create table |
| `storageEngine` | `string` | `"AppendOnly"` | Storage engine (AppendOnly/PageBased/Columnar) |
| `serviceProvider` | `IServiceProvider?` | `null` | Service provider for DI |

### SharpCoreDBSinkOptions

All properties match the parameters above.

## Storage Engines

SharpCoreDB supports three storage engines:

1. **AppendOnly** (recommended for logs)
   * Maximum write speed
   * Optimized for insert-only workloads
   * Ideal for high-volume logging

2. **PageBased**
   * General purpose
   * Good read/write balance
   * Suitable for querying logs

3. **Columnar**
   * Optimized for analytical queries
   * Best compression
   * Ideal for long-term log storage

## Performance

The sink uses multiple optimizations for maximum performance:

* **Batch processing**: Logs are written in batches (default: 50 events per 2 seconds)
* **Batch updates**: Uses `BeginBatchUpdate()`/`EndBatchUpdate()` API for better index performance
* **AppendOnly engine**: No index updates during writing
* **Async operations**: Fully asynchronous I/O
* **WAL batching**: Group commit for durability with minimal latency
* **ULID primary key**: Sortable by timestamp without needing separate index

Expected performance:
* 10,000+ logs/second on modern hardware
* Sub-millisecond latency per batch
* Minimal memory footprint

### Query Performance Tips

1. **Sort by Id (ULID) instead of Timestamp**: 
   * ULID contains timestamp in first 48 bits
   * Sorting by Id = chronological order
   * Uses primary key index (much faster than timestamp column scan)
   * `ORDER BY Id DESC` = newest first (same as `ORDER BY Timestamp DESC`)

2. **Add B-tree index on Timestamp for range queries**:
   ```csharp
   db.ExecuteSQL("CREATE INDEX idx_logs_timestamp ON Logs (Timestamp)");
   ```
   * Essential for queries like "logs between date X and Y"
   * Enables efficient range scans
   * Recommended for production logging systems

3. **Composite index for level + timestamp queries**:
   ```csharp
   db.ExecuteSQL("CREATE INDEX idx_logs_level_timestamp ON Logs (Level, Timestamp)");
   ```
   * Optimizes queries like "all errors in last hour"
   * Better than separate indexes on Level and Timestamp

## Querying Logs

### Basic Queries

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
var db = factory.Create("logs.scdb", "myPassword", isReadOnly: true);

// RECOMMENDED: Query by ULID (sortable by timestamp, much faster than Timestamp column)
// ULID contains timestamp in first 48 bits, so sorting by Id gives chronological order
var recentLogs = db.ExecuteQuery(
    "SELECT * FROM Logs WHERE Level = @0 ORDER BY Id DESC LIMIT 100",
    new Dictionary<string, object?> { { "0", "Error" } });

// Alternative: Query by Timestamp with B-tree index (for range queries)
// For better performance on timestamp ranges, create an index:
db.ExecuteSQL("CREATE INDEX idx_logs_timestamp ON Logs (Timestamp)");

var results = db.ExecuteQuery(
    "SELECT * FROM Logs WHERE Timestamp >= @0 AND Timestamp < @1 ORDER BY Timestamp DESC",
    new Dictionary<string, object?> 
    { 
        { "0", DateTime.UtcNow.AddHours(-24) },
        { "1", DateTime.UtcNow }
    });

// Query by log level with timestamp range
var errors = db.ExecuteQuery(
    "SELECT * FROM Logs WHERE Level = @0 AND Timestamp >= @1 ORDER BY Id DESC",
    new Dictionary<string, object?> 
    { 
        { "0", "Error" },
        { "1", DateTime.UtcNow.AddHours(-1) }
    });

foreach (var row in results)
{
    Console.WriteLine($"{row["Timestamp"]}: {row["Message"]}");
}
```

### Performance Comparison

```csharp
// These give the SAME chronological order:
"ORDER BY Id DESC"         // Uses primary key - FASTEST
"ORDER BY Timestamp DESC"  // Requires B-tree index - slower

// ULID contains timestamp in first 48 bits, so:
// - Newest log = highest ULID value
// - Oldest log = lowest ULID value
// - No need for separate timestamp index for simple chronological queries

// Performance test example:
var sw = System.Diagnostics.Stopwatch.StartNew();
var byUlid = db.ExecuteQuery("SELECT * FROM Logs ORDER BY Id DESC LIMIT 1000");
sw.Stop();
Console.WriteLine($"ORDER BY Id (ULID):      {sw.ElapsedMilliseconds}ms");

sw.Restart();
var byTimestamp = db.ExecuteQuery("SELECT * FROM Logs ORDER BY Timestamp DESC LIMIT 1000");
sw.Stop();
Console.WriteLine($"ORDER BY Timestamp:      {sw.ElapsedMilliseconds}ms");
```

## Index Management for Production Systems

For production logging systems, create indexes on startup for optimal query performance:

```csharp
// On application startup, create recommended indexes
var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
var db = factory.Create("logs.scdb", "myPassword");

// 1. B-tree index on Timestamp (essential for range queries)
try
{
    db.ExecuteSQL("CREATE INDEX idx_logs_timestamp ON Logs (Timestamp)");
}
catch { /* Index might already exist */ }

// 2. Composite index on (Level, Timestamp) - most common query pattern
try
{
    db.ExecuteSQL("CREATE INDEX idx_logs_level_timestamp ON Logs (Level, Timestamp)");
}
catch { /* Index might already exist */ }

// 3. Optional: Hash index on Level for exact matches
try
{
    db.ExecuteSQL("CREATE INDEX idx_logs_level_hash ON Logs (Level)");
}
catch { /* Index might already exist */ }
```

**Why these indexes?**

| Index | Purpose | Query Pattern | Performance Gain |
|-------|---------|---------------|------------------|
| Primary Key (ULID) | Chronological sorting | `ORDER BY Id` | Built-in, fastest |
| `idx_logs_timestamp` | Date range queries | `WHERE Timestamp BETWEEN x AND y` | 10-100x faster |
| `idx_logs_level_timestamp` | Filtered date ranges | `WHERE Level='Error' AND Timestamp>=x` | 5-50x faster |
| `idx_logs_level_hash` | Level filtering | `WHERE Level='Error'` | 2-10x faster |

**ULID vs Timestamp for sorting:**

```csharp
// These give the SAME chronological order:
"ORDER BY Id DESC"         // Uses primary key - FASTEST
"ORDER BY Timestamp DESC"  // Requires B-tree index - slower

// ULID contains timestamp in first 48 bits, so:
// - Newest log = highest ULID value
// - Oldest log = lowest ULID value
// - No need for separate timestamp index for simple chronological queries
```

## Error Handling

On errors during batch processing:

1. The sink calls `CancelBatchUpdate()` for rollback
2. The exception is passed to Serilog
3. Logs can be handled via fallback sink

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        path: "logs.scdb",
        password: "myPassword")
    .WriteTo.File("fallback.log") // Fallback sink
    .CreateLogger();
```

## appsettings.json Configuration

```json
{
  "Logging": {
    "SharpCoreDB": {
      "Password": "your-secure-password-here",
      "Path": "logs/app.scdb",
      "TableName": "Logs",
      "MinimumLevel": "Information",
      "BatchPostingLimit": 100,
      "Period": "00:00:05"
    }
  }
}
```

## Performance Testing

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        path: "performance_test.scdb",
        password: "test",
        batchPostingLimit: 1000,
        period: TimeSpan.FromSeconds(1),
        storageEngine: "AppendOnly")
    .CreateLogger();

var sw = System.Diagnostics.Stopwatch.StartNew();

for (int i = 0; i < 10000; i++)
{
    Log.Information("Test log {Index} with {Property}", i, $"value{i}");
}

Log.CloseAndFlush();
sw.Stop();

Console.WriteLine($"10,000 logs written in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Throughput: {10000.0 / (sw.ElapsedMilliseconds / 1000.0):F0} logs/second");
