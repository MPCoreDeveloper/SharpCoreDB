# SharpCoreDB Server — Quick Start (5 Minutes)

Get SharpCoreDB Server running and execute your first query in under 5 minutes.

---

## Option A: Docker (Fastest)

### 1. Start the Server

```bash
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB

# Generate dev certificate
dotnet dev-certs https -ep src/SharpCoreDB.Server/certs/server.pfx -p devonly --trust

# Start
docker compose -f src/SharpCoreDB.Server/docker-compose.yml up -d
```

### 2. Verify

```bash
curl -fsk https://localhost:8443/api/v1/health
# → {"status":"healthy","version":"1.6.0",...}
```

### 3. Execute Your First Query

```bash
# Get a JWT token (replace with your auth flow)
TOKEN="your-jwt-token"

# Create a table
curl -fsk -X POST https://localhost:8443/api/v1/execute \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sql": "CREATE TABLE users (id INTEGER, name TEXT, email TEXT)", "database": "appdb"}'

# Insert data
curl -fsk -X POST https://localhost:8443/api/v1/batch \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "database": "appdb",
    "statements": [
      "INSERT INTO users VALUES (1, '\''Alice'\'', '\''alice@example.com'\'')",
      "INSERT INTO users VALUES (2, '\''Bob'\'', '\''bob@example.com'\'')",
      "INSERT INTO users VALUES (3, '\''Charlie'\'', '\''charlie@example.com'\'')"
    ]
  }'

# Query data
curl -fsk -X POST https://localhost:8443/api/v1/query \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sql": "SELECT * FROM users WHERE id > 1", "database": "appdb"}'
```

---

## Option B: Run from Source

### 1. Build & Run

```bash
git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
cd SharpCoreDB

dotnet build -c Release
dotnet run --project src/SharpCoreDB.Server
```

### 2. Expected Startup Output

```
[10:30:00 INF] Starting SharpCoreDB Server v1.6.0
[10:30:00 INF] 🔒 Security Features:
[10:30:00 INF]   • TLS/HTTPS: Tls12 (required, no plain HTTP)
[10:30:00 INF]   • JWT Authentication: Enabled (via Bearer token)
[10:30:00 INF]   • Rate Limiting: 500 req/10s per IP
[10:30:00 INF] 🚀 Primary protocol (flagship): gRPC
[10:30:00 INF]   • Endpoint: https://0.0.0.0:5001
[10:30:00 INF] 📡 Secondary Protocols:
[10:30:00 INF]   • HTTP REST API: True (Port 8443)
[10:30:00 INF] 📊 Hosted Databases: 5
[10:30:00 INF]   • master (SingleFile)
[10:30:00 INF]   • appdb (SingleFile)
```

---

## Option C: .NET Client (gRPC — Recommended)

The gRPC client is the **primary** protocol for SharpCoreDB. It offers streaming results, binary serialization, and the best performance.

### 1. Add NuGet Package

```bash
dotnet add package SharpCoreDB.Client
```

### 2. Connect and Query

```csharp
using SharpCoreDB.Client;

// Connect to the server
await using var connection = new SharpCoreDBConnection(
    "Server=localhost;Port=5001;Database=appdb;Username=admin;SslMode=Required");
await connection.OpenAsync();

// Create a table
using var createCmd = connection.CreateCommand();
createCmd.CommandText = "CREATE TABLE IF NOT EXISTS products (id INTEGER, name TEXT, price REAL)";
await createCmd.ExecuteNonQueryAsync();

// Insert data
using var insertCmd = connection.CreateCommand();
insertCmd.CommandText = "INSERT INTO products VALUES (1, 'Laptop', 999.99)";
await insertCmd.ExecuteNonQueryAsync();

// Query with streaming results
using var queryCmd = connection.CreateCommand();
queryCmd.CommandText = "SELECT * FROM products";
await using var reader = await queryCmd.ExecuteReaderAsync();

while (await reader.ReadAsync())
{
    Console.WriteLine($"  {reader["id"]}: {reader["name"]} - ${reader["price"]}");
}

// Transactions
await using var tx = await connection.BeginTransactionAsync();
using var txCmd = connection.CreateCommand();
txCmd.CommandText = "INSERT INTO products VALUES (2, 'Monitor', 349.99)";
await txCmd.ExecuteNonQueryAsync();
await tx.CommitAsync();
```

---

## Multi-Tenant SaaS Reference

For a tenant-focused onboarding and isolation validation flow, see:
- `docs/server/MULTITENANT_SAAS_REFERENCE_v1.6.0.md`
- `docs/server/MULTITENANT_THREAT_MODEL_v1.6.0.md`
- `docs/server/MULTITENANT_OPERATIONS_RUNBOOK_v1.6.0.md`
- `Examples/Server/SharpCoreDB.MultiTenantSaaSSample/`

The sample includes a PowerShell isolation validation script and a REST onboarding flow.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────┐
│                   SharpCoreDB Server                  │
│                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────┐ │
│  │  gRPC :5001  │  │ HTTPS :8443  │  │ Binary     │ │
│  │  (Primary)   │  │ (REST API)   │  │ :5433      │ │
│  └──────┬───────┘  └──────┬───────┘  └─────┬──────┘ │
│         │                 │                │         │
│  ┌──────┴─────────────────┴────────────────┴──────┐  │
│  │         Authentication (JWT / TLS)             │  │
│  │         Rate Limiting (per-IP)                 │  │
│  │         Metrics & Telemetry                    │  │
│  └────────────────────┬───────────────────────────┘  │
│                       │                              │
│  ┌────────────────────┴───────────────────────────┐  │
│  │           Session Manager                      │  │
│  │           Connection Pool (min/max/idle)        │  │
│  └────────────────────┬───────────────────────────┘  │
│                       │                              │
│  ┌────────────────────┴───────────────────────────┐  │
│  │           Database Registry                    │  │
│  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐  │  │
│  │  │ master │ │ model  │ │ msdb   │ │ appdb  │  │  │
│  │  └────────┘ └────────┘ └────────┘ └────────┘  │  │
│  └────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────┘
```

---

## Available Endpoints

### REST API (HTTPS :8443)

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/v1/query` | ✅ JWT | Execute SELECT query |
| `POST` | `/api/v1/execute` | ✅ JWT | Execute INSERT/UPDATE/DELETE/CREATE |
| `POST` | `/api/v1/batch` | ✅ JWT | Execute batch of SQL statements |
| `GET` | `/api/v1/schema` | ✅ JWT | Get database schema (tables, columns) |
| `GET` | `/api/v1/databases` | ✅ JWT | List all hosted databases |
| `GET` | `/api/v1/health` | ❌ Open | Server health status |
| `GET` | `/api/v1/metrics` | ✅ JWT | Server metrics (memory, connections) |
| `GET` | `/api/v1/health/detailed` | ❌ Open | Detailed diagnostics (GC, uptime) |
| `GET` | `/health` | ❌ Open | ASP.NET Core health check |

### gRPC (HTTPS :5001)

| Service | Method | Description |
|---------|--------|-------------|
| `DatabaseService` | `Connect` | Open session to database |
| `DatabaseService` | `Disconnect` | Close session |
| `DatabaseService` | `ExecuteQuery` | Streaming SELECT results |
| `DatabaseService` | `ExecuteNonQuery` | INSERT/UPDATE/DELETE |
| `DatabaseService` | `BeginTransaction` | Start transaction |
| `DatabaseService` | `CommitTransaction` | Commit transaction |
| `DatabaseService` | `RollbackTransaction` | Rollback transaction |
| `DatabaseService` | `Ping` | Connection check |
| `DatabaseService` | `HealthCheck` | Server health |

---

## Configuration

Edit `appsettings.json` (or use environment variables with `Server__` prefix):

```jsonc
{
  "Server": {
    "GrpcPort": 5001,
    "HttpsApiPort": 8443,
    "DefaultDatabase": "master",
    "Security": {
      "TlsEnabled": true,
      "TlsCertificatePath": "./certs/server.pfx",
      "JwtSecretKey": "your-secret-key-minimum-32-characters!!",
      "MinimumTlsVersion": "Tls12"
    },
    "Databases": [
      {
        "Name": "appdb",
        "DatabasePath": "./data/user/appdb.scdb",
        "StorageMode": "SingleFile",
        "ConnectionPoolSize": 50
      }
    ]
  }
}
```

See [Configuration Reference](CONFIGURATION_SCHEMA.md) for all options.

---

## What's Next?

- **[Installation Guide](INSTALLATION.md)** — Docker, Linux, Windows, macOS
- **[Security Guide](SECURITY.md)** — TLS, JWT, hardening
- **[Client Guide](CLIENT_GUIDE.md)** — .NET client library
- **[REST API Reference](REST_API.md)** — Full HTTP endpoint docs
- **[Configuration Reference](CONFIGURATION_SCHEMA.md)** — All server settings
