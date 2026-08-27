# @sharpcoredb/client

TypeScript/JavaScript client SDK for SharpCoreDB Server.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Client version synchronized to 1.9.5.

## Features

- gRPC-first connection support with protocol fallbacks
- Promise-based async API for query and command execution
- Connection pooling and runtime metrics helpers
- TypeScript definitions for typed integration

## Changes in v1.9.5

- SDK documentation aligned to the SharpCoreDB `v1.9.5` server line
- Protocol and connection guidance refreshed
- Example references updated for current server endpoints

## Installation

```bash
npm install @sharpcoredb/client
```

## Docs

- `docs/INDEX.md`
- `docs/server/CLIENT_GUIDE.md`



