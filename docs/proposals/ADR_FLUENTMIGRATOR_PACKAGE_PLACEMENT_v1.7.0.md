# ADR — FluentMigrator Package Placement in `SharpCoreDB.Extensions` (v1.7.0)

**Status:** Accepted  
**Date:** 2026-03-23  
**Version label:** **v1.7.0 (V 1.70)**

---

## 1. Decision Statement

For **v1.7.0**, FluentMigrator integration is implemented in **`SharpCoreDB.Extensions`** (not a separate `SharpCoreDB.Provider.FluentMigrator` package).

---

## 2. Context

The project already has two established integration styles:

1. **Extension-style integrations** (Dapper, Health Checks):
   - packaged in `SharpCoreDB.Extensions`
   - cross-cutting glue, DI helpers, convenience APIs

2. **Provider-style integrations** (Dotmim.Sync):
   - dedicated package/project (`SharpCoreDB.Provider.Sync`)
   - larger domain-specific runtime with independent lifecycle

A design choice was required for FluentMigrator support.

---

## 3. Drivers

### Functional drivers

- Add first-class FluentMigrator support with DI-first setup.
- Support embedded/local execution and server/network execution.
- Maintain migration metadata table (`__SharpMigrations`).

### Packaging and product drivers

- Keep `SharpCoreDB` core clean.
- Preserve optional adoption of migration feature.
- Avoid unnecessary package proliferation where integration is glue-level.

### Non-functional drivers

- Minimize breaking changes.
- Keep onboarding simple.
- Keep long-term refactor path open if complexity grows.

---

## 4. Considered Options

### Option A — Keep FluentMigrator in `SharpCoreDB.Extensions` (chosen)

**Description:** implement processor + DI extensions in existing Extensions package.

**Pros:**
- Aligns with current extension/glue responsibility.
- Reuses existing integration discovery point for users.
- No additional package management overhead.
- Keeps migration onboarding straightforward.

**Cons:**
- `SharpCoreDB.Extensions` grows in scope.
- Requires discipline to keep feature bounded and optional.

### Option B — Create `SharpCoreDB.Provider.FluentMigrator`

**Description:** separate provider package/project for FluentMigrator.

**Pros:**
- Maximum package isolation.
- Independent release cadence.
- Strongly explicit feature boundary.

**Cons:**
- Extra package and project maintenance burden.
- More discoverability and setup friction for users.
- Not clearly justified at current complexity level.

---

## 5. Decision

We choose **Option A** for v1.7.0:

- FluentMigrator support remains in `SharpCoreDB.Extensions`.
- Feature remains **opt-in** through explicit DI registration.
- Core database package remains free from migration-specific implementation.

This is consistent with the repository split where:

- `Provider.Sync` is a dedicated package due to broad synchronization domain runtime.
- FluentMigrator support is currently integration glue with limited domain surface.

---

## 6. Why Dotmim.Sync Is Separate but FluentMigrator Is Not

`SharpCoreDB.Provider.Sync` is separate because it introduces:

- dedicated sync domain abstractions and provider contracts
- change-tracking model and lifecycle
- broader operational/runtime footprint
- stronger independent evolution pressure

FluentMigrator integration currently introduces:

- migration processor + DI registration glue
- version-table convention
- execution adapters (embedded/server/gRPC)

This footprint is currently closer to extension glue than to a full provider domain package.

---

## 7. Consequences

### Positive

- Faster adoption via existing `SharpCoreDB.Extensions` package.
- Consistent developer story with Dapper and Health Checks.
- No core pollution and no breaking package split in v1.7.0.

### Negative / Trade-offs

- Extensions package carries broader integration scope.
- Future growth may demand extraction to a dedicated package.

---

## 8. Guardrails and Exit Criteria

A split to dedicated package should be revisited when one or more conditions are met:

1. FluentMigrator integration requires substantial provider-specific sub-systems.
2. Release cadence diverges significantly from Extensions package.
3. Consumer feedback indicates package bloat/discoverability issues.
4. Additional migration ecosystems are added and need a provider layer strategy.

If triggered, migration path should include:

- compatibility shim in `SharpCoreDB.Extensions`
- non-breaking deprecation window
- clear migration docs and upgrade notes

---

## 9. Implementation Notes (v1.7.0)

- Main extension APIs:
  - `AddSharpCoreDBFluentMigrator(...)`
  - `AddSharpCoreDBFluentMigratorGrpc(...)`
- Version metadata table:
  - `__SharpMigrations`
- Server mode:
  - in-process execution path
  - remote gRPC execution path via `SharpCoreDB.Client`

---

## 10. Related Documents

- `docs/migration/FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md`
- `docs/migration/FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md`
- `docs/proposals/README.md`
- `src/SharpCoreDB.Extensions/README.md`
