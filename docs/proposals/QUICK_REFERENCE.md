# SharpCoreDB.Provider.Sync — Quick Reference

## The Pitch (One Minute)

**SharpCoreDB.Provider.Sync** is a Dotmim.Sync provider add-in that enables bidirectional synchronization between SharpCoreDB and any Dotmim.Sync-supported database.

**Primary use case:** Local-first AI agents — sync a tenant's subset of data locally, run vector search + graph queries with zero latency and full privacy on an encrypted local database.

**Compatibility requirement:** SharpCoreDB must remain **100% compatible with SQLite syntax and behavior** for all operations users could perform in SQLite. We may extend beyond SQLite, but **must never support less than SQLite**.

**Key insight:** Encryption is at-rest only; by the time the sync provider reads data, it's already decrypted. No special encryption bridge needed — the provider is just another consumer of the SharpCoreDB API.

---

## Core Technologies

| Component | Technology | Why |
|---|---|---|
| **Sync Framework** | Dotmim.Sync 1.1.x | Mature, .NET-native, supports multiple providers |
| **Data Access** | SharpCoreDB.Data.Provider (ADO.NET) | Reuse existing infrastructure |
| **Change Tracking** | Triggers + Shadow Tables | Proven pattern; works with all storage modes |
| **DI Pattern** | Microsoft.Extensions.DependencyInjection | Standard .NET pattern |
| **Encryption** | AES-256-GCM (at-rest) | SharpCoreDB's existing encryption |

---

## Architecture in 3 Steps

### 1. Change Tracking

```sql
-- Shadow table captures all changes
CREATE TABLE customers_tracking (
    pk_customer_id INTEGER PRIMARY KEY,
    timestamp BIGINT,           -- When changed
    sync_row_is_tombstone INT,  -- If deleted
    update_scope_id TEXT        -- Which sync instance
);

-- 3 triggers: AFTER INSERT/UPDATE/DELETE
-- Auto-populate the tracking table
```

### 2. Sync Provider

```csharp
public sealed class SharpCoreDBSyncProvider : CoreProvider
{
    // Implements abstract methods:
    // - SelectChanges() → query tracking table
    // - ApplyChanges() → batch insert/update/delete
    // - ProvisionTable() → create table + tracking + triggers
}
```

### 3. DI Integration

```csharp
// Program.cs
services.AddSharpCoreDBSync("Path=C:\\data\\local.scdb;Password=secret");

// Later
var provider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
```

---

## Implementation Timeline

```
Week 1:  Phase 0 — Core engine prerequisites
Week 2:  Phase 1 — Provider skeleton + DI
Week 3-4: Phase 2 — Change tracking system
Week 5-6: Phase 3 — Sync adapter (DML)
Week 7-8: Phase 4 — Testing + integration
Week 8:   Phase 5 — Filter support
Week 8-9: Phase 6 — Polish + NuGet
─────────────────────
Total: 5-7 weeks (1 dev)
```

---

## Key Features

✅ **Bidirectional sync** (push ↔ pull)  
✅ **Conflict resolution** (server-wins, client-wins, custom merge)  
✅ **Multi-tenant filtering** (sync only subset by tenant_id)  
✅ **Encrypted database** support (encryption is transparent)  
✅ **Bulk operations** (uses SharpCoreDB's InsertBatch, ExecuteBatchSQL)  
✅ **MVCC isolation** (sync reads don't block writers)  
✅ **Crash recovery** (WAL protection)  

---

## Encryption: The Key Insight

❌ **DON'T:** Create an encryption bridge  
✅ **DO:** Leverage SharpCoreDB's transparent decryption

```
.scdb file (encrypted on disk)
    ↓
Storage.ReadWrite.ReadBytes() calls CryptoService.Decrypt()
    ↓
ITable.Select() returns plaintext Dictionary<string, object>
    ↓
Sync Provider reads plaintext (like any other consumer)
    ↓
HTTPS transport encrypts for transit
```

**Result:** Encryption is invisible to the provider. No special code paths needed.

---

## Project Naming

| Artifact | Name | Pattern |
|---|---|---|
| **Project** | `SharpCoreDB.Provider.Sync` | Like `SharpCoreDB.Provider.YesSql` |
| **Namespace** | `SharpCoreDB.Provider.Sync` | Matches project |
| **NuGet Package** | `SharpCoreDB.Provider.Sync` | Matches project |
| **Version** | `1.0.0` | Independent from core |

---

## DI Registration

```csharp
// Single-line setup
services.AddSharpCoreDBSync(
    connectionString: "Path=C:\\data\\local.scdb;Password=secret",
    options: opts => {
        opts.EnableAutoTracking = true;
        opts.TombstoneRetentionDays = 30;
    }
);

// Internally registers:
// - SharpCoreDBSyncProvider (singleton)
// - SyncProviderFactory (singleton)
// - IChangeTrackingManager (singleton)
// - ITombstoneManager (singleton)
// - SyncProviderOptions (singleton)
```

---

## Usage Example

```csharp
// Setup
var clientProvider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
var serverProvider = new SqlSyncProvider(serverConnectionString);

// Define sync scope
var setup = new SyncSetup("customers", "orders", "products");

// Setup filters for multi-tenant
setup.Filters.Add("customers", "tenant_id");
setup.Filters.Add("orders", "tenant_id");

// Create agent and sync
var agent = new SyncAgent(clientProvider, serverProvider);
var parameters = new SyncParameters(("tenant_id", 42)); // Sync only tenant 42
var result = await agent.SynchronizeAsync(setup, parameters);

// Result includes:
// - Changes applied
// - Conflicts resolved
// - Duration/stats
```

---

## Success Metrics

| Metric | Target | Purpose |
|---|---|---|
| **Bidirectional Sync** | ✅ Works | Core functionality |
| **Filtered Sync** | ✅ Multi-tenant | Local-first use case |
| **Conflict Resolution** | ✅ 3+ policies | Production-ready |
| **Performance** | < 5 sec for 10K rows | Responsive |
| **Encryption** | Transparent | Zero additional overhead |
| **Code Coverage** | > 90% | Quality gate |
| **Documentation** | Complete | Easy adoption |

---

## Risks & Guardrails

**Risk:** Triggers can't execute cross-table DML  
**Guard:** Phase 0 task validates this works

**Risk:** JOIN performance degrades  
**Guard:** Hash indexes + benchmarking + tombstone cleanup

**Risk:** Concurrent sync + local writes deadlock  
**Guard:** MVCC snapshot reads + batch transactions

**Risk:** Users misconfigure DI  
**Guard:** Examples, XML docs, sample project

---

## Files to Create/Modify

### Core Engine (Phase 0)

- `src/SharpCoreDB/DataTypes.cs` — Add GUID type
- `src/SharpCoreDB/Services/SqlParser.Triggers.cs` — Validate cross-table DML
- `src/SharpCoreDB/Interfaces/IDatabase.cs` — Schema introspection API
- `src/SharpCoreDB/Services/SqlFunctions.cs` — Add SYNC_TIMESTAMP()

### Provider Add-In (Phases 1-6)

- `src/SharpCoreDB.Provider.Sync/` ← NEW
  - `SharpCoreDBSyncProvider.cs`
  - `Builders/`, `Adapters/`, `Metadata/`, `ChangeTracking/`
  - `Extensions/SyncServiceCollectionExtensions.cs`

- `tests/SharpCoreDB.Provider.Sync.Tests/` ← NEW
  - Unit tests, integration tests

---

## References

📄 **Full Proposal:** [DOTMIM_SYNC_PROVIDER_PROPOSAL.md](./DOTMIM_SYNC_PROVIDER_PROPOSAL.md) (Technical design)  
📋 **Implementation Plan:** [DOTMIM_SYNC_IMPLEMENTATION_PLAN.md](./DOTMIM_SYNC_IMPLEMENTATION_PLAN.md) (Phase breakdown)  
🔄 **Add-In Pattern:** [ADD_IN_PATTERN_SUMMARY.md](./ADD_IN_PATTERN_SUMMARY.md) (DI integration)  
📖 **README:** [README.md](./README.md) (Summary + context)  

---

## Status

✅ Proposal complete  
✅ Implementation plan complete  
✅ Add-in pattern aligned  
⏳ Ready for Phase 0 execution
