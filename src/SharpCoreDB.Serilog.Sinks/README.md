<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Serilog.Sinks

  **High-Performance Serilog Sink for SharpCoreDB**

  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Serilog.Sinks)
  [![Serilog](https://img.shields.io/badge/Serilog-4.x-purple.svg)](https://serilog.net/)

</div>

---

A high-performance Serilog sink for [SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB), optimized for efficient batch logging with built-in AES-256-GCM encryption.

## Features

- **Batch Processing** — Uses `ExecuteBatchSQLAsync` → `InsertBatch` for maximum write throughput
- **Automatic Table Creation** — Creates the `Logs` table on first use with thread-safe initialization
- **AppendOnly Engine** — Default storage engine tuned for write-once log workloads
- **AES-256-GCM Encryption** — All logs encrypted at rest with near-zero overhead (AES-NI)
- **Fully Async** — End-to-end async I/O with `ConfigureAwait(false)` for library safety
- **ULID Primary Keys** — Sortable by timestamp, no separate index needed for chronological queries
- **WAL Group Commit** — Batched durability for high-throughput logging with minimal latency
- **Configurable** — Three configuration methods: database instance, connection string, or options object

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
        password: "mySecurePassword")
    .CreateLogger();

Log.Information("Hello, SharpCoreDB!");
Log.Error(new Exception("Something failed"), "An error occurred");

Log.CloseAndFlush();
```

## Configuration Methods

### 1. Connection String (Simplest)

Creates a database internally with logging-optimized settings (async WAL, no query cache, group commit).

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

### 2. Existing Database Instance

Use when you already manage the `IDatabase` lifecycle (e.g., via DI).

```csharp
using Serilog;
using Serilog.Events;
using SharpCoreDB.Serilog.Sinks;
using SharpCoreDB.Services;
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

### 3. Options Object

Best for complex configuration and `appsettings.json` binding scenarios.

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
    BatchPostingLimit = 100,
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

## ASP.NET Core Integration

### Razor Pages / MVC Application

```csharp
using Serilog;
using Serilog.Events;
using SharpCoreDB.Serilog.Sinks;
using SharpCoreDB.Services;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB services
builder.Services.AddSharpCoreDB();

// Configure Serilog with SharpCoreDB sink
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.SharpCoreDB(
            path: "logs/webapp.scdb",
            password: context.Configuration["Logging:SharpCoreDB:Password"] ?? "default",
            tableName: "Logs",
            restrictedToMinimumLevel: LogEventLevel.Information,
            batchPostingLimit: 100,
            period: TimeSpan.FromSeconds(2));
});

var app = builder.Build();

// Optional: Create indexes on startup for query performance
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<DatabaseFactory>();
    var db = factory.Create("logs/webapp.scdb",
        app.Configuration["Logging:SharpCoreDB:Password"] ?? "default");

    try { db.ExecuteSQL("CREATE INDEX idx_logs_timestamp ON Logs (Timestamp)"); } catch { }
    try { db.ExecuteSQL("CREATE INDEX idx_logs_level_timestamp ON Logs (Level, Timestamp)"); } catch { }
}

app.UseSerilogRequestLogging();
app.MapRazorPages();
app.Run();
```

### appsettings.json

```json
{
  "Logging": {
    "SharpCoreDB": {
      "Password": "your-secure-password-here",
      "Path": "logs/app.scdb",
      "TableName": "Logs",
      "MinimumLevel": "Information",
      "BatchPostingLimit": 100,
      "Period": "00:00:02"
    }
  }
}
```

## Structured Logging

Properties are serialized as JSON in the `Properties` column:

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(path: "structured.scdb", password: "test")
    .CreateLogger();

// Scalar properties
Log.Information("User {UserId} performed {Action} at {Timestamp}",
    123, "Login", DateTime.UtcNow);

// Numeric formatting
Log.Information("Order {OrderId}: {Items} items, total {Total:C}",
    "ORD-2024-001", 5, 99.99m);

// Complex objects (destructured with @)
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

The sink automatically creates the following table:

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

| Column | Type | Description |
|--------|------|-------------|
| **Id** | `ULID` | Auto-generated, sortable by creation time (128-bit, contains timestamp in first 48 bits) |
| **Timestamp** | `DATETIME` | UTC timestamp of the log event |
| **Level** | `TEXT` | `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` |
| **Message** | `TEXT` | The rendered log message |
| **Exception** | `TEXT` | Full exception string (null if no exception) |
| **Properties** | `TEXT` | JSON object with all structured log properties |

## Storage Engines

| Engine | Best For | Characteristics |
|--------|----------|----------------|
| **AppendOnly** ⭐ | High-volume logging | Maximum write speed, insert-only workloads |
| **PageBased** | Queryable log stores | Balanced read/write, supports updates/deletes |
| **Columnar** | Log analytics | Best compression, fast aggregations (`GROUP BY`, `SUM`) |

## Configuration Reference

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `database` | `IDatabase` | — | Existing database instance (takes precedence) |
| `path` | `string` | — | Path to `.scdb` file |
| `password` | `string` | — | AES-256-GCM encryption password |
| `tableName` | `string` | `"Logs"` | Name of the log table |
| `restrictedToMinimumLevel` | `LogEventLevel` | `Verbose` | Minimum log level to capture |
| `batchPostingLimit` | `int` | `50` | Maximum events per batch |
| `period` | `TimeSpan?` | `2 sec` | Interval between batch flushes |
| `autoCreateTable` | `bool` | `true` | Auto-create table on first use |
| `storageEngine` | `string` | `"AppendOnly"` | Storage engine (`AppendOnly`/`PageBased`/`Columnar`) |
| `serviceProvider` | `IServiceProvider?` | `null` | DI provider (path-based overload only) |

## Querying Logs

### Open the Database (Read-Only)

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var sp = services.BuildServiceProvider();

var factory = sp.GetRequiredService<DatabaseFactory>();
var db = factory.Create("logs.scdb", "myPassword", isReadOnly: true);
```

### Basic Queries

```csharp
// RECOMMENDED: Sort by ULID (primary key) for chronological order — fastest
var recentErrors = db.ExecuteQuery(
    "SELECT * FROM Logs WHERE Level = @0 ORDER BY Id DESC LIMIT 100",
    new Dictionary<string, object?> { { "0", "Error" } });

// Time-range query (requires B-tree index on Timestamp)
var last24h = db.ExecuteQuery(
    "SELECT * FROM Logs WHERE Timestamp >= @0 AND Timestamp < @1 ORDER BY Id DESC",
    new Dictionary<string, object?>
    {
        { "0", DateTime.UtcNow.AddHours(-24) },
        { "1", DateTime.UtcNow }
    });

// Combined level + time filter
var recentWarnings = db.ExecuteQuery(
    "SELECT * FROM Logs WHERE Level = @0 AND Timestamp >= @1 ORDER BY Id DESC",
    new Dictionary<string, object?>
    {
        { "0", "Warning" },
        { "1", DateTime.UtcNow.AddHours(-1) }
    });

foreach (var row in recentErrors)
{
    Console.WriteLine($"[{row["Level"]}] {row["Timestamp"]}: {row["Message"]}");
}
```

### ULID vs Timestamp Sorting

```
ORDER BY Id DESC         → Uses primary key — FASTEST (no extra index needed)
ORDER BY Timestamp DESC  → Requires B-tree index — slower

ULID contains timestamp in first 48 bits:
  - Newest log = highest ULID value
  - Oldest log = lowest ULID value
  - Sorting by Id gives the same chronological order as Timestamp
```

## Index Recommendations

Create indexes on application startup for optimal query performance:

```csharp
// 1. B-tree index on Timestamp — essential for date range queries
try { db.ExecuteSQL("CREATE INDEX idx_logs_timestamp ON Logs (Timestamp)"); } catch { }

// 2. Composite index on (Level, Timestamp) — most common query pattern
try { db.ExecuteSQL("CREATE INDEX idx_logs_level_timestamp ON Logs (Level, Timestamp)"); } catch { }

// 3. Optional: Hash index on Level — fast exact-match filtering
try { db.ExecuteSQL("CREATE INDEX idx_logs_level_hash ON Logs (Level)"); } catch { }
```

| Index | Purpose | Query Pattern | Speedup |
|-------|---------|---------------|---------|
| Primary Key (ULID) | Chronological sort | `ORDER BY Id` | Built-in |
| `idx_logs_timestamp` | Date ranges | `WHERE Timestamp BETWEEN x AND y` | 10-100× |
| `idx_logs_level_timestamp` | Filtered ranges | `WHERE Level='Error' AND Timestamp>=x` | 5-50× |
| `idx_logs_level_hash` | Level filtering | `WHERE Level='Error'` | 2-10× |

## Performance

### Architecture

```
Serilog Logger
    │
    ▼
PeriodicBatchingSink (50 events / 2 sec)
    │
    ▼
SharpCoreDBSink.EmitBatchAsync()
    │
    ├─ BuildInsertSql() × N events (cached prefix, escaped values)
    │
    ├─ ExecuteBatchSQLAsync() → InsertBatch (direct storage engine write)
    │
    └─ Flush() → persist to disk
```

### Optimizations

| Technique | Benefit |
|-----------|---------|
| `ExecuteBatchSQLAsync` → `InsertBatch` | Direct storage engine writes, bypasses per-row overhead |
| Cached INSERT prefix | Avoids repeated string interpolation of table name |
| StringBuilder with capacity hint | Reduces allocation/resizing in hot path |
| WAL Group Commit (Async) | Batched durability, ~10× fewer fsyncs |
| ULID primary key | Sortable without Timestamp index |
| Thread-safe init with `Lock` | C# 14 Lock class, double-check pattern |
| `ConfigureAwait(false)` | Avoids deadlocks in library code |

### Expected Throughput

- **10,000+ logs/second** on modern hardware
- **Sub-millisecond** batch latency
- **Minimal memory** footprint (batch-and-flush pattern)

### Performance Test

```csharp
using Serilog;
using SharpCoreDB.Serilog.Sinks;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        path: "perf_test.scdb",
        password: "test",
        batchPostingLimit: 1000,
        period: TimeSpan.FromSeconds(1))
    .CreateLogger();

var sw = System.Diagnostics.Stopwatch.StartNew();

for (int i = 0; i < 10_000; i++)
{
    Log.Information("Test log {Index} with {Property}", i, $"value{i}");
}

Log.CloseAndFlush();
sw.Stop();

Console.WriteLine($"10,000 logs written in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Throughput: {10_000.0 / (sw.ElapsedMilliseconds / 1000.0):F0} logs/sec");
```

## Error Handling

The sink handles errors gracefully:

1. **Batch failures** — `ExecuteBatchSQLAsync` uses internal transactions with automatic rollback
2. **Table creation** — Thread-safe with double-check locking; falls back to verification query
3. **Fallback sinks** — Configure a secondary sink for resilience:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(path: "logs.scdb", password: "myPassword")
    .WriteTo.File("fallback.log") // Fallback if SharpCoreDB fails
    .CreateLogger();
```

## Security

- **AES-256-GCM** encryption at rest for all log data
- **SQL value escaping** — Values are escaped via SQL quoting to prevent injection
- **No plaintext** storage — every `.scdb` file is fully encrypted
- **Password-based** access — database requires master password to open

## Requirements

- .NET 10.0+
- SharpCoreDB 1.0.6+
- Serilog 4.x
- Serilog.Sinks.PeriodicBatching 5.x

## License

[MIT License](LICENSE) — Copyright © 2025-2026 MPCoreDeveloper

## Links

- **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB
- **NuGet**: https://www.nuget.org/packages/SharpCoreDB.Serilog.Sinks
- **Issues**: https://github.com/MPCoreDeveloper/SharpCoreDB/issues
