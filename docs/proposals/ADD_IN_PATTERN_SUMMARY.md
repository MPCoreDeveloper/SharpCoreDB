# SharpCoreDB.Provider.Sync — Add-In Pattern Updates

**Date:** 2025-01  
**Status:** Implementation plans updated to follow add-in pattern

---

## What Changed

The Dotmim.Sync provider implementation plan was updated to follow the **add-in pattern** established by `SharpCoreDB.Provider.YesSql`. This ensures consistency across the SharpCoreDB ecosystem and enables proper NuGet packaging and dependency injection.

### Project Naming

| Before | After | Reason |
|---|---|---|
| `SharpCoreDB.Sync` | `SharpCoreDB.Provider.Sync` | Consistent with YesSql/other provider naming |
| `SharpCoreDB.Sync.Tests` | `SharpCoreDB.Provider.Sync.Tests` | Follows standard test project naming |

### Package Identity

| Aspect | Value |
|---|---|
| **NuGet Package ID** | `SharpCoreDB.Provider.Sync` |
| **Assembly Name** | `SharpCoreDB.Provider.Sync` |
| **Namespace** | `SharpCoreDB.Provider.Sync` |
| **Version** | `1.0.0` (independent from core) |

### Dependency Injection Integration

Added formal DI extension pattern:

```csharp
// Program.cs
services.AddSharpCoreDBSync(
    connectionString: "Path=C:\\data\\local.scdb;Password=secret",
    options: opts => {
        opts.EnableAutoTracking = true;
        opts.TombstoneRetentionDays = 30;
    });

// Later in code
var provider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
```

### Project Structure

**New folders in `src/SharpCoreDB.Provider.Sync/`:**

```
Extensions/
  ├─ SyncServiceCollectionExtensions.cs    ← DI registration
  └─ SyncProviderFactory.cs                 ← Factory pattern
```

These follow the Microsoft.Extensions.DependencyInjection conventions.

### Documentation Updates

1. **Phase 1.4**: Detailed DI extension implementation task with:
   - Service registration pattern
   - Configuration options
   - Usage examples

2. **Phase 6.2**: README updated to show:
   - Installation via NuGet
   - DI setup in Program.cs
   - Multi-tenant filtering
   - Sample code

3. **Phase 6.5**: Main README now links to:
   - `SharpCoreDB.Provider.Sync` package
   - DI integration examples

### Technical Decisions

Added to TD log:

| TD | Decision | Rationale |
|---|---|---|
| TD-7 | Add-in pattern (SharpCoreDB.Provider.Sync) | Consistent with YesSql; enables optional installation |
| TD-8 | Use DI for provider factory | Microsoft.Extensions.DependencyInjection pattern |

### Milestones

Added new M2 milestone:

| M2 | DI Integration Works | `services.AddSharpCoreDBSync()` registers and resolves from container | Week 2 |

### Risk Mitigations

Added to risk register:

- **DI container misconfiguration** — Comprehensive examples, XML docs, sample project

---

## Alignment with Existing Providers

### SharpCoreDB.Provider.YesSql Pattern

```csharp
services.AddYesSqlDataStore(c => 
    c.UseSqlite("Data Source=..." /* ... */));

// SharpCoreDB.Provider.Sync follows the same pattern:
services.AddSharpCoreDBSync("Path=C:\\...;Password=...");
```

### Benefits of Add-In Pattern

✅ **Consistency** — Matches the ecosystem's provider model  
✅ **Optional** — Users choose to install only what they need  
✅ **Composable** — Integrates naturally with DI containers  
✅ **Discoverable** — Clear `SharpCoreDB.Provider.*` naming  
✅ **Versioning** — Independent versioning from core (core v1.3.5 ≠ provider v1.0.0)  
✅ **Packaging** — Proper separation of concerns for NuGet

---

## File References

- **Updated:** `docs/proposals/DOTMIM_SYNC_IMPLEMENTATION_PLAN.md`
  - Phase 1.1: Project structure (add-in naming)
  - Phase 1.4: DI extensions task (new/expanded)
  - Phase 4: Test project paths updated
  - Phase 6: NuGet metadata and README examples
  - Technical decisions: Added TD-7, TD-8

- **Updated:** `docs/proposals/DOTMIM_SYNC_PROVIDER_PROPOSAL.md`
  - Section 8: Project structure (add-in pattern)
  - Dependencies: Unchanged
  - Risks: Added DI misconfiguration mitigation

---

## Implementation Impact

### Phase 1 (Week 2)

Task 1.4 now includes implementing:
- `SyncServiceCollectionExtensions.cs` with `AddSharpCoreDBSync()` method
- `SyncProviderFactory.cs` for instance creation
- Configuration options class (`SyncProviderOptions`)

### Phase 6 (Week 8-9)

NuGet packaging now includes:
- Package ID: `SharpCoreDB.Provider.Sync`
- Multi-RID support (standard across all providers)
- Proper dependencies and metadata

### Documentation

README examples now show DI-first approach:
```csharp
// Instead of manual instantiation:
var provider = new SharpCoreDBSyncProvider { ConnectionString = "..." };

// Use DI:
services.AddSharpCoreDBSync("...");
var provider = serviceProvider.GetRequiredService<SharpCoreDBSyncProvider>();
```

---

## No Breaking Changes

These updates are **planning-level changes** — the actual implementation hasn't started yet. When Phase 1 implementation begins, it will follow the add-in pattern from the start, so no breaking changes will occur later.

---

## Next Steps

1. ✅ **Planning**: Both proposal and implementation plan updated (THIS DOCUMENT)
2. ⏳ **Phase 0**: Execute prerequisite tasks in SharpCoreDB core
3. ⏳ **Phase 1**: Create `SharpCoreDB.Provider.Sync` project with DI integration
4. ⏳ **Phase 2-6**: Complete remaining phases per the implementation plan

For questions or clarifications, see the full plans:
- [DOTMIM_SYNC_PROVIDER_PROPOSAL.md](./DOTMIM_SYNC_PROVIDER_PROPOSAL.md)
- [DOTMIM_SYNC_IMPLEMENTATION_PLAN.md](./DOTMIM_SYNC_IMPLEMENTATION_PLAN.md)
