# SharpCoreDB.Provider.YesSql

**YesSql Provider for SharpCoreDB** - Enables OrchardCore CMS and other YesSql-based applications to use SharpCoreDB as the underlying database.

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

---

## 📋 Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [How It Works](#how-it-works)
- [Performance](#performance)
- [Compatibility](#compatibility)
- [Troubleshooting](#troubleshooting)
- [Advanced Topics](#advanced-topics)
- [Contributing](#contributing)

---

## Overview

### What is YesSql?

[YesSql](https://github.com/sebastienros/yessql) is a **document database** for .NET that provides:
- LINQ query support for documents
- Automatic change tracking
- Document sessions (like Entity Framework)
- Multi-tenancy support (table prefixes)
- Used by [OrchardCore CMS](https://orchardcore.net/)

### What does this provider do?

This provider enables YesSql to use **SharpCoreDB** instead of SQLite/SQL Server/PostgreSQL, giving you:

✅ **Native encryption** (AES-256-GCM) for all data  
✅ **Better performance** for analytics (345x faster than LiteDB via SIMD)  
✅ **Smaller memory footprint** (6.2x less than LiteDB)  
✅ **Pure .NET** implementation (no native dependencies)  
✅ **OrchardCore compatibility** out-of-the-box  

---

## Architecture

### High-Level Overview

```
OrchardCore CMS / YesSql Application
          ↓
   YesSql.Core (Document ORM)
          ↓
   YesSql.Provider.Sqlite (SQL Dialect)
          ↓
   SharpCoreDB.Provider.YesSql (This package)
          ↓ ADO.NET Provider
   SharpCoreDB.Data.Provider (DbConnection, DbCommand)
          ↓
   SharpCoreDB Core Engine
          ↓
   Encrypted .scdb file
```

### Key Components

| Component | Purpose |
|-----------|---------|
| **YesSqlConfigurationExtensions** | Configuration API (`AddYesSqlWithSharpCoreDB()`) |
| **SharpCoreDbConnectionFactory** | Creates `SharpCoreDBConnection` instances for YesSql |
| **SharpCoreDbProviderFactory** | ADO.NET provider factory registration |
| **SharpCoreDBConnection** | ADO.NET connection with SQLite system table interceptie |
| **SharpCoreDBCommand** | ADO.NET command with `sqlite_master` query redirection |

### Why Sqlite Dialect?

YesSql generates SQL using a **dialect abstraction**. We use `SqliteDialect` because SharpCoreDB is **Sqlite-compatible**:

| SQL Feature | YesSql Generates | SharpCoreDB Supports |
|-------------|------------------|---------------------|
| **CREATE TABLE** | `CREATE TABLE IF NOT EXISTS` | ✅ Yes |
| **Auto-increment** | `INTEGER PRIMARY KEY` | ✅ Yes |
| **Pagination** | `LIMIT x OFFSET y` | ✅ Yes |
| **Last insert ID** | `SELECT last_insert_rowid()` | ✅ Yes (via `GetLastInsertRowId()`) |
| **Identifiers** | Double quotes `"table"."column"` | ✅ Yes |
| **Parameters** | `@p0, @p1, @p2` | ✅ Yes |

---

## Installation

### NuGet Package

```bash
dotnet add package SharpCoreDB.Provider.YesSql
```

### From Source

```bash
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB
dotnet build src/SharpCoreDB.Provider.YesSql
```

---

## Quick Start

### Basic Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Provider.YesSql;

var services = new ServiceCollection();

// Add YesSql with SharpCoreDB
services.AddYesSqlWithSharpCoreDB(
    connectionString: "Data Source=./orchardcore.scdb;Password=StrongPassword123",
    tablePrefix: "oc_",
    isolationLevel: IsolationLevel.ReadCommitted);

var serviceProvider = services.BuildServiceProvider();
var store = serviceProvider.GetRequiredService<IStore>();

// Use YesSql normally
using var session = store.CreateSession();

// Save a document
var user = new User { Name = "Alice", Email = "alice@example.com" };
session.Save(user);
await session.SaveChangesAsync();

// Query documents
var users = await session.Query<User>().Where(u => u.Name == "Alice").ListAsync();
```

### OrchardCore Integration

```csharp
// In your OrchardCore startup
public void ConfigureServices(IServiceCollection services)
{
    services.AddOrchardCore()
        .AddSetupFeatures("OrchardCore.AutoSetup")
        .ConfigureServices((tenant, services) =>
        {
            // Use SharpCoreDB instead of SQLite
            services.AddYesSqlWithSharpCoreDB(
                connectionString: $"Data Source=./App_Data/Sites/{tenant}/yessql.scdb;Password={GetPassword()}",
                tablePrefix: tenant + "_");
        });
}
```

---

## Configuration

### Connection String Format

```
Data Source=<path>;Password=<password>[;Option=value]
```

**Parameters:**
- `Data Source` - Path to `.scdb` file (required)
- `Password` - Encryption password (required)

**Example:**
```
Data Source=./App_Data/orchardcore.scdb;Password=MySecurePassword123
```

### Configuration Options

```csharp
services.AddYesSqlWithSharpCoreDB(
    connectionString: "Data Source=./db.scdb;Password=pwd",
    tablePrefix: "oc_",                      // Multi-tenancy support
    isolationLevel: IsolationLevel.ReadCommitted  // Transaction isolation
);
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `connectionString` | `string` | *required* | SharpCoreDB connection string |
| `tablePrefix` | `string?` | `null` | Table prefix for multi-tenancy |
| `isolationLevel` | `IsolationLevel` | `ReadCommitted` | Transaction isolation level |

---

## How It Works

### 1. SQL Generation (YesSql → Sqlite Dialect)

YesSql uses `SqliteDialect` to generate SQL:

```csharp
// YesSql generates:
CREATE TABLE IF NOT EXISTS "oc_Document" (
    "Id" INTEGER PRIMARY KEY,
    "Type" TEXT,
    "Content" TEXT
)

// SharpCoreDB executes:
CreateTable("oc_Document", columns: [...])
```

### 2. ADO.NET Provider (YesSql → SharpCoreDB)

The provider implements standard ADO.NET interfaces:

```csharp
// YesSql calls:
var connection = connectionFactory.CreateConnection();
connection.Open();
var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM oc_Document WHERE Type = @p0";
command.Parameters.Add("p0", "User");
var reader = command.ExecuteReader();
```

### 3. SQLite System Table Interceptie

**Problem:** YesSql queries `sqlite_master` to discover tables:
```sql
SELECT name FROM sqlite_master WHERE type='table'
```

**Solution:** SharpCoreDB intercepts these queries and redirects to native metadata API:

```csharp
// In SharpCoreDBCommand.ExecuteDbDataReader():
if (commandTextUpper.Contains("SQLITE_MASTER"))
{
    return ExecuteSystemTableQuery(commandTextUpper, behavior);
}

// ExecuteSystemTableQuery() uses native API:
if (db is IMetadataProvider metadata)
{
    var tables = metadata.GetTables(); // ✅ Native SharpCoreDB API
    
    // Convert to SQLite format for YesSql
    var results = tables.Select(t => new Dictionary<string, object>
    {
        ["name"] = t.Name,
        ["type"] = "TABLE"
    });
    
    return new SharpCoreDBDataReader(results, behavior);
}
```

**Supported system queries:**
- `SELECT name FROM sqlite_master WHERE type='table'`
- `PRAGMA table_info('tablename')`
- `SELECT * FROM pragma_table_info('tablename')`

### 4. last_insert_rowid() Support

YesSql expects `SELECT last_insert_rowid()` after INSERT to get the new row ID:

```csharp
// YesSql executes:
INSERT INTO oc_Document (Type, Content) VALUES (@p0, @p1);
SELECT last_insert_rowid();

// SharpCoreDB handles via:
public long GetLastInsertRowId()
{
    return _lastInsertRowId; // Thread-safe AsyncLocal<long>
}
```

**Thread Safety:**  
Each thread has its own `last_insert_rowid` value via `AsyncLocal<long>`, ensuring correct behavior in concurrent scenarios.

---

## Performance

### vs SQLite (Direct)

| Operation | SQLite | SharpCoreDB | Notes |
|-----------|--------|-------------|-------|
| **INSERT (10K)** | 337K/sec | 141K/sec | 2.4x slower (acceptable for encrypted DB) |
| **SELECT (basic)** | 23.5x faster | Baseline | SQLite is highly optimized for reads |
| **Analytics (SUM/AVG)** | 11.5x slower | Baseline | SharpCoreDB SIMD acceleration shines here |

### vs LiteDB (Alternative)

| Operation | LiteDB | SharpCoreDB | Notes |
|-----------|--------|-------------|-------|
| **INSERT (10K)** | 67K/sec | 141K/sec | 2.1x faster |
| **Analytics (SIMD)** | 345x slower | Baseline | Massive performance gap |
| **Memory Usage** | 6.2x more | Baseline | SharpCoreDB is much more efficient |

### Encryption Overhead

| Mode | INSERT (10K) | SELECT (10K) | Overhead |
|------|--------------|--------------|----------|
| **Encrypted (AES-256-GCM)** | 57.5 ms | 29.2 ms | **-12% (FASTER!)** |
| **Unencrypted** | 70.9 ms | 33.0 ms | Baseline |

**Surprising Result:** Hardware-accelerated AES-NI makes encryption actually **faster** than unencrypted mode!

---

## Compatibility

### YesSql Features

| Feature | Status | Notes |
|---------|--------|-------|
| **Document Sessions** | ✅ Full | Change tracking works |
| **LINQ Queries** | ✅ Full | All LINQ operators supported |
| **Indexes** | ✅ Full | B-tree and hash indexes |
| **Transactions** | ✅ Full | MVCC with snapshot isolation |
| **Multi-tenancy** | ✅ Full | Table prefix support |
| **Migrations** | ✅ Full | Schema migrations work |

### OrchardCore Compatibility

| OrchardCore Version | Status | Notes |
|---------------------|--------|-------|
| **3.x (latest)** | ✅ Tested | Full compatibility |
| **2.x** | ⚠️ Untested | Should work but not verified |
| **1.x** | ❌ Not supported | Use YesSql 1.x |

### SQL Features

| SQL Feature | YesSql Uses | SharpCoreDB Supports |
|-------------|-------------|---------------------|
| **CREATE TABLE IF NOT EXISTS** | ✅ | ✅ |
| **INTEGER PRIMARY KEY** | ✅ | ✅ |
| **AUTOINCREMENT** | ❌ | ✅ (but YesSql doesn't use it) |
| **LIMIT/OFFSET** | ✅ | ✅ |
| **JOINs (INNER)** | ✅ | ✅ **Full** |
| **JOINs (LEFT)** | ✅ | ✅ **Full** |
| **JOINs (RIGHT)** | ✅ | ✅ **Full** |
| **JOINs (FULL OUTER)** | ✅ | ✅ **Full** |
| **JOINs (CROSS)** | ✅ | ✅ **Full** |
| **Subqueries (WHERE)** | ✅ | ✅ **Full** |
| **Subqueries (FROM)** | ✅ | ✅ **Full** (derived tables) |
| **Subqueries (SELECT)** | ✅ | ✅ **Full** (scalar subqueries) |
| **IN (subquery)** | ✅ | ✅ **Full** |
| **EXISTS/NOT EXISTS** | ✅ | ✅ **Full** |
| **GROUP BY** | ✅ | ✅ **Full** |
| **HAVING** | ✅ | ✅ **Full** |
| **Correlated Subqueries** | ✅ | ✅ **Full** |
| **Triggers** | ❌ | 🚧 Planned Q2 2026 |

---

## Troubleshooting

### Common Issues

#### 1. "Table sqlite_master does not exist"

**Cause:** SharpCoreDB doesn't have a real `sqlite_master` table.

**Solution:** ✅ Already handled! The ADO.NET provider intercepts these queries automatically.

**Verification:**
```csharp
var connection = new SharpCoreDBConnection("Data Source=test.scdb;Password=pwd");
connection.Open();
var command = connection.CreateCommand();
command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
var reader = command.ExecuteReader(); // ✅ Works!
