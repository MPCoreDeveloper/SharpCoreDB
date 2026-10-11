# SharpCoreDB Server — .NET Client Guide

**Package:** `SharpCoreDB.Client`  
**Protocol:** gRPC (primary, recommended) · REST (secondary)  
**Target:** .NET 11 / C# 15

---

## Installation

```bash
dotnet add package SharpCoreDB.Client
```

---

## Connection

```csharp
using SharpCoreDB.Client;

// Connection string format
await using var connection = new SharpCoreDBConnection(
    "Server=localhost;Port=5001;Database=appdb;Username=admin;Password=secret;SslMode=Required");

await connection.OpenAsync();

// Connection is now authenticated with a gRPC session
Console.WriteLine($"Session: {connection.SessionId}");
Console.WriteLine($"Server version: {connection.ServerVersion}");
```

### Connection String Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `Server` | `localhost` | Server hostname or IP |
| `Port` | `5001` | gRPC port |
| `Database` | `master` | Target database name |
| `Username` | `anonymous` | Authentication username |
| `Password` | — | Authentication password |
| `SslMode` | `Required` | TLS mode: `Required` (always) |
| `Timeout` | `30000` | Connection timeout (ms) |

---

## Queries (SELECT)

### Basic Query

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT * FROM users WHERE age > 25";

await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var id = reader.GetInt32(0);
    var name = reader.GetString(1);
    var age = reader.GetInt32(2);
    Console.WriteLine($"  {id}: {name} ({age})");
}
```

### Column Access by Name

```csharp
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"Name: {reader["name"]}, Email: {reader["email"]}");
}
```

### Scalar Query

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT COUNT(*) FROM users";
var count = await cmd.ExecuteScalarAsync();
Console.WriteLine($"Total users: {count}");
```

---

## Non-Query (INSERT / UPDATE / DELETE)

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "INSERT INTO users VALUES (42, 'Eve', 'eve@example.com')";
var affected = await cmd.ExecuteNonQueryAsync();
Console.WriteLine($"Rows affected: {affected}");
```

### With Parameters

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "INSERT INTO users VALUES (@id, @name, @email)";
cmd.AddParameter("@id", 42);
cmd.AddParameter("@name", "Eve");
cmd.AddParameter("@email", "eve@example.com");
await cmd.ExecuteNonQueryAsync();
```

---

## Transactions

```csharp
// Begin transaction
await using var tx = await connection.BeginTransactionAsync();

try
{
    using var cmd1 = connection.CreateCommand();
    cmd1.CommandText = "INSERT INTO orders VALUES (1, 'Widget', 10)";
    await cmd1.ExecuteNonQueryAsync();

    using var cmd2 = connection.CreateCommand();
    cmd2.CommandText = "UPDATE inventory SET stock = stock - 10 WHERE product = 'Widget'";
    await cmd2.ExecuteNonQueryAsync();

    // Commit both operations
    await tx.CommitAsync();
}
catch
{
    // Automatic rollback on dispose if not committed
    // Or explicit: await tx.RollbackAsync();
    throw;
}
```

---

## Command Timeout

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = "SELECT * FROM large_table";
cmd.CommandTimeout = 60000; // 60 seconds
await using var reader = await cmd.ExecuteReaderAsync();
```

---

## Supported Parameter Types

| C# Type | Protocol Type | Example |
|---------|---------------|---------|
| `int`, `long` | `int64_value` | `cmd.AddParameter("@id", 42)` |
| `double`, `float` | `double_value` | `cmd.AddParameter("@price", 9.99)` |
| `string` | `string_value` | `cmd.AddParameter("@name", "Alice")` |
| `bool` | `bool_value` | `cmd.AddParameter("@active", true)` |
| `DateTime` | `timestamp_value` | `cmd.AddParameter("@created", DateTime.UtcNow)` |
| `Guid` | `guid_value` | `cmd.AddParameter("@id", Guid.NewGuid())` |
| `float[]` | `vector_value` | `cmd.AddParameter("@vec", new float[] { 0.1f, 0.2f })` |

---

## Error Handling

```csharp
using Grpc.Core;

try
{
    await using var reader = await cmd.ExecuteReaderAsync();
    // ...
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
{
    Console.WriteLine("Session expired — reconnect required");
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.Internal)
{
    Console.WriteLine($"SQL error: {ex.Status.Detail}");
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted)
{
    Console.WriteLine("Rate limited — retry after delay");
    await Task.Delay(1000);
}
```

### gRPC Status Codes

| Code | Meaning | Action |
|------|---------|--------|
| `OK` | Success | — |
| `Unauthenticated` | Invalid/expired session | Reconnect |
| `Internal` | SQL error or server error | Check SQL syntax |
| `ResourceExhausted` | Rate limited | Retry with backoff |
| `Unavailable` | Server unreachable | Retry with exponential backoff |

---

## Full Example

```csharp
using SharpCoreDB.Client;

// Connect
await using var conn = new SharpCoreDBConnection(
    "Server=localhost;Port=5001;Database=appdb;Username=admin;SslMode=Required");
await conn.OpenAsync();

// Create table
using var create = conn.CreateCommand();
create.CommandText = "CREATE TABLE IF NOT EXISTS products (id INTEGER, name TEXT, price REAL)";
await create.ExecuteNonQueryAsync();

// Batch insert
string[] inserts =
[
    "INSERT INTO products VALUES (1, 'Laptop', 999.99)",
    "INSERT INTO products VALUES (2, 'Mouse', 29.99)",
    "INSERT INTO products VALUES (3, 'Keyboard', 79.99)",
];
foreach (var sql in inserts)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}

// Query
using var query = conn.CreateCommand();
query.CommandText = "SELECT * FROM products WHERE price < 100";
await using var reader = await query.ExecuteReaderAsync();

Console.WriteLine("Products under $100:");
while (await reader.ReadAsync())
{
    Console.WriteLine($"  {reader["id"]}: {reader["name"]} - ${reader["price"]}");
}

// Count
using var count = conn.CreateCommand();
count.CommandText = "SELECT COUNT(*) FROM products";
var total = await count.ExecuteScalarAsync();
Console.WriteLine($"\nTotal products: {total}");
```

---

**See Also:** [Quick Start](QUICKSTART.md) · [REST API Reference](REST_API.md) · [Security Guide](SECURITY.md)
