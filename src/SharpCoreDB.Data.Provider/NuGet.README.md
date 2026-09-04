# SharpCoreDB.Data.Provider v2.0.0.2

**ADO.NET Data Provider for SharpCoreDB**

Complete ADO.NET provider enabling standard database connectivity patterns with SharpCoreDB's encryption and performance.


## Patch updates in v1.9.5 (archived; current release 2.0.0.2)

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- ✅ Inherits metadata improvements from SharpCoreDB v1.9.5
- ✅ Enterprise connectivity features
- ✅ Full ADO.NET compatibility
- ✅ Zero breaking changes

## 🚀 Key Features

- **ADO.NET Compatibility**: DbConnection, DbCommand, DbDataReader implementations
- **Standard Patterns**: Connection pooling, parameterized queries, transactions
- **Encryption**: AES-256-GCM transparent encryption
- **Performance**: High-speed data access with caching
- **Production Ready**: Enterprise-grade reliability

## 💻 Quick Example

```csharp
using System.Data;
using SharpCoreDB.Data.Provider;

using var connection = new SharpCoreDbConnection("mydb.scdb", "password");
connection.Open();

using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM users WHERE id = @id";
command.Parameters.Add("@id", 1);

using var reader = command.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"Name: {reader["name"]}");
}
```

## 📚 Documentation

- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)
- [Changelog](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/CHANGELOG.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Data.Provider --version 2.0.0.0
```

**Requires:** SharpCoreDB v1.9.5+

---

**Version:** 1.9.5 | **Status:** ✅ Production Ready




