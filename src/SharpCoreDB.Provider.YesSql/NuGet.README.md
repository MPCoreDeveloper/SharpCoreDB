# SharpCoreDB.Provider.YesSql v1.9.5

**YesSql Provider for SharpCoreDB**

YesSql ORM integration with SharpCoreDB's encryption and performance for document-oriented patterns.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- ✅ Inherits metadata improvements from SharpCoreDB v1.9.5
- ✅ YesSql provider integration
- ✅ Enterprise features support
- ✅ Zero breaking changes

## 🚀 Key Features

- **YesSql Integration**: Document-oriented ORM patterns
- **OrchardCore Compatible**: Works with OrchardCore CMS
- **Encryption**: Transparent AES-256-GCM encryption
- **Performance**: High-speed document queries
- **Indexing**: Custom document indexing strategies

## 📚 Documentation

- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Provider.YesSql --version 1.9.5
```

**Requires:** SharpCoreDB v1.9.5+, YesSql.Core v5.4.7+

---

**Version:** 1.9.5 | **Status:** ✅ Production Ready




