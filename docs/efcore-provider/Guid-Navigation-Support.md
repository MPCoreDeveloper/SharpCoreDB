# Guid Key + Navigation Support in SharpCoreDB.EntityFrameworkCore

**Status:** In Progress (Bug Identified)  
**Priority:** High  
**Target Version:** 1.10.0+  
**Last Updated:** 2025-02-28

---

## Overview

This document tracks the effort to make the `SharpCoreDB.EntityFrameworkCore` provider fully support common and recommended EF Core patterns when entities use **Guid** (or other non-integer) primary keys, especially when combined with:

- `.Include(x => x.Navigation)`
- `.Where(x => x.Navigation.Any(...))`
- `.Contains<Guid>()` filters
- Both normal `Include` and `AsSplitQuery()` strategies

## Problem Statement

As of version 1.9.0, the following pattern fails or produces incorrect results when the principal entity uses a `Guid` primary key:

```csharp
await dbContext.Companies
    .Include(c => c.Vacancies)
    .Where(c => c.Vacancies.Any(v => v.IsActive))
    .OrderBy(c => c.Name)
    .ToListAsync();
```

**Symptoms:**
- `Invalid column ordinal` exceptions during materialization
- `FormatException` (e.g., trying to convert Title strings to Boolean)
- `Where(...Any())` returning zero results even when data exists

**Root Cause:**  
Incomplete / fragile handling in `SharpCoreDBDataReader` and query result shaping when EF Core generates JOINs or split queries for navigation properties on Guid-keyed entities.

This is **not** a Microsoft EF Core bug — it is a limitation in our custom provider.

## Current Status

| Area                        | Status          | Notes |
|----------------------------|-----------------|-------|
| Basic CRUD with Guid keys  | ✅ Working     | `TestGuidEntity` tests pass |
| Simple `Include` (no filter) | ⚠️ Partial    | Works in some cases, fragile |
| `Include` + `Where(...Any())` | ❌ Broken     | Main issue reported |
| `AsSplitQuery()` + Guid    | ❌ Broken     | Also affected |
| `Contains<Guid>()`         | ⚠️ Unreliable | Can trigger ordinal errors |
| Data Reader (`SharpCoreDBDataReader`) | 🔧 Being hardened | See plan below |

## Related Code & Tests

- **Reproduction / Test Entities:**  
  `tests/SharpCoreDB.EntityFrameworkCore.Tests/Integration/CompleteExampleIntegrationTests.cs`  
  (`TestCompany`, `TestVacancy`, `TestCompanyVacancyDbContext`)

- **Recommended Workaround (current best practice):**  
  `tests/SharpCoreDB.EntityFrameworkCore.Tests/Integration/CompanyVacancyRepository.cs`

- **Seed Data Structure:**  
  `tests/companies.vacancies.seed.json`

- **GitHub Issue (with full plan):**  
  See `docs/issues/efcore-guid-navigation-bug.md`

## Implementation Plan (Summary)

The full detailed plan is registered in the repository planning system and mirrored in the GitHub issue.

### High-Level Phases

1. **Phase 1 – Data Reader Hardening** (Priority)
   - Preserve original column keys (stop destructive normalization)
   - Build rich `name → ordinal` lookup with multiple fallbacks
   - Make `GetBoolean`, `GetGuid`, `GetValue` extremely defensive during Include shaping
   - Add unit tests for reader with simulated Include-shaped rows

2. **Phase 2 – Navigation / Include Result Shaping**
   - Investigate how query results are produced for EF Core `Include` SQL
   - Ensure stable column counts and ordinals for child entities
   - Improve support for both single-result-set and split-query paths

3. **Phase 3 – Guid-Specific Improvements**
   - Reliable `Contains<Guid>` / `IN` list handling
   - Proper Guid parameter and value conversion in relationship scenarios

4. **Phase 4 – Testing & Validation**
   - Promote existing Guid Company/Vacancy tests to use the **ideal** one-liner pattern
   - Add more complex cases (filtered Includes, multiple navigations, deep graphs)
   - Run against real seed data

5. **Phase 5 – Documentation & Release**
   - Update EF Core provider README with supported patterns
   - Add dedicated section on “Navigation Loading with Guid Keys”
   - Remove or deprecate workarounds once fixed

## Progress Log

| Date       | Phase | Action | Status | Notes |
|------------|-------|--------|--------|-------|
| 2025-02-28 | -     | Bug reported + reproduction tests added | ✅ | `GetActiveWithVacanciesAsync_GuidKey_*` tests created |
| 2025-02-28 | -     | Defensive improvements to reader | 🔧 | Partial – still hitting ordinal issues on complex Includes |
| 2025-02-28 | -     | GitHub issue + formal plan created | ✅ | `docs/issues/efcore-guid-navigation-bug.md` |
| 2025-02-28 | -     | This tracking document created | ✅ | `docs/efcore-provider/Guid-Navigation-Support.md` |
| 2025-02-28 | Phase 1 | Reader hardening + best-match ordinal logic | ✅ | No more hard crashes on Include |
| 2025-02-28 | Root Cause | Identified in `BuildParameterDictionary` (missing Guid normalization) | ✅ | Guid FKs were not persisted as strings |
| 2025-02-28 | Fix Applied | `Guid g => g.ToString("D")` normalization in EF Command | ✅ | Ideal pattern now works |
| 2025-02-28 | Proper Fix | Guid mapped as text-backed with intent for ValueConverter | In Progress | Long-term architectural improvement |

## Workarounds (Until Fixed)

Use the pattern in `CompanyVacancyRepository.GetActiveWithVacanciesAsync()`:

- Load companies + vacancies separately
- Attach navigation in memory
- Apply filter client-side

This is reliable today but not as elegant as the native EF Core pattern.

## Acceptance Criteria

- The ideal pattern shown in the **Problem Statement** returns correct results with Guid keys.
- No more `Invalid column ordinal` or type conversion errors during Include materialization.
- Both `Include` and `AsSplitQuery()` paths are stable.
- New tests using the clean one-liner pass in CI.
- Workaround code can be simplified or removed.

## Related Issues / PRs

- GitHub Issue: [To be linked after posting]
- Plan: Registered in repository planning system (see `plan` tool output)
- Test file: `CompleteExampleIntegrationTests.cs`

---

**Owner:** SharpCoreDB Team  
**Next Step:** Proceed to Phase 1 (Data Reader Hardening) once the GitHub issue is created and triaged.

---

*This document is the single source of truth for tracking progress on Guid + Navigation support in the EF Core provider.*
