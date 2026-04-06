# SharpCoreDB Admin Tooling & Adoption Guide (v1.6.0)

**Version:** v1.6.0  
**Audience:** DBAs, developers, DevOps engineers, enterprise evaluators  
**Last updated:** 2026

---

## Overview

This guide covers everything you need to connect external database tools to SharpCoreDB, run admin operations, monitor the server, and migrate from custom tooling to standard SQL clients.

SharpCoreDB exposes three connectivity endpoints:

| Endpoint | Protocol | Default Port | Best for |
|---|---|---|---|
| **Binary protocol** | PostgreSQL wire (TLS) | 5433 | All PostgreSQL-compatible tools |
| **gRPC API** | gRPC / HTTP2 (TLS) | 5001 | Native .NET clients, high-throughput apps |
| **HTTPS REST API** | HTTP/2 + JSON (TLS) | 8443 | Scripting, CI/CD, monitoring, web admin |

---

## Tool Compatibility Matrix

Tested against SharpCoreDB v1.6.0 binary protocol (port 5433).

### GUI Tools

| Tool | Version Tested | Connect | Browse | Query | Export | Import | Notes |
|---|---|---|---|---|---|---|---|
| **DBeaver Community** | 24.x | ✅ | ✅ | ✅ | ✅ | ⚠️ | Use PostgreSQL driver. Large object import requires workaround. |
| **DBeaver PRO** | 24.x | ✅ | ✅ | ✅ | ✅ | ✅ | Full import/export via PostgreSQL COPY. |
| **Beekeeper Studio** | 4.x | ✅ | ✅ | ✅ | ✅ | ✅ | Select PostgreSQL connection type. |
| **DataGrip** | 2024.x | ✅ | ✅ | ✅ | ✅ | ⚠️ | Requires PostgreSQL 15+ compatibility mode. Some DDL introspection warnings. |
| **pgAdmin 4** | 8.x | ✅ | ✅ | ✅ | ⚠️ | ⚠️ | Use "PostgreSQL" server type. `pg_dump` not supported. |
| **TablePlus** | 5.x | ✅ | ✅ | ✅ | ✅ | ⚠️ | PostgreSQL connection. CSV import works; native import does not. |
| **HeidiSQL** | 12.x | ⚠️ | ⚠️ | ✅ | ⚠️ | ❌ | Connection works; some metadata queries fail. Query execution is stable. |
| **Azure Data Studio** | 1.x | ⚠️ | ❌ | ✅ | ❌ | ❌ | Requires PostgreSQL extension. DDL browsing not fully supported. |

### CLI Tools

| Tool | Version | Status | Notes |
|---|---|---|---|
| **psql** | 14+ | ✅ | Full query and admin support. Use `--sslmode=require`. |
| **pgcli** | 4.x | ✅ | Auto-complete works. Some `\d` meta-commands may return partial results. |
| **pg_dump** | 14+ | ⚠️ | Partial — structure dump works; custom data types may not round-trip. |
| **pg_restore** | 14+ | ⚠️ | Partial — standard table restores work. |

### BI / Analytics

| Tool | Status | Notes |
|---|---|---|
| **Power BI Desktop** | ✅ via ODBC | Use psqlODBC DSN. DirectQuery and Import modes supported. |
| **Tableau Desktop** | ✅ via ODBC | Use "Other Databases (ODBC)" connection with psqlODBC. |
| **Excel (Power Query)** | ✅ via ODBC | Add psqlODBC DSN; use "From ODBC" in Power Query. |
| **Apache Spark** | ✅ via JDBC | Use pgjdbc driver (`org.postgresql:postgresql:42.7.x`). |
| **Metabase** | ✅ | Use PostgreSQL database type, port 5433. |
| **Grafana** | ✅ | Use PostgreSQL data source plugin. |

### Programmatic

| Driver / Library | Language | Status | Notes |
|---|---|---|---|
| **SharpCoreDB.Client** | C# / .NET 10 | ✅ Native | Primary recommended driver. gRPC + ADO.NET. |
| **Npgsql** | C# / .NET | ✅ | Use via binary protocol. All standard operations work. |
| **psycopg2** | Python | ✅ | `sslmode='require'`, port 5433. |
| **psycopg3** | Python | ✅ | Recommended for async Python. |
| **pg8000** | Python | ✅ | Pure Python, no binary dependencies. |
| **asyncpg** | Python | ✅ | High-performance async Python client. |
| **pgjdbc** | Java | ✅ | Standard JDBC; add `ssl=true&sslmode=require`. |
| **node-postgres (pg)** | JavaScript/Node | ✅ | `ssl: { rejectUnauthorized: false }` for dev certs. |
| **go-pq / pgx** | Go | ✅ | `sslmode=require` in connection string. |

---

## Setup Flows

### Flow 1 — DBeaver (recommended for teams)

1. **Install DBeaver Community** from [dbeaver.io](https://dbeaver.io/download/).
2. **New Connection** → Select **PostgreSQL**.
3. Fill in:
   - Host: `your-server`
   - Port: `5433`
   - Database: `appdb`
   - Username / Password: as configured in `appsettings.json`
4. **Connection settings → SSL tab**:
   - SSL: Enabled
   - SSL mode: `require`
   - For dev/self-signed certs: disable certificate verification.
5. Click **Test Connection** → should return "Connected".
6. Browse schemas, tables, and run SQL in the SQL editor.

> **Tip:** DBeaver caches metadata. If you add tables in another session, right-click the schema → Refresh.

---

### Flow 2 — psql (CLI)

```bash
# Basic connection
psql "host=your-server port=5433 dbname=appdb user=admin sslmode=require"

# With password prompt
PGPASSWORD=yourpassword psql -h your-server -p 5433 -U admin -d appdb

# Verify metadata
\dt             -- list tables
\d tablename    -- describe table
SELECT version();
SELECT * FROM information_schema.tables LIMIT 10;
```

---

### Flow 3 — Npgsql (.NET)

```csharp
using Npgsql;

var connStr = "Host=your-server;Port=5433;Database=appdb;Username=admin;Password=secret;SSL Mode=Require;Trust Server Certificate=true";
await using var conn = new NpgsqlConnection(connStr);
await conn.OpenAsync();

await using var cmd = new NpgsqlCommand("SELECT id, name FROM users LIMIT 10", conn);
await using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetInt32(0)}: {reader.GetString(1)}");
}
```

---

### Flow 4 — Python (psycopg2)

```python
import psycopg2

conn = psycopg2.connect(
    host="your-server",
    port=5433,
    dbname="appdb",
    user="admin",
    password="secret",
    sslmode="require"
)
cur = conn.cursor()
cur.execute("SELECT id, name FROM users LIMIT 10")
for row in cur.fetchall():
    print(row)
cur.close()
conn.close()
```

---

### Flow 5 — Power BI Desktop (via ODBC)

1. Install [psqlODBC 64-bit](https://www.postgresql.org/ftp/odbc/versions/msi/).
2. Open **ODBC Data Sources (64-bit)** → Add **System DSN** → select **PostgreSQL Unicode(x64)**.
3. Configure: Server = `your-server`, Port = `5433`, Database = `appdb`, SSL Mode = `require`.
4. In Power BI: **Get Data → ODBC** → select your DSN → authenticate → load tables.

---

### Flow 6 — Web Admin UI

The optional web admin is served on the HTTPS API port (8443) at the `/admin` path when `EnableWebAdmin: true` is set in `appsettings.json`.

1. Enable in configuration:
   ```json
   "EnableWebAdmin": true,
   "WebAdminPath": "/admin"
   ```
2. Open `https://your-server:8443/admin` in a browser.
3. Log in with admin credentials.
4. Available pages:
   - **Dashboard** — server health, uptime, database status.
   - **Databases** — configured databases, storage modes, encryption status.
   - **Query** — ad-hoc SQL runner (500-row cap; all queries logged).
   - **Metrics** — connection counts, latency percentiles, protocol breakdown.

> The web admin is disabled by default. Enable only in environments where you control network access.

---

## Known Limitations & Mitigations

| Limitation | Affected Tools | Mitigation |
|---|---|---|
| `pg_dump` generates warnings for unsupported catalog entries | pg_dump, pgAdmin | Use SharpCoreDB's built-in export (Diagnostics → Export Snapshot) for full fidelity backups. |
| `LISTEN/NOTIFY` not supported | pgAdmin (notifications), Debezium | Use SharpCoreDB Event Sourcing package for change notification. |
| Large object API (`lo_*` functions) | Tools that use LOBs | Store binary data as `BLOB` columns; use file-system or object store for large files. |
| Scrollable/updatable cursors | Some BI tools (Tableau live queries) | Use Import mode instead of Live Query in BI tools that require scrollable cursors. |
| Some `pg_catalog` views return fewer columns | DataGrip, pgAdmin DDL editor | Minor cosmetic warnings; queries execute correctly. Tracked for v1.7.0 catalog expansion. |
| `pg_dump` schema-only mode may miss custom types | pg_dump `--schema-only` | Run `SHOW CREATE TABLE` in SharpCoreDB Viewer for accurate DDL. |
| HeidiSQL metadata queries may time out | HeidiSQL | Increase HeidiSQL "Net read timeout" to 30s in connection settings. |
| Case-sensitive identifiers | Tools sending double-quoted names | SharpCoreDB is case-insensitive for unquoted identifiers; double-quoted names are case-sensitive. |

---

## Diagnostics & Monitoring Quickstart

### Health Check Endpoint

```bash
curl -fsk https://your-server:8443/api/v1/health
# Response: {"status":"healthy","version":"1.6.0","uptime":"...","databases":[...]}
```

Integrate into monitoring systems (Prometheus blackbox exporter, Grafana, Datadog HTTP check):

```yaml
# Prometheus blackbox scrape example
- job_name: sharpcoredb_health
  metrics_path: /probe
  params:
    module: [http_2xx]
  static_configs:
    - targets: ['https://your-server:8443/api/v1/health']
```

### Metrics Endpoint

```bash
curl -fsk -H "Authorization: Bearer $TOKEN" https://your-server:8443/api/v1/metrics
```

Key metrics available:

| Metric | Description |
|---|---|
| `active_connections` | Current open connections |
| `total_queries` | Queries executed since startup |
| `query_latency_p50_ms` | Median query latency |
| `query_latency_p99_ms` | 99th percentile query latency |
| `bytes_read` / `bytes_written` | I/O totals |
| `protocol_grpc_requests` | gRPC request count |
| `protocol_binary_requests` | Binary protocol request count |
| `protocol_https_requests` | HTTP REST request count |

### SharpCoreDB Viewer Diagnostics

From the Viewer tool (SharpCoreDB.Viewer):
1. Connect to a database.
2. Toolbar → **Diagnostics** (ℹ️ icon).
3. Click **Run Diagnostics** to capture:
   - Page count, page size, total storage size.
   - Cache size, journal mode.
   - Integrity check status.
   - Table row counts.
4. Use **Optimize Database** or **Checkpoint WAL** to run admin operations.
5. Use **Export Snapshot** to save a JSON diagnostics snapshot for offline analysis.

---

## Migration: From Custom Tooling to Standard SQL Clients

### If you used raw SharpCoreDB.Client (embedded mode)

Your embedded `SharpCoreDB` object becomes a `SharpCoreDBConnection` connecting to the server:

```csharp
// Before (embedded)
using var db = new SharpCoreDatabase("./data/app.scdb");
var results = db.ExecuteSQL("SELECT * FROM users");

// After (server mode, Npgsql)
await using var conn = new NpgsqlConnection("Host=localhost;Port=5433;Database=appdb;...");
await conn.OpenAsync();
await using var cmd = new NpgsqlCommand("SELECT * FROM users", conn);
await using var reader = await cmd.ExecuteReaderAsync();
```

### If you used the custom HTTP/gRPC client

The REST API format is unchanged. JWT auth token flow is the same. Only the connection endpoint changes:

```bash
# Before: direct localhost embedded API
curl http://localhost:5000/api/query ...

# After: HTTPS server API
curl -fsk https://your-server:8443/api/v1/query -H "Authorization: Bearer $TOKEN" ...
```

### If you used custom SQL scripts against a file database

1. Start the SharpCoreDB server, pointing to your existing `.scdb` file.
2. Connect with psql or DBeaver using the binary protocol.
3. Run your existing SQL — the SQL dialect is unchanged.
4. Use `COPY` or `INSERT ... SELECT` to migrate data between databases.

---

## Connection String Reference

### SharpCoreDB.Client (native .NET)

```
Server=localhost;Port=5001;Database=appdb;Username=admin;Password=secret;SslMode=Required
```

### Npgsql / PostgreSQL wire protocol

```
Host=localhost;Port=5433;Database=appdb;Username=admin;Password=secret;SSL Mode=Require;Trust Server Certificate=true
```

### psql CLI

```
postgresql://admin:secret@localhost:5433/appdb?sslmode=require
```

### pgjdbc (Java)

```
jdbc:postgresql://localhost:5433/appdb?user=admin&password=secret&ssl=true&sslmode=require
```

### psycopg2 (Python)

```
host=localhost port=5433 dbname=appdb user=admin password=secret sslmode=require
```

### ODBC (DSN-less)

```
Driver={PostgreSQL Unicode(x64)};Server=localhost;Port=5433;Database=appdb;Uid=admin;Pwd=secret;SSLmode=require;
```

---

## Support Matrix by Version

| SharpCoreDB Version | Binary Protocol | REST API | Web Admin | Min psqlODBC | Min pgjdbc |
|---|---|---|---|---|---|
| v1.6.0 | ✅ v3 full | ✅ v1 | ✅ optional | 13.02 | 42.7 |
| v1.5.x | ✅ v3 partial | ✅ v1 | ❌ | 13.02 | 42.7 |
| v1.4.x | ⚠️ v3 minimal | ✅ v1 | ❌ | Not certified | Not certified |

---

*This guide is version-aligned to SharpCoreDB v1.6.0. For older versions, consult the archived docs.*
