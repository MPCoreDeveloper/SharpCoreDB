# PySharpDB

Python client SDK for SharpCoreDB Server.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Client version synchronized to 1.9.5.

## Features

- Sync and async client APIs
- gRPC/HTTP/WebSocket transport targeting
- Connection and pooling abstractions
- TLS-aware connectivity patterns

## Changes in v1.9.5

- Documentation aligned to SharpCoreDB `v1.9.5`
- Current state clarified: Python transport parity is still in progress
- Examples and endpoint guidance refreshed

## Installation

```bash
pip install pysharpcoredb
```

## Docs

- `docs/INDEX.md`
- `docs/server/CLIENT_GUIDE.md`



