# SharpCoreDB.Analytics

Advanced analytics extension for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Analytics`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- Window functions (`ROW_NUMBER`, `RANK`, `DENSE_RANK`, `LAG`, `LEAD`)
- Statistical and bivariate analysis helpers
- Time-series and OLAP-oriented helpers
- SIMD-friendly execution for high-throughput analytics workloads

## Changes in v1.9.5

- Package version synchronized to `v1.9.5`
- Analytics docs aligned with production feature set
- Inherits core durability/parser improvements from SharpCoreDB v1.9.5
- No intended breaking changes from v1.5.0

## Installation

```bash
dotnet add package SharpCoreDB.Analytics --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `docs/analytics/README.md`



