# GitHub Issue Template – Ready to Post

---

**Copy everything below the line into a new GitHub Issue.**

---

# SharpCoreDB.EntityFrameworkCore: `Include` + `Where(x => Navigation.Any(...))` fails with Guid primary keys

## Description

When using entities with `Guid` primary keys, the following common and recommended EF Core pattern either throws or returns incorrect results:

```csharp
var result = await dbContext.Companies
    .Include(x => x.Vacancies)
    .AsNoTracking()
    .Where(x => x.Vacancies.Any(v => v.IsActive))
    .OrderBy(x => x.Name)
    .ToListAsync();
```

This works correctly with `int` / `long` keys but breaks when the primary key (and foreign key) is `Guid`.

### Observed Errors
- `System.FormatException: String 'DevOps' was not recognized as a valid Boolean`
- `System.IndexOutOfRangeException: Invalid column ordinal: X`
- The `Where(...Any())` filter returns 0 results even when matching data exists

## Reproduction

**Entities (simplified):**
```csharp
public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Vacancy> Vacancies { get; set; } = [];
}

public class Vacancy
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Company? Company { get; set; }
}
```

**Data shape** (from `tests/companies.vacancies.seed.json`):
- Multiple companies
- Some have active vacancies, some have only inactive ones

**Query**:
The exact pattern shown above (or any equivalent `GetActiveWithVacanciesAsync` implementation).

## Root Cause

The bug lives in `SharpCoreDB.EntityFrameworkCore`:

- `Storage/SharpCoreDBDataReader.cs` — aggressive column name normalization + de-duplication causes ordinal collisions when EF Core materializes `Include` result sets (parent + child columns often normalize to the same name).
- Result rows produced for navigation queries do not provide stable column ordinals that EF Core’s relational shaper (`PopulateIncludeCollection`, split query paths, etc.) expects when the key type is `Guid`.
- `Guid` handling in combination with `Contains<Guid>` / navigation `Any()` exposes weaknesses that `int` keys do not trigger.

This is **not** a bug in Microsoft Entity Framework Core — it is incomplete / fragile support in our custom provider for relationship loading with non-integer key types.

## Current Workarounds (until fixed)

Users are currently forced to use less elegant patterns such as:
- Two-query approach (first fetch active IDs, then load)
- Full load + client-side filtering
- Avoiding `Include` + server-side navigation filters together

## Acceptance Criteria (Definition of Done)

- The following code must work reliably and return correct results:
  ```csharp
  await dbContext.Companies
      .Include(x => x.Vacancies)
      .Where(x => x.Vacancies.Any(v => v.IsActive))
      .ToListAsync();
  ```
- Both normal `Include` and `AsSplitQuery()` paths must succeed.
- No `Invalid column ordinal` or type conversion crashes during navigation materialization when using `Guid` keys.
- New integration tests using Guid-keyed entities with navigation properties pass consistently.
- The workaround helper (`CompanyVacancyRepository`) can be simplified or deprecated for this scenario.

## Proposed Plan

See the detailed implementation plan in the attached document / comments below (or linked file `docs/issues/efcore-guid-navigation-bug.md` in the repo).

High-level phases:
1. Harden `SharpCoreDBDataReader` (preserve original keys, robust lookup, defensive getters)
2. Improve result set shaping for EF Core `Include` / navigation queries
3. Add proper `Guid` + relationship regression coverage
4. Documentation and release

## Labels
`bug`, `ef-core-provider`, `guid`, `include`, `navigation`, `high-priority`

## Milestone
Target: 1.10.0 or next patch after 1.9.5

---

**Reproduction repository / branch**: `master` (see `CompleteExampleIntegrationTests.cs` for the current test skeleton using `TestCompany` / `TestVacancy` with Guid keys).

---

## Resolution (February 2025)

**Root Cause Identified:**
The bug was in `BuildParameterDictionary()` inside `SharpCoreDBCommand.cs` (EF Core provider). Only `DateTime` values were being normalized to strings. Raw `Guid` objects were passed through during INSERT, and the underlying SharpCoreDB engine did not reliably persist them when used as foreign keys.

**Fix Applied:**
Added Guid normalization (to canonical "D" string format) in `BuildParameterDictionary()`, mirroring the existing DateTime handling.

**Current Status:**
- The ideal pattern now works with Guid keys.
- Reader stability improved during Phase 1.
- A follow-up task exists to move the conversion into a proper `ValueConverter` in `SharpCoreDBTypeMappingSource.cs`.

---

**End of GitHub Issue body**

---

## Additional Files to Create / Update

After posting the issue, we should also do:

1. Create the detailed plan file (already prepared via the `plan` tool).
2. Link the issue to a tracking document in `docs/efcore-provider/`.

Would you like me to also create:

- A tracking document `docs/efcore-provider/Guid-Navigation-Support.md`?
- A proper GitHub Issue template addition?

Just say the word and I’ll generate the files.
