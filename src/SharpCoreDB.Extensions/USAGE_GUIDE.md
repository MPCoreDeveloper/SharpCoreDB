# SharpCoreDB.Extensions - Complete Usage Guide

Volledige gids voor Dapper integratie en ASP.NET Core health checks met SharpCoreDB.

## ?? Inhoudsopgave

1. [Installatie](#installatie)
2. [Dapper Integration](#dapper-integration)
3. [Health Checks](#health-checks)
4. [Connection Management](#connection-management)
5. [Advanced Scenarios](#advanced-scenarios)
6. [Performance Optimization](#performance-optimization)
7. [Best Practices](#best-practices)

---

## ?? Installatie

```bash
# Installeer SharpCoreDB.Extensions
dotnet add package SharpCoreDB.Extensions

# Dependencies (automatisch geïnstalleerd):
# - SharpCoreDB >= 1.0.0
# - Dapper >= 2.1.66
# - Microsoft.Extensions.Diagnostics.HealthChecks >= 10.0.1
```

---

## ?? Dapper Integration

### Basis Setup

```csharp
using SharpCoreDB.Extensions.Dapper;
using Dapper;

// Open connection
using var connection = new SharpCoreDBConnection(
    "Data Source=app.db;Encryption=true;Password=SecurePass");

connection.Open();

// Query
var products = await connection.QueryAsync<Product>(
    "SELECT * FROM Products WHERE Price > @MinPrice",
    new { MinPrice = 100 });

foreach (var product in products)
{
    Console.WriteLine($"{product.Name}: ${product.Price}");
}
```

### CRUD Operations

#### Insert

```csharp
// Single insert
var product = new Product 
{ 
    Name = "Laptop", 
    Price = 999.99m,
    Category = "Electronics"
};

var id = await connection.ExecuteScalarAsync<int>(
    @"INSERT INTO Products (Name, Price, Category) 
      VALUES (@Name, @Price, @Category);
      SELECT last_insert_rowid();",
    product);

Console.WriteLine($"Inserted product with ID: {id}");

// Bulk insert
var products = new[]
{
    new Product { Name = "Mouse", Price = 29.99m },
    new Product { Name = "Keyboard", Price = 79.99m },
    new Product { Name = "Monitor", Price = 299.99m }
};

var rowsAffected = await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price) VALUES (@Name, @Price)",
    products);

Console.WriteLine($"Inserted {rowsAffected} products");
```

#### Query

```csharp
// Query all
var allProducts = await connection.QueryAsync<Product>(
    "SELECT * FROM Products");

// Query single
var product = await connection.QuerySingleOrDefaultAsync<Product>(
    "SELECT * FROM Products WHERE Id = @Id",
    new { Id = 1 });

// Query with filter
var expensiveProducts = await connection.QueryAsync<Product>(
    "SELECT * FROM Products WHERE Price > @MinPrice ORDER BY Price DESC",
    new { MinPrice = 500 });

// Query first
var cheapest = await connection.QueryFirstOrDefaultAsync<Product>(
    "SELECT * FROM Products ORDER BY Price LIMIT 1");

// Dynamic query (zonder type)
var dynamicResult = await connection.QueryAsync(
    "SELECT Name, Price FROM Products");

foreach (var row in dynamicResult)
{
    Console.WriteLine($"{row.Name}: ${row.Price}");
}
```

#### Update

```csharp
// Single update
var rowsAffected = await connection.ExecuteAsync(
    "UPDATE Products SET Price = @Price WHERE Id = @Id",
    new { Id = 1, Price = 899.99m });

// Bulk update
var updates = new[]
{
    new { Id = 1, Price = 899.99m },
    new { Id = 2, Price = 25.99m },
    new { Id = 3, Price = 69.99m }
};

await connection.ExecuteAsync(
    "UPDATE Products SET Price = @Price WHERE Id = @Id",
    updates);

// Update with condition
await connection.ExecuteAsync(
    "UPDATE Products SET Price = Price * 1.1 WHERE Category = @Category",
    new { Category = "Electronics" });
```

#### Delete

```csharp
// Delete single
await connection.ExecuteAsync(
    "DELETE FROM Products WHERE Id = @Id",
    new { Id = 1 });

// Delete multiple
await connection.ExecuteAsync(
    "DELETE FROM Products WHERE Price < @MaxPrice",
    new { MaxPrice = 10 });

// Delete all
await connection.ExecuteAsync("DELETE FROM Products");
```

### Transactions

```csharp
using var connection = new SharpCoreDBConnection(connectionString);
connection.Open();

using var transaction = connection.BeginTransaction();
try
{
    // Multiple operations in transaction
    await connection.ExecuteAsync(
        "INSERT INTO Orders (CustomerId, Total) VALUES (@CustomerId, @Total)",
        new { CustomerId = 1, Total = 999.99m },
        transaction);

    var orderId = await connection.ExecuteScalarAsync<int>(
        "SELECT last_insert_rowid()",
        transaction: transaction);

    await connection.ExecuteAsync(
        "INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (@OrderId, @ProductId, @Quantity)",
        new { OrderId = orderId, ProductId = 5, Quantity = 2 },
        transaction);

    await connection.ExecuteAsync(
        "UPDATE Products SET Stock = Stock - @Quantity WHERE Id = @ProductId",
        new { ProductId = 5, Quantity = 2 },
        transaction);

    transaction.Commit();
    Console.WriteLine("Transaction completed successfully");
}
catch (Exception ex)
{
    transaction.Rollback();
    Console.WriteLine($"Transaction failed: {ex.Message}");
    throw;
}
```

### Multi-Mapping

```csharp
// One-to-One mapping
var orders = await connection.QueryAsync<Order, Customer, Order>(
    @"SELECT o.*, c.* 
      FROM Orders o 
      INNER JOIN Customers c ON o.CustomerId = c.Id",
    (order, customer) =>
    {
        order.Customer = customer;
        return order;
    },
    splitOn: "Id"); // Split on Customer.Id column

// One-to-Many mapping
var customerDict = new Dictionary<int, Customer>();

var customers = await connection.QueryAsync<Customer, Order, Customer>(
    @"SELECT c.*, o.*
      FROM Customers c
      LEFT JOIN Orders o ON c.Id = o.CustomerId
      ORDER BY c.Id",
    (customer, order) =>
    {
        if (!customerDict.TryGetValue(customer.Id, out var existingCustomer))
        {
            existingCustomer = customer;
            existingCustomer.Orders = new List<Order>();
            customerDict.Add(customer.Id, existingCustomer);
        }

        if (order != null)
        {
            existingCustomer.Orders.Add(order);
        }

        return existingCustomer;
    },
    splitOn: "Id");

var result = customerDict.Values;

// Three-way mapping
var orderDetails = await connection.QueryAsync<Order, Customer, Product, Order>(
    @"SELECT o.*, c.*, p.*
      FROM Orders o
      INNER JOIN Customers c ON o.CustomerId = c.Id
      INNER JOIN OrderItems oi ON o.Id = oi.OrderId
      INNER JOIN Products p ON oi.ProductId = p.Id",
    (order, customer, product) =>
    {
        order.Customer = customer;
        order.Products = order.Products ?? new List<Product>();
        order.Products.Add(product);
        return order;
    },
    splitOn: "Id,Id");
```

### Stored Procedures

```csharp
// Execute stored procedure
var result = await connection.QueryAsync<Product>(
    "GetProductsByCategory",
    new { Category = "Electronics" },
    commandType: CommandType.StoredProcedure);

// Stored procedure with output parameters
var parameters = new DynamicParameters();
parameters.Add("@CategoryId", 1);
parameters.Add("@TotalCount", dbType: DbType.Int32, direction: ParameterDirection.Output);

var products = await connection.QueryAsync<Product>(
    "GetCategoryProducts",
    parameters,
    commandType: CommandType.StoredProcedure);

int totalCount = parameters.Get<int>("@TotalCount");
Console.WriteLine($"Total products: {totalCount}");
```

### Bulk Operations

```csharp
// Efficient bulk insert (10,000 records example)
var products = Enumerable.Range(1, 10000)
    .Select(i => new Product
    {
        Name = $"Product {i}",
        Price = 10 + (i * 0.5m),
        Category = i % 2 == 0 ? "Electronics" : "Books"
    })
    .ToList();

// Option 1: Batch inserts
var batchSize = 1000;
for (int i = 0; i < products.Count; i += batchSize)
{
    var batch = products.Skip(i).Take(batchSize);
    await connection.ExecuteAsync(
        "INSERT INTO Products (Name, Price, Category) VALUES (@Name, @Price, @Category)",
        batch);
}

// Option 2: Transaction for all inserts
using var transaction = connection.BeginTransaction();
await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price, Category) VALUES (@Name, @Price, @Category)",
    products,
    transaction);
transaction.Commit();

Console.WriteLine($"Inserted {products.Count} products");
```

---

## ?? Health Checks

### Basic Setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection"),
        name: "sharpcoredb",
        tags: new[] { "db", "ready" });

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
```

### Custom Health Query

```csharp
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: connectionString,
        healthQuery: "SELECT COUNT(*) FROM Products WHERE Stock > 0",
        name: "database-products",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "db", "products" });
```

### Multiple Database Checks

```csharp
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: builder.Configuration.GetConnectionString("MainDatabase"),
        name: "main-database",
        tags: new[] { "db", "main" })
    .AddSharpCoreDB(
        connectionString: builder.Configuration.GetConnectionString("CacheDatabase"),
        name: "cache-database",
        tags: new[] { "db", "cache" },
        failureStatus: HealthStatus.Degraded); // Cache can be degraded
```

### Health Check with Timeout

```csharp
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(
        connectionString: connectionString,
        name: "sharpcoredb",
        timeout: TimeSpan.FromSeconds(3));
```

### Custom Health Check Response

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };

        await context.Response.WriteAsJsonAsync(response);
    }
});
```

### Health Check UI (Optioneel)

```bash
dotnet add package AspNetCore.HealthChecks.UI
dotnet add package AspNetCore.HealthChecks.UI.Client
dotnet add package AspNetCore.HealthChecks.UI.InMemory.Storage
```

```csharp
// Program.cs
builder.Services
    .AddHealthChecks()
    .AddSharpCoreDB(connectionString, "sharpcoredb");

builder.Services
    .AddHealthChecksUI()
    .AddInMemoryStorage();

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecksUI(options => options.UIPath = "/health-ui");
```

**Access UI:** `https://localhost:5001/health-ui`

---

## ?? Connection Management

### Connection String Builder

```csharp
var builder = new SharpCoreConnectionStringBuilder
{
    DataSource = "app.db",
    Encryption = true,
    Password = "SecurePassword123",
    Pooling = true,
    MaxPoolSize = 100,
    MinPoolSize = 5,
    ConnectionTimeout = 30,
    CommandTimeout = 60
};

using var connection = new SharpCoreDBConnection(builder.ToString());
```

### Connection Pooling

```csharp
// Enable connection pooling (enabled by default)
var connectionString = "Data Source=app.db;Pooling=true;MaxPoolSize=100;MinPoolSize=5";

// Disable pooling for single-user scenarios
var singleUserString = "Data Source=app.db;Pooling=false";
```

### Dependency Injection

```csharp
// Startup.cs / Program.cs
builder.Services.AddScoped<IDbConnection>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("DefaultConnection");
    return new SharpCoreDBConnection(connectionString);
});

// Repository pattern
public class ProductRepository
{
    private readonly IDbConnection _connection;

    public ProductRepository(IDbConnection connection)
    {
        _connection = connection;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        return await _connection.QueryAsync<Product>("SELECT * FROM Products");
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        return await _connection.QuerySingleOrDefaultAsync<Product>(
            "SELECT * FROM Products WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<int> AddAsync(Product product)
    {
        return await _connection.ExecuteScalarAsync<int>(
            @"INSERT INTO Products (Name, Price, Category) 
              VALUES (@Name, @Price, @Category);
              SELECT last_insert_rowid();",
            product);
    }
}
```

---

## ?? Advanced Scenarios

### Pagination

```csharp
public async Task<PagedResult<Product>> GetProductsPagedAsync(
    int page, 
    int pageSize, 
    string? searchTerm = null)
{
    var offset = (page - 1) * pageSize;

    var sql = @"
        SELECT * FROM Products 
        WHERE (@SearchTerm IS NULL OR Name LIKE '%' || @SearchTerm || '%')
        ORDER BY Id
        LIMIT @PageSize OFFSET @Offset";

    var countSql = @"
        SELECT COUNT(*) FROM Products 
        WHERE (@SearchTerm IS NULL OR Name LIKE '%' || @SearchTerm || '%')";

    var products = await connection.QueryAsync<Product>(
        sql,
        new { SearchTerm = searchTerm, PageSize = pageSize, Offset = offset });

    var totalCount = await connection.ExecuteScalarAsync<int>(
        countSql,
        new { SearchTerm = searchTerm });

    return new PagedResult<Product>
    {
        Items = products.ToList(),
        Page = page,
        PageSize = pageSize,
        TotalCount = totalCount,
        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
    };
}
```

### Dynamic Filtering

```csharp
public async Task<IEnumerable<Product>> SearchProductsAsync(ProductFilter filter)
{
    var conditions = new List<string>();
    var parameters = new DynamicParameters();

    if (!string.IsNullOrEmpty(filter.Name))
    {
        conditions.Add("Name LIKE @Name");
        parameters.Add("Name", $"%{filter.Name}%");
    }

    if (filter.MinPrice.HasValue)
    {
        conditions.Add("Price >= @MinPrice");
        parameters.Add("MinPrice", filter.MinPrice.Value);
    }

    if (filter.MaxPrice.HasValue)
    {
        conditions.Add("Price <= @MaxPrice");
        parameters.Add("MaxPrice", filter.MaxPrice.Value);
    }

    if (!string.IsNullOrEmpty(filter.Category))
    {
        conditions.Add("Category = @Category");
        parameters.Add("Category", filter.Category);
    }

    var where = conditions.Any() 
        ? "WHERE " + string.Join(" AND ", conditions) 
        : "";

    var sql = $"SELECT * FROM Products {where} ORDER BY {filter.SortBy ?? "Id"}";

    return await connection.QueryAsync<Product>(sql, parameters);
}
```

### Caching Strategy

```csharp
using Microsoft.Extensions.Caching.Memory;

public class CachedProductRepository
{
    private readonly IDbConnection _connection;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    public CachedProductRepository(IDbConnection connection, IMemoryCache cache)
    {
        _connection = connection;
        _cache = cache;
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        const string cacheKey = "all_products";

        if (_cache.TryGetValue(cacheKey, out IEnumerable<Product>? cachedProducts))
        {
            return cachedProducts!;
        }

        var products = await _connection.QueryAsync<Product>("SELECT * FROM Products");

        _cache.Set(cacheKey, products, _cacheExpiration);

        return products;
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        var cacheKey = $"product_{id}";

        if (_cache.TryGetValue(cacheKey, out Product? cachedProduct))
        {
            return cachedProduct;
        }

        var product = await _connection.QuerySingleOrDefaultAsync<Product>(
            "SELECT * FROM Products WHERE Id = @Id",
            new { Id = id });

        if (product != null)
        {
            _cache.Set(cacheKey, product, _cacheExpiration);
        }

        return product;
    }

    public async Task InvalidateCacheAsync()
    {
        // Implement cache invalidation strategy
        _cache.Remove("all_products");
    }
}
```

---

## ? Performance Optimization

### Use Buffered Queries (Default)

```csharp
// Buffered (default) - loads all results into memory
var products = await connection.QueryAsync<Product>("SELECT * FROM Products");
// Best for: Small to medium result sets

// Non-buffered - streams results
var largeResultSet = await connection.QueryAsync<Product>(
    "SELECT * FROM Products",
    buffered: false);
// Best for: Very large result sets, memory constrained
```

### Connection Lifetime

```csharp
// Good: Short-lived connections with pooling
public async Task<Product?> GetProductAsync(int id)
{
    using var connection = new SharpCoreDBConnection(connectionString);
    connection.Open();
    return await connection.QuerySingleOrDefaultAsync<Product>(
        "SELECT * FROM Products WHERE Id = @Id",
        new { Id = id });
}

// Avoid: Long-lived connections
// Don't store connection as instance field without pooling
```

### Batch Operations

```csharp
// Efficient batch inserts
var products = GenerateProducts(1000);

// Good: Single Execute with multiple parameters
await connection.ExecuteAsync(
    "INSERT INTO Products (Name, Price) VALUES (@Name, @Price)",
    products);

// Avoid: Loop with individual Execute calls
// foreach (var product in products)
// {
//     await connection.ExecuteAsync(..., product);
// }
```

---

## ? Best Practices

### 1. Always Use Parameters

```csharp
// Good: Parameterized query
var products = await connection.QueryAsync<Product>(
    "SELECT * FROM Products WHERE Category = @Category",
    new { Category = category });

// Bad: String concatenation (SQL injection risk!)
// var sql = $"SELECT * FROM Products WHERE Category = '{category}'";
```

### 2. Use Transactions for Multiple Operations

```csharp
// Good: Transaction for related operations
using var transaction = connection.BeginTransaction();
try
{
    await connection.ExecuteAsync(sql1, param1, transaction);
    await connection.ExecuteAsync(sql2, param2, transaction);
    transaction.Commit();
}
catch
{
    transaction.Rollback();
    throw;
}
```

### 3. Dispose Connections Properly

```csharp
// Good: using statement
using var connection = new SharpCoreDBConnection(connectionString);

// Good: using declaration
using var conn = new SharpCoreDBConnection(connectionString);
// Auto-disposed at end of scope
```

### 4. Use Async Methods

```csharp
// Good: Async all the way
var products = await connection.QueryAsync<Product>(sql);

// Avoid: Blocking async
// var products = connection.QueryAsync<Product>(sql).Result;
```

### 5. Handle Nulls Properly

```csharp
// Use nullable reference types
var product = await connection.QuerySingleOrDefaultAsync<Product?>(
    "SELECT * FROM Products WHERE Id = @Id",
    new { Id = id });

if (product != null)
{
    // Use product
}
```

---

## ?? Security

### Encrypted Connections

```csharp
var connectionString = new SharpCoreConnectionStringBuilder
{
    DataSource = "secure.db",
    Encryption = true,
    Password = "VerySecurePassword123!",
    EncryptionAlgorithm = "AES-256-GCM"
}.ToString();
```

### Environment Variables

```csharp
var password = Environment.GetEnvironmentVariable("DB_PASSWORD") 
    ?? throw new InvalidOperationException("DB_PASSWORD not set");

var connectionString = $"Data Source=app.db;Encryption=true;Password={password}";
```

### Azure Key Vault

```csharp
var keyVaultUrl = builder.Configuration["KeyVault:Url"];
var secretClient = new SecretClient(
    new Uri(keyVaultUrl), 
    new DefaultAzureCredential());

var secret = await secretClient.GetSecretAsync("DatabasePassword");
var connectionString = $"Data Source=app.db;Encryption=true;Password={secret.Value.Value}";
```

---

## ?? Troubleshooting

### "Connection timeout"
**Solution:** Increase timeout or check file permissions
```csharp
var builder = new SharpCoreConnectionStringBuilder
{
    DataSource = "app.db",
    ConnectionTimeout = 60 // seconds
};
```

### "Database is locked"
**Solution:** Use WAL mode or reduce concurrent writes
```csharp
await connection.ExecuteAsync("PRAGMA journal_mode=WAL");
```

### "Out of memory"
**Solution:** Use non-buffered queries for large result sets
```csharp
var results = await connection.QueryAsync<T>(sql, buffered: false);
```

---

## ?? Resources

- **SharpCoreDB**: https://www.nuget.org/packages/SharpCoreDB
- **Dapper**: https://github.com/DapperLib/Dapper
- **Health Checks**: https://docs.microsoft.com/aspnet/core/host-and-deploy/health-checks
- **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB

---

**Happy coding with SharpCoreDB.Extensions!** ??
