<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Data.Provider

  **ADO.NET Data Provider for SharpCoreDB**

  [![NuGet Version](https://img.shields.io/nuget/v/SharpCoreDB.Data.Provider)](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)
  [![NuGet Downloads](https://img.shields.io/nuget/dt/SharpCoreDB.Data.Provider)](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![GitHub Stars](https://img.shields.io/github/stars/MPCoreDeveloper/SharpCoreDB)](https://github.com/MPCoreDeveloper/SharpCoreDB/stargazers)

</div>

---

## Overview

Complete ADO.NET Data Provider for **SharpCoreDB** — a high-performance encrypted embedded database engine.
Use the familiar `DbConnection` / `DbCommand` / `DbDataReader` APIs with SharpCoreDB's AES-256-GCM encryption, SIMD acceleration, and zero-config deployment.

### Features

| Feature | Details |
|---|---|
| **Full ADO.NET Compliance** | `DbConnection`, `DbCommand`, `DbDataReader`, `DbTransaction`, `DbDataAdapter`, `DbCommandBuilder`, `DbProviderFactory` |
| **Connection Pooling** | Built-in instance pooling with reference counting — multiple connections share one database instance |
| **Async Support** | `OpenAsync`, `CloseAsync`, `ExecuteNonQueryAsync`, `ExecuteScalarAsync`, `ExecuteReaderAsync` |
| **Parameterized Queries** | Named parameters (`@param`) with automatic type inference |
| **Transactions** | `BeginTransaction` / `Commit` / `Rollback` backed by SharpCoreDB's batch update mechanism |
| **Schema Discovery** | `GetSchema("Tables")`, `GetSchema("Columns")` via `IMetadataProvider` |
| **DI Registration** | `AddSharpCoreDBDataProvider()` extension for `IServiceCollection` |
| **Cross-Platform** | Windows, Linux, macOS (x64 and ARM64) |

---

## Installation

```bash
dotnet add package SharpCoreDB.Data.Provider
```

**Requirements:** .NET 10.0 or later.

---

## Connection String

| Key | Alias | Required | Description |
|---|---|---|---|
| `Path` | `Data Source` | **Yes** | File path to the `.scdb` database or directory |
| `Password` | — | **Yes** | Master password for AES-256-GCM encryption |
| `ReadOnly` | — | No | Open in read-only mode (`true` / `false`, default `false`) |
| `Cache` | — | No | Cache mode (`Shared` / `Private`, default `Private`) |

**Examples:**

```
Path=C:\data\mydb.scdb;Password=StrongPassword!
Data Source=./mydb.scdb;Password=secret;ReadOnly=true
Path=/var/lib/myapp/data.scdb;Password=s3cur3;Cache=Shared
```

### Connection String Builder

```csharp
var builder = new SharpCoreDBConnectionStringBuilder
{
    Path = @"C:\data\mydb.scdb",
    Password = "StrongPassword!",
    ReadOnly = false,
    Cache = "Private"
};

string connStr = builder.ConnectionString;
// "Path=C:\data\mydb.scdb;Password=StrongPassword!;ReadOnly=False;Cache=Private"
```

---

## Quick Start

### Open a Connection and Execute Queries

```csharp
using SharpCoreDB.Data.Provider;

const string connectionString = "Path=./mydb.scdb;Password=StrongPassword!";

using var connection = new SharpCoreDBConnection(connectionString);
connection.Open();

using var command = connection.CreateCommand();

// Create a table
command.CommandText = "CREATE TABLE users (id INT, name TEXT, age INT)";
command.ExecuteNonQuery();

// Insert data
command.CommandText = "INSERT INTO users VALUES (1, 'Alice', 30)";
command.ExecuteNonQuery();

// Query data
command.CommandText = "SELECT * FROM users";
using var reader = command.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"ID={reader.GetInt32(0)}, Name={reader.GetString(1)}, Age={reader.GetInt32(2)}");
}
```

### Async Usage

```csharp
using SharpCoreDB.Data.Provider;

const string connectionString = "Path=./mydb.scdb;Password=StrongPassword!";

await using var connection = new SharpCoreDBConnection(connectionString);
await connection.OpenAsync();

await using var command = new SharpCoreDBCommand("SELECT COUNT(*) FROM users", connection);
var count = await command.ExecuteScalarAsync();

Console.WriteLine($"Total users: {count}");
```

---

## Parameterized Queries

Use named parameters prefixed with `@` to prevent SQL injection:

```csharp
using var command = new SharpCoreDBCommand(
    "INSERT INTO users VALUES (@id, @name, @age)", connection);

command.Parameters.Add("@id", 2);
command.Parameters.Add("@name", "Bob");
command.Parameters.Add("@age", 25);

command.ExecuteNonQuery();
```

Or use `SharpCoreDBParameter` for explicit type control:

```csharp
command.Parameters.Add(new SharpCoreDBParameter("@salary", DbType.Decimal) { Value = 75000.00m });
```

Supported `DbType` mappings: `Int32`, `Int64`, `String`, `Boolean`, `DateTime`, `Decimal`, `Double`, `Single`, `Guid`, `Binary`, and ULID (stored as 26-character string).

---

## Transactions

Transactions are backed by SharpCoreDB's batch update mechanism. When the transaction is committed, all deferred index rebuilds and WAL flushes are performed atomically:

```csharp
using var connection = new SharpCoreDBConnection(connectionString);
connection.Open();

using var transaction = connection.BeginTransaction();

try
{
    using var cmd = new SharpCoreDBCommand(connection: connection)
    {
        Transaction = (SharpCoreDBTransaction)transaction
    };

    cmd.CommandText = "INSERT INTO accounts VALUES (1, 'Savings', 10000)";
    cmd.ExecuteNonQuery();

    cmd.CommandText = "INSERT INTO accounts VALUES (2, 'Checking', 5000)";
    cmd.ExecuteNonQuery();

    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

> **Note:** If a transaction is disposed without `Commit()`, it is automatically rolled back.

---

## DbProviderFactory

Register the provider for use with tooling that relies on `DbProviderFactory`:

```csharp
using System.Data.Common;
using SharpCoreDB.Data.Provider;

// Register once at startup
DbProviderFactories.RegisterFactory(
    "SharpCoreDB.Data.Provider",
    SharpCoreDBProviderFactory.Instance);

// Resolve via factory name
var factory = DbProviderFactories.GetFactory("SharpCoreDB.Data.Provider");
using var connection = factory.CreateConnection()!;
connection.ConnectionString = "Path=./mydb.scdb;Password=StrongPassword!";
connection.Open();
```

---

## Dependency Injection

### Basic Registration

```csharp
using SharpCoreDB.Data.Provider;

var builder = WebApplication.CreateBuilder(args);

// Register the provider factory
builder.Services.AddSharpCoreDBDataProvider();
```

### Registration with Default Connection String

```csharp
builder.Services.AddSharpCoreDBDataProvider(
    "Path=./mydb.scdb;Password=StrongPassword!");
```

This registers:
- `DbProviderFactory` as `SharpCoreDBProviderFactory`
- `SharpCoreDBConnection` (transient) pre-configured with the connection string
- `DbConnection` resolving to `SharpCoreDBConnection`

### Inject in a Service

```csharp
public class UserRepository(SharpCoreDBConnection connection)
{
    public async Task<int> GetUserCountAsync(CancellationToken ct = default)
    {
        await connection.OpenAsync(ct);

        await using var cmd = new SharpCoreDBCommand("SELECT COUNT(*) FROM users", connection);
        var result = await cmd.ExecuteScalarAsync(ct);

        return Convert.ToInt32(result);
    }
}
```

---

## Schema Discovery

Query table and column metadata through standard ADO.NET schema APIs:

```csharp
using var connection = new SharpCoreDBConnection(connectionString);
connection.Open();

// List tables
var tables = connection.GetSchema("Tables");
foreach (DataRow row in tables.Rows)
{
    Console.WriteLine($"Table: {row["TABLE_NAME"]}, Type: {row["TABLE_TYPE"]}");
}

// List columns for a specific table
var columns = connection.GetSchema("Columns", ["users"]);
foreach (DataRow row in columns.Rows)
{
    Console.WriteLine($"  {row["COLUMN_NAME"]} ({row["DATA_TYPE"]})");
}
```

Supported schema collections: `MetaDataCollections`, `Tables`, `Columns`.

---

## DataAdapter / DataSet

Fill a `DataTable` or `DataSet` using the standard adapter pattern:

```csharp
using var adapter = new SharpCoreDBDataAdapter(
    "SELECT * FROM users", connection);

var dataTable = new DataTable();
adapter.Fill(dataTable);

foreach (DataRow row in dataTable.Rows)
{
    Console.WriteLine($"{row["name"]} — age {row["age"]}");
}
```

Auto-generate INSERT/UPDATE/DELETE commands with `SharpCoreDBCommandBuilder`:

```csharp
using var adapter = new SharpCoreDBDataAdapter("SELECT * FROM users", connection);
using var builder = new SharpCoreDBCommandBuilder(adapter);

// builder.GetInsertCommand(), builder.GetUpdateCommand(), etc.
```

---

## Advanced: Direct Database Access

For scenarios that need access to the underlying engine (compiled queries, VACUUM, storage statistics):

```csharp
using var connection = new SharpCoreDBConnection(connectionString);
connection.Open();

// Access the IDatabase instance
var db = connection.DbInstance!;

// Compiled query for hot paths (5-10x faster)
var stmt = db.Prepare("SELECT * FROM users WHERE age > @age");
var results = db.ExecuteCompiledQuery(stmt, new() { ["age"] = 25 });

// VACUUM
var vacuumResult = await db.VacuumAsync(VacuumMode.Quick);

// Storage statistics
var stats = db.GetStorageStatistics();
Console.WriteLine($"Database size: {stats.TotalSizeBytes} bytes");
```

---

## Connection Pooling

The provider includes built-in instance pooling. Multiple `SharpCoreDBConnection` objects sharing the same connection string will reuse a single database instance, preventing file-locking issues:

```
Connection A ─┐
              ├─► Pooled IDatabase instance (ref count = 3)
Connection B ─┤
              │
Connection C ─┘
```

When the last connection is closed, the instance is flushed, saved, and disposed.

Call `SharpCoreDBInstancePool.Instance.Clear()` during application shutdown to force-release all pooled instances:

```csharp
// In Program.cs or a hosted service shutdown handler
SharpCoreDBInstancePool.Instance.Clear();
```

---

## Class Reference

| Class | Base Class | Description |
|---|---|---|
| `SharpCoreDBConnection` | `DbConnection` | Database connection with pooling |
| `SharpCoreDBCommand` | `DbCommand` | SQL command execution |
| `SharpCoreDBDataReader` | `DbDataReader` | Forward-only result reader |
| `SharpCoreDBTransaction` | `DbTransaction` | Transaction via batch updates |
| `SharpCoreDBParameter` | `DbParameter` | Query parameter with type inference |
| `SharpCoreDBParameterCollection` | `DbParameterCollection` | Parameter collection |
| `SharpCoreDBProviderFactory` | `DbProviderFactory` | Factory (singleton) |
| `SharpCoreDBDataAdapter` | `DbDataAdapter` | DataSet / DataTable adapter |
| `SharpCoreDBCommandBuilder` | `DbCommandBuilder` | Auto-generate DML commands |
| `SharpCoreDBConnectionStringBuilder` | `DbConnectionStringBuilder` | Build / parse connection strings |
| `SharpCoreDBException` | `Exception` | Provider-specific exception |
| `SharpCoreDBInstancePool` | — | Internal connection pool with ref counting |

---

## Performance

The provider inherits SharpCoreDB's performance characteristics:

- **345× faster analytics** than LiteDB with SIMD vectorization
- **11.5× faster** than SQLite for aggregations
- **AES-256-GCM encryption** with near-zero overhead
- **B-tree indexes** for O(log n) range queries
- **Compiled queries** for 5-10× faster repeated execution

For detailed benchmarks, see the [main repository](https://github.com/MPCoreDeveloper/SharpCoreDB).

---

## License

MIT License — see [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.

## Contributing

Contributions are welcome! Please see the [contributing guidelines](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/CONTRIBUTING.md).

## Support

- [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- [Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/wiki)
