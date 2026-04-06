# SharpCoreDB Server Configuration Schema (v1.7.0)

**Supported Formats:** TOML, JSON, YAML  
**Default File:** `appsettings.json`  
**Environment Variables:** `SHARPCOREDB_*` prefix  

---

## Basic Server Configuration

### TOML Format
```toml
[server]
server_name = "SharpCoreDB-01"
bind_address = "0.0.0.0"
grpc_port = 5001
enable_grpc = true
enable_grpc_http3 = true
https_api_port = 8443
enable_https_api = true
max_connections = 1000
connection_timeout_seconds = 300
default_database = "master"

[server.security]
tls_enabled = true
minimum_tls_version = "Tls12"
tls_certificate_path = "/etc/sharpcoredb/certs/server.crt"
tls_private_key_path = "/etc/sharpcoredb/certs/server.key"
jwt_secret_key = "your-256-bit-secret-here"
jwt_expiration_hours = 24
enable_api_keys = true

[server.databases.master]
name = "master"
database_path = "/data/master.db"
storage_mode = "SingleFile"
encryption_enabled = false
connection_pool_size = 50
is_system_database = true
is_read_only = false

[server.databases.app]
name = "app"
database_path = "/data/app.db"
storage_mode = "Directory"
encryption_enabled = true
encryption_key_file = "/etc/sharpcoredb/keys/app.key"
connection_pool_size = 200
is_system_database = false
is_read_only = false

[server.system_databases]
enabled = true
master_database_name = "master"
model_database_name = "model"
temp_database_name = "temp"

[server.logging]
level = "Information"
file_path = "/var/log/sharpcoredb/server.log"
max_file_size_mb = 100
max_files = 30
structured_logging = true

[server.performance]
query_cache_size_mb = 256
connection_pool_max_idle_time_seconds = 300
max_concurrent_queries = 500
memory_limit_mb = 4096
cpu_limit_cores = 4
```

### JSON Format
```json
{
  "server": {
    "serverName": "SharpCoreDB-01",
    "bindAddress": "0.0.0.0",
    "grpcPort": 5001,
    "enableGrpc": true,
    "enableGrpcHttp3": true,
    "httpsApiPort": 8443,
    "enableHttpsApi": true,
    "maxConnections": 1000,
    "connectionTimeoutSeconds": 300,
    "defaultDatabase": "master",

    "security": {
      "tlsEnabled": true,
      "minimumTlsVersion": "Tls12",
      "tlsCertificatePath": "/etc/sharpcoredb/certs/server.crt",
      "tlsPrivateKeyPath": "/etc/sharpcoredb/certs/server.key",
      "jwtSecretKey": "your-256-bit-secret-here",
      "jwtExpirationHours": 24,
      "enableApiKeys": true
    },

    "databases": {
      "master": {
        "name": "master",
        "databasePath": "/data/master.db",
        "storageMode": "SingleFile",
        "encryptionEnabled": false,
        "connectionPoolSize": 50,
        "isSystemDatabase": true,
        "isReadOnly": false
      },
      "app": {
        "name": "app",
        "databasePath": "/data/app.db",
        "storageMode": "Directory",
        "encryptionEnabled": true,
        "encryptionKeyFile": "/etc/sharpcoredb/keys/app.key",
        "connectionPoolSize": 200,
        "isSystemDatabase": false,
        "isReadOnly": false
      }
    },

    "systemDatabases": {
      "enabled": true,
      "masterDatabaseName": "master",
      "modelDatabaseName": "model",
      "tempDatabaseName": "temp"
    },

    "logging": {
      "level": "Information",
      "filePath": "/var/log/sharpcoredb/server.log",
      "maxFileSizeMb": 100,
      "maxFiles": 30,
      "structuredLogging": true
    },

    "performance": {
      "queryCacheSizeMb": 256,
      "connectionPoolMaxIdleTimeSeconds": 300,
      "maxConcurrentQueries": 500,
      "memoryLimitMb": 4096,
      "cpuLimitCores": 4
    }
  }
}
```

---

## Configuration Sections

### Server Section

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `serverName` | string | "SharpCoreDB" | Server display name |
| `bindAddress` | string | "0.0.0.0" | IP address to bind to |
| `grpcPort` | int | 5001 | gRPC HTTPS port |
| `enableGrpc` | bool | true | Enable gRPC protocol |
| `enableGrpcHttp3` | bool | false | Enable gRPC over HTTP/3 |
| `httpsApiPort` | int | 8443 | HTTPS REST API port |
| `enableHttpsApi` | bool | true | Enable HTTPS REST API |
| `maxConnections` | int | 1000 | Maximum concurrent connections |
| `connectionTimeoutSeconds` | int | 300 | Connection timeout |
| `defaultDatabase` | string | "master" | Default database name |

### Security Section

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `tlsEnabled` | bool | true | Require TLS encryption |
| `minimumTlsVersion` | enum | "Tls12" | Minimum TLS version (Tls12, Tls13) |
| `tlsCertificatePath` | string | required | Path to TLS certificate (.crt/.pem) |
| `tlsPrivateKeyPath` | string | required | Path to TLS private key |
| `jwtSecretKey` | string | required | JWT signing secret (256-bit) |
| `jwtExpirationHours` | int | 24 | JWT token expiration |
| `enableApiKeys` | bool | true | Enable API key authentication |

### Database Configuration

Each database is configured as a named section:

```toml
[server.databases.myapp]
name = "myapp"
database_path = "/data/myapp.db"
storage_mode = "SingleFile"
encryption_enabled = true
encryption_key_file = "/keys/myapp.key"
connection_pool_size = 100
is_system_database = false
is_read_only = false
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `name` | string | required | Logical database name |
| `databasePath` | string | required | File system path to database |
| `storageMode` | enum | "SingleFile" | SingleFile, Directory, Columnar |
| `encryptionEnabled` | bool | false | Enable AES-256-GCM encryption |
| `encryptionKeyFile` | string | null | Path to encryption key file |
| `connectionPoolSize` | int | 50 | Connection pool size |
| `isSystemDatabase` | bool | false | Marks as system database |
| `isReadOnly` | bool | false | Read-only access |

### System Databases

```toml
[server.system_databases]
enabled = true
master_database_name = "master"
model_database_name = "model"
temp_database_name = "temp"
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | true | Enable system databases |
| `masterDatabaseName` | string | "master" | Master system database |
| `modelDatabaseName` | string | "model" | Model system database |
| `tempDatabaseName` | string | "temp" | Temporary database |

### Logging Configuration

```toml
[server.logging]
level = "Information"
file_path = "/var/log/sharpcoredb/server.log"
max_file_size_mb = 100
max_files = 30
structured_logging = true
console_enabled = true
console_level = "Warning"
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `level` | enum | "Information" | Minimum log level |
| `filePath` | string | null | Log file path |
| `maxFileSizeMb` | int | 100 | Max log file size |
| `maxFiles` | int | 30 | Max log file count |
| `structuredLogging` | bool | true | Enable structured logging |
| `consoleEnabled` | bool | true | Enable console logging |
| `consoleLevel` | enum | "Warning" | Console log level |

### Performance Tuning

```toml
[server.performance]
query_cache_size_mb = 256
connection_pool_max_idle_time_seconds = 300
max_concurrent_queries = 500
memory_limit_mb = 4096
cpu_limit_cores = 4
enable_query_plan_caching = true
query_plan_cache_size_mb = 128
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `queryCacheSizeMb` | int | 256 | Query result cache size |
| `connectionPoolMaxIdleTimeSeconds` | int | 300 | Connection pool idle timeout |
| `maxConcurrentQueries` | int | 500 | Max concurrent query execution |
| `memoryLimitMb` | int | 4096 | Memory limit per database |
| `cpuLimitCores` | int | 4 | CPU core limit |
| `enableQueryPlanCaching` | bool | true | Enable query plan caching |
| `queryPlanCacheSizeMb` | int | 128 | Query plan cache size |

### Projection Runtime (Optional)

```toml
[server.projections]
enabled = false
enable_hosted_worker = false
use_persistent_checkpoints = false
use_open_telemetry_metrics = false
database_name = "app"
checkpoint_table_name = "scdb_projection_checkpoints"
runtime_database_id = "main"
runtime_tenant_id = "default"
from_global_sequence = 1
batch_size = 1000
poll_interval_milliseconds = 250
run_on_start = true
# max_iterations = 20 # optional; omit for continuous processing
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `enabled` | bool | false | Enables projection runtime service wiring |
| `enableHostedWorker` | bool | false | Starts projection hosted worker |
| `usePersistentCheckpoints` | bool | false | Persists checkpoints in SharpCoreDB tables |
| `useOpenTelemetryMetrics` | bool | false | Replaces default projection metrics with OpenTelemetry adapter |
| `databaseName` | string | null | Database used for event store/checkpoint runtime |
| `checkpointTableName` | string | `scdb_projection_checkpoints` | Table name for persistent checkpoints |
| `runtimeDatabaseId` | string | `main` | Logical projection runtime database scope |
| `runtimeTenantId` | string | `default` | Logical projection runtime tenant scope |
| `fromGlobalSequence` | long | 1 | Start sequence when no checkpoint exists |
| `batchSize` | int | 1000 | Projection run batch size |
| `pollIntervalMilliseconds` | int | 250 | Background worker poll interval |
| `runOnStart` | bool | true | Executes first projection cycle immediately |
| `maxIterations` | int? | null | Optional bounded iterations for controlled runs |

---

## Environment Variables

Configuration can be overridden with environment variables:

```bash
# Server settings
export SHARPCOREDB_SERVER__SERVERNAME="Production-DB"
export SHARPCOREDB_SERVER__MAXCONNECTIONS=2000

# Security settings
export SHARPCOREDB_SERVER__SECURITY__JWTSECRETKEY="your-secret-key"
export SHARPCOREDB_SERVER__SECURITY__TLSCERTIFICATEPATH="/certs/server.crt"

# Database settings
export SHARPCOREDB_SERVER__DATABASES__MYAPP__CONNECTIONPOOLSIZE=500

# Logging
export SHARPCOREDB_SERVER__LOGGING__LEVEL="Debug"
```

---

## Configuration Validation

The server validates configuration on startup:

### Required Settings
- `server.security.tlsEnabled = true` (TLS cannot be disabled)
- `server.security.tlsCertificatePath` (certificate required)
- `server.security.tlsPrivateKeyPath` (private key required)
- `server.security.jwtSecretKey` (256-bit secret required)
- `server.defaultDatabase` (must reference existing database)

### Automatic Defaults
- System databases are created automatically if enabled
- Default database is created if it doesn't exist
- Connection pools are sized based on `maxConnections`

### Runtime Validation
- Certificate files must exist and be readable
- Database paths must be writable (unless read-only)
- Encryption keys must be valid AES-256 keys
- Port numbers must be available

---

## Advanced Configuration

### High Availability Setup

```toml
[server]
server_name = "SharpCoreDB-Cluster-01"
max_connections = 5000

[server.databases.shared]
name = "shared"
database_path = "/shared/data/shared.db"
storage_mode = "Directory"
encryption_enabled = true
connection_pool_size = 1000

[server.replication]
enabled = true
role = "primary"  # primary, secondary, arbiter
cluster_name = "prod-cluster"
replication_port = 5433
sync_mode = "synchronous"  # synchronous, asynchronous

[server.replication.nodes]
node1 = "sharpcoredb-02:5433"
node2 = "sharpcoredb-03:5433"
node3 = "sharpcoredb-04:5433"
```

### Monitoring Integration

```toml
[server.monitoring]
enabled = true
prometheus_port = 9090
metrics_prefix = "sharpcoredb"
enable_tracing = true
tracing_endpoint = "http://jaeger:14268/api/traces"

[server.monitoring.alerts]
high_connection_count_threshold = 800
slow_query_threshold_ms = 1000
memory_usage_threshold_mb = 3500
```

### Development Configuration

```toml
[server]
server_name = "SharpCoreDB-Dev"
max_connections = 100

[server.security]
tls_enabled = false  # Only for development!
jwt_secret_key = "dev-secret-key-12345678901234567890123456789012"

[server.logging]
level = "Debug"
console_enabled = true
console_level = "Information"

[server.databases.dev]
name = "dev"
database_path = "./data/dev.db"
storage_mode = "SingleFile"
encryption_enabled = false
```

---

## Configuration File Locations

The server looks for configuration files in this order:

1. `appsettings.json` (current directory)
2. `appsettings.{Environment}.json` (e.g., `appsettings.Production.json`)
3. `sharpcoredb.toml` (TOML format)
4. `sharpcoredb.yaml` (YAML format)
5. Environment variables
6. Command line arguments

### Docker Configuration

```yaml
# docker-compose.yml
version: '3.8'
services:
  sharpcoredb:
    image: sharpcoredb/server:1.7.0
    ports:
      - "5001:5001"   # gRPC
      - "8443:8443"   # HTTPS API
    volumes:
      - ./data:/data
      - ./certs:/certs
      - ./config:/app/config
    environment:
      - SHARPCOREDB_SERVER__DATABASES__APP__DATABASEPATH=/data/app.db
      - SHARPCOREDB_SERVER__SECURITY__TLSCERTIFICATEPATH=/certs/server.crt
    configs:
      - source: server_config
        target: /app/appsettings.json

configs:
  server_config:
    file: ./config/appsettings.json
```

---

**Last Updated:** January 28, 2026  
**Configuration Version:** 1.0  
**Supported Formats:** JSON, TOML, YAML
