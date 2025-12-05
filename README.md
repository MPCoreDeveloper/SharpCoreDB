# SharpCoreDB

A lightweight, encrypted, file-based database engine for .NET that supports SQL operations with built-in security features. Perfect for time-tracking, invoicing, and project management applications.

## Features

### Core Database Features
- **SQL Support**: Execute common SQL commands including CREATE TABLE, INSERT, SELECT, UPDATE, and DELETE
- **AES-256-GCM Encryption**: All data is encrypted at rest using industry-standard encryption
- **Write-Ahead Logging (WAL)**: Ensures durability and crash recovery
- **User Authentication**: Built-in user management with secure password hashing
- **Multiple Data Types**: Support for INTEGER, TEXT, REAL, BLOB, BOOLEAN, DATETIME, LONG, DECIMAL, ULID, and GUID
- **Auto-Generated Fields**: Automatic generation of ULID and GUID values
- **Primary Key Support**: Define primary keys for data integrity
- **JOIN Operations**: Support for INNER JOIN and LEFT JOIN queries
- **Readonly Mode**: Open databases in readonly mode for safe concurrent access
- **Dependency Injection**: Seamless integration with Microsoft.Extensions.DependencyInjection
- **B-Tree Indexing**: Efficient data indexing using B-tree data structures

### New Production-Ready Features
- **Connection Pooling**: Built-in DatabasePool class for connection reuse and resource management
- **Connection Strings**: Parse and build connection strings with ConnectionStringBuilder
- **Auto Maintenance**: Automatic VACUUM and WAL checkpointing with AutoMaintenanceService
- **UPSERT Support**: INSERT OR REPLACE and INSERT ON CONFLICT DO UPDATE syntax
- **INDEX Support**: CREATE INDEX with automatic usage in SELECT WHERE queries
- **EXPLAIN Plans**: Query plan analysis with EXPLAIN command
- **Date/Time Functions**: NOW(), DATE(), STRFTIME(), DATEADD() functions
- **Aggregate Functions**: SUM(), AVG(), COUNT(DISTINCT), GROUP_CONCAT()
- **PRAGMA Commands**: table_info(), index_list(), foreign_key_list() for metadata queries

### Extensions Package (SharpCoreDB.Extensions)
- **Dapper Integration**: IDbConnection wrapper for using Dapper with SharpCoreDB
- **Health Checks**: Built-in health check provider for ASP.NET Core
- **Monitoring**: Database health and connectivity checks

### Benchmarks
- **Performance Testing**: Compare SharpCoreDB with SQLite, LiteDB, and DuckDB
- **Time-Tracking Scenarios**: Specialized benchmarks for time-tracking applications

## Installation

Add the SharpCoreDB project to your solution and reference it from your application.

## Usage

### Setting Up the Database

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// Set up Dependency Injection
var services = new ServiceCollection();
services.AddSharpCoreDB();
var serviceProvider = services.BuildServiceProvider();

// Get the Database Factory
var factory = serviceProvider.GetRequiredService<DatabaseFactory>();

// Create a database instance
string dbPath = "/path/to/database";
string masterPassword = "yourMasterPassword";
var db = factory.Create(dbPath, masterPassword);
```

### Creating Tables

```csharp
// Create a simple table
db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");

// Create a table with various data types and primary key
db.ExecuteSQL("CREATE TABLE products (id INTEGER PRIMARY KEY, name TEXT, active BOOLEAN, created DATETIME, price DECIMAL, ulid ULID AUTO, guid GUID AUTO)");
```

### Inserting Data

```csharp
// Insert with all values
db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");

// Insert with specific columns (auto-generated fields will be filled automatically)
db.ExecuteSQL("INSERT INTO products (id, name, price) VALUES ('1', 'Widget', '19.99')");
```

### Querying Data

```csharp
// Select all records
db.ExecuteSQL("SELECT * FROM users");

// Select with WHERE clause
db.ExecuteSQL("SELECT * FROM products WHERE active = 'true'");

// Select with ORDER BY
db.ExecuteSQL("SELECT * FROM products ORDER BY name ASC");

// JOIN queries
db.ExecuteSQL("SELECT products.name, users.name FROM products JOIN users ON products.id = users.id");

// LEFT JOIN
db.ExecuteSQL("SELECT products.name, users.name FROM products LEFT JOIN users ON products.id = users.id");
```

### Updating Data

```csharp
db.ExecuteSQL("UPDATE products SET name = 'Updated Widget' WHERE id = '1'");
```

### Deleting Data

```csharp
db.ExecuteSQL("DELETE FROM products WHERE id = '1'");
```

### Readonly Mode

```csharp
// Open database in readonly mode (allows dirty reads, prevents modifications)
var dbReadonly = factory.Create(dbPath, masterPassword, isReadOnly: true);
dbReadonly.ExecuteSQL("SELECT * FROM users"); // Works
// dbReadonly.ExecuteSQL("INSERT INTO users VALUES ('2', 'Bob')"); // Throws exception
```

### User Management

```csharp
// Create a user
db.CreateUser("username", "password");

// Login
bool success = db.Login("username", "password");
```

### Connection Pooling

```csharp
using SharpCoreDB.Services;

// Create a database pool
var pool = new DatabasePool(services, maxPoolSize: 10);

// Get a database from the pool (reuses existing instances)
var db1 = pool.GetDatabase(dbPath, masterPassword);
var db2 = pool.GetDatabase(dbPath, masterPassword); // Same instance as db1

// Return database to pool when done
pool.ReturnDatabase(db1);

// Get pool statistics
var stats = pool.GetPoolStatistics();
Console.WriteLine($"Total: {stats["TotalConnections"]}, Active: {stats["ActiveConnections"]}");
```

### Connection Strings

```csharp
using SharpCoreDB.Services;

// Parse a connection string
var connectionString = "Data Source=app.sharpcoredb;Password=MySecret123;ReadOnly=False;Cache=Shared";
var builder = new ConnectionStringBuilder(connectionString);

// Access parsed properties
Console.WriteLine($"Database: {builder.DataSource}");
Console.WriteLine($"Read-only: {builder.ReadOnly}");

// Build a connection string
var newBuilder = new ConnectionStringBuilder
{
    DataSource = "myapp.db",
    Password = "secret",
    ReadOnly = false,
    Cache = "Private"
};
string connStr = newBuilder.BuildConnectionString();
```

### Auto Maintenance

```csharp
using SharpCoreDB.Services;

// Set up automatic maintenance (VACUUM and WAL checkpointing)
using var maintenance = new AutoMaintenanceService(
    db, 
    intervalSeconds: 300,      // Run every 5 minutes
    writeThreshold: 1000       // Or after 1000 writes
);

// Maintenance runs automatically in background
db.ExecuteSQL("INSERT INTO users VALUES ('1', 'Alice')");
maintenance.IncrementWriteCount(); // Track writes

// Manually trigger maintenance
maintenance.TriggerMaintenance();
```

### CREATE INDEX

```csharp
// Create an index for faster queries
db.ExecuteSQL("CREATE INDEX idx_user_email ON users (email)");

// Create a unique index
db.ExecuteSQL("CREATE UNIQUE INDEX idx_user_id ON users (id)");

// Queries automatically use indexes when available
db.ExecuteSQL("SELECT * FROM users WHERE email = 'user@example.com'");
```

### EXPLAIN Query Plans

```csharp
// See how a query will be executed
db.ExecuteSQL("EXPLAIN SELECT * FROM users WHERE email = 'test@example.com'");
// Output: QUERY PLAN, shows if index is used or full table scan
```

### PRAGMA Commands

```csharp
// Get table schema information
db.ExecuteSQL("PRAGMA table_info(users)");

// List indexes on a table
db.ExecuteSQL("PRAGMA index_list(users)");

// List foreign keys (when implemented)
db.ExecuteSQL("PRAGMA foreign_key_list(orders)");
```

### Date/Time Functions

```csharp
// Use built-in date functions in queries
db.ExecuteSQL("INSERT INTO logs VALUES ('1', NOW())");
db.ExecuteSQL("SELECT * FROM logs WHERE DATE(timestamp) = '2024-01-15'");

// Format dates
db.ExecuteSQL("SELECT STRFTIME(created, 'yyyy-MM-dd') FROM users");

// Date arithmetic
db.ExecuteSQL("SELECT * FROM subscriptions WHERE DATEADD(created, 30, 'days') > NOW()");
```

### Aggregate Functions

```csharp
// Use aggregate functions in queries
db.ExecuteSQL("SELECT SUM(amount) FROM transactions");
db.ExecuteSQL("SELECT AVG(duration) FROM time_entries");
db.ExecuteSQL("SELECT COUNT(DISTINCT user_id) FROM sessions");
db.ExecuteSQL("SELECT GROUP_CONCAT(tags, '|') FROM posts");
```

### UPSERT Operations

```csharp
// Insert or replace if exists
db.ExecuteSQL("INSERT OR REPLACE INTO users VALUES ('1', 'Alice', 'alice@example.com')");

// Insert on conflict with update
db.ExecuteSQL("INSERT INTO users (id, name, email) VALUES ('1', 'Alice', 'alice@example.com') ON CONFLICT DO UPDATE");
```

### Dapper Integration (SharpCoreDB.Extensions)

```csharp
using SharpCoreDB.Extensions;
using Dapper;

// Get a Dapper-compatible connection
var connection = db.GetDapperConnection();

// Use Dapper queries
var users = connection.Query<User>("SELECT * FROM users WHERE active = true");
```

### Health Checks (SharpCoreDB.Extensions)

```csharp
using SharpCoreDB.Extensions;
using Microsoft.Extensions.DependencyInjection;

// Add health check to ASP.NET Core
services.AddHealthChecks()
    .AddSharpCoreDB(db, "sharpcoredb", testQuery: "SELECT 1");

// Health check endpoint will verify database connectivity
```

## Supported Data Types

| Type | Description |
|------|-------------|
| INTEGER | 32-bit integer |
| TEXT | String value |
| REAL | Double-precision floating point |
| BLOB | Binary data |
| BOOLEAN | True/false value |
| DATETIME | Date and time value |
| LONG | 64-bit integer |
| DECIMAL | High-precision decimal |
| ULID | Universally Unique Lexicographically Sortable Identifier |
| GUID | Globally Unique Identifier |

## Requirements

- .NET 10.0 or later

## License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.
