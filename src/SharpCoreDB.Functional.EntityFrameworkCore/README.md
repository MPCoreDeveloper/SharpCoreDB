# SharpCoreDB.Functional.EntityFrameworkCore

Entity Framework Core adapter for `SharpCoreDB.Functional`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Functional.EntityFrameworkCore`


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
- Complements `SharpCoreDB.EntityFrameworkCore` provider usage

## Changes in v1.9.5

- Functional EF Core adapter introduced in `v1.9.5`
- Documentation aligned with modular functional package family
- Keeps dependencies optional and transitive through package references

## Installation

```bash
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Functional/README.md`



