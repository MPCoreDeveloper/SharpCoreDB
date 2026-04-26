# SharpCoreDB.Data.Provider v1.7.1

**ADO.NET Data Provider for SharpCoreDB**

Complete ADO.NET provider enabling standard database connectivity patterns with SharpCoreDB's encryption and performance.


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## ✨ What's New in v1.7.1

- ✅ Inherits metadata improvements from SharpCoreDB v1.7.1
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
dotnet add package SharpCoreDB.Data.Provider --version 1.7.1
```

**Requires:** SharpCoreDB v1.7.1+

---

**Version:** 1.7.1 | **Status:** ✅ Production Ready


