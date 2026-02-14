<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB.EntityFrameworkCore
  
  **Entity Framework Core 10 Provider for SharpCoreDB**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.3.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.EntityFrameworkCore)
  [![EF Core](https://img.shields.io/badge/EF%20Core-10.0.2-purple.svg)](https://docs.microsoft.com/ef/core/)
  
</div>

---

## Overview

Entity Framework Core 10 database provider for **SharpCoreDB** — a high-performance encrypted embedded database engine. Use familiar EF Core APIs with SharpCoreDB's AES-256-GCM encryption, SIMD acceleration, and zero-config deployment.

**Latest (v1.3.0):** Fixed CREATE TABLE COLLATE clause emission for UseCollation() ✅

---

## Installation

```bash
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.3.0
```

**Requirements:**
- .NET 10.0 or later
- Entity Framework Core 10.0.2 or later
- SharpCoreDB 1.3.0 or later (installed automatically)

---

## Quick Start

### 1. Define Your Entities and DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using SharpCoreDB.EntityFrameworkCore;

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int Age { get; set; }
    public decimal Salary { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Connection string format: Data Source=path;Password=pass;Cache=Shared|Private;ReadOnly=true|false
        optionsBuilder.UseSharpCoreDB(
            "Data Source=./myapp.db;Password=MySecurePassword123!");
    }
}
```

### 2. Use EF Core Normally

```csharp
await using var context = new AppDbContext();

// Create database and tables from model
await context.Database.EnsureCreatedAsync();

// INSERT
context.Users.Add(new User { Name = "Alice", Age = 30, Salary = 75000 });
await context.SaveChangesAsync();

// QUERY with LINQ
var highEarners = await context.Users
    .Where(u => u.Salary > 50000)
    .OrderBy(u => u.Name)
    .ToListAsync();

// AGGREGATIONS
var avgSalary = await context.Users.AverageAsync(u => u.Salary);
var totalSalary = await context.Users.SumAsync(u => u.Salary);
```

---

## Connection String Format

| Key | Description | Required | Default |
|-----|-------------|----------|---------|
| `Data Source` | Path to the database file or directory | ✅ Yes | — |
| `Password` | Encryption password (AES-256-GCM) | ✅ Yes | `"default"` |
| `Cache` | `Shared` (connection pooling) or `Private` | No | `Private` |
| `ReadOnly` | Open database in read-only mode | No | `false` |

**Examples:**
```
Data Source=./data.db;Password=MySecurePass123
Data Source=C:\databases\app.db;Password=Pass;Cache=Shared
Data Source=/var/data/app.db;Password=Pass;ReadOnly=true
```

---

## Dependency Injection (ASP.NET Core / Razor Pages)

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register DbContext with SharpCoreDB
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB(
        builder.Configuration.GetConnectionString("SharpCoreDB")
        ?? "Data Source=./app.db;Password=SecurePassword123;Cache=Shared"));

var app = builder.Build();

// Ensure database is created on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}
```

### appsettings.json

```json
{
  "ConnectionStrings": {
    "SharpCoreDB": "Data Source=./app.db;Password=SecurePassword123;Cache=Shared"
  }
}
```

---

## Provider-Specific Options

```csharp
optionsBuilder.UseSharpCoreDB(
    "Data Source=./data.db;Password=MyPass",
    options =>
    {
        // Set command timeout (inherited from RelationalDbContextOptionsBuilder)
        options.CommandTimeout(30);

        // Set max batch size for SaveChanges
        options.MaxBatchSize(100);
    });
```

### Generic DbContext Registration

```csharp
// Type-safe registration with UseSharpCoreDB<TContext>
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSharpCoreDB<AppDbContext>(
        "Data Source=./app.db;Password=Pass123",
        o => o.CommandTimeout(60)));
```

---

## Supported EF Core Features

### ✅ Working

| Feature | Status |
|---------|--------|
| **CRUD** (Add, Update, Delete, Find) | ✅ Full |
| **LINQ Queries** (Where, Select, OrderBy, GroupBy, Join) | ✅ Full |
| **SaveChanges / SaveChangesAsync** | ✅ Full |
| **EnsureCreated / EnsureDeleted** | ✅ Full |
| **Transactions** (Begin, Commit, Rollback) | ✅ Full |
| **Async operations** (ToListAsync, SaveChangesAsync, etc.) | ✅ Full |
| **Change Tracking** | ✅ Full |
| **Migrations** (CreateTable, DropTable, AddColumn, DropColumn, CreateIndex, DropIndex, RenameTable, AlterColumn) | ✅ Full |
| **Type Mappings** (int, long, string, bool, double, float, decimal, DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly, Guid, byte[], byte, short, char, etc.) | ✅ Full |
| **LINQ String Translations** (Contains → LIKE, StartsWith, EndsWith, ToUpper → UPPER, ToLower → LOWER, Trim, Replace, Substring, EF.Functions.Like) | ✅ Full |
| **LINQ Member Translations** (DateTime.Now → NOW(), DateTime.UtcNow, string.Length → LENGTH()) | ✅ Full |
| **SQL Functions** (SUM, AVG, COUNT, GROUP_CONCAT, DATEADD, STRFTIME) | ✅ Full |
| **Indexes** (B-tree, Unique) | ✅ Full |
| **Relationships / Navigation Properties** | ✅ Via SQL JOINs |
| **Connection Pooling** (Cache=Shared) | ✅ Full |

### ⚠️ Limitations

| Feature | Notes |
|---------|-------|
| **Compiled Queries** (`EF.CompileQuery`) | Queries work via relational pipeline; compiled query caching is passthrough |
| **Value Conversions** | Supported via EF Core's built-in converters |
| **Spatial Types** | Not supported (no geometry/geography) |
| **JSON Columns** | Not supported |
| **Batch UPDATE/DELETE** (`ExecuteUpdate`/`ExecuteDelete`) | Not yet implemented |
| **COLLATE Support** | ✅ Fixed in v1.3.0 - CREATE TABLE now emits COLLATE clauses |

---

## Collation Support (v1.3.0+)

SharpCoreDB supports column-level collations including `NOCASE` for case-insensitive comparisons and `LOCALE()` for culture-specific sorting.

### Basic Collation Configuration

```csharp
public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            // Case-insensitive username
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .UseCollation("NOCASE");  // ✅ Fixed in v1.3.0
                
            // Locale-specific email sorting
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .UseCollation("LOCALE(\"en-US\")");
        });
    }
}
```

### Generated SQL (v1.3.0+)

```sql
CREATE TABLE User (
    Id INTEGER PRIMARY KEY AUTO,
    Username TEXT COLLATE NOCASE NOT NULL,
    Email TEXT COLLATE LOCALE("en-US") NOT NULL
)
```

### Direct SQL Queries with Collations

```csharp
// Case-insensitive WHERE clause (uses NOCASE from column definition)
var users = await db.Users
    .FromSqlRaw("SELECT * FROM User WHERE Username = 'ALICE'")
    .ToListAsync();

// Will match 'alice', 'Alice', 'ALICE', etc.
```

### Known Limitations

- **EF Core LINQ Query Provider**: Full LINQ query translation for collations is pending infrastructure work
- **Workaround**: Use `FromSqlRaw` for complex collation queries or call direct SQL via `ExecuteQuery()`
- **What Works**: CREATE TABLE emission, direct SQL queries, case-insensitive WHERE clauses
- **What's Pending**: Full LINQ expression translation (e.g., `db.Users.Where(u => u.Username == "ALICE")`)

---

## Encryption

All data is encrypted at rest with **AES-256-GCM** (Galois/Counter Mode):

- **Key Derivation**: PBKDF2 with SHA-256
- **Hardware Acceleration**: Uses AES-NI instructions when available
- **Authenticated Encryption**: Prevents tampering and ensures data integrity

```csharp
// Load password securely from environment
var password = Environment.GetEnvironmentVariable("DB_PASSWORD")
    ?? throw new InvalidOperationException("DB_PASSWORD not set");

optionsBuilder.UseSharpCoreDB($"Data Source=./secure.db;Password={password}");
```

---

## Migrations

### Create & Apply Migrations

```bash
dotnet ef migrations add InitialCreate --project YourProject.csproj
dotnet ef database update --project YourProject.csproj
```

### Supported Migration Operations

| Operation | SQL Generated |
|-----------|---------------|
| `CreateTable` | `CREATE TABLE ...` |
| `DropTable` | `DROP TABLE IF EXISTS ...` |
| `AddColumn` | `ALTER TABLE ... ADD COLUMN ...` |
| `DropColumn` | `ALTER TABLE ... DROP COLUMN ...` |
| `RenameTable` | `ALTER TABLE ... RENAME TO ...` |
| `AlterColumn` | `ALTER TABLE ... ALTER COLUMN ...` |
| `CreateIndex` | `CREATE [UNIQUE] INDEX ...` |
| `DropIndex` | `DROP INDEX IF EXISTS ...` |
| `InsertData` | `INSERT OR REPLACE INTO ...` |

---

## Complete Example

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.EntityFrameworkCore;

// --- Entities ---
public class Blog
{
    public int BlogId { get; set; }
    public required string Title { get; set; }
    public string? Url { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Post> Posts { get; set; } = [];
}

public class Post
{
    public int PostId { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public int BlogId { get; set; }
    public Blog Blog { get; set; } = null!;
}

// --- DbContext ---
public class BlogDbContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();

    public BlogDbContext(DbContextOptions<BlogDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);

        modelBuilder.Entity<Blog>()
            .HasIndex(b => b.Title);
    }
}

// --- Usage ---
var services = new ServiceCollection();
services.AddDbContext<BlogDbContext>(options =>
    options.UseSharpCoreDB("Data Source=./blog.db;Password=MySecurePassword123;Cache=Shared"));

var provider = services.BuildServiceProvider();
await using var context = provider.GetRequiredService<BlogDbContext>();

await context.Database.EnsureCreatedAsync();

// Create
context.Blogs.Add(new Blog
{
    Title = "My Tech Blog",
    Url = "https://myblog.com",
    CreatedAt = DateTime.UtcNow,
    Posts =
    [
        new Post { Title = "First Post", Content = "Hello World!" },
        new Post { Title = "EF Core with SharpCoreDB", Content = "It works!" }
    ]
});
await context.SaveChangesAsync();

// Query with LINQ
var blogs = await context.Blogs
    .Where(b => b.Title.Contains("Tech"))
    .OrderByDescending(b => b.CreatedAt)
    .ToListAsync();

var postCount = await context.Posts.CountAsync();
Console.WriteLine($"Found {blogs.Count} blogs with {postCount} total posts");
```

---

## Platform Support

| Platform | Architectures | Status |
|----------|--------------|--------|
| Windows | x64, ARM64 | ✅ Fully Supported |
| Linux | x64, ARM64 | ✅ Fully Supported |
| macOS | x64 (Intel), ARM64 (Apple Silicon) | ✅ Fully Supported |
| Android | ARM64, x64 | ✅ Fully Supported |
| iOS | ARM64 | ✅ Fully Supported |
| IoT/Embedded | ARM64, x64 | ✅ Fully Supported |

---

## Troubleshooting

### "Connection string must be configured"

Ensure you pass a valid connection string with at least `Data Source`:

```csharp
// ❌ Wrong — empty or missing
optionsBuilder.UseSharpCoreDB("");

// ✅ Correct
optionsBuilder.UseSharpCoreDB("Data Source=./data.db;Password=MyPass");
```

### "Database instance is not initialized"

The connection is not open. EF Core opens connections automatically, but if using raw SQL, ensure the connection is open first.

### Migration not applying

Ensure the database file is not locked by another process. Dispose contexts properly:

```csharp
await using (var context = new AppDbContext())
{
    await context.Database.MigrateAsync();
}
```

---

## Resources

- **NuGet Package**: [SharpCoreDB.EntityFrameworkCore](https://www.nuget.org/packages/SharpCoreDB.EntityFrameworkCore)
- **Core Library**: [SharpCoreDB](https://www.nuget.org/packages/SharpCoreDB)
- **Repository**: [GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)
- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

---

## License

MIT License — see [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.

---

**Version**: 1.3.0  
**Last Updated**: 2026  
**Compatibility**: .NET 10.0+, EF Core 10.0.2+, SharpCoreDB 1.3.0, C# 14  
**Platforms**: Windows, Linux, macOS, Android, iOS, IoT (x64, ARM64)
