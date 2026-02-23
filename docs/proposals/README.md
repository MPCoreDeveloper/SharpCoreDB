# Dotmim.Sync Provider — Complete Proposal Summary

**Status:** ✅ Proposal & Implementation Plan Complete  
**Documents:** 
- `docs/proposals/DOTMIM_SYNC_PROVIDER_PROPOSAL.md` — Technical architecture
- `docs/proposals/DOTMIM_SYNC_IMPLEMENTATION_PLAN.md` — Phased execution plan
- `docs/proposals/ADD_IN_PATTERN_SUMMARY.md` — Add-in pattern alignment

---

## At a Glance

### What is it?

A **Dotmim.Sync provider for SharpCoreDB** that enables:

- ✅ **Bidirectional sync** between SharpCoreDB and any Dotmim.Sync provider (PostgreSQL, SQL Server, SQLite, MySQL)
- ✅ **Multi-tenant filtering** for local-first AI agent architectures
- ✅ **Encryption transparency** — at-rest encryption is invisible to the provider
- ✅ **Full SQLite compatibility** — SharpCoreDB must support all SQLite syntax/behavior users rely on (never less)

### Why?

**Use Case:** Hybrid AI Architecture
```
Server (PostgreSQL)          Client (SharpCoreDB local)     Local Agent
    ↓                              ↓                              ↓
Multi-tenant data       Syncs tenant subset            Vector search + graphs
(global knowledge)      (50KB-100MB decrypted local)   (zero latency, full privacy)
```

### Key Stats

| Aspect | Value |
|---|---|
| **Estimated Effort** | 5-7 weeks |
| **Target Version** | SharpCoreDB.Provider.Sync v1.0.0 |
| **Package ID** | `SharpCoreDB.Provider.Sync` |
| **Target Framework** | .NET 10, C# 14 |
| **License** | MIT |

---

## Architecture Highlights

### 1. Change Tracking (Shadow Tables + Triggers)

```sql
-- For each tracked table, create a shadow tracking table
CREATE TABLE customers_tracking (
    pk_customer_id INTEGER PRIMARY KEY,
    update_scope_id TEXT,
    timestamp BIGINT,
    sync_row_is_tombstone INTEGER,
    last_change_datetime TEXT
);

-- 3 triggers per table: AFTER INSERT/UPDATE/DELETE
-- Automatically capture changes for Dotmim.Sync to detect
```

✅ **Why this approach?**
- Matches SQLite/MySQL Dotmim.Sync provider pattern
- Works with all SharpCoreDB storage modes (columnar, page-based, hybrid)
- Leverages existing trigger system (v1.2+)

### 2. Encryption is Transparent

❌ **No encryption bridge needed!**

```
Disk (encrypted .scdb file)
    ↓ [CryptoService.Decrypt() in Storage.ReadWrite]
Memory (plaintext rows)
    ↓ [ITable.Select() / ExecuteQuery()]
Sync Provider (reads plaintext, like any other consumer)
    ↓ [HTTPS transport layer]
Server (stores in its own format)
```

SharpCoreDB's encryption-at-rest is automatic and invisible to the sync provider.

### 3. Add-In Pattern (Like YesSql)

```csharp
// Program.cs
services.AddSharpCoreDBSync("Path=C:\\data\\local.scdb;Password=secret");

// Later
var provider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
var agent = new SyncAgent(provider, serverProvider);
await agent.SynchronizeAsync(setup);
```

✅ **Benefits:**
- Consistent with SharpCoreDB ecosystem (YesSql pattern)
- Optional installation via NuGet
- Proper DI integration
- Independent versioning (core v1.3.5 ≠ provider v1.0.0)

---

## Phased Implementation (6 Phases)

| Phase | Duration | Focus |
|---|---|---|
| **Phase 0** | 1 week | Core engine prerequisites (GUID support, trigger validation, schema API, timestamp function) |
| **Phase 1** | 1 week | Provider skeleton + DI integration (compilable, stubs for builders) |
| **Phase 2** | 2 weeks | Change tracking system (shadow tables, triggers, scope metadata) |
| **Phase 3** | 1-2 weeks | Sync adapter (select changes, apply changes, bulk operations, conflict resolution) |
| **Phase 4** | 1-2 weeks | Testing + integration (unit tests, SQLite roundtrip, 10K row benchmark) |
| **Phase 5** | 0.5 weeks | Multi-tenant filtering support |
| **Phase 6** | 0.5 weeks | NuGet packaging, documentation, samples |

**Total: 5-7 weeks**

### Key Milestones

1. **M1 (Week 2)**: Provider compiles, `SyncAgent` accepts it
2. **M2 (Week 2)**: DI registration works (`AddSharpCoreDBSync()`)
3. **M3 (Week 4)**: Change tracking via triggers functional
4. **M4 (Week 5)**: One-way sync (server → client) works
5. **M5 (Week 6)**: Bidirectional sync + conflict resolution
6. **M6 (Week 8)**: Multi-tenant filtered sync
7. **M7 (Week 9)**: Release candidate (all tests, docs, NuGet ready)

---

## Project Structure

```
src/SharpCoreDB.Provider.Sync/              ← Add-in project
  SharpCoreDBSyncProvider.cs
  Builders/
    ├─ SharpCoreDBDatabaseBuilder.cs
    ├─ SharpCoreDBTableBuilder.cs
    └─ SharpCoreDBScopeInfoBuilder.cs
  Adapters/
    ├─ SharpCoreDBSyncAdapter.cs
    └─ SharpCoreDBObjectNames.cs
  Metadata/
    ├─ SharpCoreDBDbMetadata.cs
    └─ SharpCoreDBSchemaReader.cs
  ChangeTracking/
    ├─ ChangeTrackingManager.cs
    ├─ TrackingTableBuilder.cs
    └─ TombstoneManager.cs
  Extensions/
    ├─ SyncServiceCollectionExtensions.cs  ← DI
    └─ SyncProviderFactory.cs

tests/SharpCoreDB.Provider.Sync.Tests/
  ├─ ChangeTrackingTests.cs
  ├─ TypeMappingTests.cs
  ├─ ConflictResolutionTests.cs
  ├─ FilteredSyncTests.cs
  └─ Integration/
      ├─ BasicSyncIntegrationTests.cs
      ├─ EncryptedSyncTests.cs
      └─ MultiTenantSyncTests.cs
```

---

## Technical Decisions

| # | Decision | Why |
|---|---|---|
| TD-1 | Shadow tables + triggers (not WAL-based) | Proven pattern, works with all storage modes |
| TD-2 | Client-side first (not server) | Primary use case is local-first; server later |
| TD-3 | No encryption adapter | Encryption is at-rest; transparent at API layer |
| TD-4 | Long ticks for timestamps | Monotonic, no timezone issues, Dotmim.Sync standard |
| TD-5 | Separate tracking tables | Clean separation, no user table pollution |
| TD-6 | Pin Dotmim.Sync 1.1.x | Stable; 2.0 is preview |
| TD-7 | Add-in pattern (Provider.Sync) | Ecosystem consistency, optional installation |
| TD-8 | Use DI for factory | MS.Extensions.DependencyInjection standard |
| TD-9 | Reuse ADO.NET provider | Dotmim.Sync uses DbConnection; don't reinvent |

---

## Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Triggers can't execute cross-table DML | Low | Critical | Verify Phase 0; fallback to direct API calls |
| JOIN performance with large tables | Medium | High | Hash indexes; benchmarking; tombstone cleanup |
| Concurrent sync + local writes → deadlocks | Low | High | MVCC reads; batch update transactions |
| DI misconfiguration by users | Low | Medium | Examples, XML docs, sample project |
| Dotmim.Sync 2.0 API breaks | Medium | High | Pin 1.1.x; abstract interface for migration |

---

## Success Criteria

✅ **Bidirectional sync** works (SharpCoreDB ↔ SQLite)  
✅ **Filtered sync** works (multi-tenant parameters)  
✅ **Conflict resolution** works (server-wins, client-wins, custom)  
✅ **Encryption transparent** (encrypted DB syncs same as unencrypted)  
✅ **Performance target** (10K rows in < 5 seconds)  
✅ **Reliability** (WAL crash recovery, MVCC isolation)  

---

## Future Roadmap (Post v1.0)

| Feature | Priority | Notes |
|---|---|---|
| Zero-Knowledge Sync | High | E2E encrypted where server never sees plaintext |
| Server-Side Provider | Medium | SharpCoreDB ↔ SharpCoreDB sync |
| WebSocket Transport | Medium | Real-time instead of poll-based |
| Selective Column Sync | Medium | Sync only specific columns |
| Compression | Low | gzip/brotli for metered connections |
| Offline Queue | Low | Queue changes while offline |
| Vector Sync | Low | Sync embeddings for local AI |
| Graph Sync | Low | Sync graph edges/nodes |

---

## Resources Required

- **1 Senior .NET Developer** (SharpCoreDB + Dotmim.Sync experience)
- **1 Code Reviewer** (Dotmim.Sync + DI patterns)
- **Access to:** PostgreSQL, SQL Server for integration testing
- **CI/CD:** GitHub Actions for automated tests + NuGet publish

---

## Next Actions

1. ✅ **Planning Complete** (this document + detailed plans)
2. ⏳ **Phase 0:** Start prerequisite work in core engine
3. ⏳ **Phase 1:** Create `SharpCoreDB.Provider.Sync` project
4. ⏳ **Phases 2-6:** Execute per implementation plan

---

## Documents

📄 **Core Proposal:** [DOTMIM_SYNC_PROVIDER_PROPOSAL.md](./DOTMIM_SYNC_PROVIDER_PROPOSAL.md)
- Architecture overview
- Change tracking design
- Type mapping
- Project structure

📋 **Implementation Plan:** [DOTMIM_SYNC_IMPLEMENTATION_PLAN.md](./DOTMIM_SYNC_IMPLEMENTATION_PLAN.md)
- Phase-by-phase breakdown (7 phases, 6 sections per phase)
- Deliverables per phase
- Technical decisions
- Milestones
- Risk register

🔄 **Add-In Pattern Guide:** [ADD_IN_PATTERN_SUMMARY.md](./ADD_IN_PATTERN_SUMMARY.md)
- Naming conventions
- DI integration
- NuGet metadata
- Alignment with YesSql pattern

---

## Questions?

Refer to the detailed proposal documents for:
- **Architecture deep-dive** → `DOTMIM_SYNC_PROVIDER_PROPOSAL.md`
- **Execution roadmap** → `DOTMIM_SYNC_IMPLEMENTATION_PLAN.md`
- **Add-in pattern FAQ** → `ADD_IN_PATTERN_SUMMARY.md`
