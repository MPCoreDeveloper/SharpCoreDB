# FluentMigrator with SharpCoreDB — Server Mode Guide (v1.6.0)

**Scope:** `SharpCoreDB.Extensions` FluentMigrator integration in **server/network** scenarios  
**Version label:** **v1.6.0 (V 1.60)**

---

## Decision Summary (Package Placement)

FluentMigrator support is intentionally delivered through `SharpCoreDB.Extensions` for v1.6.0 because it is currently an integration/glue feature (DI registration + migration processor + execution adapters), not a standalone provider domain.

Dotmim.Sync remains a separate package (`SharpCoreDB.Provider.Sync`) because it has a broader provider/runtime scope and independent lifecycle.

For the full architecture decision, see:

- `docs/proposals/ADR_FLUENTMIGRATOR_PACKAGE_PLACEMENT_v1.6.0.md`

---

## 1. What Server Mode Means

In this context, server mode covers two distinct patterns:

1. **In-process server-host execution**  
   Migrations run in the server process with direct access to engine services (`IDatabase` / `DbConnection`).

2. **Remote gRPC execution**  
   Migrations run from an external app and execute SQL over `SharpCoreDB.Client` to a SharpCoreDB server.

Both are supported, but they differ in capabilities and operational behavior.

---

## 2. Mode A — In-Process Server Host

### When to use

- You own the server process startup.
- You want migration execution before accepting traffic.
- You want local service-level control and diagnostics.

### Registration model

Use the standard registration:

```csharp
builder.Services.AddSharpCoreDBFluentMigrator(runner =>
{
    runner.ScanIn(typeof(Program).Assembly).For.Migrations();
});
```

This path resolves execution via:

- custom executor (if registered)
- then `IDatabase`
- then `DbConnection`

---

## 3. Mode B — Remote gRPC Server Execution

### When to use

- Schema changes are orchestrated from a deploy/ops tool.
- Migration runner is separate from server host process.
- You want network-based centralized migration jobs.

### Registration model

Use gRPC-specific registration:

```csharp
using FluentMigrator.Runner;
using SharpCoreDB.Extensions.Extensions;

builder.Services.AddSharpCoreDBFluentMigratorGrpc(
    "Server=localhost;Port=5001;Database=master;SSL=true;Username=admin;Password=secret",
    runner => runner.ScanIn(typeof(Program).Assembly).For.Migrations());
```

This wires:

- `ISharpCoreDbMigrationSqlExecutor` → `SharpCoreDbGrpcMigrationSqlExecutor`
- SQL execution through `SharpCoreDB.Client`

### Behavior notes

- `MigrateUp` / target version / rollback are available.
- SQL is sent to server using authenticated gRPC connection string settings.
- `PerformDBOperationExpression` does **not** receive an `IDbConnection` in remote mode.

---

## 4. Security Requirements and Recommendations

1. Use TLS-enabled server endpoints (`SSL=true`, TLS 1.2+).
2. Use least-privilege migration credentials.
3. Restrict migration account scope per database/environment.
4. Run migrations from trusted deployment agents only.
5. Audit migration runs and version-table updates.

---

## 5. Operational Patterns

### A) Startup migration in server host

- Preferred for single-service ownership deployments.
- Run before endpoint binding/traffic readiness.

### B) External migration job (remote)

- Preferred for multi-service orchestration.
- Run as pre-deploy or deploy-gate stage.
- Fail fast if migration job fails; block app rollout.

### C) Rollback strategy

- Keep rollback scripts explicit in `Down()`.
- For irreversible changes, document manual rollback plan.

---

## 6. Compatibility Matrix

| Capability | In-Process Server Host | Remote gRPC |
|-----------|------------------------|-------------|
| `MigrateUp()` | ✅ | ✅ |
| `MigrateUp(version)` | ✅ | ✅ |
| `Rollback(steps)` | ✅ | ✅ |
| `PerformDBOperationExpression` with connection object | ✅ (if connection available) | ⚠️ Limited (`null` connection) |
| Uses local `IDatabase` | ✅ | ❌ |
| Uses `SharpCoreDB.Client` network transport | ❌ | ✅ |

---

## 7. Troubleshooting (Server Mode)

### A) Authentication/session failures in remote mode

Symptoms:
- gRPC errors on command execution
- migration runner aborts early

Fix:
- validate connection string credentials
- verify target database exists
- verify TLS/connectivity settings

### B) Migration works in embedded but fails in remote

Cause:
- remote mode has no direct `IDbConnection` callback object for DB-operation expressions.

Fix:
- replace DB-operation callback with explicit migration SQL steps
- avoid connection-dependent callback logic in remote pipelines

### C) Connection timeouts

Fix:
- increase `SharpCoreDbGrpcMigrationOptions.CommandTimeoutMs`
- split large schema/data migrations into smaller incremental steps

---

## 8. Example: External Migration Job (Remote gRPC)

```csharp
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Extensions.Extensions;

var services = new ServiceCollection();

services.AddSharpCoreDBFluentMigratorGrpc(
    "Server=db.example.internal;Port=5001;Database=master;SSL=true;Username=migrator;Password=***",
    runner => runner.ScanIn(typeof(Program).Assembly).For.Migrations(),
    configureGrpc: options => options = options with { CommandTimeoutMs = 120000 });

using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var migrationRunner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
migrationRunner.MigrateUp();
```

---

## 9. Production Checklist (Server Mode)

- [ ] Correct mode selected: in-process vs remote gRPC.
- [ ] Migration credentials scoped and secret-managed.
- [ ] TLS enforced in all non-local environments.
- [ ] Migration step integrated in deployment pipeline.
- [ ] Failure policy blocks rollout on migration errors.
- [ ] `__SharpMigrations` monitored and audited.

---

## 10. Related Docs

- `docs/migration/FLUENTMIGRATOR_EMBEDDED_MODE_v1.6.0.md`
- `docs/server/CLIENT_GUIDE.md`
- `docs/migration/MIGRATION_GUIDE.md`
- `src/SharpCoreDB.Extensions/README.md`
