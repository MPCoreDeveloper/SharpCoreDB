# FluentMigrator with SharpCoreDB — Embedded Mode Guide (v1.7.0)

**Scope:** `SharpCoreDB.Extensions` FluentMigrator integration in **embedded/local** execution mode  
**Version label:** **v1.7.0 (V 1.70)**

---

## 0. Short Answer for the Most Common Question

**Question:** Does FluentMigrator only work with a remote SharpCoreDB server because the built-in `ISharpCoreDbMigrationSqlExecutor` implementation is the gRPC one?

**Answer:** **No. Embedded mode is fully supported.**

The gRPC executor is only the built-in implementation of the **optional custom executor extension point**. It is used for **remote** execution scenarios.

For **embedded/in-process** execution, FluentMigrator does **not** require any `ISharpCoreDbMigrationSqlExecutor` registration. The migration pipeline runs directly against:

1. `IDatabase` (preferred embedded path)
2. `DbConnection` (fallback path)

If your application registers SharpCoreDB locally and calls `AddSharpCoreDBFluentMigrator(...)`, migrations execute locally in-process without gRPC.

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

### What must be registered for embedded execution

`AddSharpCoreDBFluentMigrator(...)` registers the FluentMigrator processor integration, but it does **not** force a remote client path.

Embedded execution works when the DI container can resolve one of these local execution sources:

- `IDatabase`
- `DbConnection`

That means this is valid embedded usage:

```csharp
using FluentMigrator.Runner;
using SharpCoreDB.Extensions.Extensions;
using SharpCoreDB.Interfaces;

builder.Services.AddSingleton<IDatabase>(database);
builder.Services.AddSharpCoreDBFluentMigrator(runner =>
{
    runner.ScanIn(typeof(Program).Assembly).For.Migrations();
});
```

A custom `ISharpCoreDbMigrationSqlExecutor` is optional and should only be added when you intentionally want to override SQL execution behavior.

### Why the API can look misleading at first

Users often inspect the package and notice that the only built-in `ISharpCoreDbMigrationSqlExecutor` implementation is the gRPC one.

That does **not** mean FluentMigrator is remote-only.

It means:

- the custom executor interface is an extension point
- the current built-in custom executor implementation targets remote gRPC execution
- embedded execution uses `IDatabase` or `DbConnection` directly instead of going through that custom interface

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

`SharpCoreDbMigrationExecutor` resolves the execution target in this order:

1. `ISharpCoreDbMigrationSqlExecutor` (custom override, if registered)
2. `IDatabase` (normal embedded engine path)
3. `DbConnection` fallback

This design allows one integration pipeline to support both local and remote execution models.

### What happens when no custom executor is registered

If you only use `AddSharpCoreDBFluentMigrator(...)` and register an embedded SharpCoreDB `IDatabase`, the executor uses the `IDatabase` path directly.

No gRPC client is created.
No network hop is required.
No remote server dependency is introduced.

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
- For embedded scenarios, prefer registering `IDatabase`.

### B) "I do not see an embedded migration SQL executor implementation"

Cause:
- You are looking at the custom executor extension point and assuming every execution mode must implement it.

Fix:
- Use `AddSharpCoreDBFluentMigrator(...)` for embedded mode.
- Ensure `IDatabase` or `DbConnection` is registered.
- Only use `AddSharpCoreDBFluentMigratorGrpc(...)` when you explicitly want remote execution over gRPC.

### C) Migration expression not supported

Cause:
- Expression translates to SQL not supported by the current engine behavior.

Fix:
- Rewrite migration to equivalent supported SQL operations.
- For advanced custom behavior, use explicit SQL in migration steps.

### D) Version table missing

Cause:
- Processor not initialized in the migration pipeline.

Fix:
- Ensure runner is created from DI and migrations are actually invoked.

---

## 8. Production Checklist (Embedded)

- [ ] `AddSharpCoreDBFluentMigrator(...)` is registered.
- [ ] Embedded `IDatabase` or `DbConnection` is registered in DI.
- [ ] No remote gRPC executor is registered unless intentionally overriding execution behavior.
- [ ] Migrations are scanned from correct assembly.
- [ ] Startup path executes `MigrateUp()` exactly once.
- [ ] Backup/restore strategy exists before schema upgrades.
- [ ] Rollback policy is documented for failed deployments.
- [ ] `__SharpMigrations` is monitored in diagnostics.

---

## 9. Related Docs

- `docs/migration/FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md`
- `docs/migration/MIGRATION_GUIDE.md`
- `src/SharpCoreDB.Extensions/README.md`
