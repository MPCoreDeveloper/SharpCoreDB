# SharpCoreDB Roadmap

## 1) Read-only instances and schema changes

- Problem
  - A read-only database instance loads the metadata (tables/indexes) once during construction.
  - New tables created later by a read-write instance are invisible to already opened read-only instances.
  - Result: queries against newly created tables fail on those read-only instances until they are recreated.

- Current guidance (to document in guides/demos)
  - Open read-only instances after completing schema changes; or
  - Dispose and recreate read-only instances after CREATE/ALTER; or
  - Run reporting/read-only processes that restart on schema changes.

- Plan (non-breaking; v1.1.x)
  1. API: `IDatabase.ReloadMetadata()`
     - Forces a reload of `meta.json` and rebinds in-memory tables/indexes.
     - Allowed for read-only instances (no write locks needed).
  2. Opt-in auto-refresh for read-only
     - `DatabaseConfig` flag: `AutoReloadMetadataOnChange` (default: false).
     - File watcher on `meta.json` → debounce → `ReloadMetadata()`.
  3. Schema versioning
     - Extend meta with `SchemaVersion` (monotonic counter).
     - Validate at query start: mismatch → (optional) auto-reload when the flag is active.
  4. Events
     - Read-write: `OnSchemaChanged` after `CREATE/DROP/ALTER` (increments `SchemaVersion`).
  5. Connection pool integration
     - Invalidate/refresh read-only pooled instances on schema changes (when pooling is enabled).

- Testing
  - Unit: `ReloadMetadata` reflects a newly created table; existing queries keep working.
  - Integration: Read-write → CREATE TABLE; read-only with AutoReload flag sees the new table without recreation.

- Documentation
  - Add a section in `docs/guides/EXAMPLES.md`: timing of read-only instances, `ReloadMetadata()` usage, and the opt-in auto-reload flag.

- Expected impact
  - Backwards compatible; no breaking changes.
  - Minimal runtime overhead when auto-reload is disabled (default).

- Target versions
  - v1.1.0: API + docs
  - v1.1.1: opt-in auto-reload + tests

---

## 2) Users & permissions demo (completed)
- Demo shows:
  - Two users (`writer`, `reader`) and two connections (RW/RO).
  - RW performs schema and data mutations; RO performs selects and blocks writes.
  - Included in `SharpCoreDB.Demo` and referenced from docs.

---

## 3) Quality-of-life improvements
- More specific exception for write attempts on read-only connections (include hint to recreate or reload).
- `IDatabaseInfo` surface: `IsReadOnly`, `SchemaVersion`, `Tables` (for diagnostics/telemetry).

---

## Git compliance
- Follow Conventional Commits for changes related to this roadmap:
  - `feat(db): add ReloadMetadata API to refresh catalog on read-only instances`
  - `feat(config): add AutoReloadMetadataOnChange flag`
  - `docs(guide): document read-only behavior and metadata reload`
  - `test(db): add integration tests for metadata reload`
- Ensure updates include unit/integration tests and documentation changes in the same PR when feasible.

