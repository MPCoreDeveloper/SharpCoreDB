# Modern C# 14 Features Guide - SharpCoreDB

**Version**: 1.0  
**Target**: C# 14 / .NET 10  
**Date**: December 2025

## Overview

This guide documents all C# 14 features applied during the SharpCoreDB modernization effort, providing before/after examples and best practices.

---

## 1. Collection Expressions

**What**: New syntax for initializing collections using `[]` instead of `new List<T>()`, `new[]`, or `ToList()`.

**Why**: More concise, consistent, and readable. Works with any collection type.

### Before (C# 11):
```csharp
var list = new List<string>();
var array = new[] { 1, 2, 3 };
var items = someQuery.ToList();
```

### After (C# 14):
```csharp
var list = new List<string>();  // Still valid, but...
List<string> list = [];         // ✅ Preferred: collection expression
var array = [1, 2, 3];          // ✅ Array initialization
List<string> items = [.. someQuery];  // ✅ With spread operator
```

### Spread Operator:
```csharp
// Before
var combined = list1.Concat(list2).ToList();

// After
List<int> combined = [.. list1, .. list2];
```

### Applied In:
- `Database.Core.cs`: Field initializations
- `Database.Batch.cs`: Batch statement collections
- `SqlParser.Core.cs`: Token parsing
- `Storage.Core.cs`: Buffer management

---

## 2. File-Scoped Namespaces

**What**: Declare namespace once at the top without braces.

**Why**: Reduces indentation, cleaner code, one less level of nesting.

### Before:
```csharp
namespace SharpCoreDB
{
    public class Database
    {
        // ...
    }
}
```

### After (C# 14):
```csharp
namespace SharpCoreDB;

public class Database
{
    // ...
}
```

### Applied In:
- **All** .cs files in SharpCoreDB project
- All partial class files
- All interface files

---

## 3. Primary Constructors

**What**: Constructor parameters declared directly in class declaration.

**Why**: Eliminates boilerplate field declarations for simple classes.

### Before:
```csharp
public class DatabaseFactory
{
    private readonly IServiceProvider _services;
    
    public DatabaseFactory(IServiceProvider services)
    {
        _services = services;
    }
}
```

### After (C# 14):
```csharp
public class DatabaseFactory(IServiceProvider services)
{
    // 'services' parameter is automatically available as a field
    public IDatabase Create(string path, string password)
    {
        var crypto = services.GetRequiredService<ICryptoService>();
        // ...
    }
}
```

### Applied In:
- `DatabaseFactory.cs`
- `ColumnStore<T>` (partial)
- Service classes with simple dependencies

---

## 4. Pattern Matching: `is null` / `is not null`

**What**: More expressive null checking with pattern syntax.

**Why**: Clearer intent, consistent with other patterns, avoids operator overloads.

### Before:
```csharp
if (value == null) { }
if (value != null) { }
```

### After (C# 14):
```csharp
if (value is null) { }      // ✅ Preferred
if (value is not null) { }  // ✅ Preferred
```

### Applied In:
- All null checks in Database.*.cs
- All null checks in Storage.*.cs
- Parameter validation
- Return value checks

---

## 5. `ArgumentNullException.ThrowIfNull()`

**What**: Built-in method for argument validation.

**Why**: Eliminates boilerplate, consistent error messages, better performance.

### Before:
```csharp
public void Method(string value)
{
    if (value == null)
        throw new ArgumentNullException(nameof(value));
}
```

### After (C# 14):
```csharp
public void Method(string value)
{
    ArgumentNullException.ThrowIfNull(value);
}
```

### Applied In:
- `Database.Execution.cs`: All public methods
- `IDatabase` implementations
- Service constructors

---

## 6. Init-Only Setters

**What**: Properties that can only be set during object initialization.

**Why**: Immutable after construction, better than readonly for object initializers.

### Before:
```csharp
public class DatabaseConfig
{
    public bool EnableQueryCache { get; set; }
    // Problem: Can be changed after construction!
}
```

### After (C# 14):
```csharp
public class DatabaseConfig
{
    public bool EnableQueryCache { get; init; }  // ✅ Immutable after init
}

// Usage:
var config = new DatabaseConfig
{
    EnableQueryCache = true
};
// config.EnableQueryCache = false; // ❌ Compile error!
```

### Applied In:
- `DatabaseConfig`: All properties
- `SecurityConfig`: All properties
- `QueryPlanCache.CacheEntry`

---

## 7. Target-Typed `new`

**What**: Omit type name when creating objects if type is obvious.

**Why**: Reduces redundancy, cleaner code.

### Before:
```csharp
Dictionary<string, object> dict = new Dictionary<string, object>();
Lock lockObj = new Lock();
```

### After (C# 14):
```csharp
Dictionary<string, object> dict = new();  // ✅ Type inferred
Lock lockObj = new();                      // ✅ Type inferred
```

### Applied In:
- `Database.Core.cs`: Lock, dictionary initializations
- Field declarations with explicit types
- Return statements

---

## 8. Switch Expressions

**What**: Expression-based switch for concise value returns.

**Why**: More functional style, forces exhaustive handling, cleaner.

### Before:
```csharp
string GetType(DataType type)
{
    switch (type)
    {
        case DataType.Integer:
            return "INT";
        case DataType.String:
            return "TEXT";
        default:
            return "UNKNOWN";
    }
}
```

### After (C# 14):
```csharp
string GetType(DataType type) => type switch
{
    DataType.Integer => "INT",
    DataType.String => "TEXT",
    _ => "UNKNOWN"
};
```

### Applied In:
- `Database.Core.cs`: IsSchemaChangingCommand()
- `SqlParser.DML.cs`: Type parsing
- `Storage.Core.cs`: Mode selection

---

## 9. Expression-Bodied Members

**What**: Single-expression methods/properties using `=>`.

**Why**: Concise for simple operations.

### Before:
```csharp
public string DbPath
{
    get { return _dbPath; }
}

public void ClearCache()
{
    queryCache?.Clear();
}
```

### After (C# 14):
```csharp
public string DbPath => _dbPath;  // ✅ Property

public void ClearCache() => queryCache?.Clear();  // ✅ Method
```

### Applied In:
- Simple property getters
- Single-line methods
- Wrapper methods

---

## 10. Tuple Deconstruction

**What**: Unpack tuple values directly into variables.

**Why**: Cleaner than accessing tuple members.

### Before:
```csharp
var stats = GetStatistics();
var hits = stats.Hits;
var misses = stats.Misses;
```

### After (C# 14):
```csharp
var (hits, misses, hitRate, count) = GetStatistics();  // ✅ Deconstructed
```

### Applied In:
- `Database.Statistics.cs`: GetQueryCacheStatistics()
- Multiple return value methods
- Iteration over key-value pairs: `foreach (var (key, value) in dict)`

---

## 11. Global Using Directives

**What**: Define common usings once for entire project.

**Why**: Reduces repetition in every file.

### GlobalUsings.cs (new file):
```csharp
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using SharpCoreDB.Interfaces;
global using SharpCoreDB.DataStructures;
```

### Applied In:
- New `GlobalUsings.cs` file in project root
- Removed from individual files

---

## 12. Async File I/O

**What**: Use async methods for file operations.

**Why**: Non-blocking I/O, better scalability, responsive UI.

### Before:
```csharp
var data = File.ReadAllBytes(path);
File.WriteAllBytes(path, data);
```

### After (C# 14):
```csharp
var data = await File.ReadAllBytesAsync(path, cancellationToken);
await File.WriteAllBytesAsync(path, data, cancellationToken);
```

### Applied In:
- `Storage.ReadWrite.cs`: Read/Write operations
- `Database.Core.cs`: Load/SaveMetadata (with sync fallback)
- `GroupCommitWAL`: WAL operations

### Backward Compatibility:
```csharp
// Keep sync versions for backward compatibility
public void Write(string path, byte[] data)
{
    WriteAsync(path, data).GetAwaiter().GetResult();
}

public async Task WriteAsync(string path, byte[] data, CancellationToken ct = default)
{
    await File.WriteAllBytesAsync(path, data, ct);
}
```

---

## 13. `ValueTask<T>` for Hot Paths

**What**: Lightweight alternative to `Task<T>` for frequently synchronous operations.

**Why**: Reduces allocations when operation completes synchronously.

### Before:
```csharp
public Task<List<Row>> QueryAsync(string sql)
{
    if (queryCache.TryGetValue(sql, out var cached))
    {
        return Task.FromResult(cached);  // ❌ Allocates Task
    }
    return QueryInternalAsync(sql);
}
```

### After (C# 14):
```csharp
public ValueTask<List<Row>> QueryAsync(string sql)
{
    if (queryCache.TryGetValue(sql, out var cached))
    {
        return new ValueTask<List<Row>>(cached);  // ✅ No allocation
    }
    return new ValueTask<List<Row>>(QueryInternalAsync(sql));
}
```

### Applied In:
- Query cache fast paths
- Prepared statement execution
- Hot path database operations

---

## 14. `Lock` Statement (C# 14)

**What**: New `Lock` type for better lock semantics.

**Why**: More efficient, prevents lock stealing, better debugging.

### Before:
```csharp
private readonly object _lock = new();

lock (_lock)
{
    // Critical section
}
```

### After (C# 14):
```csharp
private readonly Lock _lock = new();  // ✅ New Lock type

lock (_lock)  // ✅ Same syntax, better implementation
{
    // Critical section
}
```

### Applied In:
- `Database.Core.cs`: `_walLock`
- Transaction management
- Cache synchronization

---

## 15. Raw String Literals (for SQL)

**What**: Multi-line strings with `"""` that preserve formatting.

**Why**: Clean SQL queries without escaping.

### Before:
```csharp
var sql = "SELECT * FROM users\n" +
          "WHERE id = @id\n" +
          "ORDER BY name";
```

### After (C# 14):
```csharp
var sql = """
    SELECT * FROM users
    WHERE id = @id
    ORDER BY name
    """;
```

### Applied In:
- SQL test fixtures
- Example queries in documentation
- Query templates

---

## Migration Checklist

### High Priority (Performance Impact)
- [ ] Apply collection expressions (`[]`)
- [ ] Convert to async File I/O
- [ ] Apply `ValueTask<T>` for hot paths
- [ ] Use `Lock` type for synchronization
- [ ] Apply `ArgumentNullException.ThrowIfNull()`

### Medium Priority (Code Quality)
- [ ] Apply file-scoped namespaces
- [ ] Use `is null` / `is not null`
- [ ] Apply primary constructors
- [ ] Convert to switch expressions
- [ ] Apply init-only setters

### Low Priority (Nice to Have)
- [ ] Create global usings file
- [ ] Apply target-typed new
- [ ] Use tuple deconstruction
- [ ] Apply expression-bodied members
- [ ] Use raw string literals for SQL

---

## Testing Requirements

After applying each modernization:

1. **Run Full Test Suite**
   ```bash
   dotnet test
   ```
   - All 141+ tests must pass
   - No new warnings

2. **Performance Benchmarks**
   ```bash
   cd SharpCoreDB.Benchmarks
   dotnet run -c Release
   ```
   - No regression in key metrics
   - Document improvements

3. **API Compatibility Check**
   - All public API signatures unchanged
   - Sync methods still work
   - Configuration classes backward compatible

---

## Example: Complete File Modernization

### Before (C# 11):
```csharp
namespace SharpCoreDB
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    
    public class DatabaseFactory
    {
        private readonly IServiceProvider _services;
        
        public DatabaseFactory(IServiceProvider services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            _services = services;
        }
        
        public IDatabase Create(string path, string password)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
                
            var config = new DatabaseConfig();
            config.EnableQueryCache = true;
            config.QueryCacheSize = 1000;
            
            return new Database(_services, path, password, false, config);
        }
    }
}
```

### After (C# 14):
```csharp
namespace SharpCoreDB;  // ✅ File-scoped namespace

/// <summary>
/// Factory for creating Database instances with dependency injection.
/// Modern C# 14 with primary constructor pattern.
/// </summary>
/// <param name="services">The service provider for dependency injection.</param>
public class DatabaseFactory(IServiceProvider services)  // ✅ Primary constructor
{
    public IDatabase Create(string path, string password)
    {
        ArgumentNullException.ThrowIfNull(path);      // ✅ Modern validation
        ArgumentNullException.ThrowIfNull(password);
        
        var config = new DatabaseConfig               // ✅ Target-typed new
        {
            EnableQueryCache = true,                  // ✅ Init-only setters
            QueryCacheSize = 1000
        };
        
        return new Database(services, path, password, false, config);
    }
}
```

---

## Resources

- [C# 14 Language Specification](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
- [.NET 10 Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- [Collection Expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/collection-expressions)
- [Primary Constructors](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12#primary-constructors)
- [File-Scoped Namespaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/namespace)

---

**Status**: ✅ Guide Complete - Ready for Implementation
