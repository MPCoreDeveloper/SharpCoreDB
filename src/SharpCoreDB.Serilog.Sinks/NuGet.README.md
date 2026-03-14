# SharpCoreDB.Serilog.Sinks v1.5.0

**Serilog Sink for SharpCoreDB**

Efficient batch logging to SharpCoreDB with AES-256-GCM encryption and AppendOnly storage for maximum write speed.

## ✨ What's New in v1.5.0

- ✅ Inherits metadata improvements from SharpCoreDB v1.5.0
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
dotnet add package SharpCoreDB.Serilog.Sinks --version 1.5.0
```

**Requires:** SharpCoreDB v1.5.0+, Serilog v2.13+

---

**Version:** 1.5.0 | **Status:** ✅ Production Ready

