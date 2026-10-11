<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="180"/>

# SharpCoreDB

**High-Performance Encrypted Database for .NET 11**  
**Embedded engine + production gRPC server in one ecosystem**

[![GitHub stars](https://img.shields.io/github/stars/MPCoreDeveloper/SharpCoreDB?style=for-the-badge)](https://github.com/MPCoreDeveloper/SharpCoreDB/stargazers)
[![NuGet](https://img.shields.io/nuget/v/SharpCoreDB?style=for-the-badge)](https://www.nuget.org/packages/SharpCoreDB)
[![NuGet downloads](https://img.shields.io/nuget/dt/SharpCoreDB?style=for-the-badge)](https://www.nuget.org/packages/SharpCoreDB)
[![.NET 11](https://img.shields.io/badge/.NET-11-blue?style=for-the-badge)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![Platforms](https://img.shields.io/badge/Platforms-Windows%20%7C%20Linux%20%7C%20macOS-informational?style=for-the-badge)](#)
[![Roadmap](https://img.shields.io/badge/Roadmap-View%20Plan-blueviolet?style=for-the-badge)](./ROADMAP.md)
[![SonarCloud Quality Gate](https://img.shields.io/sonar/quality_gate/MPCoreDeveloper_SharpCoreDB?server=https%3A%2F%2Fsonarcloud.io&style=for-the-badge&logo=sonarcloud)](https://sonarcloud.io/dashboard?id=MPCoreDeveloper_SharpCoreDB)
</div>

---

SharpCoreDB is for .NET teams that want **SQLite-like simplicity**, **enterprise-grade security**, and **server-scale capabilities** without leaving the .NET ecosystem.

Use it when you need:
- Fast embedded storage with **AES-256-GCM encryption** and ACID guarantees
- A secure network database via **gRPC (HTTP/2 + HTTP/3)**
- Built-in **vector search**, **advanced analytics**, and **GraphRAG/graph algorithms**
- A production-focused stack validated by the **core suite on this line (1,998 tests, 0 failed,
  measured 2026-10-11)**, the **RC.3 sweep of 2,897 tests across 16 suites**, and **backward
  compatibility**

> **Current release: v2.1.0 (2026-10-10) — the .NET 11 / C# 15 line, on `master`.** **Published on
> NuGet.org** (all 27 packable packages) from `master`, `net11.0` only. Highlights: the fixed-width inline capacity
> ships at 24 bytes by default on both storage paths (worth **+34 %** on the batched multi-row INSERT
> shape and **0,63× → 0,87×** on the tracked fair-PK INSERT ratio), the overflow arena appends a row's payloads in one call instead of one
> file open per value, and in-place UPDATE/DELETE with deferred index maintenance now run the
> fixed-width fair-PK layout **1,2–1,5× ahead of the legacy variable-length layout** on UPDATE and DELETE.
> Validated by **2,897 tests across 16 suites (0 failed, 16 skipped)** and the write-path regression
> gate (PASSED). Benchmarks re-measured and republished on 2026-10-11 (`92794084`), every ratio
> same-run: on the default-posture fair-PK shape SharpCoreDB is **INSERT 1,17× (1,03–1,27) and READ
> 1,13× (1,12–1,15) ahead** of SQLite and **UPDATE 0,53× (0,42–0,53) and DELETE 0,47× (0,46–0,62)**
> of it, and per-record encryption costs the otherwise-identical fixed-width arm **1,2–1,9×**. This
> corrects the earlier "ahead on UPDATE/DELETE" reading, which was taken while the benchmark left the
> *SQLite* arm allocating a command and two parameters per row — SQLite's own PK UPDATE/DELETE
> references rose **3,05×** (288.108 → 878.557 / 385.116 → 1.172.704) once the comparator held one
> prepared command, so that band is superseded, not better. Full notes:
> [`docs/2.1.0-RC.3_WHAT_CHANGED.md`](docs/2.1.0-RC.3_WHAT_CHANGED.md) — the RC.3 validation record
> (its own install snippets pin the RC.3 packages; the shipped line is `2.1.0`);
> the promotion itself re-verified the core suite at **1995 / 0 failed** and is documented in
> [`docs/net11/PROMOTION_TO_MAIN.md`](docs/net11/PROMOTION_TO_MAIN.md).

> **Previous line — net10.0 / C# 14 stable, parked on `release/v2.0`: v2.0.0.3 (2026-09-12)** — the v2.x engine line: fixed-width record layout is the
> default for new PK tables, UPDATE/DELETE run in-place with commit-time tombstones, and a reopen
> data-integrity hardening batch closed silent-data-loss edge cases (empty-value overflow reload,
> single-file fixed-width arena markers, legacy 1.x delete-after-update purge).
>
> **Performance (fair-PK, median-of-3, tuned harness):** UPDATE **~163–245K ops/s**, DELETE
> **~106–172K ops/s**, INSERT **~125–152K ops/s**, READ **~72–110K ops/s** (SQLite reference:
> UPDATE ~270–315K, DELETE ~353–420K, INSERT ~186–190K, READ ~95–107K). Honest default-config
> caveat: the pure default config runs ~1.3–1.6x slower because the file-level wrapper still pays
> AES work while per-record at-rest encryption is off (`NoEncryptMode` root cause, P3d). Full
> note: the SQLite references in this block are the pre-comparator-correction ones described above,
> so read them as that line's history rather than as today's reference. Full report:
> [`docs/2.0.0.2_WHAT_CHANGED.md`](docs/2.0.0.2_WHAT_CHANGED.md) and
> [`docs/benchmarks/default-config-pk.md`](docs/benchmarks/default-config-pk.md).

> Full documentation: **`docs/INDEX.md`** · Manual: **`docs/manual/README.md`** · Performance: **`docs/manual/performance.md`**

---

> **🖥️ Looking for the graphical UI?** The `SharpCoreDB.Viewer` / `SharpCoreDB.WebViewer` projects have
> moved to the standalone repo **[MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS)**.
> This repository now contains only the engine, providers, and server. See [docs/SCDMS.md](docs/SCDMS.md).

## Requirements and the two release lines

| | **v2.1.0 — this line (`master`)** | **v2.0.0.3 — parked line (`release/v2.0`)** |
|---|---|---|
| Target framework | `net11.0` only (every project under `src`) | `net10.0` |
| Language | C# 15 (`LangVersion=preview`, always on) | C# 14 |
| SDK | .NET 11 RC, pinned in `global.json` (`11.0.100-rc.1.26425.128`, `allowPrerelease`, `rollForward: latestPatch`) | .NET 10 (`10.0.x`) |
| Pipeline | `.github/workflows/ci.yml` | `.github/workflows/ci-net10.yml` |
| Packages | `2.1.0` — the 27 packable projects under `src` | `2.0.0.3` |
| Status | current release, receives new work | servicing only |

- **Pick one line per solution.** The package references are line-specific: a `net10.0` project uses the
  `2.0.0.x` packages, a `net11.0` project the `2.1.0` ones. Do not mix them in one solution.
- **The .NET 11 SDK is required to build** — the `global.json` pin is deliberate and `dotnet test` is not
  this repository's runner (Microsoft.Testing.Platform is; run the built test executable, or see
  [`docs/INDEX.md`](docs/INDEX.md)).
- **Runtimes shipped:** `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.
- **SQLite compatibility:** supported SQLite syntax and features are never fewer than SQLite's; extra
  capability is fine. `INTEGER PRIMARY KEY` shapes are the like-for-like comparison in the numbers below.
- **SIMD standard:** `System.Runtime.Intrinsics` only (`AVX-512 → AVX2 → SSE2 → scalar`), never
  `System.Numerics.Vector<T>` — see [`.github/SIMD_STANDARDS.md`](.github/SIMD_STANDARDS.md).
- **Encryption and durability are never traded for speed:** AES-256-GCM at rest, WAL semantics and the
  crash-consistency / reopen round-trip matrix are release gates.

## Why SharpCoreDB?

- **One stack, two deployment models**: embedded and server mode
- **Performance-first design**: SIMD acceleration, zero-allocation read paths, optimized query paths
- **Security by default**: TLS 1.2+, JWT, optional mTLS, RBAC, encrypted single-file storage
- **Modern .NET 11-native**: C# 15, optional ecosystem packages, production-ready modules

### Quick comparison

| Capability | SharpCoreDB | LiteDB | SQLite | RavenDB / MongoDB |
|---|---|---|---|---|
| .NET-native embedded experience | ✅ First-class | ✅ | ⚠️ via provider wrappers | ❌ network-first |
| Built-in encrypted single-file DB (AES-256-GCM) | ✅ | ⚠️ limited/variant approaches | ❌ (extensions/custom setup) | ❌ |
| Built-in gRPC server mode in same ecosystem | ✅ | ❌ | ❌ | ⚠️ different server architecture |
| Vector search + GraphRAG tooling | ✅ | ❌ | ⚠️ extension-dependent | ⚠️ feature varies by product/tier |
| Advanced analytics + SIMD focus | ✅ | ⚠️ basic querying | ⚠️ strong SQL, fewer .NET-specific SIMD paths | ⚠️ server-side analytics patterns |
| Optional Event Sourcing / CQRS packages | ✅ | ❌ | ❌ | ⚠️ usually external patterns |

---

## What's new in v2.1.0 (current release)

The v1.x benchmark gap (point reads/updates/deletes **16–52x behind SQLite** because every update
appended a new row) is closed as an engine behaviour. The numbers below are re-measured on
**2026-10-11** (`92794084`, v2.1.0 / .NET 11) on the **fair-PK harness (median of 3 runs, `id INTEGER
PRIMARY KEY`, ascending-PK batches)**, and every ratio is taken against **that run's own SQLite arm** —
this project reports ratios, never lone absolutes, because SQLite's own reference drifts between runs.

**Default posture, `--pk-default` (pure `DatabaseConfig`: encryption on, no harness flags).** Ratio is
SharpCoreDB ÷ SQLite, so **> 1,00x means SharpCoreDB is ahead**; the range is the paired per-rep range,
and one that straddles 1,00x means the cell is inside this box's noise:

| Operation (ops/s) | **SharpCoreDB v2.1.0 (default, encrypted)** | SQLite (same run) | ratio |
|---|---:|---:|---:|
| **INSERT** | **204.263** | 174.469 | **1,17x** (1,03–1,27) ✅ |
| **READ** | **134.634** | 116.916 | **1,13x** (1,12–1,15) ✅ |
| **UPDATE** | **398.709** | 773.323 | **0,53x** (0,42–0,53) |
| **DELETE** | **509.941** | 1.085.140 | **0,47x** (0,46–0,62) |

**Record-layout A/B on the tuned `--pk` harness** — same workload, three interchangeable layouts,
plaintext so the layout is the only variable: the **fixed-width** layout is **1,2–1,5× ahead of the
legacy variable-length layout** on UPDATE (534–629K vs 360–372K) and DELETE (687–716K vs 425–466K)
while level on INSERT/READ. Per-record encryption costs the same fixed-width arm **1,2–1,9×**
(INSERT 1,18–1,25× · READ 1,22–1,32× · UPDATE 1,83–1,91× · DELETE 1,41–1,57×).

Honest notes: these are ascending-PK batches on the fixed-width Columnar fast path — other query
shapes fall back to correct-but-slower generic paths. SQLite's PK UPDATE/DELETE references are really
~0,80M / ~1,00M ops/s on this box; the ~270–315K / ~353–420K references published earlier were
measured while the harness left the SQLite arm allocating a command and two parameters per row, so
they understate SQLite and are superseded. Full tables and the API ladder:
**[Performance Guide](docs/manual/performance.md)** · **[`docs/BENCHMARK_RESULTS.md`](docs/BENCHMARK_RESULTS.md)**. Roadmap:
**[`docs/performance/V2_PERFORMANCE_PLAN.md`](docs/performance/V2_PERFORMANCE_PLAN.md)**.

Headline changes, grouped by area. Everything below is in the shipped `2.1.0` packages — the
`2.1.0-RC.3` line plus the work that landed between that tag and `v2.1.0`.

**Storage and record layout**

- ⚡ **Fixed-width record layout is the default for new PK tables** — constant-stride records with an
  out-of-line `.ovf` overflow arena; UPDATE/DELETE are **in-place overwrites** with commit-time
  tombstone markers (no full-file rewrite)
- ⚡ **The inline capacity ships at 24 bytes on both storage paths** — multi-file and single-file
  (`.scdb`) — worth **+34 %** on the batched multi-row INSERT shape and **0,63× → 0,87×** on the tracked
  fair-PK INSERT ratio; 32 measures inside the noise at +20 % of the file, so 24 is the knee
- ⚡ **Existing databases upgrade their record layout themselves on the first writable open** — no
  rewrite, no export/import, and no format break (the capacity is part of the persisted layout)
- ⚡ **Single-file (`.scdb`) mode now uses the same record layout as multi-file mode** (it never had) and
  honours the configured inline capacity
- ⚙️ **`.scdb` growth is configurable** — `DatabaseConfig.SingleFileMinExtensionBytes`, minimum **1 MiB**
  (was 10 MiB): every small database drops from 14,7 MB to **6,4 MB**
- 🗜️ **Block-level Brotli/GZip compression** for single-file (`.scdb`) storage — transparent, per-block,
  applied before encryption / removed after decryption (#344)
- 🛡️ **Envelope encryption + full at-rest metadata encryption** — password-based per-file DEK
  (PBKDF2-HMAC-SHA256), encrypted block registry / FSM / WAL, key & password rotation APIs (#341 follow-on)
- ⚙️ **Configurable metadata region sizing** — `FsmSizePages` / `BlockRegistrySizePages` /
  `TableDirectorySizePages` remove the 512 MB single-file ceiling (#345)

**SQL semantics (SQLite parity)**

- ⚡ **A declared column type is honoured the way SQLite honours it** — `DOUBLE`/`FLOAT` are REAL
  columns, `INT`/`SMALLINT`/`INT8` are INTEGER columns, resolved by a single type map
- ⚡ **`DELETE FROM t` with no `WHERE` deletes every row**, as SQLite does — it used to be refused
- ⚡ **`INSERT` with an explicit column list writes the named order** through every batch path, not
  positionally
- ⚡ **`information_schema` is implemented in the engine**, so every protocol answers it
- ⚡ **The async entry points validate like their synchronous twins** — `ExecuteSQLAsync` validates,
  `ExecuteBatchSQLAsync` composes inside a caller's transaction

**Performance and allocation (this line)**

- ⚡ **Index maintenance left the write path** — automatic hash indexes register **on demand**, the first
  time an operation filters on a column, instead of one per column at `CREATE TABLE`; arm-B/C INSERT
  reaches **1,60× / 1,26×**
- ⚡ **Single-key index operations no longer dispatch through reflection** (`IndexManager`,
  `GenericHashIndex`, `Table.BTreeIndexing`)
- ⚡ **Batch INSERT's hash-index maintenance no longer rebuilds its key map per batch** —
  `hash-index` 4.611.325 → 3.530.478 B per 10.000-row call
- ⚡ **The fixed-width codec allocates no `List<>` scratch per row** (706 → 547 B/row on the fair-PK
  INSERT arm), and the encrypted UPDATE locate no longer copies every ciphertext frame before decrypting
  it (431 → 279 B/record)
- ⚡ **Zstd blocks no longer round-trip through a stream** — 2.480 → 0 B of managed allocation per 4 KB block
- 🧪 **CI is split by line instead of by SDK** (`ci.yml` here, `ci-net10.yml` on the parked branch), and
  the 18 % coverage floor is measured by a dedicated job rather than carried unmeasured

**Server**

- 🔧 **A plain client disconnect is no longer logged as an authentication failure** — a peer that aborts
  its socket (TCP reset, Winsock 10053/10054) is now reported as what it is
- 🔧 **The server image is net11-aware** (`sdk:11.0` / `aspnet:11.0`)

### Carried from the v2.0.x line

- ⚡ **Single-pass contiguous UPDATE/DELETE (B8/B9)** — strictly ascending `pk = literal` batches
  resolved as ONE contiguous range with decode verification (no per-key B-tree search)
- ⚡ **Zero-allocation reads** — first-class `ExecuteQueryStruct(sql, params)` API + `VariableLengthSchema` cache
- ⚡ **In-place UPDATE patches (WP11)** — single-row UPDATE overwrites changed fields at cached fixed column offsets
  (no deserialize → re-serialize round trip); growing records relocate across pages with correct PK/hash re-pointing
- ⚡ **Unified DELETE core (WP12)** — one shared delete path with key-only hash index cleanup
- ⚡ **Exact-size row serialization (WP13)** — one allocation per insert/update (no `ArrayPool.Rent` + `ToArray()` double allocation)
- ⚡ **Zero-reparse SELECT fast path** — `SimpleSelectPlan` resolves simple point-lookup plans without re-lexing
- ⚡ **SIMD WHERE filters** — tiered `System.Runtime.Intrinsics` kernels (AVX-512 → AVX2 → SSE2 → scalar,
  mask-based, with the 512-bit tier gated at 1.024 elements) for `int32` and `double` columns;
  `System.Numerics.Vector<T>` is deliberately **not** used, so codegen is deterministic under NativeAOT
- ⚡ **Compiled regexes everywhere** on hot paths; regex-free `NormalizeSql`; cached DI
- ⚡ **Removed hidden `D:\*.log` debug writes** that throttled every SELECT/execute/transaction/INSERT
- 🛡️ **Native AOT readiness** — AOT-safe `TypeConverter`, `Option<T>` reader, `[RequiresDynamicCode]` annotations,
  source-generated DTOs/JSON (`tools/SharpCoreDB.AotSmoke` publishes + runs, exit 0). Measured for value as
  well: the harness's **AOT ÷ JIT** A/B leaves native AOT **at or behind the JIT on 12 of 16 cells**, so AOT is
  a deployment option (image size, startup, no JIT) and **not** a speed lever here —
  [`docs/BENCHMARK_RESULTS.md`](docs/BENCHMARK_RESULTS.md)
- 🛡️ **Reopen data-integrity hardening (2.0.0.2)** — empty-value overflow reload, single-file
  fixed-width arena markers, and legacy 1.x delete-after-update purge (`ReopenRoundTripMatrixTests`)
- ✅ **Validated by the core suite on this line (1,998 tests, 0 failed, measured 2026-10-11)** — the
  **RC.3 sweep covered 2,897 tests across 16 suites** (CQRS 64, VectorSearch 248, EF Core 116, Provider.Sync 135, Search 58, HybridSearch 6, Functional.Linq2DB 24, …)

## Quick Start (under 30 seconds)

### 1) Embedded mode

```bash
dotnet add package SharpCoreDB --version 2.1.0
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// DI-wired factory (recommended):
var services = new ServiceCollection();
services.AddSharpCoreDB();
using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

using var db = factory.Create(@"C:\data\appdb", masterPassword: "s3cret!");
db.ExecuteSQL("CREATE TABLE Users (Id INT, Name TEXT)");
db.ExecuteBatchSQL([
    "INSERT INTO Users VALUES (1, 'Ada')",
    "INSERT INTO Users VALUES (2, 'Linus')"
]);
db.Flush();
```

> ⚡ v2.x hot paths: read point lookups with `db.FindByPrimaryKey("Users", key: 1)` (Direct API,
> no SQL parsing) and batch-load with `db.InsertBatch("Users", rows)`. See the [Performance Guide](docs/manual/performance.md).

### 2) Server mode (gRPC-first)

```bash
dotnet run --project src/SharpCoreDB.Server -c Release
```

Health endpoint: `https://localhost:8443/health`  
gRPC endpoint: `https://localhost:5001`

Install client/server packages:

```bash
dotnet add package SharpCoreDB.Server.Core --version 2.1.0
dotnet add package SharpCoreDB.Client --version 2.1.0
```

> The server host is a standalone executable (`src/SharpCoreDB.Server`), distributed as the
> `sharpcoredb-server` binary and the `ghcr.io/mpcoredeveloper/sharpcoredb-server` container image —
> it is **not** published as the `SharpCoreDB.Server` NuGet package. The packable server-side
> libraries are `SharpCoreDB.Server.Core` and `SharpCoreDB.Server.Protocol`.


---

## Previous releases

- **v2.0.0.3** — the net10.0 / C# 14 stable line, parked on `release/v2.0` and built by `ci-net10.yml`
  (the complete package list is above). Engine foundation it carries: fixed-width record layout by default
  for new PK tables, in-place UPDATE/DELETE with commit-time tombstones, and the reopen data-integrity
  hardening batch.
- **v1.9.6** — fixed the critical `WHERE col IN (...)` regression (Issue #339); SonarCloud quality-gate onboarding; 2,404 tests passing.
- **v1.9.5** — full version synchronization across all packages; token-aware named-parameter binding fix (Issue #336); server parameter forwarding fix (Issue #337); standards-compliant Crockford Base32 ULID encoding; new `SharpCoreDB.Functional.Linq2DB` adapter.
- **v1.9.1** — EF Core provider with Guid-keyed entity CRUD + reliable two-query relationship materialization; 22/22 EF Core integration tests; runnable EF Core demo; version alignment across 25+ packages.

> Full history: [`docs/CHANGELOG.md`](docs/CHANGELOG.md)

---

## Available NuGet packages

### v2.1.0 — current release (`master`, net11.0 / C# 15 preview)

```bash
# Core
dotnet add package SharpCoreDB --version 2.1.0

# Server/client
dotnet add package SharpCoreDB.Server.Core --version 2.1.0
dotnet add package SharpCoreDB.Server.Protocol --version 2.1.0
dotnet add package SharpCoreDB.Client --version 2.1.0
dotnet add package SharpCoreDB.Client.Protocol --version 2.1.0
dotnet add package SharpCoreDB.Data.Provider --version 2.1.0

# Engines and extensions
dotnet add package SharpCoreDB.Analytics --version 2.1.0
dotnet add package SharpCoreDB.VectorSearch --version 2.1.0
dotnet add package SharpCoreDB.Search --version 2.1.0
dotnet add package SharpCoreDB.HybridSearch --version 2.1.0
dotnet add package SharpCoreDB.Graph --version 2.1.0
dotnet add package SharpCoreDB.Graph.Advanced --version 2.1.0
dotnet add package SharpCoreDB.Distributed --version 2.1.0
dotnet add package SharpCoreDB.Provider.Sync --version 2.1.0
dotnet add package SharpCoreDB.Provider.YesSql --version 2.1.0
dotnet add package SharpCoreDB.EntityFrameworkCore --version 2.1.0
dotnet add package SharpCoreDB.Extensions --version 2.1.0
dotnet add package SharpCoreDB.Identity --version 2.1.0
dotnet add package SharpCoreDB.Serilog.Sinks --version 2.1.0

# Optional architecture packages
dotnet add package SharpCoreDB.EventSourcing --version 2.1.0
dotnet add package SharpCoreDB.Projections --version 2.1.0
dotnet add package SharpCoreDB.CQRS --version 2.1.0

# Optional functional adapters
dotnet add package SharpCoreDB.Functional --version 2.1.0
dotnet add package SharpCoreDB.Functional.Dapper --version 2.1.0
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 2.1.0
dotnet add package SharpCoreDB.Functional.Linq2DB --version 2.1.0

# Hosting and tooling
dotnet add package SharpCoreDB.Aspire.Hosting --version 2.1.0
```

> The 27 commands above are the complete packable set of this line. **`2.1.0` is a stable release**, so
> NuGet selects it by default for a `net11.0` project; a `net10.0` project resolves to the `2.0.0.3` line
> instead. Pin the version explicitly (as above) when you want to be certain, and tick *Include
> prerelease* only if you are deliberately testing an RC build.
>
> The **server host is a standalone executable**, not a NuGet package: run
> `dotnet run --project src/SharpCoreDB.Server -c Release` or use the
> `ghcr.io/mpcoredeveloper/sharpcoredb-server` container image. The packable server-side libraries are
> `SharpCoreDB.Server.Core` (hosting) and `SharpCoreDB.Server.Protocol` (wire contracts).

### v2.0.0.3 — stable (parked on `release/v2.0`, net10.0 / C# 14)

```bash
# Core
dotnet add package SharpCoreDB --version 2.0.0.3

# Server/client
dotnet add package SharpCoreDB.Server.Core --version 2.0.0.3
dotnet add package SharpCoreDB.Client --version 2.0.0.3

# Engines and extensions
dotnet add package SharpCoreDB.Analytics --version 2.0.0.3
dotnet add package SharpCoreDB.VectorSearch --version 2.0.0.3
dotnet add package SharpCoreDB.Graph --version 2.0.0.3
dotnet add package SharpCoreDB.Graph.Advanced --version 2.0.0.3
dotnet add package SharpCoreDB.Distributed --version 2.0.0.3
dotnet add package SharpCoreDB.Provider.Sync --version 2.0.0.3
dotnet add package SharpCoreDB.EntityFrameworkCore --version 2.0.0.3
dotnet add package SharpCoreDB.Extensions --version 2.0.0.3

# Optional architecture packages
dotnet add package SharpCoreDB.EventSourcing --version 2.0.0.3
dotnet add package SharpCoreDB.Projections --version 2.0.0.3
dotnet add package SharpCoreDB.CQRS --version 2.0.0.3

# Optional functional adapters
dotnet add package SharpCoreDB.Functional --version 2.0.0.3
dotnet add package SharpCoreDB.Functional.Dapper --version 2.0.0.3
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 2.0.0.3
dotnet add package SharpCoreDB.Functional.Linq2DB --version 2.0.0.3
```

---

## Important documentation links

- **Manual (start here):** [`docs/manual/README.md`](docs/manual/README.md) — full feature manual with index
- **Performance Guide:** [`docs/manual/performance.md`](docs/manual/performance.md) — when SharpCoreDB is fastest + measured v2.x numbers
- **Roadmap:** [`ROADMAP.md`](./ROADMAP.md) — shipped features, near-term plans, long-term vision
- Documentation hub: `docs/INDEX.md`
- Feature matrix: `docs/FEATURE_MATRIX.md`
- Server docs: `docs/server/README.md` · Server quick start: `docs/server/QUICKSTART.md`
- GraphRAG docs: `docs/graphrag/00_START_HERE.md`
- SQL dialect: `docs/manual/query.md`
- Migration docs: `docs/migration/README.md`
- Single-file SQL support and limits: `docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`
- EF Core provider docs: `src/SharpCoreDB.EntityFrameworkCore/README.md`, `src/SharpCoreDB.EntityFrameworkCore/USAGE.md`
- Functional adapters: `src/SharpCoreDB.Functional.Linq2DB/README.md` and sibling projects
- Optional architecture packages: `src/SharpCoreDB.EventSourcing/README.md`, `src/SharpCoreDB.Projections/README.md`, `src/SharpCoreDB.CQRS/README.md`
- Package publish/readme guidance: `NuGet.README.md` and per-package `src/<package>/NuGet.README.md`


---

## Contributing

Contributions are welcome. Please open an issue for ideas, bug reports, and feature proposals, or submit a PR directly.

---

## License

This project is licensed under the MIT License. See `LICENSE` for details.

---

**Made with ❤️ for the .NET community**




