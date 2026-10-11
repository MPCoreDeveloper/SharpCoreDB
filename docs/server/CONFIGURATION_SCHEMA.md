# SharpCoreDB Server Configuration Schema (2.1.0)

**Format:** JSON, through ASP.NET Core configuration (`appsettings.json` by default)
**Root section:** `Server`
**Bound type:** `ServerConfiguration` — `src/SharpCoreDB.Server.Core/Configuration/ServerConfiguration.cs`
**Environment variables:** nested with a double underscore, e.g. `Server__Security__JwtSecretKey`

> **There is no TOML and no YAML support.** The server registers JSON configuration sources only
> (`Microsoft.Extensions.Configuration.Json`); no TOML or YAML parser is referenced by the server or any
> project it compiles. A `sharpcoredb.toml` or `sharpcoredb.yaml` file is **not read**, and underscorized
> keys (`server_name`, `tls_enabled`, …) never bind to anything. Earlier revisions of this page showed
> TOML samples built on those keys — they described a format the server cannot consume. Use JSON.

Keys that are not properties of the bound type are ignored by the binder, so a typo fails silently: if a
setting appears to have no effect, check its spelling against the tables below first.

---

## 1. How configuration is loaded

`Program.cs` builds the host with `WebApplication.CreateBuilder(args)`, which registers the standard
providers, and adds one extra JSON file when `--appsettings` is supplied:

| Order | Source | Notes |
|-------|--------|-------|
| 1 | `appsettings.json` | content root; the shipped sample is `src/SharpCoreDB.Server/appsettings.json` |
| 2 | `appsettings.{Environment}.json` | e.g. `appsettings.Production.json` |
| 3 | `--appsettings <path>` / `--appsettings=<path>` | added with `optional: false` — a missing file **fails the process** instead of falling back silently |
| 4 | Environment variables | `Server__…` nested form |
| 5 | Command-line arguments | standard `--key=value` |

Later sources override earlier ones. The section is materialised with
`builder.Configuration.GetSection("Server").Get<ServerConfiguration>()`; a missing section throws
`InvalidOperationException("Server configuration is missing")`.

---

## 2. Complete example

This is the structure of the shipped `src/SharpCoreDB.Server/appsettings.json` with development values
reduced to one database:

```json
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning", "Grpc": "Information" }
  },
  "AllowedHosts": "*",
  "Server": {
    "ServerName": "SharpCoreDB Server (Development)",
    "BindAddress": "0.0.0.0",
    "GrpcPort": 5001,
    "EnableGrpc": true,
    "EnableHttpsApi": true,
    "HttpsApiPort": 8443,
    "EnableWebAdmin": false,
    "WebAdminPath": "/admin",
    "EnableWebSocket": true,
    "WebSocketPath": "/ws",
    "WebSocketMaxMessageSize": 4194304,
    "WebSocketKeepAliveSeconds": 30,
    "MaxConnections": 1000,
    "ConnectionTimeoutSeconds": 300,
    "DefaultDatabase": "master",
    "Databases": [
      {
        "Name": "appdb",
        "DatabasePath": "./data/user/appdb.scdb",
        "StorageMode": "SingleFile",
        "EncryptionEnabled": true,
        "EncryptionKeyFile": "./secrets/appdb.key",
        "ConnectionPoolSize": 50,
        "IsSystemDatabase": false,
        "IsReadOnly": false
      }
    ],
    "SystemDatabases": {
      "Enabled": true,
      "MasterDatabaseName": "master",
      "ModelDatabaseName": "model",
      "MsdbDatabaseName": "msdb",
      "TempDbDatabaseName": "tempdb"
    },
    "Security": {
      "TlsEnabled": true,
      "TlsCertificatePath": "./certs/server.pfx",
      "TlsPrivateKeyPath": null,
      "MinimumTlsVersion": "Tls12",
      "JwtSecretKey": "CHANGE-ME-in-production-must-be-at-least-32-characters-long",
      "JwtExpirationHours": 24,
      "EnableApiKeys": true,
      "Users": [
        { "Username": "admin", "PasswordHash": "<sha256-hex>", "Role": "admin" }
      ]
    },
    "Logging": {
      "Level": "Information",
      "FilePath": "./logs/sharpcoredb-server.log",
      "MaxFileSizeMb": 100,
      "MaxFiles": 10
    },
    "Performance": {
      "QueryCacheSizeMb": 256,
      "MaxConcurrentQueries": 500
    }
  }
}

---

## 3. `Server` section

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `ServerName` | string | **required** | Display name |
| `BindAddress` | string | `0.0.0.0` | Bind address |
| `GrpcPort` | int | `5001` | gRPC over TLS port |
| `EnableGrpc` | bool | `true` | Flagship protocol |
| `EnableGrpcHttp3` | bool | `true` | gRPC over HTTP/3 (QUIC) |
| `EnableBinaryProtocol` | bool | `true` | Secondary binary protocol |
| `BinaryProtocolPort` | int | `5433` | Binary-protocol port |
| `EnableHttpsApi` | bool | `true` | REST/JSON over HTTPS |
| `HttpsApiPort` | int | `8443` | REST/JSON port |
| `EnableWebSocket` | bool | `true` | WebSocket endpoint |
| `WebSocketPath` | string | `/ws` | WebSocket path |
| `WebSocketMaxMessageSize` | int | `4194304` (4 MB) | Max WebSocket frame |
| `WebSocketKeepAliveSeconds` | int | `30` | Keep-alive interval |
| `EnableWebAdmin` | bool | `false` | Admin UI |
| `WebAdminPath` | string | `/admin` | Admin UI path (longest-prefix match wins over the global route prefix) |
| `MaxConnections` | int | `10000` | Maximum concurrent connections |
| `ConnectionTimeoutSeconds` | int | `300` | Connection timeout |
| `DefaultDatabase` | string | **required** | Database used when a client does not name one |
| `Databases` | array | **required** | Per-database instances, see §3.1 |
| `SystemDatabases` | object | defaults | see §3.2 |
| `Security` | object | **required** | see §3.3 |
| `Logging` | object | defaults | see §3.4 |
| `Performance` | object | defaults | see §3.5 |
| `ConnectionPool` | object | defaults | see §3.6 |
| `TenantQuotas` | object | defaults | see §3.7 |
| `Projections` | object | disabled | see §3.8 |

### 3.1 `Databases` (array)

`Databases` is a **JSON array**, one object per hosted database — not a name-keyed object.

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `Name` | string | **required** | Logical name clients use |
| `DatabasePath` | string | **required** | Physical path (`*.scdb` for `SingleFile`) |
| `StorageMode` | string | `SingleFile` | `SingleFile`, `Directory` or `Columnar` |
| `EncryptionEnabled` | bool | `false` | Per-database at-rest encryption |
| `EncryptionKeyFile` | string? | `null` | Key file for this database |
| `EncryptionMasterPassword` | string? | `null` | Resolved master password, as an alternative to a key file |
| `ConnectionPoolSize` | int | `1000` | Pool size for this database |
| `IsSystemDatabase` | bool | `false` | Marks a system database |
| `IsReadOnly` | bool | `false` | Read-only instance |

### 3.2 `SystemDatabases`

| Key | Type | Default |
|-----|------|---------|
| `Enabled` | bool | `true` |
| `MasterDatabaseName` | string | `master` |
| `ModelDatabaseName` | string | `model` |
| `MsdbDatabaseName` | string | `msdb` |
| `TempDbDatabaseName` | string | `tempdb` |

### 3.3 `Security`

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `RequireAuthentication` | bool | — | Appears in the shipped sample; **not** a property of `SecurityConfiguration` (the binder ignores it, as it does `AuthMethods`) |
| `TlsEnabled` | bool | `true` | Setting `false` aborts startup — see §5 |
| `MinimumTlsVersion` | string | `Tls12` | `Tls12` (allows TLS 1.2 + 1.3) or `Tls13` (1.3 only); anything else aborts startup |
| `TlsCertificatePath` | string | **required** | `.pfx`, or `.crt`/`.pem` with a private key |
| `TlsPrivateKeyPath` | string? | `null` | **Required** when the certificate is not a PFX |
| `TlsCertificatePassword` | string? | `null` | For password-protected PFX files |
| `JwtSecretKey` | string | **required** | Must be at least 32 characters — see §5 |
| `JwtExpirationHours` | int | `24` | Token lifetime |
| `EnableApiKeys` | bool | `true` | API-key authentication |
| `Users` | array | `[]` | see below |
| `EnableMutualTls` | bool | `false` | Client-certificate authentication |
| `ClientCaCertificatePath` | string? | `null` | CA that must have signed client certificates |
| `CertificateRoleMappings` | array | `[]` | see below |
| `TenantEncryption` | object | disabled | see below |

**`Users[]`** — `Username` (required), `PasswordHash` (required, **SHA-256 hex**, not plaintext),
`Role` (`admin` | `writer` | `reader`, default `reader`), `TenantId` (default `default`),
`AllowedDatabases` (default `["*"]`), `ScopeVersion` (default `1`).

**`CertificateRoleMappings[]`** — `Thumbprint` (required, SHA-256 hex, case-insensitive),
`Role` (default `reader`), `Description`.

**`TenantEncryption`** — `Enabled` (`false`), `RequireDedicatedTenantKey` (`false`),
`DefaultProvider` (`configuration`), `DefaultKeyReference`, `DefaultKeyReferenceByTenant` (map),
`KeyMaterialByReference` (map, for local/dev use).


### 3.4 `Logging`

Note this is `Server:Logging` (SharpCoreDB's own file logging). ASP.NET's log *filter* section is the
separate top-level `Logging:LogLevel` documented by Microsoft.

| Key | Type | Default |
|-----|------|---------|
| `Level` | string | `Information` |
| `FilePath` | string | `/var/log/sharpcoredb/server.log` |
| `MaxFileSizeMb` | int | `100` |
| `MaxFiles` | int | `30` |
| `StructuredLogging` | bool | `true` |

### 3.5 `Performance`

| Key | Type | Default |
|-----|------|---------|
| `QueryCacheSizeMb` | int | `256` |
| `ConnectionPoolMaxIdleTimeSeconds` | int | `300` |
| `MaxConcurrentQueries` | int | `500` |
| `MemoryLimitMb` | long | `4096` |
| `CpuLimitCores` | int | `4` |

### 3.6 `ConnectionPool`

| Key | Type | Default |
|-----|------|---------|
| `MinPoolSize` | int | `2` |
| `MaxPoolSize` | int | `1000` |
| `IdleTimeoutSeconds` | int | `300` |
| `AcquireTimeoutSeconds` | int | `30` |
| `EnableHealthChecks` | bool | `true` |
| `HealthCheckIntervalSeconds` | int | `60` |

### 3.7 `TenantQuotas`

| Key | Type | Default |
|-----|------|---------|
| `Enabled` | bool | `true` |
| `DefaultMaxActiveSessions` | int | `100` |
| `DefaultMaxRequestsPerSecond` | int | `500` |
| `DefaultMaxStorageMb` | long | `4096` |
| `DefaultMaxBatchSize` | int | `1000` |
| `PlanDefaults` | map | `{}` — plan tier → `{ MaxActiveSessions, MaxRequestsPerSecond, MaxStorageMb, MaxBatchSize }` |

### 3.8 `Projections` (optional)

Every projection feature is off by default, so the optional package stays optional.

| Key | Type | Default |
|-----|------|---------|
| `Enabled` | bool | `false` |
| `EnableHostedWorker` | bool | `false` |
| `UsePersistentCheckpoints` | bool | `false` |
| `UseOpenTelemetryMetrics` | bool | `false` |
| `DatabaseName` | string? | `null` (falls back to `DefaultDatabase`) |
| `CheckpointTableName` | string | `scdb_projection_checkpoints` |
| `RuntimeDatabaseId` | string | `main` |
| `RuntimeTenantId` | string | `default` |
| `FromGlobalSequence` | long | `1` |
| `BatchSize` | int | `1000` |
| `PollIntervalMilliseconds` | int | `250` |
| `RunOnStart` | bool | `true` |
| `MaxIterations` | int? | `null` (continuous) |

---

## 4. Environment variables

The nested form uses `__` between levels. These are the real names (no `SHARPCOREDB_` prefix):

```bash
export Server__ServerName="Production-DB"
export Server__MaxConnections=2000
export Server__Security__JwtSecretKey="<at-least-32-characters>"
export Server__Security__TlsCertificatePath="/certs/server.pfx"
export Server__Databases__0__Name="appdb"
export Server__Databases__0__DatabasePath="/data/appdb.scdb"
export Server__Logging__Level="Debug"
```

Array elements are addressed by index (`__0__`, `__1__`), which is why `Databases` must be an array in
both JSON and environment-variable form.

---

## 5. Startup validation

`Program.cs` fails fast rather than starting a half-configured server:

| Check | Behaviour when violated |
|-------|-------------------------|
| `Server` section present | `InvalidOperationException("Server configuration is missing")` |
| `Security:JwtSecretKey` length | `InvalidOperationException("JWT secret key must be at least 32 characters.")` |
| `Security:TlsEnabled` | `InvalidOperationException("TLS must be enabled. Plain HTTP is not supported.")` — there is no plain-HTTP mode |
| `Security:TlsCertificatePath` | `InvalidOperationException("TLS certificate path is required.")` |
| `Security:TlsPrivateKeyPath` | Required unless the certificate ends in `.pfx` |
| `Security:MinimumTlsVersion` | Only `Tls12` and `Tls13` are accepted; anything else throws |

Members marked **required** in the schema above (`ServerName`, `DefaultDatabase`, `Databases`,
`Security`, per-database `Name`/`DatabasePath`, `JwtSecretKey`, `TlsCertificatePath`) must be present in
the effective configuration — the shipped `appsettings.json` sets them all.

---

## 6. Docker

The published image is `ghcr.io/mpcoredeveloper/sharpcoredb-server` (built by
`.github/workflows/docker-publish.yml` on `v*` tags, excluding the parked `v2.0*` line):

```yaml
services:
  sharpcoredb:
    image: ghcr.io/mpcoredeveloper/sharpcoredb-server:2.1.0
    ports:
      - "5001:5001"   # gRPC over TLS
      - "8443:8443"   # HTTPS REST API
    volumes:
      - ./data:/data
      - ./certs:/certs
      - ./config:/app/config
    environment:
      - Server__Security__JwtSecretKey=${SHARPCOREDB_JWT_SECRET}
      - Server__Security__TlsCertificatePath=/certs/server.pfx
      - Server__Databases__0__Name=appdb
      - Server__Databases__0__DatabasePath=/data/appdb.scdb
    configs:
      - source: server_config
        target: /app/appsettings.json

configs:
  server_config:
    file: ./config/appsettings.json
```

---

**Verified against:** `src/SharpCoreDB.Server.Core/Configuration/ServerConfiguration.cs`,
`src/SharpCoreDB.Server/Program.cs`, `src/SharpCoreDB.Server/appsettings.json` — SharpCoreDB 2.1.0.

```
