<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Data.Provider

  **ADO.NET Data Provider for SharpCoreDB**

  **Version:** 1.7.2  
  **Status:** Production Ready ✅

  [![NuGet Version](https://img.shields.io/nuget/v/SharpCoreDB.Data.Provider)](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)
  [![NuGet Downloads](https://img.shields.io/nuget/dt/SharpCoreDB.Data.Provider)](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![GitHub Stars](https://img.shields.io/github/stars/MPCoreDeveloper/SharpCoreDB)](https://github.com/MPCoreDeveloper/SharpCoreDB/stargazers)

</div>

---


## Patch updates in v1.7.2

- ✅ Aligned package metadata and version references to the synchronized 1.7.2 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Overview

Complete ADO.NET Data Provider for **SharpCoreDB** — a high-performance encrypted embedded database engine. Use standard `DbConnection`, `DbCommand`, `DbDataReader` APIs with:

- ✅ **Full ADO.NET Compliance** - Standard interfaces
- ✅ **Connection Pooling** - Efficient resource management
- ✅ **Async Support** - Non-blocking operations
- ✅ **Parameterized Queries** - Safe from SQL injection
- ✅ **Transactions** - ACID compliance
- ✅ **Schema Discovery** - GetSchema() support
- ✅ **AES-256-GCM Encryption** - At rest
- ✅ **SIMD Acceleration** - Analytics queries
- ✅ **Phase 9 Analytics** - COUNT, AVG, STDDEV, PERCENTILE, window functions
- ✅ **Cross-Platform** - Windows, Linux, macOS

---

## Changes in v1.7.2

- Package version standardized to `v1.7.2`
- Documentation refreshed to align with current provider behavior
- Inherits core metadata durability and parser reliability fixes

---

## Installation

```bash
dotnet add package SharpCoreDB.Data.Provider --version 1.7.2
```

**Requirements:** .NET 10.0+

---

## Quick Start

### Basic Connection

```csharp
using SharpCoreDB.Data;

const string connectionString = "Data Source=./myapp.db;Password=SecurePassword!";

using var connection = new SharpCoreDBConnection(connectionString);
await connection.OpenAsync();

// Create command
using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM users WHERE age > @minAge";
command.Parameters.AddWithValue("@minAge", 18);

// Execute
using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"User: {reader["name"]}");
}
```

### With DbProviderFactory

```csharp
// Get factory
var factory = DbProviderFactories.GetFactory("SharpCoreDB");

// Create connection
using var connection = factory.CreateConnection();
connection.ConnectionString = "Data Source=./app.db;Password=secure!";
await connection.OpenAsync();
```

### With Dependency Injection

```csharp
services.AddSharpCoreDBDataProvider("Data Source=./app.db;Password=secure!");

// Inject DbConnection factory
public class UserRepository
{
    private readonly DbConnection _connection;

    public UserRepository(SharpCoreDBConnectionFactory factory)
    {
        _connection = factory.CreateConnection();
    }
}
```

---

## Features

- Full ADO.NET compatibility (`DbConnection`, `DbCommand`, `DbDataReader`)
- Async operations, transactions, and parameterized queries
- Connection pooling and schema discovery support
- Uses SharpCoreDB encryption and performance capabilities

---

## API Reference

### SharpCoreDBConnection

| Method | Purpose |
|--------|---------|
| `OpenAsync()` | Open connection |
| `CloseAsync()` | Close connection |
| `BeginTransactionAsync()` | Start transaction |
| `CreateCommand()` | Create command |
| `GetSchemaAsync(collection)` | Get schema information |
| `ChangeDatabase(name)` | Switch database |

### SharpCoreDBCommand

| Method | Purpose |
|--------|---------|
| `ExecuteNonQueryAsync()` | Execute INSERT/UPDATE/DELETE |
| `ExecuteScalarAsync()` | Get first cell result |
| `ExecuteReaderAsync()` | Get data reader |
| `PrepareAsync()` | Prepare command (optional) |

### SharpCoreDBDataReader

| Method | Purpose |
|--------|---------|
| `ReadAsync()` | Advance to next row |
| `GetValue(ordinal)` | Get value by index |
| `GetFieldValue<T>(ordinal)` | Get typed value |
| `IsDBNull(ordinal)` | Check for NULL |
| `GetOrdinal(name)` | Get column index by name |

---

## Common Patterns

### Repository with ADO.NET

```csharp
public class UserRepository
{
    private readonly string _connectionString;

    public UserRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<User> GetUserAsync(int id)
    {
        using var connection = new SharpCoreDBConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM users WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new User
            {
                Id = (int)reader["id"],
                Name = (string)reader["name"],
                Age = (int)reader["age"]
            };
        }

        return null;
    }
}
```

### DataSet Operations

```csharp
public async Task<DataSet> GetUserDataSetAsync()
{
    using var connection = new SharpCoreDBConnection(_connectionString);
    await connection.OpenAsync();

    using var adapter = new DbDataAdapter
    {
        SelectCommand = connection.CreateCommand()
        {
            CommandText = "SELECT id, name, age FROM users"
        }
    };

    var dataSet = new DataSet();
    await adapter.FillAsync(dataSet);
    return dataSet;
}
```

---

## Performance Tips

1. **Use Connection Pooling** - Default pool size of 5
2. **Parameterized Queries** - Prevent SQL injection and reuse plans
3. **Batch Operations** - Use DbDataAdapter for bulk changes
4. **Async All The Way** - Use ...Async() methods
5. **Close Readers** - Use `using` statements

---

## See Also

- **[Core SharpCoreDB](../SharpCoreDB/README.md)** - Database engine
- **[Extensions](../SharpCoreDB.Extensions/README.md)** - Dapper, repositories
- **[Entity Framework Core](../SharpCoreDB.EntityFrameworkCore/README.md)** - EF Core provider
- **[User Manual](../../docs/USER_MANUAL.md)** - Complete guide

---

## License

MIT License - See [LICENSE](../../LICENSE)

---

**Last Updated:** April 26, 2026 | Version 1.7.2

