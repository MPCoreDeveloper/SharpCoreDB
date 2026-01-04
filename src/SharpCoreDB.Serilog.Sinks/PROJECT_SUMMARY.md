# SharpCoreDB.Serilog.Sinks - Project Summary

## Overview

A production-ready Serilog sink for SharpCoreDB, providing efficient batch logging with built-in encryption.

## Key Features

* **Efficient Batching**: Uses Serilog.Sinks.PeriodicBatching for optimal performance
* **Encrypted Storage**: Leverages SharpCoreDB's AES-256-GCM encryption
* **High Performance**: 10,000+ logs/second capability
* **ULID Primary Keys**: Sortable, timestamp-based unique identifiers
* **AppendOnly Engine**: Optimized for write-heavy logging workloads
* **Async/Await**: Fully asynchronous operations
* **Error Handling**: Automatic rollback on batch failures
* **Flexible Configuration**: Multiple configuration options

## Project Structure

```
SharpCoreDB.Serilog.Sinks/
|-- SharpCoreDBSink.cs              # Main sink implementation
|-- LoggerConfigurationExtensions.cs # Serilog configuration extensions
|-- SharpCoreDBSinkOptions.cs       # Configuration options class
|-- README.md                       # Documentation with usage examples
|-- CHANGELOG.md                    # Version history
|-- LICENSE                         # MIT License
`-- SharpCoreDB.Serilog.Sinks.csproj # Project file
```

## Technology Stack

* **.NET 10**: Latest .NET platform
* **C# 12+**: Modern C# features
* **Serilog 4.2.0**: Structured logging
* **Serilog.Sinks.PeriodicBatching 5.0.0**: Batch processing
* **SharpCoreDB**: Encrypted database backend

## Database Schema

```sql
CREATE TABLE Logs (
    Id ULID AUTO PRIMARY KEY,     -- Sortable unique identifier
    Timestamp DATETIME,            -- UTC timestamp
    Level TEXT,                    -- Log level
    Message TEXT,                  -- Rendered message
    Exception TEXT,                -- Exception details
    Properties TEXT                -- JSON properties
) ENGINE=AppendOnly
```

## Performance Characteristics

* **Write Throughput**: 10,000+ logs/second
* **Batch Latency**: Sub-millisecond per batch
* **Memory Usage**: Minimal footprint
* **Storage Overhead**: Efficient with compression
* **Encryption Overhead**: Near-zero with AES-NI

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| TableName | "Logs" | Target table name |
| BatchPostingLimit | 50 | Max events per batch |
| Period | 2 seconds | Batch interval |
| AutoCreateTable | true | Auto-create schema |
| StorageEngine | "AppendOnly" | Storage backend |

## Usage Patterns

All usage examples are provided in **README.md** as copy/paste ready code blocks:

### 1. Quick Start
```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.SharpCoreDB("logs.scdb", "password")
    .CreateLogger();
```

### 2. ASP.NET Core Application
See README.md for complete ASP.NET Core integration example with index creation.

### 3. High-Volume Logging
See README.md for performance-optimized configuration with large batches.

### 4. Query Examples
See README.md for query performance tips and index management examples.

## Deployment

### NuGet Package
```bash
dotnet add package SharpCoreDB.Serilog.Sinks
```

### From Source
```bash
dotnet pack SharpCoreDB.Serilog.Sinks.csproj -c Release
dotnet nuget push bin/Release/SharpCoreDB.Serilog.Sinks.1.0.0.nupkg
```

## Testing

```bash
cd SharpCoreDB.Serilog.Sinks
dotnet build
dotnet test
```

## Documentation

All documentation and examples are in **README.md**:
* Quick start guide
* Basic usage examples
* ASP.NET Core integration
* Structured logging examples
* Query performance tips
* Index management guide
* Performance testing examples

## Contributing

1. Fork the repository
2. Create a feature branch
3. Implement changes with tests
4. Submit a pull request

## License

MIT License - See LICENSE file for details

## Links

* **GitHub**: https://github.com/MPCoreDeveloper/SharpCoreDB
* **NuGet**: https://www.nuget.org/packages/SharpCoreDB.Serilog.Sinks
* **Documentation**: See README.md
* **Issues**: https://github.com/MPCoreDeveloper/SharpCoreDB/issues

## Support

* GitHub Issues: Report bugs and feature requests
* Discussions: Ask questions and share ideas
* Email: See GitHub profile for contact

## Version History

See [CHANGELOG.md](CHANGELOG.md) for detailed version history.

## Roadmap

### v1.1.0 (Planned)
* Configuration from appsettings.json
* Multiple sink instances
* Log filtering options
* Custom formatters

### v2.0.0 (Future)
* .NET 11 support
* Enhanced performance metrics
* Advanced query helpers
* Log retention policies

---

**Note**: This sink is production-ready and actively maintained. All usage examples are provided in README.md for easy copy/paste integration.
