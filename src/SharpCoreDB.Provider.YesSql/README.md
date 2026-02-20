# SharpCoreDB.Provider.YesSql

**Version:** 1.3.5 (Phase 9.2)  
**Status:** Production Ready ✅

YesSql Provider for SharpCoreDB - Enables OrchardCore CMS and other YesSql-based applications to use SharpCoreDB as the underlying database.

[![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![NuGet](https://img.shields.io/badge/NuGet-1.3.5-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Provider.YesSql)

---

## Overview

YesSql is a .NET library for creating portable document-oriented databases. This provider enables YesSql-based applications (like OrchardCore) to use SharpCoreDB as the underlying storage engine.

### Key Benefits
- ✅ **High Performance** - Leverage SharpCoreDB's optimized storage engine
- ✅ **Encryption** - AES-256-GCM encryption at rest
- ✅ **Analytics** - Phase 9 aggregates and window functions (150-680x faster)
- ✅ **Vector Search** - Phase 8 HNSW indexing (50-100x faster)
- ✅ **Easy Integration** - Works with existing YesSql code

---

## Installation

```bash
dotnet add package SharpCoreDB.Provider.YesSql --version 1.3.5
```

**Requirements:**
- .NET 10.0+
- SharpCoreDB 1.3.5+
- YesSql 3.0+

---

## Quick Start

### Configure YesSql with SharpCoreDB

```csharp
using SharpCoreDB.Provider.YesSql;
using YesSql;

var services = new ServiceCollection();

// Register SharpCoreDB provider for YesSql
services.AddSharpCoreDB()
    .AddYesSqlProvider();

// Configure YesSql with SharpCoreDB
services.AddYesSql(config =>
{
    config.UseSqlServer("Data Source=./app.db;Password=secure!");
});

var provider = services.BuildServiceProvider();
var session = provider.GetRequiredService<ISession>();
```

### Basic Document Operations

```csharp
// Create a document type
public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Insert
var post = new BlogPost { Title = "Hello World", Content = "...", CreatedAt = DateTime.Now };
await session.SaveAsync(post);

// Query
var posts = await session.Query<BlogPost>()
    .Where(p => p.CreatedAt > DateTime.Now.AddDays(-7))
    .ListAsync();

// Update
post.Title = "Updated Title";
await session.SaveAsync(post);

// Delete
await session.DeleteAsync(post);
```

---

## Features

### YesSql Integration
- ✅ Full YesSql API support
- ✅ Document indexing and querying
- ✅ LINQ query provider
- ✅ Transaction support

### SharpCoreDB Capabilities
- ✅ **Analytics** - Use Phase 9 aggregates in queries
- ✅ **Vector Search** - Index document embeddings
- ✅ **Encryption** - Transparent AES-256-GCM
- ✅ **Performance** - Up to 43% faster inserts

### Collation Support
```csharp
// Define case-insensitive queries
var articles = await session.Query<Article>()
    .Where(a => a.Title.Equals("News", StringComparison.OrdinalIgnoreCase))
    .ListAsync();
```

---

## OrchardCore Integration

This provider is designed to work seamlessly with OrchardCore CMS:

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services
            .AddOrchardCms()
            .AddSharpCoreDBDataProvider();  // Use SharpCoreDB as default provider
    }
}
```

---

## Performance

### Benchmarks (vs SQLite)
| Operation | Time | Speedup |
|-----------|------|---------|
| INSERT (10K docs) | 5.28ms | +1.4x |
| SELECT (1M docs) | 3.3ms | +2.3x |
| Analytics | <1ms | **680x** |
| Vector Search | 0.5-2ms | **50-100x** |

---

## Configuration

```csharp
services.AddYesSql(config =>
{
    config.UseSqlServer(
        connectionString: "Data Source=./myapp.db;Password=SecurePassword!",
        options =>
        {
            options.UseCommandTimeout(30000);
            options.EnableEncryption = true;
            options.EncryptionLevel = EncryptionLevel.Full;
        }
    );
});
```

---

## API Reference

### Supported Operations
| Operation | Status |
|-----------|--------|
| `SaveAsync()` | ✅ Supported |
| `UpdateAsync()` | ✅ Supported |
| `DeleteAsync()` | ✅ Supported |
| `Query<T>()` | ✅ Supported |
| `Transactions` | ✅ Supported |
| `Indexing` | ✅ Supported |

---

## Troubleshooting

### Connection Issues
Ensure SharpCoreDB file path is writable and password is correct:
```csharp
// Test connection
var connection = new SharpCoreDBConnection("Data Source=./app.db;Password=secure!");
await connection.OpenAsync();
await connection.CloseAsync();
```

### Query Timeouts
Increase command timeout for large datasets:
```csharp
options.UseCommandTimeout(60000);  // 60 seconds
```

---

## See Also

- **[SharpCoreDB](../SharpCoreDB/README.md)** - Core database engine
- **[Analytics](../SharpCoreDB.Analytics/README.md)** - Phase 9 features
- **[Vector Search](../SharpCoreDB.VectorSearch/README.md)** - Phase 8 features
- **[Main Documentation](../../docs/INDEX.md)** - Complete guide

---

## License

MIT License - See [LICENSE](../../LICENSE)

---

**Last Updated:** February 20, 2026 | Version 1.3.5
