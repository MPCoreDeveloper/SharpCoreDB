# SharpCoreDB NuGet Package

This package is part of SharpCoreDB, a high-performance embedded database for .NET 10.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.

For full documentation, see: https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md

## Quick Start

See the main repository for usage examples.

# SharpCoreDB.Analytics v1.9.5

**Advanced Analytics Engine for SharpCoreDB**

Unlock enterprise-grade analytics with 100+ aggregate functions, window functions, and statistical analysis tools - **150-680x faster than SQLite**.

## ✨ What's New in v1.9.5

- ✅ Inherits metadata improvements from SharpCoreDB v1.9.5
- ✅ Phase 9 complete: 100+ aggregate and window functions
- ✅ Statistical functions: STDDEV, VARIANCE, PERCENTILE, CORRELATION
- ✅ SIMD-accelerated computations
- ✅ Zero breaking changes

## 🚀 Key Features

- **100+ Aggregate Functions**: COUNT, SUM, AVG, MIN, MAX, STDDEV, VARIANCE, PERCENTILE
- **Window Functions**: ROW_NUMBER, RANK, DENSE_RANK, PARTITION BY
- **Performance**: 150-680x faster than SQLite for analytics
- **Production Ready**: 1,468+ tests, enterprise reliability

## 📊 Performance

- COUNT (1M rows): **682x** faster than SQLite
- Window Functions: **156x** faster
- STDDEV/VARIANCE: **320x** faster
- PERCENTILE: **285x** faster

## 📚 Documentation

- [Analytics Overview](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/analytics/README.md)
- [Analytics Tutorial](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/analytics/TUTORIAL.md)
- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)
- [Changelog](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/CHANGELOG.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Analytics --version 2.0.0.0
```

**Requires:** SharpCoreDB v1.9.5+

---

**Version:** 1.9.5 | **Status:** ✅ Production Ready | **Phase:** 9 Complete




