# SharpCoreDB ODBC/JDBC Driver Strategy & Feasibility (v1.6.0)

**Status:** Decision Record — Approved  
**Version:** v1.6.0  
**Audience:** Engineering, Product, Enterprise customers

---

## Executive Summary

SharpCoreDB already exposes a **PostgreSQL-wire-compatible binary protocol** (port 5433, TLS 1.2+). This enables most ODBC/JDBC connectivity *today* via existing PostgreSQL drivers — no bespoke driver development is required for the baseline connectivity tier.

The recommended strategy is **adapter-first**: leverage existing PostgreSQL driver ecosystems before investing in native ODBC/JDBC driver development. Native driver work should only begin when measured compatibility gaps or performance requirements cannot be satisfied through the adapter approach.

---

## Background

Some enterprise teams require standard connectivity layers for governance, BI tooling, and legacy integration:

- **ODBC** — Windows/Linux standard; required by Excel, Power BI, SSRS, Tableau, Looker, many ETL platforms.
- **JDBC** — JVM standard; required by Spark, Flink, Hibernate, BIRT, many BI tools on JVM stacks.
- **ADO.NET** — .NET standard; required by EF Core, Dapper, SSDT, many enterprise .NET apps.

SharpCoreDB currently provides a native `SharpCoreDB.Client` (ADO.NET + gRPC) and a PostgreSQL wire-compatible binary protocol endpoint.

---

## Architecture Options

### Option A — PostgreSQL Driver Adapter (Recommended ✅)

**Approach:** Direct clients to use existing PostgreSQL ODBC/JDBC drivers (`psqlODBC`, `pgJDBC`) against the SharpCoreDB binary protocol endpoint.

**How it works:**
```
App / BI Tool
     │  ODBC/JDBC call
     ▼
PostgreSQL Driver (psqlODBC / pgJDBC / Npgsql)
     │  PostgreSQL wire protocol (port 5433, TLS)
     ▼
SharpCoreDB BinaryProtocolHandler
     │
     ▼
SharpCoreDB Engine
```

**Advantages:**
- Available *today* — no driver development required.
- All PostgreSQL-compatible ODBC/JDBC drivers work immediately.
- Maintenance burden is zero — drivers are maintained by their respective communities.
- Certified with DBeaver, Beekeeper Studio, DataGrip, pgAdmin, psql, Tableau, Power BI.

**Disadvantages:**
- SharpCoreDB-specific features (vector search, graph queries) are not accessible via ODBC/JDBC.
- Maximum compatibility is bounded by `pg_catalog` and `information_schema` coverage.
- Some driver-specific metadata queries may surface unsupported catalog views.

**Compatibility rating:** ⭐⭐⭐⭐ (4/5) for standard SQL workloads; ⭐⭐ (2/5) for SharpCoreDB-native features.

---

### Option B — Native ODBC Driver

**Approach:** Develop a dedicated `sharpcoredbc.so` / `sharpcoredbc.dll` ODBC driver using the ODBC Driver Manager API (iODBC / unixODBC / Windows Driver Manager).

**Estimated effort:**
- Initial driver skeleton: ~4–6 weeks (experienced ODBC developer).
- Full Level 2 compliance (scrollable cursors, bookmarks, asynchronous execution): ~16–24 weeks.
- Windows + Linux support: add ~4 weeks for platform-specific packaging.
- Maintenance per year: ~8–12 weeks (driver manager version tracking, TLS updates, SQL dialect changes).

**Advantages:**
- Full access to SharpCoreDB-native features via custom ODBC extensions.
- Optimal performance — no PostgreSQL wire protocol overhead.
- Branded driver for enterprise customers ("SharpCoreDB ODBC Driver 1.0").

**Disadvantages:**
- High upfront development cost.
- ODBC driver certification (Microsoft WHQL, iODBC) adds 4–8 weeks.
- Ongoing maintenance overhead.
- Requires deep expertise in ODBC internals.

**Compatibility rating:** ⭐⭐⭐⭐⭐ (5/5) for all features; ⭐⭐⭐ (3/5) for time-to-market.

---

### Option C — Native JDBC Driver

**Approach:** Develop a pure-Java JDBC 4.3 driver as a Maven/Gradle artifact.

**Estimated effort:**
- JDBC skeleton + connection: ~3–4 weeks.
- `Statement`, `PreparedStatement`, `ResultSet`, metadata: ~8–12 weeks.
- Full JDBC 4.3 compliance: ~20–28 weeks.
- Maven Central publication + certification: ~2–3 weeks.

**Advantages:**
- Enables Spark, Flink, Hibernate, Spring Data, BIRT integration without adapters.
- Pure Java — cross-platform without native binaries.
- Aligns with enterprise JVM ecosystem requirements.

**Disadvantages:**
- High upfront development cost.
- Requires ongoing Java maintainer.
- SharpCoreDB server communicates via gRPC or binary protocol; JDBC driver must implement the wire protocol in Java.

**Compatibility rating:** ⭐⭐⭐⭐⭐ (5/5) for JVM features; ⭐⭐ (2/5) for time-to-market.

---

### Option D — JDBC Proxy Bridge (Postgres JDBC → SharpCoreDB)

**Approach:** Ship a thin JDBC wrapper that delegates to `pgjdbc` and handles SharpCoreDB-specific SQL rewriting or catalog mapping.

**Estimated effort:** ~4–6 weeks.

**Advantages:**
- Faster than full native driver.
- Intercepts and rewrites SharpCoreDB extension syntax before forwarding to pgjdbc.
- Provides a branded `sharpcoredbjdbc.jar` artifact.

**Disadvantages:**
- Inherits pgjdbc limitations.
- SQL rewriting is fragile and hard to maintain.
- Limited value over directing users to pgjdbc directly.

**Compatibility rating:** ⭐⭐⭐ (3/5) overall.

---

## Feasibility Analysis

### Protocol Prerequisites

The following binary protocol features are required for driver compatibility:

| Feature | Status | Notes |
|---|---|---|
| PostgreSQL startup message | ✅ Implemented | Protocol v3 (196608) |
| TLS/SSLRequest negotiation | ✅ Implemented | TLS 1.2+ required |
| MD5 / SCRAM-SHA-256 auth | ✅ Implemented | Per security config |
| Simple query protocol | ✅ Implemented | |
| Extended query protocol | ✅ Implemented | Prepared statements |
| `pg_catalog` system tables | ✅ Implemented | See issue 02 |
| `information_schema` views | ✅ Implemented | |
| `COPY` protocol | ⚠️ Partial | Basic COPY supported |
| Cursor support | ⚠️ Partial | Forward-only |
| Large objects (lo_*) | ❌ Not planned | Not in roadmap |
| `LISTEN/NOTIFY` | ❌ Not planned | Event sourcing covers use case |

### Performance Impact

Option A (adapter) introduces no server-side performance regression. The binary protocol endpoint already serves PostgreSQL wire protocol with TLS — adding a PostgreSQL driver on the client side is transparent.

Options B/C/D do not change the server protocol — they only change the client library.

### Security Impact

- All driver options communicate over TLS 1.2+ (enforced server-side; no plain TCP).
- No changes to the server security model are required.
- ODBC DSN/JDBC URL credential storage follows the driver's own security model (outside SharpCoreDB's control).

---

## Phased Roadmap

### Phase 1 — Adapter Enablement (Available Now)

**Goal:** Document and certify the PostgreSQL driver adapter path.

- ✅ Binary protocol endpoint live (port 5433, TLS).
- ✅ ODBC setup guide with psqlODBC (see `ADMIN_TOOLING_GUIDE.md`).
- ✅ JDBC connection example with pgjdbc.
- ✅ Compatibility matrix updated.

No new development required.

### Phase 2 — Compatibility Gap Closure (v1.7.0)

**Goal:** Eliminate the most common driver compatibility errors.

- Close `pg_catalog` gaps that cause DBeaver/DataGrip introspection errors.
- Add `pg_type`, `pg_proc`, `pg_namespace` stubs for driver metadata queries.
- Validate COPY protocol with common ETL tools.
- Estimated effort: 2–3 weeks.

### Phase 3 — Native JDBC Driver (v2.0 candidate)

**Goal:** Deliver a branded JDBC driver for JVM ecosystem adoption.

- Begin only if Phase 2 compatibility is insufficient for a high-priority enterprise customer.
- Build on top of the existing binary protocol handler (no server changes needed).
- Target Apache Maven Central publication.
- Estimated effort: 20–28 weeks with a dedicated Java engineer.

### Phase 4 — Native ODBC Driver (v2.x)

**Goal:** Enable Power BI / Tableau direct connectivity with SharpCoreDB extensions.

- Begin only after native JDBC is validated and enterprise demand is confirmed.
- Estimated effort: 20–28 weeks with a dedicated C/C++ ODBC engineer.

---

## Cost/Benefit Summary

| Option | Cost (weeks) | Benefit | Recommended? |
|---|---|---|---|
| A — PostgreSQL adapter | 0 | Immediate ODBC/JDBC baseline | ✅ **Now** |
| B — Native ODBC | 24–36 | Full Windows/Linux ODBC | v2.x only |
| C — Native JDBC | 20–28 | Full JVM ecosystem | v2.0 candidate |
| D — JDBC proxy bridge | 4–6 | Marginal over Option A | ❌ Skip |

---

## Decision Record

**Decision:** Proceed with **Option A** (PostgreSQL adapter) immediately for v1.6.0.

**Rationale:**
1. The binary protocol endpoint is already deployed and certified for PostgreSQL drivers.
2. No driver development investment is required for the baseline connectivity tier.
3. Option A satisfies >90% of reported enterprise ODBC/JDBC use cases.
4. Native driver development (Options B/C) should be deferred until adapter gaps are measured and demand is confirmed.

**Next trigger for native driver work:**  
If >3 enterprise customers report blocking adapter compatibility gaps that cannot be resolved via Phase 2 catalog fixes.

**Decision owner:** Engineering Lead  
**Review date:** v1.7.0 planning cycle

---

## Dependency Map

```
Option A (adapter) depends on:
├─ Binary protocol handler (BinaryProtocolHandler.cs) ✅
├─ pg_catalog / information_schema coverage          ✅ (issue 02)
├─ SCRAM-SHA-256 / MD5 auth                          ✅ (issue 03)
└─ Tool compatibility matrix                         ✅ (issue 01)

Option C (native JDBC) additionally depends on:
├─ Stable binary protocol spec (this document)
├─ pgjdbc source reference implementation
└─ Dedicated Java engineer

Option B (native ODBC) additionally depends on:
├─ Option C stable
├─ Dedicated C/C++ ODBC engineer
└─ WHQL / iODBC certification budget
```

---

## Quick-Start: ODBC via psqlODBC (Today)

**Windows:**
1. Download [psqlODBC](https://www.postgresql.org/ftp/odbc/versions/msi/) (64-bit MSI).
2. Create a System DSN: Driver = `PostgreSQL Unicode(x64)`, Server = `your-server`, Port = `5433`, Database = `appdb`, SSL Mode = `require`.
3. Test connection — enter credentials.

**Linux (unixODBC):**
```bash
sudo apt install odbc-postgresql unixodbc-dev
# Add to /etc/odbcinst.ini:
# [PostgreSQL]
# Driver = /usr/lib/x86_64-linux-gnu/odbc/psqlodbcw.so

# Add to ~/.odbc.ini:
# [SharpCoreDB]
# Driver = PostgreSQL
# Server = your-server
# Port   = 5433
# Database = appdb
# SSLmode = require

isql -v SharpCoreDB username password
```

## Quick-Start: JDBC via pgjdbc (Today)

```java
// Maven: org.postgresql:postgresql:42.7.x
String url = "jdbc:postgresql://your-server:5433/appdb?ssl=true&sslmode=require";
try (Connection conn = DriverManager.getConnection(url, "username", "password")) {
    Statement stmt = conn.createStatement();
    ResultSet rs   = stmt.executeQuery("SELECT 1 AS result");
    while (rs.next()) {
        System.out.println(rs.getInt("result"));
    }
}
```

---

*This document is version-aligned to SharpCoreDB v1.6.0. Review at each major version milestone.*
