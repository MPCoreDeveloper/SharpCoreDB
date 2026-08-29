# SharpCoreDB.Provider.YesSql

YesSql provider integration for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Provider.YesSql`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- SharpCoreDB-backed storage for document-oriented patterns
- Works with OrchardCore/YesSql style usage
- Uses SharpCoreDB encryption and performance characteristics
- .NET 10 compatible provider components

## Changes in v1.9.5

- Package/docs standardized to `v1.9.5`
- Documentation refreshed around provider role and usage
- Inherits SharpCoreDB core reliability/parser improvements
- No intended breaking changes from v1.5.0

## Installation

```bash
dotnet add package SharpCoreDB.Provider.YesSql --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- Root README: `README.md`



