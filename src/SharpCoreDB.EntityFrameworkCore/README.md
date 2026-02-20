<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB.EntityFrameworkCore
  
  **Entity Framework Core 10 Provider for SharpCoreDB**
  
  **Version:** 1.3.5 (Phase 9.2)  
  **Status:** Production Ready ✅
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.5-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.EntityFrameworkCore)
  [![EF Core](https://img.shields.io/badge/EF%20Core-10.0.2-purple.svg)](https://docs.microsoft.com/ef/core/)
  
</div>

---

## Overview

Entity Framework Core 10 database provider for **SharpCoreDB** — a high-performance encrypted embedded database engine for .NET 10. Use familiar EF Core APIs with SharpCoreDB's:

- ✅ **AES-256-GCM encryption** at rest (0% overhead)
- ✅ **SIMD acceleration** for analytics (150-680x faster)
- ✅ **Vector search** integration (Phase 8)
- ✅ **Graph algorithms** (Phase 6.2, 30-50% faster)
- ✅ **Collation support** (Binary, NoCase, Unicode, Locale-aware)
- ✅ **Zero-config deployment** - Single file, no server

**v1.3.5 Features:**
- ✅ CREATE TABLE COLLATE clause support
- ✅ Direct SQL query execution with proper collation handling
- ✅ Full ACID transaction support
- ✅ Phase 9 Analytics integration (COUNT, AVG, STDDEV, PERCENTILE, RANK, etc.)

---

## Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.3.5
```

**Requirements:**
- .NET 10.0+
- Entity Framework Core 10.0.2+
- SharpCoreDB 1.3.5+ (installed automatically)

---

## Quick Start

### 1. Define Your DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Age { get; set; }
    public string Email { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSharpCoreDB("Data Source=./myapp.db;Password=SecurePassword!");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .Property(u => u.Name)
            .UseCollation("NOCASE");  // Case-insensitive search

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email);  // B-tree index for fast lookups
    }
}
```

### 2. Use in Your Application

```csharp
using var context = new AppDbContext();

// Create tables
await context.Database.EnsureCreatedAsync();

// Add data
context.Users.Add(new User { Name = "Alice", Age = 30, Email = "alice@example.com" });
await context.SaveChangesAsync();

// Query (direct SQL for now, LINQ coming in Phase 10)
var users = context.Users
    .FromSqlRaw("SELECT * FROM users WHERE age > {0}", 25)
    .ToList();

foreach (var user in users)
{
    Console.WriteLine($"{user.Name}: {user.Age}");
}
```

---

## Features

### 1. Collation Support (v1.3.5)

```csharp
modelBuilder.Entity<Product>()
    .Property(p => p.Name)
    .UseCollation("BINARY");  // Case-sensitive

modelBuilder.Entity<Category>()
    .Property(c => c.Name)
    .UseCollation("NOCASE");  // Case-insensitive

modelBuilder.Entity<City>()
    .Property(c => c.Name)
    .UseCollation("LOCALE('tr-TR')");  // Turkish collation

// CREATE TABLE statement includes COLLATE clause
await context.Database.EnsureCreatedAsync();
```

### 2. Encryption

```csharp
// All data encrypted automatically with AES-256-GCM
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSharpCoreDB("Data Source=./secure.db;Password=StrongPassword!;Encryption=Full")
    .Options;

using var context = new AppDbContext(options);
```

### 3. Indexes

```csharp
modelBuilder.Entity<User>()
    .HasIndex(u => u.Email)
    .IsUnique();  // UNIQUE constraint + B-tree index

modelBuilder.Entity<User>()
    .HasIndex(u => new { u.LastName, u.FirstName });  // Composite index
```

### 4. SQL Queries (Direct)

```csharp
// Raw SQL with proper collation handling
var users = context.Users
    .FromSqlRaw("SELECT * FROM users WHERE name COLLATE NOCASE = {0}", "alice")
    .ToList();

// Execute non-query
await context.Database.ExecuteSqlAsync(
    "UPDATE users SET age = age + 1 WHERE id = {0}",
    userId
);
```

### 5. Analytics Integration (Phase 9)

```csharp
// Use with SQL to run analytics
var stats = context.Users
    .FromSqlRaw(@"
        SELECT 
            COUNT(*) as total,
            AVG(age) as avg_age,
            STDDEV(age) as age_stddev,
            PERCENTILE(age, 0.75) as age_75th
        FROM users
    ")
    .ToList();

// Or use directly
var result = await context.Database.ExecuteQuery<UserStats>(
    "SELECT COUNT(*) as total, AVG(age) as avg_age, STDDEV(age) as age_stddev FROM users"
);
```

### 6. Transactions

```csharp
using var transaction = await context.Database.BeginTransactionAsync();
try
{
    context.Users.Add(new User { Name = "Bob", Age = 28 });
    await context.SaveChangesAsync();
    
    context.Users.Add(new User { Name = "Carol", Age = 32 });
    await context.SaveChangesAsync();
    
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

---

## Connection String Options

```
Data Source=./myapp.db;          // File path (required)
Password=SecurePassword!;         // Encryption password
Encryption=Full;                  // Full|None (default: Full)
Cache=Shared;                     // Shared|Private (default: Shared)
ReadOnly=false;                   // Read-only mode
Timeout=30000;                    // Operation timeout (ms)
```

---

## API Reference

### DbContext Configuration

| Method | Purpose |
|--------|---------|
| `UseSharpCoreDB(connectionString)` | Configure SharpCoreDB provider |
| `EnsureCreatedAsync()` | Create tables from model |
| `EnsureDeletedAsync()` | Drop all tables |
| `BeginTransactionAsync()` | Start transaction |

### Model Builder

| Method | Purpose |
|--------|---------|
| `UseCollation("type")` | Set collation (BINARY, NOCASE, LOCALE(...)) |
| `HasIndex()` | Create B-tree index |
| `HasIndex().IsUnique()` | UNIQUE constraint |
| `Property().HasMaxLength()` | Column constraints |

### Query Methods

| Method | Purpose |
|--------|---------|
| `FromSqlRaw(sql, params)` | Raw SQL queries |
| `ExecuteSqlAsync(sql, params)` | Execute commands |
| `ExecuteQuery<T>(sql)` | Typed SQL results |

---

## Known Limitations & Status

### ✅ Supported
- CREATE TABLE with properties, indexes, constraints
- Raw SQL queries (FromSqlRaw)
- Direct SQL execution
- Collation support (v1.3.5)
- Transactions (ACID)
- Encryption (AES-256-GCM)
- Entity insert/update/delete via SaveChangesAsync

### 🟡 In Progress (Phase 10)
- Full LINQ query provider
- LINQ to SQL translation for complex queries
- Query optimization

### ℹ️ Notes
- For complex queries, use `FromSqlRaw()` with raw SQL
- Analytics queries work via raw SQL
- LINQ queries are translated to SQL in Phase 10

---

## Common Patterns

### Repository with EF Core

```csharp
public class Repository<T> where T : class, IEntity
{
    protected readonly AppDbContext Context;

    public Repository(AppDbContext context)
    {
        Context = context;
    }

    public async Task<T> GetByIdAsync(int id)
    {
        return await Context.Set<T>().FindAsync(id);
    }

    public async Task AddAsync(T entity)
    {
        Context.Set<T>().Add(entity);
        await Context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            Context.Set<T>().Remove(entity);
            await Context.SaveChangesAsync();
        }
    }
}
```

### Service Layer

```csharp
public class UserService
{
    private readonly AppDbContext _context;

    public UserService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User> RegisterAsync(string name, int age, string email)
    {
        var user = new User { Name = name, Age = age, Email = email };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<List<User>> SearchByNameAsync(string namePrefix)
    {
        return await _context.Users
            .FromSqlRaw("SELECT * FROM users WHERE name LIKE {0}", namePrefix + "%")
            .ToListAsync();
    }
}
```

### Dependency Injection

```csharp
services.AddDbContext<AppDbContext>(options =>
{
    options.UseSharpCoreDB("Data Source=./app.db;Password=secure!");
});

services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
services.AddScoped<UserService>();
```

---

## Performance Tips

1. **Create Indexes** on frequently queried columns
2. **Use Raw SQL** for complex queries until Phase 10
3. **Batch Operations** - Use AddRange for better performance
4. **Disable Change Tracking** for read-only queries: `.AsNoTracking()`
5. **Use Compiled Queries** for repeated queries

---

## Migration to SharpCoreDB from SQLite

```csharp
// 1. Update DbContext options
options.UseSharpCoreDB("Data Source=./app.db;Password=secure!")

// 2. Supported collation syntax
.UseCollation("NOCASE")  // Same as SQLite

// 3. Run migrations
await context.Database.EnsureCreatedAsync()

// 4. No code changes needed for basic operations!
```

---

## See Also

- **[Core SharpCoreDB](../SharpCoreDB/README.md)** - Database engine
- **[Analytics Engine](../SharpCoreDB.Analytics/README.md)** - Data analysis
- **[Vector Search](../SharpCoreDB.VectorSearch/README.md)** - Embeddings
- **[User Manual](../../docs/USER_MANUAL.md)** - Complete guide
- **[EF Core Documentation](https://docs.microsoft.com/ef/core/)** - Microsoft reference

---

## Testing

```bash
# Run EF Core tests
dotnet test tests/SharpCoreDB.EntityFrameworkCore.Tests

# Run with coverage
dotnet-coverage collect -f cobertura -o coverage.xml dotnet test
```

---

## License

MIT License - See [LICENSE](../../LICENSE)

---

**Last Updated:** February 19, 2026 | Version 1.3.5
