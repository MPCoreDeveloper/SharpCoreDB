# SharpCoreDB.Serilog.Sinks v2.0.0.2

**Serilog Sink for SharpCoreDB**

Efficient batch logging to SharpCoreDB with AES-256-GCM encryption and AppendOnly storage for maximum write speed.


## Patch updates in v1.9.5 (archived; current release 2.0.0.2)

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- ✅ Inherits metadata improvements from SharpCoreDB v1.9.5
- ✅ Efficient batch logging
- ✅ Enterprise encryption
- ✅ Production ready

## 🚀 Key Features

- **Batch Logging**: Write-optimized for high-throughput logging
- **Encryption**: AES-256-GCM transparent encryption
- **AppendOnly**: Maximum write performance
- **Async Support**: Full async/await support
- **Structured Logging**: Rich context preservation

## 💻 Quick Example

```csharp
using Serilog;
using Serilog.Sinks.SharpCoreDB;

Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB(
        databasePath: "logs.scdb",
        password: "logpassword",
        batchSize: 1000)
    .CreateLogger();

Log.Information("Application started");
Log.Error(ex, "An error occurred");
```

## 📚 Documentation

- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Serilog.Sinks --version 2.0.0.0
```

**Requires:** SharpCoreDB v1.9.5+, Serilog v2.13+

---

**Version:** 1.9.5 | **Status:** ✅ Production Ready




