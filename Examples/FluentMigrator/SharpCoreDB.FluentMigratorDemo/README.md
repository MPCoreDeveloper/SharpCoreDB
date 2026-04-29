# SharpCoreDB + FluentMigrator Demo

This example demonstrates using [FluentMigrator](https://fluentmigrator.github.io/) with a **SharpCoreDB embedded single-file database** (`.scdb`).

## What it shows

- Registering `SharpCoreDB` and `FluentMigrator` side-by-side via DI
- Running schema migrations against a `.scdb` file (no server required)
- Handling of **quoted identifiers** — FluentMigrator generates SQL like  
  `CREATE TABLE IF NOT EXISTS "Products" ("Id" INTEGER NOT NULL, ...)` which SharpCoreDB parses correctly as of v1.8.0
- Rollback and re-apply of migrations
- Querying migrated + seeded data

## Migrations

| Version | Class | Description |
|---------|-------|-------------|
| 1 | `M001_InitialSchema` | Creates `Categories` and `Products` tables |
| 2 | `M002_SeedData` | Inserts initial rows |

## Running

```bash
dotnet run
```

Or from the solution root:

```bash
dotnet run --project Examples/FluentMigrator/SharpCoreDB.FluentMigratorDemo
```

## DI Setup

```csharp
services.AddSingleton(database);           // IDatabase — embedded single-file
services.AddSharpCoreDB();

services.AddSharpCoreDBFluentMigrator(runner =>
    runner.AddSQLite()                     // registers the SQLite SQL generator (required)
          .ScanIn(typeof(MyMigration).Assembly).For.Migrations());
```

Then resolve and run:

```csharp
var runner = serviceProvider.GetRequiredService<ISharpCoreDbMigrationRunner>();
runner.MigrateUp();
```

## Notes

- The `__SharpMigrations` version table is created automatically by FluentMigrator via the processor — no manual pre-creation needed.
- `StorageMode` is `SingleFile` — no directory, no separate page files.
- Encryption is disabled in this demo for simplicity; enable via `DatabaseOptions.CreateSingleFileDefault(enableEncryption: true, encryptionKey: "...")`.
