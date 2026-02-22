# Dotmim.Sync for SharpCoreDB — Visual Summary

## 🎯 The Vision

```
┌─────────────────────────────────────────────────────────────────┐
│                   LOCAL-FIRST AI AGENT ARCHITECTURE              │
├─────────────────────────────────────────────────────────────────┤
│                                                                   │
│  SERVER                    SYNC                    CLIENT          │
│  ┌──────────────┐         ┌────────┐         ┌────────────────┐  │
│  │ PostgreSQL   │◄────────│   DI   │────────►│ SharpCoreDB    │  │
│  │ SQL Server   │   HTTPS │Provider│  Filter │  (encrypted)   │  │
│  │   ~100GB     │◄────────│        │────────►│   ~50-100MB    │  │
│  │  Multi-tenant│         │Triggers│         │  Tenant subset │  │
│  │   knowledge  │         │ + Sync │         │  (tenant_id=42)│  │
│  └──────────────┘         └────────┘         └────────────────┘  │
│       │                      │                      │             │
│       │ Global data          │ Bidirectional        │ Local data  │
│       │ (all tenants)        │ conflict resolution  │ (1 tenant)  │
│       │                      │                      │             │
│       └──────────────────────┴──────────────────────┘             │
│                                                                   │
│       ┌──────────────────────────────────────────────────┐       │
│       │        LOCAL AI AGENT (Zero Latency)            │       │
│       │  ┌─────────────────────────────────────────┐    │       │
│       │  │ • Vector Search (embeddings)            │    │       │
│       │  │ • Graph Query (relationships)           │    │       │
│       │  │ • Full Privacy (encrypted local DB)     │    │       │
│       │  │ • Zero Network Latency                  │    │       │
│       │  └─────────────────────────────────────────┘    │       │
│       └──────────────────────────────────────────────────┘       │
│                                                                   │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔄 How It Works

### Step 1: Change Tracking (Shadow Tables)
```
User Application
    │
    ├─► INSERT INTO customers (id, name, email)
    │   VALUES (42, 'John', 'john@example.com')
    │
    ├─ Trigger fires: AFTER INSERT ON customers
    │   INSERT INTO customers_tracking
    │   VALUES (42, NULL, SYNC_TIMESTAMP(), 0, NOW())
    │
    ├─ Change detected ✓
    └─ Ready for sync ✓
```

### Step 2: Sync Provider Detects Changes
```
Dotmim.Sync Agent
    │
    ├─► SharpCoreDBSyncProvider.SelectChangesAsync()
    │   SELECT c.*, ct.timestamp
    │   FROM customers c
    │   INNER JOIN customers_tracking ct ON c.id = ct.pk_customer_id
    │   WHERE ct.timestamp > @last_sync_timestamp
    │
    ├─ Returns changed rows ✓
    └─ Passes to Dotmim.Sync ✓
```

### Step 3: Sync Server (PostgreSQL/SQL Server)
```
Dotmim.Sync Core
    │
    ├─► [Server Provider] detects its own changes
    │
    ├─► Applies client changes to server
    │
    ├─► Detects conflicts (if both sides changed row X)
    │   └─ Apply conflict resolution policy (server-wins, client-wins, custom)
    │
    └─ Sends server's changes to client ✓
```

### Step 4: Sync Client (SharpCoreDB)
```
Dotmim.Sync Core
    │
    ├─► SharpCoreDBSyncProvider.ApplyChangesAsync()
    │   • Batch inserts via ITable.InsertBatch()
    │   • Batch updates via ExecuteBatchSQL()
    │   • Batch deletes via ExecuteBatchSQL()
    │
    ├─► Update tracking timestamps
    │
    ├─ All wrapped in BeginBatchUpdate/EndBatchUpdate
    │   └─ Single atomic WAL flush
    │
    └─ Sync complete ✓
```

---

## 📦 Project Structure

```
SharpCoreDB.Provider.Sync (Add-In)
├── SharpCoreDBSyncProvider.cs         ← CoreProvider implementation
│
├── Builders/                          ← DDL generation
│   ├── SharpCoreDBDatabaseBuilder.cs
│   ├── SharpCoreDBTableBuilder.cs
│   └── SharpCoreDBScopeInfoBuilder.cs
│
├── Adapters/                          ← DML execution
│   ├── SharpCoreDBSyncAdapter.cs
│   └── SharpCoreDBObjectNames.cs
│
├── Metadata/                          ← Schema & types
│   ├── SharpCoreDBDbMetadata.cs
│   └── SharpCoreDBSchemaReader.cs
│
├── ChangeTracking/                    ← Triggers & tombstones
│   ├── ChangeTrackingManager.cs
│   ├── TrackingTableBuilder.cs
│   └── TombstoneManager.cs
│
├── Extensions/                        ← DI integration ⭐
│   ├── SyncServiceCollectionExtensions.cs  (AddSharpCoreDBSync)
│   └── SyncProviderFactory.cs
│
└── SharpCoreDB.Provider.Sync.csproj   ← .NET 10, C# 14
```

---

## 🚀 Implementation Phases

```
WEEK 1          WEEK 2          WEEKS 3-4       WEEKS 5-6
┌────┐         ┌────┐          ┌─────┐        ┌──────┐
│ P0 │ ───────►│ P1 │ ────────►│ P2  │ ──────►│ P3   │
└────┘         └────┘          └─────┘        └──────┘
 Core            Provider        Tracking       Adapter
 Engine          Skeleton        System         (DML)
 (Prerequisites) (+ DI)          (Triggers)     (Sync Logic)
   │                                │              │
   └─ GUID type              ┌──────┴──────────┐  └─ Select changes
   └─ Triggers               │ M1: Compiles    │    └─ Apply changes
   └─ Schema API             │ M2: DI works    │    └─ Conflicts
   └─ JOIN perf              └─────────────────┘    └─ Bulk ops
   └─ Timestamp fn

WEEK 7-8        WEEK 8          WEEKS 8-9
┌─────┐        ┌────┐          ┌──────┐
│ P4  │ ──────►│ P5 │ ────────►│ P6   │
└─────┘        └────┘          └──────┘
 Testing        Filtering       Documentation
 (Integration)  (Multi-tenant)  (NuGet, Samples)
   │              │               │
   └─ 5 unit     └─ Filtered    └─ README
   └─ 3 integ       sync         └─ Sample app
   └─ 10K row      └─ M6: Filtered  └─ XML docs
   └─ SQLite       └─ M7: Release  └─ NuGet
   └─ M3-5          candidate      metadata
```

---

## 🔑 The Encryption Insight

```
┌──────────────────────────────────────┐
│ THE KEY: Encryption is AT-REST ONLY  │
└──────────────────────────────────────┘

❌ DON'T BUILD:
   Encryption Bridge
   └─ Input: encrypted
   └─ Process: decrypt/re-encrypt
   └─ Output: encrypted

✅ DO USE:
   SharpCoreDB's transparent decryption
   └─ Input: plaintext (decrypted automatically)
   └─ Process: sync using plaintext
   └─ Output: plaintext (encrypted automatically by storage layer)

RESULT:
   Sync provider = just another consumer of SharpCoreDB API
   Zero special handling needed
   No encryption bridge, flags, or adapter code
```

---

## 📊 Timeline at a Glance

```
Total Effort: 5-7 weeks
Team: 1 developer + 1 reviewer

Week 1  ▓░░░░░░░ Phase 0: Core prerequisites (GUID, triggers, schema API)
Week 2  ░▓░░░░░░ Phase 1: Provider skeleton + DI registration
Week 3  ░░▓▓░░░░ Phase 2: Change tracking system (3 triggers + shadow tables)
Week 4  ░░░░     (Phase 2 continues)
Week 5  ░░░░▓▓░░ Phase 3: Sync adapter (select/apply changes, conflicts)
Week 6  ░░░░░░   (Phase 3 continues)
Week 7  ░░░░░░▓░ Phase 4: Testing + integration (11 test suites)
Week 8  ░░░░░░▓░ Phase 5: Multi-tenant filtering
Week 8  ░░░░░░░▓ Phase 6: Polish + NuGet packaging

Milestones:
M1: Week 2  ✓ Provider compiles
M2: Week 2  ✓ DI registration works
M3: Week 4  ✓ Change tracking functional
M4: Week 5  ✓ One-way sync works
M5: Week 6  ✓ Bidirectional + conflicts
M6: Week 8  ✓ Filtered sync
M7: Week 9  ✓ Release candidate (NuGet-ready)
```

---

## 🎯 Success Criteria

```
┌────────────────────────────────────┐
│ FEATURE                  │ STATUS  │
├────────────────────────────────────┤
│ Bidirectional sync       │ ✅ Goal │
│ Conflict resolution      │ ✅ Goal │
│ Multi-tenant filtering   │ ✅ Goal │
│ Encrypted DB support     │ ✅ Goal │
│ 10K rows in <5 sec       │ ✅ Goal │
│ >90% code coverage       │ ✅ Goal │
│ Complete documentation   │ ✅ Goal │
│ NuGet package ready      │ ✅ Goal │
└────────────────────────────────────┘
```

---

## 🔗 Architecture Layers

```
┌──────────────────────────────────────────────────────────┐
│ LAYER 7: User Application Code                          │
│ └─ services.AddSharpCoreDBSync("...")                   │
│                                                         │
│ LAYER 6: Dependency Injection                          │
│ └─ SyncServiceCollectionExtensions                     │
│ └─ SyncProviderFactory                                │
│                                                         │
│ LAYER 5: Dotmim.Sync Framework                         │
│ └─ SyncAgent, CoreProvider abstraction                 │
│                                                         │
│ LAYER 4: Provider Implementation                       │
│ ├─ SharpCoreDBSyncProvider (CoreProvider)             │
│ ├─ Builders (DDL generation)                          │
│ ├─ Adapters (DML execution)                           │
│ ├─ ChangeTracking (triggers + tombstones)             │
│ └─ Metadata (schema & type mapping)                   │
│                                                         │
│ LAYER 3: SharpCoreDB.Data.Provider (ADO.NET)          │
│ └─ DbConnection, DbCommand, DbDataReader              │
│                                                         │
│ LAYER 2: SharpCoreDB Core Engine                      │
│ ├─ IDatabase, ITable, IStorageEngine                  │
│ ├─ Triggers, MVCC, WAL                                │
│ ├─ CryptoService (AES-256-GCM, at-rest)               │
│ └─ ExecuteSQL, ExecuteBatchSQL                        │
│                                                         │
│ LAYER 1: Storage                                       │
│ └─ .scdb file (encrypted on disk)                     │
└──────────────────────────────────────────────────────────┘
```

---

## 🔐 Encryption Model

```
┌──────────────────────────────────────────────────────────┐
│ RUNTIME: ALL PLAINTEXT (between layers)                  │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  Application                                            │
│     │ plaintext rows                                   │
│     ▼                                                   │
│  Sync Provider                                         │
│     │ plaintext rows (no special handling)            │
│     ▼                                                   │
│  Dotmim.Sync                                           │
│     │ plaintext rows (all consumers are the same)     │
│     ▼                                                   │
│  SharpCoreDB (Insert/Select)                           │
│     │ plaintext rows                                   │
│     ▼                                                   │
│  CryptoService (automatic)                             │
│     │ Encrypt before writing to disk                  │
│     ▼                                                   │
│  Disk (.scdb file)                                     │
│     └─ ENCRYPTED AT REST ✓                             │
│                                                          │
└──────────────────────────────────────────────────────────┘

KEY POINT:
The sync provider never touches encryption keys.
Encryption is completely transparent.
No bridge, flags, or special code paths needed.
```

---

## 📝 DI Usage Pattern

```csharp
// Program.cs
services.AddSharpCoreDBSync(
    connectionString: "Path=C:\\data\\local.scdb;Password=secret",
    options: opts => {
        opts.EnableAutoTracking = true;
        opts.TombstoneRetentionDays = 30;
    }
);

// Later in your sync code
var provider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
var agent = new SyncAgent(provider, serverProvider);
await agent.SynchronizeAsync(setup);
```

---

## 🏆 Why This Design?

```
PRINCIPLE             │ BENEFIT
──────────────────────┼────────────────────────────────────
Use Dotmim.Sync      │ Mature framework, multiple providers
Shadow tables+triggers│ Proven pattern, all storage modes
At-rest encryption   │ No sync overhead, transparent
DI integration       │ Standard .NET pattern, composable
Add-in pattern       │ Ecosystem consistency, optional
Batch operations     │ Performance (5-10x faster)
MVCC reads          │ No blocking, concurrent access
WAL protection      │ Crash recovery, durability
```

---

**Ready to build?** Start with the [Implementation Plan](./DOTMIM_SYNC_IMPLEMENTATION_PLAN.md) 🚀
