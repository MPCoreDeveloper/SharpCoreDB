# SharpCoreDB NuGet Package

This package is part of SharpCoreDB, a high-performance embedded database for .NET 10.


## What's New in v1.8.0

- **FluentMigrator alignment**: `AddSharpCoreDBFluentMigrator()` now defaults both generator and processor to SQLite-compatible mode.
- Inherits all v1.8.0 engine improvements (Auto-ROWID, GRAPH_RAG, SIMD optimization, Logging.Abstractions 10.0.7).
- Zero breaking changes.

## Documentation

For full documentation, see: https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md

## Quick Start

See the main repository for usage examples.

# SharpCoreDB.Extensions v1.8.0

**Dapper Integration and ASP.NET Core Extensions**

Dapper ORM integration and ASP.NET Core health check extensions for SharpCoreDB.

## ✨ What's New in v1.8.0

- ✅ **FluentMigrator alignment**: `AddSharpCoreDBFluentMigrator()` defaults generator and processor to SQLite-compatible mode
- ✅ Inherits v1.8.0 engine improvements (Auto-ROWID, GRAPH_RAG SQL, SIMD optimization)
- ✅ `Microsoft.Extensions.Logging.Abstractions` updated to 10.0.7
- ✅ Zero breaking changes

## 🚀 Key Features

- **Dapper Integration**: Lightweight ORM for SharpCoreDB
- **Health Checks**: ASP.NET Core health check extensions
- **Dependency Injection**: Seamless DI integration
- **Connection Pooling**: Built-in connection management
- **Query Caching**: Automatic query plan caching
- **FluentMigrator**: Defaults to SQLite-compatible migration processing for `AddSharpCoreDBFluentMigrator()`

## FluentMigrator Default Behavior

When you register FluentMigrator with:

```csharp
services.AddSharpCoreDBFluentMigrator();
```

the extension now aligns both sides of the pipeline by default:

- FluentMigrator uses the `sqlite` generator
- the SharpCoreDB processor defaults `ProviderSwitches` to `syntax=sqlite`

This ensures SQLite-incompatible migration operations fail fast with clear errors instead of producing mismatched SQL behavior.

To opt into a different syntax mode, configure `ProcessorOptions.ProviderSwitches` explicitly after registration.

## 💻 Quick Example

```csharp
using Dapper;
using SharpCoreDB.Extensions;

using var connection = new SharpCoreDbConnection("mydb.scdb", "password");

var users = connection.Query<User>(
    "SELECT * FROM users WHERE active = @active",
    new { active = true }
);

var userId = connection.QuerySingle<int>(
    "SELECT id FROM users WHERE email = @email",
    new { email = "user@example.com" }
);
```

## 📚 Documentation

- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Extensions --version 1.8.0
```

**Requires:** SharpCoreDB v1.8.0+

---

**Version:** 1.8.0 | **Status:** ✅ Production Ready



