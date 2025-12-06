# SharpCoreDB Examples

<!--
MIT License

Copyright (c) 2025 MPCoreDeveloper

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
-->

## Console Application with Dependency Injection

Create a simple console app demonstrating DI integration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

class Program
{
    static async Task Main(string[] args)
    {
        // Set up DI container
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();

        // Get database factory
        var factory = provider.GetRequiredService<DatabaseFactory>();

        // Create database
        var db = factory.Create("console.db", "password123");

        // Create table
        await db.ExecuteSQLAsync("CREATE TABLE tasks (id INTEGER PRIMARY KEY, title TEXT, completed BOOLEAN)");

        // Insert sample data
        await db.ExecuteSQLAsync("INSERT INTO tasks VALUES (1, 'Learn SharpCoreDB', false)");
        await db.ExecuteSQLAsync("INSERT INTO tasks VALUES (2, 'Build app', true)");

        // Query data
        var results = await db.ExecuteSQLAsync("SELECT * FROM tasks");
        foreach (var row in results)
        {
            Console.WriteLine($"{row["id"]}: {row["title"]} - {(bool)row["completed"] ? "Done" : "Pending"}");
        }
    }
}
```

## Entity Framework Core Migration Example

Using SharpCoreDB with EF Core for migrations:

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB(ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Email).IsRequired();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Price).HasColumnType("DECIMAL");
        });
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }
}

// Usage
class Program
{
    static void Main()
    {
        var connectionString = "Data Source=app.db;Password=secure123";
        using var context = new AppDbContext(connectionString);

        // Create database
        context.Database.EnsureCreated();

        // Add data
        context.Users.Add(new User { Id = 1, Name = "Alice", Email = "alice@example.com" });
        context.SaveChanges();

        // Query with LINQ
        var users = context.Users.Where(u => u.Name.StartsWith("A")).ToList();
    }
}
```

## Performance Tuning Guide

Optimize SharpCoreDB for high-performance scenarios:

### 1. Use High-Performance Configuration

```csharp
var config = DatabaseConfig.HighPerformance;
var db = factory.Create("fast.db", "password", false, config);
```

### 2. Enable Query Caching

```csharp
var config = new DatabaseConfig
{
    EnableQueryCache = true,
    QueryCacheSize = 1000
};
var db = factory.Create("cached.db", "password", false, config);
```

### 3. Create Indexes for Fast Queries

```csharp
// Create hash index for O(1) lookups
db.ExecuteSQL("CREATE INDEX idx_user_email ON users (email)");
db.ExecuteSQL("CREATE INDEX idx_product_category ON products (category)");
```

### 4. Use Batch Operations

```csharp
var batch = new List<string>();
for (int i = 0; i < 1000; i++)
{
    batch.Add($"INSERT INTO logs VALUES ({i}, 'Log entry {i}')");
}
db.ExecuteBatchSQL(batch); // Single transaction
```

### 5. Connection Pooling

```csharp
using SharpCoreDB.Services;

var pool = new DatabasePool(services, maxPoolSize: 10);
var db = pool.GetDatabase("shared.db", "password");
// Reuse connections
pool.ReturnDatabase(db);
```

### 6. Async Operations

```csharp
// Parallel processing
var tasks = Enumerable.Range(0, 100)
    .Select(i => db.ExecuteSQLAsync($"INSERT INTO data VALUES ({i}, 'value{i}')"));
await Task.WhenAll(tasks);
```

### 7. Memory-Mapped Files for Large Databases

SharpCoreDB uses memory-mapped files for efficient I/O.

### 8. Monitor Performance

```csharp
var stats = db.GetQueryCacheStatistics();
Console.WriteLine($"Cache hit rate: {stats.HitRate:P2}");
```

## Parameterized Queries

Parameterized queries help prevent SQL injection by separating SQL code from data.

### Example: Insert with Parameters

```csharp
using SharpCoreDB;

var db = new DatabaseFactory(services).Create("path/to/db", "password");
var parameters = new Dictionary<string, object?>
{
    ["0"] = "John Doe",
    ["1"] = 30
};
await db.ExecuteSQLAsync("INSERT INTO users (name, age) VALUES (?, ?)", parameters);
```

### Example: Select with Parameters

```csharp
var parameters = new Dictionary<string, object?>
{
    ["0"] = "John Doe"
};
await db.ExecuteSQLAsync("SELECT * FROM users WHERE name = ?", parameters);
```

## Extended SQL Support

### LIMIT and OFFSET

```csharp
// Get first 10 users
db.ExecuteSQL("SELECT * FROM users LIMIT 10");

// Skip first 5, get next 10
db.ExecuteSQL("SELECT * FROM users LIMIT 10 OFFSET 5");
```

### ORDER BY with Indexes

Create an index for faster sorting:

```csharp
db.ExecuteSQL("CREATE INDEX idx_age ON users (age)");
db.ExecuteSQL("SELECT * FROM users ORDER BY age DESC");
```

### Subqueries

Subqueries in WHERE clauses are supported:

```csharp
db.ExecuteSQL("SELECT * FROM users WHERE age > (SELECT AVG(age) FROM users)");
