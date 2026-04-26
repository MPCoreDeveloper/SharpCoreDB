# SharpCoreDB Server Documentation

SharpCoreDB Server turns SharpCoreDB into a **real network database server** for .NET 10.

**Version line:** `v1.7.1`

## v1.7.1 highlights

- gRPC-first server runtime over HTTPS (HTTP/2 + HTTP/3)
- Multi-database hosting with system-database security model
- JWT/RBAC baseline with optional mTLS hardening
- Production operations guidance for health, metrics, and deployment

## What Server Mode Means

In server mode, SharpCoreDB runs as a network service instead of an in-process embedded engine:

- **Primary protocol:** gRPC over HTTPS (HTTP/2 + HTTP/3)
- **Secondary protocols:** HTTPS REST API and WebSocket streaming
- **Security model:** TLS 1.2+, JWT, optional mTLS, RBAC
- **Operational model:** multi-database hosting, health checks, metrics, connection pooling

## Start Here

- `../FEATURE_MATRIX_v1.7.1.md` — package feature coverage
- `QUICKSTART.md` — first server startup in minutes
- `INSTALLATION.md` — platform-specific installation and service setup
- `CONFIGURATION_SCHEMA.md` — full server configuration reference

## Installers and Deployment

- **Windows Service:** `installers/windows/install-service.ps1`
- **Linux systemd:** `installers/linux/install.sh`
- **macOS launchd:** `installers/macos/install.sh`
- **Docker:** `src/SharpCoreDB.Server/docker-compose.yml`

For complete production installation instructions, see `INSTALLATION.md`.

## API and Protocols

- `REST_API.md`
- `BINARY_PROTOCOL_SPEC.md`
- `CLIENT_GUIDE.md`

## Security and Operations

- `SECURITY.md`
- `SYSTEM_DATABASES_SECURITY.md`

## Eventing and Stream Model

- `EVENT_SOURCING_RFC.md`
- `EVENT_STREAM_MODEL_FINAL.md`

## Maintenance Note

Superseded phase plans, kickoff notes, and completion status reports were removed to keep server documentation focused on current operational guidance.

