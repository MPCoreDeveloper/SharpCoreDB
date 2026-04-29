# SharpCoreDB.Server v1.8.0

Network database server package for `SharpCoreDB`.


## Patch updates in v1.8.0

- ✅ Aligned package metadata and version references to the synchronized 1.8.0 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- gRPC-first protocol stack (HTTP/2 + HTTP/3)
- HTTPS REST API and WebSocket streaming support
- JWT, RBAC, TLS 1.2+, and optional mTLS
- Multi-database hosting and production operations hooks
- Health checks, metrics, and deployment options (Docker/services)

## Changes in v1.8.0

- Package/docs synchronized to `v1.8.0`
- Server documentation updated to current production feature set
- Client/SDK references aligned with current ecosystem packages

## Installation

```bash
dotnet add package SharpCoreDB.Server --version 1.8.0
```

## Documentation

- `docs/INDEX.md`
- `docs/server/README.md`
- `docs/server/QUICKSTART.md`

