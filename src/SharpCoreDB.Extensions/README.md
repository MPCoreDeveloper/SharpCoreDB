<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>

  # SharpCoreDB.Extensions

  **Dapper Integration · Health Checks · Repository Pattern · Bulk Operations · Performance Monitoring · FluentMigrator**

  **Version:** 1.8.0  
  **Status:** Production Ready ✅

  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![C#](https://img.shields.io/badge/C%23-14-blueviolet.svg)](https://learn.microsoft.com/dotnet/csharp/)
  [![NuGet](https://img.shields.io/badge/NuGet-1.8.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB.Extensions)

</div>

---

Convenience extensions package for `SharpCoreDB`.


## Patch updates in v1.8.0

- ✅ Aligned package metadata and version references to the synchronized 1.8.0 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Dapper integration helpers
- ASP.NET Core health check integration
- FluentMigrator integration hooks (optional)
- Repository/bulk utility helpers for common workflows
- Extension points for developer productivity in .NET applications

## FluentMigrator

`AddSharpCoreDBFluentMigrator()` uses FluentMigrator's SQLite generator by default and now also defaults the processor to SQLite syntax compatibility.

This means SQLite-specific migration restrictions are enforced automatically for the standard registration path, which matches the generated SQL dialect and avoids unsupported DDL slipping through at runtime.

```csharp
services.AddSharpCoreDB();
services.AddSingleton<DatabaseFactory>();
services.AddSingleton<IDatabase>();
services.AddSharpCoreDBFluentMigrator();
```

### Default behavior

With the default registration:

- FluentMigrator generator id defaults to `sqlite`
- processor `ProviderSwitches` defaults to `syntax=sqlite`
- SQLite-incompatible operations such as `ALTER COLUMN`, `CREATE SEQUENCE`, and `ALTER TABLE ... ADD CONSTRAINT` are rejected with a clear `NotSupportedException`

### Override behavior

If you need a different syntax mode, configure `ProcessorOptions.ProviderSwitches` explicitly after registration.

```csharp
services.AddSharpCoreDBFluentMigrator();
services.Configure<ProcessorOptions>(options =>
{
    options.ProviderSwitches = "syntax=postgresql";
});
```

Explicit configuration is preserved and is not overwritten by the extension.

## Changes in v1.8.0

- Package/docs synchronized to `v1.8.0`
- Guidance updated for optional migration/health scenarios
- FluentMigrator now defaults to SQLite syntax compatibility when using `AddSharpCoreDBFluentMigrator()`
- Inherits core reliability improvements from SharpCoreDB v1.8.0

## Installation

```bash
dotnet add package SharpCoreDB.Extensions --version 1.8.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Extensions/NuGet.README.md`

