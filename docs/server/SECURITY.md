# SharpCoreDB Server — Security Guide

**Version:** 1.7.0  
**Policy:** HTTPS/TLS required. No plain HTTP endpoints.

---

## Table of Contents

1. [Security Architecture](#security-architecture)
2. [TLS / HTTPS](#tls--https)
3. [JWT Authentication](#jwt-authentication)
4. [Mutual TLS (Certificate Authentication)](#mutual-tls-certificate-authentication)
5. [Role-Based Access Control (RBAC)](#role-based-access-control-rbac)
6. [Rate Limiting](#rate-limiting)
7. [Systemd Hardening (Linux)](#systemd-hardening-linux)
8. [Docker Hardening](#docker-hardening)
9. [Production Checklist](#production-checklist)

---

## Security Architecture

```
Client Request
       │
       ▼
┌──────────────────────────────────────┐
│  TLS Termination (Kestrel)           │  ← TLS 1.2+ enforced
│  Minimum: TLS 1.2 | Preferred: 1.3  │
│  Optional: Mutual TLS (mTLS)         │
└──────────────┬───────────────────────┘
               │
┌──────────────┴───────────────────────┐
│  Authentication Layer                │
│  ├─ JWT Bearer (primary)             │  ← HMAC-SHA256 signed tokens
│  └─ Client Certificate (fallback)    │  ← mTLS thumbprint mapping
└──────────────┬───────────────────────┘
               │
┌──────────────┴───────────────────────┐
│  RBAC Authorization                  │  ← Admin / Writer / Reader roles
│  gRPC: Method-level permissions      │
│  REST: [Authorize(Roles)] attributes │
└──────────────┬───────────────────────┘
               │
┌──────────────┴───────────────────────┐
│  Rate Limiting (per-IP)              │  ← Fixed-window, configurable
│  Default: 500 req / 10s per IP       │
└──────────────┬───────────────────────┘
               │
               ▼
         Database Engine
```

---

## TLS / HTTPS

### Policy

SharpCoreDB Server **requires** TLS. There are no plain HTTP endpoints. The server will refuse to start if TLS is not configured.

### Configuration

```json
{
  "Server": {
    "Security": {
      "TlsEnabled": true,
      "TlsCertificatePath": "./certs/server.pfx",
      "TlsPrivateKeyPath": null,
      "MinimumTlsVersion": "Tls12"
    }
  }
}
```

| Setting | Values | Default | Description |
|---------|--------|---------|-------------|
| `TlsEnabled` | `true` | `true` | Must be true (server fails on `false`) |
| `TlsCertificatePath` | File path | Required | PFX or PEM certificate |
| `TlsPrivateKeyPath` | File path | `null` | Separate key file (PEM only) |
| `MinimumTlsVersion` | `Tls12`, `Tls13` | `Tls12` | Minimum TLS protocol version |

### Supported TLS Versions

| Version | Support | Notes |
|---------|---------|-------|
| TLS 1.3 | ✅ Preferred | Best security, zero round-trip handshake |
| TLS 1.2 | ✅ Supported | Minimum allowed version |
| TLS 1.1 | ❌ Rejected | Deprecated, insecure |
| TLS 1.0 | ❌ Rejected | Deprecated, insecure |
| SSL 3.0 | ❌ Rejected | Deprecated, insecure |

### Certificate Types

```bash
# PFX bundle (certificate + private key in one file)
"TlsCertificatePath": "./certs/server.pfx"

# PEM separate files
"TlsCertificatePath": "./certs/server.crt"
"TlsPrivateKeyPath": "./certs/server.key"
```

### Certificate Rotation

Replace the certificate file and restart the server:

```bash
# Linux
sudo systemctl restart sharpcoredb

# Docker
docker compose restart sharpcoredb

# Windows
Restart-Service SharpCoreDB
```

---

## JWT Authentication

### How It Works

1. Client calls `Connect` (gRPC) or authenticates via external flow
2. Server returns a signed JWT token
3. Client sends `Authorization: Bearer <token>` on all subsequent requests
4. Server validates signature, expiration, and claims

### Configuration

```json
{
  "Server": {
    "Security": {
      "JwtSecretKey": "your-secret-key-minimum-32-characters!!",
      "JwtExpirationHours": 24
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `JwtSecretKey` | Required | HMAC-SHA256 signing key (≥32 chars) |
| `JwtExpirationHours` | `24` | Token lifetime in hours |

### Token Structure

```json
{
  "header": {
    "alg": "HS256",
    "typ": "JWT"
  },
  "payload": {
    "nameid": "admin",
    "session": "abc123-...",
    "role": "admin,reader",
    "nbf": 1720000000,
    "exp": 1720086400,
    "iat": 1720000000
  }
}
```

### Public Endpoints (No Auth Required)

| Endpoint | Protocol | Description |
|----------|----------|-------------|
| `/health` | HTTP | ASP.NET Core health check |
| `/api/v1/health` | HTTP | Server health |
| `/api/v1/health/detailed` | HTTP | Detailed diagnostics |
| `HealthCheck` | gRPC | Server health |
| `Connect` | gRPC | Session creation |

### Security Recommendations

- **Rotate** `JwtSecretKey` periodically (at least every 90 days)
- **Use** 256-bit (32+ character) random keys
- **Never** commit secrets to version control
- **Use** environment variables for production secrets:

```bash
export Server__Security__JwtSecretKey="$(openssl rand -base64 32)"
```

---

## Mutual TLS (Certificate Authentication)

Mutual TLS (mTLS) allows clients to authenticate using X.509 client certificates instead of (or in addition to) JWT tokens. This is ideal for service-to-service communication where certificates are managed by infrastructure.

### How It Works

1. Server presents its TLS certificate (standard HTTPS)
2. Server requests a client certificate during TLS handshake
3. Client presents its certificate signed by a trusted CA
4. Server validates the certificate chain and maps the thumbprint to a role
5. Client is authenticated without needing a JWT token

### Configuration

```json
{
  "Server": {
    "Security": {
      "EnableMutualTls": true,
      "ClientCaCertificatePath": "./certs/client-ca.crt",
      "CertificateRoleMappings": [
        {
          "Thumbprint": "A1B2C3D4E5F6...",
          "Role": "admin",
          "Description": "CI/CD pipeline certificate"
        },
        {
          "Thumbprint": "F6E5D4C3B2A1...",
          "Role": "writer",
          "Description": "Application service certificate"
        }
      ]
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableMutualTls` | `false` | Enable client certificate authentication |
| `ClientCaCertificatePath` | `null` | CA certificate to validate client certs |
| `CertificateRoleMappings` | `[]` | Thumbprint → role mappings |

### Authentication Priority

When both JWT and mTLS are available, the server uses this priority:

1. **JWT Bearer token** (if `Authorization: Bearer <token>` header is present)
2. **Client certificate** (if presented during TLS handshake)
3. **Reject** (if neither is available, except for public endpoints)

### Generate Client Certificates

```bash
# 1. Create a CA (one time)
openssl req -x509 -newkey rsa:4096 -keyout ca.key -out ca.crt \
    -days 3650 -nodes -subj "/CN=SharpCoreDB Client CA"

# 2. Create a client key and CSR
openssl req -newkey rsa:2048 -keyout client.key -out client.csr \
    -nodes -subj "/CN=my-service"

# 3. Sign with CA
openssl x509 -req -in client.csr -CA ca.crt -CAkey ca.key \
    -CAcreateserial -out client.crt -days 365

# 4. Get thumbprint for role mapping
openssl x509 -in client.crt -fingerprint -noout | tr -d ':'

# 5. Create PFX for .NET clients
openssl pkcs12 -export -out client.pfx -inkey client.key -in client.crt
```

### Using mTLS with gRPC Clients

```csharp
var handler = new HttpClientHandler();
handler.ClientCertificates.Add(new X509Certificate2("client.pfx", "password"));

var channel = GrpcChannel.ForAddress("https://server:5001", new GrpcChannelOptions
{
    HttpHandler = handler,
});
```

---

## Role-Based Access Control (RBAC)

SharpCoreDB enforces three roles with hierarchical permissions:

| Role | Permissions |
|------|-------------|
| **Reader** | `Read`, `SchemaRead`, `VectorSearch`, `ViewMetrics` |
| **Writer** | All Reader + `Write`, `Transaction`, `Batch` |
| **Admin** | All Writer + `SchemaModify`, `CreateDatabase`, `ManageUsers` |

### gRPC Method Permissions

| Method | Required Permission |
|--------|--------------------|
| `Connect`, `HealthCheck` | Public (no auth) |
| `ExecuteQuery`, `Ping` | `Read` |
| `ExecuteNonQuery` | `Write` |
| `BeginTransaction`, `CommitTransaction`, `RollbackTransaction` | `Transaction` |
| `VectorSearch` | `VectorSearch` |

### REST Endpoint Permissions

| Endpoint | Required Role |
|----------|---------------|
| `GET /api/v1/health` | Public |
| `POST /api/v1/auth/login` | Public |
| `POST /api/v1/query` | Reader+ |
| `POST /api/v1/execute` | Writer+ |
| `POST /api/v1/batch` | Writer+ |
| `GET /api/v1/schema` | Reader+ |
| `GET /api/v1/metrics` | Reader+ |

---

## Rate Limiting

### Configuration

Rate limiting is enforced per client IP address using a fixed-window algorithm.

```json
{
  "Server": {
    "Performance": {
      "MaxConcurrentQueries": 500
    }
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentQueries` | `500` | Max requests per IP per 10-second window |
| Queue limit | `100` | Queued requests before 429 response |

### Rate Limit Response

When exceeded, clients receive:
- **HTTP:** `429 Too Many Requests`
- **gRPC:** `RESOURCE_EXHAUSTED` status code

---

## Systemd Hardening (Linux)

The provided `sharpcoredb.service` unit includes defense-in-depth:

```ini
# Filesystem isolation
ProtectSystem=strict          # Root filesystem read-only
ProtectHome=true              # No access to /home
PrivateTmp=true               # Isolated /tmp
ReadWritePaths=/opt/sharpcoredb/data /opt/sharpcoredb/logs

# Process isolation
NoNewPrivileges=true          # Cannot gain additional privileges
RestrictSUIDSGID=true         # No setuid/setgid binaries
ProtectKernelTunables=true    # No /proc or /sys writes
ProtectControlGroups=true     # No cgroup modifications

# Resource limits
LimitNOFILE=65536             # Max open file descriptors
LimitNPROC=4096               # Max processes
MemoryMax=4G                  # Memory ceiling
```

### Additional Hardening

Add to the `[Service]` section:

```ini
# Network namespace (if only localhost access is needed)
PrivateNetwork=true

# Restrict system calls
SystemCallFilter=@system-service
SystemCallArchitectures=native

# No kernel module loading
ProtectKernelModules=true
```

---

## Docker Hardening

### Built-in Security

The Dockerfile includes:
- **Non-root user** (`sharpcoredb:1000`)
- **Read-only certificate mount** (`./certs:/app/certs:ro`)
- **Health check** (auto-restart on failure)
- **Resource limits** via `docker-compose.yml`

### Additional Docker Hardening

```yaml
services:
  sharpcoredb:
    # Read-only root filesystem
    read_only: true
    tmpfs:
      - /tmp:size=100M

    # Drop all capabilities
    cap_drop:
      - ALL

    # No privilege escalation
    security_opt:
      - no-new-privileges:true

    # Network isolation
    networks:
      - sharpcoredb-internal
```

---

## Production Checklist

### Before Deployment

- [ ] **TLS certificate** from a trusted CA (not self-signed)
- [ ] **JWT secret** is 32+ random characters (not hardcoded)
- [ ] **Secret management** via environment variables or vault (not in config files)
- [ ] **Firewall** only allows ports 5001 (gRPC) and 8443 (HTTPS)
- [ ] **Non-root** user runs the server process
- [ ] **Log rotation** configured (Serilog: 30 files, 100MB each)
- [ ] **Rate limiting** tuned for expected load
- [ ] **mTLS** enabled for service-to-service communication (if applicable)
- [ ] **Certificate role mappings** reviewed and least-privilege

### Monitoring

- [ ] **Health check** `/api/v1/health` monitored by load balancer
- [ ] **Detailed health** `/api/v1/health/detailed` for GC/memory alerts
- [ ] **Metrics** `/api/v1/metrics` scraped by monitoring system
- [ ] **Logs** forwarded to centralized logging (ELK/Grafana/Datadog)
- [ ] **Alerts** on: unhealthy status, high memory, connection pool exhaustion

### Periodic

- [ ] Rotate JWT signing key (every 90 days)
- [ ] Renew TLS certificate (before expiration)
- [ ] Renew client certificates and update role mappings
- [ ] Review access logs for anomalies
- [ ] Update .NET runtime and NuGet packages
- [ ] Run integration tests before each deployment

---

**See Also:** [Installation Guide](INSTALLATION.md) · [Quick Start](QUICKSTART.md) · [Configuration](CONFIGURATION_SCHEMA.md)
