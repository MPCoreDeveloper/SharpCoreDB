# SharpCoreDB.Functional.Dapper

Dapper adapter for `SharpCoreDB.Functional`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Functional.Dapper`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- `Task<Option<T>>` for optional reads
- `Task<Fin<Unit>>` for write operations
- `Task<Seq<T>>` for sequence-based query results
- Entry points for `IDbConnection` and `IDatabase` integration

## Changes in v1.9.5

- Functional Dapper adapter introduced in `v1.9.5`
- Documentation aligned to optional modular architecture
- Keeps production dependencies flowing through transitive package references

## Installation

```bash
dotnet add package SharpCoreDB.Functional.Dapper --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Functional/README.md`



