# SharpCoreDB ADO.NET Data Provider

[![NuGet Version](https://img.shields.io/nuget/v/SharpCoreDB.Data.Provider)](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)
[![NuGet Downloads](https://img.shields.io/nuget/dt/SharpCoreDB.Data.Provider)](https://www.nuget.org/packages/SharpCoreDB.Data.Provider)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
[![GitHub Stars](https://img.shields.io/github/stars/MPCoreDeveloper/SharpCoreDB)](https://github.com/MPCoreDeveloper/SharpCoreDB/stargazers)
[![GitHub Issues](https://img.shields.io/github/issues/MPCoreDeveloper/SharpCoreDB)](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

Complete ADO.NET Data Provider for SharpCoreDB - enables seamless integration and standard ADO.NET usage with the high-performance SharpCoreDB embedded database.

## Features

- **Full ADO.NET Compliance**: Implements `DbConnection`, `DbCommand`, `DbDataReader`, and more
- **High Performance**: Leverages SharpCoreDB's SIMD-accelerated analytics and B-tree indexes
- **Enterprise Security**: AES-256-GCM encryption with zero overhead
- **Cross-Platform**: Supports Windows, Linux, and macOS
- **Easy Integration**: Drop-in replacement for other ADO.NET providers

## Installation

Install the package via NuGet:

```bash
dotnet add package SharpCoreDB.Data.Provider
```

## Usage

### Basic Connection and Query

```csharp
using System.Data.Common;
using SharpCoreDB.Data.Provider;

// Create connection
var connectionString = "Data Source=./mydb.db;Password=StrongPassword!";
using var connection = new SharpCoreDBConnection(connectionString);
connection.Open();

// Create command
using var command = connection.CreateCommand();
command.CommandText = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)";

// Execute DDL
command.ExecuteNonQuery();

// Insert data
command.CommandText = "INSERT INTO users VALUES (1, 'Alice', 30)";
command.ExecuteNonQuery();

// Query data
command.CommandText = "SELECT * FROM users";
using var reader = command.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"ID: {reader.GetInt32(0)}, Name: {reader.GetString(1)}, Age: {reader.GetInt32(2)}");
}
```

### Using with DbProviderFactory

```csharp
using System.Data.Common;

// Register the provider
DbProviderFactories.RegisterFactory("SharpCoreDB.Data.Provider", SharpCoreDBProviderFactory.Instance);

// Use factory
var factory = DbProviderFactories.GetFactory("SharpCoreDB.Data.Provider");
using var connection = factory.CreateConnection();
connection.ConnectionString = "Data Source=./mydb.db;Password=StrongPassword!";
connection.Open();

// Rest of the code is standard ADO.NET
```

### Advanced Features

Leverage SharpCoreDB's advanced features through the provider:

```csharp
// B-tree indexes for fast range queries
command.CommandText = "CREATE INDEX idx_age ON users(age) USING BTREE";
command.ExecuteNonQuery();

// SIMD-accelerated analytics
command.CommandText = "SELECT AVG(age), SUM(age) FROM users";
using var reader = command.ExecuteReader();
if (reader.Read())
{
    var avgAge = reader.GetDouble(0);
    var sumAge = reader.GetDouble(1);
}
```

## Performance

This provider inherits SharpCoreDB's exceptional performance:

- **345x faster analytics** than LiteDB with SIMD vectorization
- **11.5x faster** than SQLite for aggregations
- **AES-256-GCM encryption** with 0% overhead
- **B-tree indexes** for O(log n) range queries

For detailed benchmarks, see the [main SharpCoreDB repository](https://github.com/MPCoreDeveloper/SharpCoreDB).

## Requirements

- .NET 10.0 or later
- C# 14

## License

MIT License - see [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.

## Contributing

Contributions are welcome! Please see the [contributing guidelines](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/CONTRIBUTING.md).

## Support

- [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- [Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/wiki)
