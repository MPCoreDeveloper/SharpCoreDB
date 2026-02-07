# SharpCoreDB.Extensions — Hands-On Usage Guide

A practical, step-by-step guide covering real-world scenarios with **SharpCoreDB.Extensions**.
Every code sample is self-contained and ready to copy into your project.

---

## Prerequisites

```bash
dotnet add package SharpCoreDB.Extensions   # v1.0.6+
```

All examples assume you have a running `IDatabase` instance:

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions;

var factory = new DatabaseFactory(serviceProvider);
using var db = factory.Create("./myapp.scdb", "YourPassword!");
```

---

## 1. Connecting Dapper to SharpCoreDB

`GetDapperConnection()` is an extension method on `IDatabase` that returns a standard `IDbConnection` Dapper can work with.

```csharp
using var conn = db.GetDapperConnection();
conn.Open();

// Any Dapper method now works
var rows = conn.Query<Product>("SELECT * FROM products");
```

> **Tip:** The connection is lightweight — create it per operation rather than caching it long-term.

---

## 2. CRUD with the Repository Pattern

### Step 1 — Define your entity

```csharp
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

### Step 2 — Create the table

```csharp
db.ExecuteSQL("""
    CREATE TABLE customers (
        Id INTEGER PRIMARY KEY,
        Name TEXT NOT NULL,
        Email TEXT NOT NULL,
        CreatedAt TEXT
    )
""");
db.Flush();
```

### Step 3 — Use the repository

```csharp
var repo = new DapperRepository<Customer, int>(db, "customers", keyColumn: "Id");

// Insert
repo.Insert(new Customer
{
    Name = "Alice",
    Email = "alice@example.com",
    CreatedAt = DateTime.UtcNow
});

// Read
var alice = repo.GetById(1);
Console.WriteLine(alice?.Name); // "Alice"

// Update
alice!.Email = "alice.new@example.com";
repo.Update(alice);

// Delete
repo.Delete(1);

// Count
long total = repo.Count();
```

### Step 4 — Async operations

```csharp
await repo.InsertAsync(new Customer { Name = "Bob", Email = "bob@test.com" });
var bob = await repo.GetByIdAsync(2);
var all = await repo.GetAllAsync();
await repo.DeleteAsync(2);
```

### Step 5 — Read-only repository (queries only)

```csharp
var readRepo = new ReadOnlyDapperRepository<Customer, int>(db, "customers");
var customers = readRepo.GetAll();          // ✅ Allowed
var count = readRepo.Count();              // ✅ Allowed
// readRepo.Insert(...)                    // ❌ Not available — read-only
```

---

## 3. Bulk Operations

For inserting thousands of rows, use `BulkInsert` instead of individual `INSERT` loops.

### Bulk Insert

```csharp
var newCustomers = Enumerable.Range(1, 5000).Select(i => new Customer
{
    Name = $"Customer {i}",
    Email = $"c{i}@bulk.test",
    CreatedAt = DateTime.UtcNow
});

int inserted = db.BulkInsert("customers", newCustomers, batchSize: 1000);
Console.WriteLine($"Inserted {inserted} rows");

// Don't forget to flush!
db.Flush();
```

### Async Bulk Insert with Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

int count = await db.BulkInsertAsync(
    "customers",
    newCustomers,
    batchSize: 500,
    cts.Token);
```

### Bulk Update

```csharp
var updated = customers.Select(c =>
{
    c.Email = c.Email.Replace("@bulk.test", "@updated.test");
    return c;
});

db.BulkUpdate("customers", updated, keyProperty: "Id");
```

### Bulk Delete

```csharp
int[] idsToDelete = [10, 20, 30, 40, 50];
db.BulkDelete("customers", idsToDelete, keyColumn: "Id");
```

---

## 4. Pagination

```csharp
var page1 = await db.QueryPagedAsync<Customer>(
    "SELECT * FROM customers ORDER BY Id",
    pageNumber: 1,
    pageSize: 25);

Console.WriteLine($"Page {page1.PageNumber} of {page1.TotalPages}");
Console.WriteLine($"Showing {page1.Items.Count} of {page1.TotalCount} total");

if (page1.HasNextPage)
{
    var page2 = await db.QueryPagedAsync<Customer>(
        "SELECT * FROM customers ORDER BY Id",
        pageNumber: 2,
        pageSize: 25);
}
```

---

## 5. Performance Monitoring

### Track individual queries

```csharp
var result = db.QueryWithMetrics<Customer>(
    "SELECT * FROM customers WHERE Email LIKE @Pattern",
    new { Pattern = "%@example.com" },
    queryName: "CustomersByEmail");

Console.WriteLine($"Found {result.Metrics.RowCount} rows");
Console.WriteLine($"Took {result.Metrics.ExecutionTime.TotalMilliseconds:F2}ms");
Console.WriteLine($"Memory delta: {result.Metrics.MemoryUsed} bytes");
```

### Async with metrics

```csharp
var asyncResult = await db.QueryWithMetricsAsync<Customer>(
    "SELECT * FROM customers",
    queryName: "AllCustomers");
```

### Get a performance report

```csharp
// After running several queries...
var report = DapperPerformanceExtensions.GetPerformanceReport();

Console.WriteLine($"Total queries tracked: {report.TotalQueries}");
Console.WriteLine($"Average time:         {report.AverageExecutionTime.TotalMilliseconds:F2}ms");
Console.WriteLine($"Total time:           {report.TotalExecutionTime.TotalMilliseconds:F2}ms");
Console.WriteLine($"Total rows processed: {report.TotalRowsProcessed}");

if (report.SlowestQuery is not null)
{
    Console.WriteLine($"Slowest query: {report.SlowestQuery.QueryName} " +
                      $"({report.SlowestQuery.ExecutionTime.TotalMilliseconds:F2}ms)");
}

// Reset metrics
DapperPerformanceExtensions.ClearMetrics();
```

### Set a timeout warning

```csharp
var results = db.QueryWithTimeout<Customer>(
    "SELECT * FROM customers",
    timeout: TimeSpan.FromMilliseconds(500),
    onTimeout: elapsed =>
    {
        Console.WriteLine($"Warning: Query exceeded timeout: {elapsed.TotalMilliseconds}ms");
    });
```

---

## 6. Multi-Table JOIN Mapping

### Two-table JOIN

```csharp
public record Order(int Id, int CustomerId, decimal Total);
public record OrderWithCustomer(Order Order, Customer Customer);

var orders = db.QueryMultiMapped<Order, Customer, OrderWithCustomer>(
    """
    SELECT o.Id, o.CustomerId, o.Total,
           c.Id, c.Name, c.Email
    FROM orders o
    JOIN customers c ON o.CustomerId = c.Id
    """,
    (order, customer) => new OrderWithCustomer(order, customer),
    splitOn: "Id");
```

### Custom mapping function

```csharp
var dtos = db.QueryWithMapping(
    "SELECT Id, Name, Email FROM customers",
    row => new
    {
        Id = (int)row["Id"],
        DisplayName = $"{row["Name"]} <{row["Email"]}>"
    });

foreach (var dto in dtos)
{
    Console.WriteLine(dto.DisplayName);
}
```

### Column-to-property mapping

```csharp
// When DB column names don't match C# property names
DapperMappingExtensions.CreateTypeMap<Customer>(new Dictionary<string, string>
{
    ["customer_name"] = "Name",
    ["email_addr"] = "Email",
    ["created_at"] = "CreatedAt"
});

// Now queries automatically map to the correct properties
var mapped = db.QueryMapped<Customer>(
    "SELECT customer_name, email_addr, created_at FROM customers_v2");
```

---

## 7. Unit of Work

Coordinate multiple repository operations in a single transaction scope:

```csharp
using var uow = new DapperUnitOfWork(db);
uow.BeginTransaction();

try
{
    var customerRepo = uow.GetRepository<Customer, int>("customers");
    var orderRepo = uow.GetRepository<Order, int>("orders");

    customerRepo.Insert(new Customer { Name = "Charlie", Email = "charlie@test.com" });
    orderRepo.Insert(new Order { CustomerId = 1, Total = 199.99m });

    uow.Commit();
    Console.WriteLine("Transaction committed successfully.");
}
catch (Exception ex)
{
    uow.Rollback();
    Console.WriteLine($"Transaction rolled back: {ex.Message}");
    throw;
}
```

---

## 8. Health Checks in ASP.NET Core

### Minimal API setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB
builder.Services.AddSharpCoreDB();

builder.Services.AddSingleton<IDatabase>(sp =>
{
    var factory = sp.GetRequiredService<DatabaseFactory>();
    return factory.Create("./webapp.scdb", builder.Configuration["DbPassword"]!);
});

var db = builder.Services.BuildServiceProvider().GetRequiredService<IDatabase>();

// Basic health check
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(db, name: "sharpcoredb", tags: ["db", "ready"]);

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();
```

### Separate liveness and readiness probes

```csharp
// Lightweight for liveness (fast, no query)
builder.Services.AddHealthChecks()
    .AddSharpCoreDBLightweight(db, name: "live", tags: ["live"]);

// Comprehensive for readiness (full diagnostics)
builder.Services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(db, name: "ready", tags: ["ready"]);

app.MapHealthChecks("/health/live", new()
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains("ready")
});
```

### Custom thresholds

```csharp
builder.Services.AddHealthChecks()
    .AddSharpCoreDB(db, options =>
    {
        options.TestQuery = "SELECT COUNT(*) FROM customers";
        options.DegradedThresholdMs = 200;   // > 200ms = Degraded
        options.UnhealthyThresholdMs = 1000; // > 1000ms = Unhealthy
        options.CheckQueryCache = true;
        options.CheckPerformanceMetrics = true;
        options.Timeout = TimeSpan.FromSeconds(5);
    });
```

---

## 9. Type Mapping

### Automatic type inference

```csharp
// Create a typed parameter
var param = DapperTypeMapper.CreateParameter("price", 19.99m);
// param.DbType == DbType.Decimal, param.Size is set automatically

// Check compatibility
bool ok = DapperTypeMapper.IsCompatible(42, DbType.Int64); // true (numeric widening)
```

### Convert values

```csharp
object? result = DapperTypeMapper.ConvertValue("2025-06-15", typeof(DateTime));
// result is DateTime(2025, 6, 15)

object? guid = DapperTypeMapper.ConvertValue(
    "550e8400-e29b-41d4-a716-446655440000",
    typeof(Guid));
```

---

## 10. Razor Pages Integration

Since this project includes Razor Pages, here's how to wire everything together:

### `Program.cs`

```csharp
using SharpCoreDB;
using SharpCoreDB.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register SharpCoreDB services
builder.Services.AddSharpCoreDB();
builder.Services.AddRazorPages();

// Create and register the database as a singleton
builder.Services.AddSingleton<IDatabase>(sp =>
{
    var factory = sp.GetRequiredService<DatabaseFactory>();
    return factory.Create("./webapp.scdb", builder.Configuration["DbPassword"]!);
});

var app = builder.Build();
app.MapRazorPages();
app.MapHealthChecks("/health");
app.Run();
```

### Page Model

```csharp
public class CustomersModel(IDatabase database) : PageModel
{
    public List<Customer> Customers { get; set; } = [];

    public async Task OnGetAsync()
    {
        Customers = (await database.QueryAsync<Customer>(
            "SELECT * FROM customers ORDER BY Name")).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string name, string email)
    {
        await database.ExecuteAsync(
            "INSERT INTO customers (Name, Email, CreatedAt) VALUES (@Name, @Email, @Now)",
            new { Name = name, Email = email, Now = DateTime.UtcNow });

        return RedirectToPage();
    }
}
```

---

## Tips & Best Practices

1. **Always call `db.Flush()` after write operations** to persist data to disk.
2. **Use `BulkInsert`/`BulkInsertAsync` for batch inserts** — individual `INSERT` loops are much slower.
3. **Prefer async methods** (`QueryAsync`, `ExecuteAsync`) in web applications.
4. **Use `CancellationToken`** in async operations to support graceful shutdown.
5. **Use the lightweight health check** (`AddSharpCoreDBLightweight`) for Kubernetes liveness probes.
6. **Use the comprehensive health check** (`AddSharpCoreDBComprehensive`) for monitoring dashboards.
7. **Monitor performance** with `QueryWithMetrics` in development to catch slow queries early.
8. **Cache is automatic** — `EntityMetadataCache` caches reflection results for repository operations.
9. **Connection per operation** — `GetDapperConnection()` is cheap; create a new one per query scope.
10. **Flush after batch operations** — call `db.Flush()` and `db.ForceSave()` after bulk writes.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Data not persisted after restart | Missing `Flush()` call | Call `db.Flush()` after writes |
| `InvalidOperationException: Connection must be open` | Forgot to call `Open()` | Call `connection.Open()` after `GetDapperConnection()` |
| Health check always "Degraded" | Threshold order was wrong (fixed in v1.0.6) | Update to latest version |
| Slow `Insert`/`Update` in repository | Reflection on every call (fixed in v1.0.6) | Update to latest version — metadata is now cached |
| `InvalidCastException` in mapping | DB column type doesn't match C# property | Use `DapperTypeMapper.ConvertValue()` or custom mapping |

---

## Version History

| Version | Changes |
|---------|---------|
| 1.0.6 | Health check threshold fix, reflection caching, primary constructors, OpenAsync support, full subquery/JOIN support |
| 1.0.0 | Initial release: Dapper integration, health checks, repository pattern |

---

*For full API documentation, see the [README](README.md).*
*For coding standards, see `.github/CODING_STANDARDS_CSHARP14.md`.*
