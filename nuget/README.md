# NuGet Packaging - SharpCoreDB

This directory contains assets for NuGet packaging.

## 📦 Current Version: **1.1.1** (February 2026)

### Installation

```bash
# Install latest version (1.1.1)
dotnet add package SharpCoreDB --version 1.1.1

# Or install with wildcard (gets latest)
dotnet add package SharpCoreDB
```

### Package Details
- **Package ID**: SharpCoreDB
- **Current Version**: 1.1.1
- **License**: MIT
- **Target Framework**: .NET 10.0
- **Language**: C# 14
- **NuGet Gallery**: https://www.nuget.org/packages/SharpCoreDB/1.1.1

### Version History

- **v1.1.1** (Feb 8, 2026): 
  - 🐛 Fixed critical localization bug (culture-independent parsing)
  - 🔄 Added `[Obsolete]` attributes to sync methods
  - ✅ No breaking changes - full backward compatibility
- **v1.1.0** (Jan 31, 2026): 
  - 🎉 Major RDBMS improvements
  - 🏆 Single-File performance breakthrough (37% faster than SQLite)
  - ⚡ 17x INSERT speedup with in-memory cache architecture
- **v1.0.0**: Initial production release

### Quick Start

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

// Async API (recommended as of v1.1.1)
using var db = factory.Create("./app_db", "StrongPassword!");
await db.ExecuteSQLAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
await db.ExecuteSQLAsync("INSERT INTO users VALUES (1, 'Alice')");
var rows = await db.ExecuteQueryAsync("SELECT * FROM users");
```

---

## Package Icon

Place your package icon here as `icon.png` (128x128 or 256x256 pixels recommended).

Current icon location: `../SharpCoreDB.jpg` (referenced in SharpCoreDB.csproj)

## README

A package README can be included by referencing `README.md` from the root.

## Building Packages

To build NuGet packages for v1.1.1:

```bash
# Build all packages (v1.1.1)
dotnet pack --configuration Release --output ./artifacts

# Build specific package
dotnet pack src/SharpCoreDB/SharpCoreDB.csproj --configuration Release --output ./artifacts

# Build with explicit version
dotnet pack -p:Version=1.1.1 --configuration Release --output ./artifacts
```

## Publishing to NuGet

```bash
# Publish v1.1.1 (requires API key)
dotnet nuget push artifacts/SharpCoreDB.1.1.1.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json

# Verify publication
# Visit: https://www.nuget.org/packages/SharpCoreDB/1.1.1
```

## Package Configuration

Package metadata is configured in `src/SharpCoreDB/SharpCoreDB.csproj`:
- **Package ID**: SharpCoreDB
- **Version**: 1.1.1
- **Authors**: MPCoreDeveloper
- **Company**: SharpCoreDB
- **Description**: Lightweight, encrypted, file-based database engine with SQL support, AES-256-GCM encryption, and production-ready performance
- **License**: MIT
- **Icon**: SharpCoreDB.jpg
- **Tags**: database, embedded, encryption, sql, nosql, net10, csharp14, performance
- **Repository**: https://github.com/MPCoreDeveloper/SharpCoreDB

## Multi-Platform Support

The package includes runtime-specific assemblies for:
- Windows x64 (win-x64)
- Windows ARM64 (win-arm64)
- Linux x64 (linux-x64)
- Linux ARM64 (linux-arm64)
- macOS x64 (osx-x64)
- macOS ARM64 (osx-arm64)

## Packages Produced

1. **SharpCoreDB** (v1.1.1) - Core library
   - Main NuGet package
   - All storage engines included
   - Multi-platform support
   - Symbol package (.snupkg) for debugging

2. **Future Packages** (Planned)
   - SharpCoreDB.Extensions - Extension methods
   - SharpCoreDB.Data.Provider - ADO.NET provider
   - SharpCoreDB.EntityFrameworkCore - EF Core provider
   - SharpCoreDB.Serilog.Sinks - Serilog sink

## Release Checklist

Before publishing v1.1.1:

- [x] Version updated in SharpCoreDB.csproj (1.1.1)
- [x] CHANGELOG.md updated with release notes
- [x] README.md updated with version badge
- [x] All tests passing (772/772)
- [x] Build succeeds with 0 errors
- [x] Package builds successfully
- [ ] Package published to NuGet.org
- [ ] GitHub release created with tag v1.1.1
- [ ] Release notes published on GitHub
