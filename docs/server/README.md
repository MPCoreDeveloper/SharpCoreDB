# SharpCoreDB Server Documentation

SharpCoreDB Server turns SharpCoreDB into a **real network database server** for .NET 10.
It supports secure remote access, multiple hosted databases (including system databases), and production-ready operations.

## What Server Mode Means

In server mode, SharpCoreDB runs as a network service instead of an in-process embedded engine:

- **Primary protocol:** gRPC over HTTPS (HTTP/2 + HTTP/3)
- **Secondary protocols:** HTTPS REST API and WebSocket streaming
- **Security model:** TLS 1.2+, JWT, optional mTLS, RBAC
- **Operational model:** multi-database hosting, health checks, metrics, connection pooling

## Start Here

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
