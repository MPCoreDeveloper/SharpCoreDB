# SharpCoreDB Dapper Integration

A comprehensive Dapper integration for SharpCoreDB, providing a rich set of features for database operations with modern C# patterns.

## Features

### ? Core Functionality
- **DbConnection & DbCommand Implementation** - Full ADO.NET compatibility
- **DbDataReader Support** - Stream query results efficiently
- **Transaction Management** - Full transaction support with savepoints
- **Parameter Handling** - Advanced type mapping and parameter conversion

### ? Query Operations
- **Synchronous & Asynchronous Queries** - Full async/await support
- **Typed Queries** - Strongly-typed result mapping
- **Dynamic Queries** - Flexible dynamic result handling
- **Pagination** - Built-in pagination support with metadata
- **Multi-Result Sets** - Handle multiple result sets from queries

### ? Bulk Operations
- **Bulk Insert** - High-performance batch inserts
- **Bulk Update** - Efficient batch updates
- **Bulk Delete** - Mass delete operations
- **Upsert** - Insert or update in one operation

### ? Repository Pattern
- **Generic Repository** - CRUD operations out of the box
- **Read-Only Repository** - For query-only scenarios
- **Unit of Work** - Transaction management across repositories
- **Custom Queries** - Support for custom SQL in repositories

### ? Performance Monitoring
- **Query Metrics** - Track execution time, memory usage, row counts
- **Performance Reports** - Aggregate performance statistics
- **Timeout Warnings** - Alert on slow queries
- **Batch Metrics** - Monitor batch operation performance

### ? Advanced Mapping
- **Custom Mappers** - Define custom mapping logic
- **Multi-Table Joins** - Map complex join results
- **Type Conversion** - Automatic type conversion
- **Column Mapping** - Map database columns to properties

## Installation

```bash
dotnet add package SharpCoreDB.Extensions
```

## Quick Start

### Basic Query
```csharp
using SharpCoreDB.Extensions;

// Get Dapper-compatible connection
using var connection = database.GetDapperConnection();
connection.Open();

// Execute query
var users = connection.Query<User>("SELECT * FROM Users");
```

### Async Query
```csharp
// Using extension methods
var users = await database.QueryAsync<User>(
    "SELECT * FROM Users WHERE Age > @MinAge",
    new { MinAge = 18 });
```

### Repository Pattern
```csharp
var userRepository = new DapperRepository<User, int>(database, "Users");

// CRUD operations
var user = userRepository.GetById(1);
userRepository.Insert(new User { Name = "Alice" });
userRepository.Update(user);
userRepository.Delete(1);

// Async operations
var users = await userRepository.GetAllAsync();
```

### Bulk Operations
```csharp
var users = new List<User> { /* ... */ };

// Bulk insert
var inserted = database.BulkInsert("Users", users, batchSize: 1000);

// Bulk update
var updated = database.BulkUpdate("Users", users, "Id", batchSize: 1000);
```

### Performance Monitoring
```csharp
var result = database.QueryWithMetrics<User>(
    "SELECT * FROM Users",
    queryName: "GetAllUsers");

Console.WriteLine($"Execution: {result.Metrics.ExecutionTime.TotalMilliseconds}ms");
Console.WriteLine($"Rows: {result.Metrics.RowCount}");

// Get performance report
var report = DapperPerformanceExtensions.GetPerformanceReport();
Console.WriteLine($"Average query time: {report.AverageExecutionTime}");
```

### Transactions
```csharp
using var connection = database.GetDapperConnection();
connection.Open();
using var transaction = connection.BeginTransaction();

try
{
    connection.Execute("INSERT INTO Users ...", transaction: transaction);
    connection.Execute("UPDATE Orders ...", transaction: transaction);
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### Unit of Work
```csharp
using var uow = new DapperUnitOfWork(database);

try
{
    uow.BeginTransaction();
    
    var userRepo = uow.GetRepository<User, int>("Users");
    var orderRepo = uow.GetRepository<Order, int>("Orders");
    
    userRepo.Insert(new User { Name = "Alice" });
    orderRepo.Insert(new Order { Total = 99.99m });
    
    uow.Commit();
}
catch
{
    uow.Rollback();
    throw;
}
```

### Pagination
```csharp
var page = await database.QueryPagedAsync<User>(
    "SELECT * FROM Users",
    pageNumber: 1,
    pageSize: 20);

Console.WriteLine($"Page {page.PageNumber} of {page.TotalPages}");
Console.WriteLine($"Has next: {page.HasNextPage}");
```

### Custom Mapping
```csharp
var users = database.QueryWithMapping(
    "SELECT * FROM Users",
    row => new User
    {
        Id = Convert.ToInt32(row["Id"]),
        Name = row["Name"].ToString()
    });
```

### Multi-Table Joins
```csharp
var results = database.QueryMultiMapped<User, Order, UserWithOrders>(
    @"SELECT u.*, o.*
      FROM Users u
      INNER JOIN Orders o ON u.Id = o.UserId",
    (user, order) => new UserWithOrders { User = user, Order = order },
    splitOn: "Id");
```

## API Reference

### Connection Extensions
- `GetDapperConnection()` - Get an IDbConnection for Dapper

### Async Query Extensions
- `QueryAsync<T>()` - Execute query and return typed results
- `QueryFirstOrDefaultAsync<T>()` - Get first result or default
- `QuerySingleAsync<T>()` - Get single result
- `ExecuteAsync()` - Execute command and return affected rows
- `ExecuteScalarAsync<T>()` - Execute scalar query
- `QueryMultipleAsync()` - Execute multiple queries
- `QueryPagedAsync<T>()` - Execute paginated query

### Bulk Operations
- `BulkInsert<T>()` - Bulk insert entities
- `BulkInsertAsync<T>()` - Async bulk insert
- `BulkUpdate<T>()` - Bulk update entities
- `BulkDelete<TKey>()` - Bulk delete by keys
- `BulkUpsert<T>()` - Upsert entities

### Mapping Extensions
- `QueryWithMapping<T>()` - Query with custom mapper
- `QueryMapped<T>()` - Query with automatic mapping
- `QueryMultiMapped<T1, T2, TResult>()` - Multi-table join mapping
- `MapToType<T>()` - Map dictionary to type
- `MapToDictionary()` - Map object to dictionary

### Performance Extensions
- `QueryWithMetrics<T>()` - Query with performance tracking
- `ExecuteWithMetrics()` - Command with performance tracking
- `QueryWithTimeout<T>()` - Query with timeout warning
- `GetPerformanceReport()` - Get aggregate performance stats

### Repository Pattern
- `DapperRepository<TEntity, TKey>` - Generic CRUD repository
- `ReadOnlyDapperRepository<TEntity, TKey>` - Query-only repository
- `DapperUnitOfWork` - Transaction coordinator

## Type Mapping

The integration includes comprehensive type mapping:

- **Numeric Types**: byte, short, int, long, float, double, decimal
- **Text Types**: string, char
- **Date/Time**: DateTime, DateTimeOffset, TimeSpan
- **Binary**: byte[]
- **Other**: bool, Guid, enum

Automatic conversion between compatible types with proper null handling.

## Performance Tips

1. **Use Bulk Operations** - For inserting/updating multiple records
2. **Enable Pagination** - For large result sets
3. **Monitor Performance** - Use metrics to identify slow queries
4. **Use Transactions** - For related operations
5. **Async Operations** - For I/O-bound operations

## Health Checks

SharpCoreDB.Extensions provides comprehensive health check support with multiple configuration options:

### Basic Health Check
```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(
        database,
        name: "sharpcoredb",
        testQuery: "SELECT 1");
```

### Lightweight Health Check (Connection Only)
```csharp
services.AddHealthChecks()
    .AddSharpCoreDBLightweight(
        database,
        name: "sharpcoredb-lite",
        tags: new[] { "database", "quick" });
```

### Comprehensive Health Check (All Diagnostics)
```csharp
services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(
        database,
        name: "sharpcoredb-detailed",
        tags: new[] { "database", "detailed" });
```

### Custom Configuration
```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(
        database,
        configure: options =>
        {
            options.TestQuery = "SELECT 1";
            options.CheckQueryCache = true;
            options.CheckPerformanceMetrics = true;
            options.DegradedThresholdMs = 1000;    // Degraded if > 1000ms
            options.UnhealthyThresholdMs = 5000;   // Unhealthy if > 5000ms
            options.Timeout = TimeSpan.FromSeconds(10);
        },
        name: "sharpcoredb");
```

### Health Check Options

- **TestQuery** - Query to execute for testing (default: "SELECT 1")
- **TestConnection** - Test database connection (default: true)
- **UseAsync** - Use async execution (default: true)
- **CheckQueryCache** - Include query cache statistics (default: true)
- **CheckTableCount** - Include table count check (default: false)
- **CheckPerformanceMetrics** - Include performance metrics (default: true)
- **DegradedThresholdMs** - Response time threshold for degraded status (default: 1000ms)
- **UnhealthyThresholdMs** - Response time threshold for unhealthy status (default: 5000ms)
- **Timeout** - Health check timeout (default: 10 seconds)

### Kubernetes/Container Support

```csharp
// Liveness probe (fast, connection only)
services.AddHealthChecks()
    .AddSharpCoreDBLightweight(
        database,
        name: "liveness",
        tags: new[] { "k8s", "liveness" });

// Readiness probe (thorough)
services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(
        database,
        name: "readiness",
        tags: new[] { "k8s", "readiness" });
```

### Health Check Data

Health checks return detailed diagnostic data:

- **connection** - Connection status
- **query_execution_ms** - Query execution time
- **cache_hit_rate** - Query cache hit rate
- **cache_hits** - Total cache hits
- **cache_misses** - Total cache misses
- **cached_queries** - Number of cached queries
- **avg_query_time_ms** - Average query execution time
- **total_queries** - Total queries executed
- **slowest_query_ms** - Slowest query execution time
- **health_check_duration_ms** - Health check duration

### ASP.NET Core Integration

```csharp
// In Program.cs or Startup.cs
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(database);

// Configure endpoints
app.UseHealthChecks("/health");
app.UseHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

See `HealthCheckExamples.cs` for comprehensive examples.

## Compatibility

- **.NET 10.0+**
- **C# 14**
- **Dapper 2.1+**

## License

MIT License - See LICENSE file for details

## Contributing

Contributions are welcome! Please submit issues and pull requests to the GitHub repository.

## Support

For issues and questions, please use the GitHub issue tracker.
