# SharpCoreDB.Client v1.5.0 - .NET Client Library

**ADO.NET-Style Client for SharpCoreDB Network Server**

High-performance .NET client library for connecting to SharpCoreDB.Server with familiar ADO.NET patterns.

## 🚀 Key Features

✅ **ADO.NET-Style API** - Familiar patterns for .NET developers  
✅ **Full Async/Await** - Modern C# 14 async patterns  
✅ **gRPC Protocol** - High-performance primary protocol  
✅ **Connection Pooling** - Efficient connection reuse  
✅ **Type Safety** - Strong typing throughout  
✅ **Transaction Support** - BEGIN, COMMIT, ROLLBACK  
✅ **Parameter Binding** - SQL injection protection  
✅ **Connection Strings** - Standard connection string format  

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Client
```

## 💻 Quick Start

```csharp
using SharpCoreDB.Client;

// Connect to SharpCoreDB server
await using var connection = new SharpCoreDBConnection(
    "Server=localhost;Port=5001;Database=mydb;SSL=true;Username=admin;Password=***"
);
await connection.OpenAsync();

// Execute query
await using var command = new SharpCoreDBCommand("SELECT * FROM users WHERE age > @age", connection);
command.Parameters.Add("@age", 21);

await using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"Name: {reader["name"]}, Age: {reader["age"]}");
}
```

## 🔗 API Reference

### SharpCoreDBConnection

```csharp
// Connection string format
var connectionString = "Server=localhost;Port=5001;Database=mydb;SSL=true;Username=admin;Password=***";
var connection = new SharpCoreDBConnection(connectionString);

// Open/close
await connection.OpenAsync();
await connection.CloseAsync();

// Properties
string serverVersion = connection.ServerVersion;
string sessionId = connection.SessionId;
ConnectionState state = connection.State;

// Ping server
TimeSpan latency = await connection.PingAsync();
```

### SharpCoreDBCommand

```csharp
var command = new SharpCoreDBCommand("INSERT INTO users (name, age) VALUES (@name, @age)", connection);

// Parameters (SQL injection protection)
command.Parameters.Add("@name", "Alice");
command.Parameters.Add("@age", 30);

// Execute methods
var reader = await command.ExecuteReaderAsync();
int affected = await command.ExecuteNonQueryAsync();
object scalar = await command.ExecuteScalarAsync();
```

### SharpCoreDBDataReader

```csharp
await using var reader = await command.ExecuteReaderAsync();

while (await reader.ReadAsync())
{
    // Access by name
    string name = reader.GetString("name");
    int age = reader.GetInt32("age");
    
    // Or by index
    var value = reader[0];
    
    // Check for null
    bool isNull = reader.IsDBNull(1);
}
```

## 🔐 Connection String Options

| Parameter | Description | Required | Default |
|-----------|-------------|----------|---------|
| Server | Server hostname or IP | Yes | - |
| Port | gRPC port | No | 5001 |
| Database | Database name | No | master |
| SSL | Enable TLS/SSL | No | true |
| Username | Authentication username | No | - |
| Password | Authentication password | No | - |
| Timeout | Connection timeout (seconds) | No | 30 |
| PreferHttp3 | Use HTTP/3 if available | No | false |

## 📊 Performance

- **Connection Pooling** - Automatic connection reuse
- **Sub-millisecond Latency** - 0.8-1.2ms query latency
- **Zero-Copy Operations** - gRPC streaming optimization
- **Minimal Allocations** - C# 14 performance features

## 🔄 Transaction Support

```csharp
await using var connection = new SharpCoreDBConnection(connectionString);
await connection.OpenAsync();

// Begin transaction
await using var transaction = await connection.BeginTransactionAsync();

try
{
    // Execute commands in transaction
    await using var cmd1 = new SharpCoreDBCommand("INSERT INTO users ...", connection);
    await cmd1.ExecuteNonQueryAsync();
    
    await using var cmd2 = new SharpCoreDBCommand("UPDATE users ...", connection);
    await cmd2.ExecuteNonQueryAsync();
    
    // Commit
    await transaction.CommitAsync();
}
catch
{
    // Rollback on error
    await transaction.RollbackAsync();
    throw;
}
```

## 🧪 Testing Support

```csharp
// Mock-friendly interfaces
public interface ISharpCoreDBConnection : IAsyncDisposable
{
    Task OpenAsync(CancellationToken cancellationToken = default);
    Task<ISharpCoreDBCommand> CreateCommandAsync(string sql);
    // ...
}
```

## 🌐 Multi-Language Support

**Python Client:**
```bash
pip install pysharpcoredb
```

**JavaScript/TypeScript:**
```bash
npm install @sharpcoredb/client
```

## 📚 Documentation

**Client Guide:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/CLIENT_GUIDE.md

**Server Setup:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/QUICKSTART.md

**Full Documentation:** https://github.com/MPCoreDeveloper/SharpCoreDB

## 🏆 Why SharpCoreDB.Client?

✅ **Familiar API** - ADO.NET patterns you already know  
✅ **Modern C# 14** - Latest language features  
✅ **High Performance** - gRPC protocol, connection pooling  
✅ **Type Safe** - Strong typing throughout  
✅ **Production Ready** - Tested with 1,468+ tests  
✅ **Async First** - Full async/await support  

## 📦 Related Packages

- **SharpCoreDB.Server** - Network database server
- **SharpCoreDB** - Core embedded database engine
- **SharpCoreDB.Analytics** - Advanced analytics functions
- **SharpCoreDB.VectorSearch** - Vector search capabilities

## 📄 License

MIT License - See https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE

---

**Version:** 1.5.0 | **Release Date:** March 8, 2026 | **Status:** ✅ Production Ready
