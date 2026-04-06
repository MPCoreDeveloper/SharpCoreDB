# FluentMigrator with SharpCoreDB — Embedded Mode Guide (v1.6.0)

**Scope:** `SharpCoreDB.Extensions` FluentMigrator integration in **embedded/local** execution mode  
**Version label:** **v1.6.0 (V 1.60)**

---

## 1. What Embedded Mode Means

Embedded mode runs migrations directly against a local SharpCoreDB engine instance in the same process.

Execution path:

1. `IMigrationRunner` executes migration expressions.
2. `SharpCoreDbProcessor` translates/handles migration operations.
3. `SharpCoreDbMigrationExecutor` resolves execution target.
4. Embedded path uses `IDatabase` first (or `DbConnection` fallback).
5. SQL executes locally without network transport.

This mode is the default path when you call:

- `AddSharpCoreDBFluentMigrator(...)`

---

## 2. DI Registration (Embedded)

Use `AddSharpCoreDBFluentMigrator(...)` and scan migrations:

```csharp
using FluentMigrator.Runner;
using SharpCoreDB.Extensions.Extensions;

builder.Services.AddSharpCoreDBFluentMigrator(runner =>
{
    runner.ScanIn(typeof(Program).Assembly).For.Migrations();
});
```

### Recommended startup execution

```csharp
using FluentMigrator.Runner;

using var scope = app.Services.CreateScope();
var migrationRunner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
migrationRunner.MigrateUp();
```

### Automatic startup execution (`IHostedService`)

```csharp
public sealed class SharpCoreDbMigrationHostedService(IServiceProvider serviceProvider) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

---

## 3. Migration State and Version Table

SharpCoreDB FluentMigrator integration stores version metadata in:

- `__SharpMigrations`

Columns:

- `Version` (primary key)
- `AppliedOn`
- `Description`

The table is created automatically by `SharpCoreDbMigrationExecutor.EnsureVersionTable(...)` when processor creation occurs.

---

## 4. Supported Workflow in Embedded Mode

Typical commands:

- `runner.MigrateUp()`
- `runner.MigrateUp(targetVersion)`
- `runner.Rollback(steps)`

Typical migration pattern:

```csharp
[Migration(2026032301, "Create users table")]
public sealed class CreateUsersTableMigration : Migration
{
    public override void Up()
    {
        Create.Table("users")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("name").AsString(200).NotNullable()
            .WithColumn("created_utc").AsDateTime().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("users");
    }
}
```

---

## 5. Embedded Mode Behavior Details

### Resolution order in `SharpCoreDbMigrationExecutor`

1. `ISharpCoreDbMigrationSqlExecutor` (custom override, if registered)
2. `IDatabase` (embedded engine path)
3. `DbConnection` fallback

If you only use `AddSharpCoreDBFluentMigrator(...)` and embedded database DI, path (2) is used.

### Transaction model

FluentMigrator transaction lifecycle methods are present on the processor.
Behavior depends on SharpCoreDB SQL capabilities and executed statements.

---

## 6. Performance and Reliability Recommendations

1. Run migrations once at startup before serving traffic.
2. Keep migrations idempotent where practical.
3. Prefer additive schema changes for online upgrades.
4. Use explicit rollback logic in `Down()` for controlled reversions.
5. Keep migration scripts deterministic (no environment-dependent random behavior).

---

## 7. Troubleshooting (Embedded)

### A) "No SharpCoreDB execution source found"

Cause:
- `IDatabase` and `DbConnection` not registered, and no custom executor registered.

Fix:
- Register SharpCoreDB database services before `AddSharpCoreDBFluentMigrator(...)`.

### B) Migration expression not supported

Cause:
- Expression translates to SQL not supported by the current engine behavior.

Fix:
- Rewrite migration to equivalent supported SQL operations.
- For advanced custom behavior, use explicit SQL in migration steps.

### C) Version table missing

Cause:
- Processor not initialized in the migration pipeline.

Fix:
- Ensure runner is created from DI and migrations are actually invoked.

---

## 8. Production Checklist (Embedded)

- [ ] `AddSharpCoreDBFluentMigrator(...)` is registered.
- [ ] Migrations are scanned from correct assembly.
- [ ] Startup path executes `MigrateUp()` exactly once.
- [ ] Backup/restore strategy exists before schema upgrades.
- [ ] Rollback policy is documented for failed deployments.
- [ ] `__SharpMigrations` is monitored in diagnostics.

---

## 9. Related Docs

- `docs/migration/FLUENTMIGRATOR_SERVER_MODE_v1.6.0.md`
- `docs/migration/MIGRATION_GUIDE.md`
- `src/SharpCoreDB.Extensions/README.md`
