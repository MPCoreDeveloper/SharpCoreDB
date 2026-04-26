# SharpCoreDB NuGet Package

This package is part of SharpCoreDB, a high-performance embedded database for .NET 10.


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Documentation

For full documentation, see: https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md

## Quick Start

See the main repository for usage examples.

# SharpCoreDB.Extensions v1.7.1

**Dapper Integration and ASP.NET Core Extensions**

Dapper ORM integration and ASP.NET Core health check extensions for SharpCoreDB.

## ✨ What's New in v1.7.1

- ✅ Inherits metadata improvements from SharpCoreDB v1.7.1
- ✅ Dapper integration for lightweight ORM
- ✅ ASP.NET Core health checks
- ✅ Zero breaking changes

## 🚀 Key Features

- **Dapper Integration**: Lightweight ORM for SharpCoreDB
- **Health Checks**: ASP.NET Core health check extensions
- **Dependency Injection**: Seamless DI integration
- **Connection Pooling**: Built-in connection management
- **Query Caching**: Automatic query plan caching

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
dotnet add package SharpCoreDB.Extensions --version 1.7.1
```

**Requires:** SharpCoreDB v1.7.1+

---

**Version:** 1.7.1 | **Status:** ✅ Production Ready


