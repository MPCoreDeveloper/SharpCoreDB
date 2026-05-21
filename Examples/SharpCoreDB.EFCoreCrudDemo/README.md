# SharpCoreDB EF Core Full CRUD Demo

This is a **runnable console application** that demonstrates a complete end-to-end CRUD workflow using:

- SharpCoreDB.EntityFrameworkCore provider
- Guid primary keys and foreign keys
- The **recommended reliable patterns** for reading relationships (instead of relying on `Include` + server-side navigation filters, which have current limitations)

## What it does

1. Loads the real dataset from `companies.vacancies.seed.json` (Delta Logistics, Nordic Retail, TechNova + their vacancies)
2. **Create** – Seeds the entire dataset
3. **Read** – Retrieves companies with active vacancies using the recommended two-query pattern
4. **Update** – Modifies a vacancy (title + status)
5. **Create (extra)** – Adds a brand new company with a vacancy
6. **Delete** – Removes a specific vacancy
7. Final verification using the reliable read pattern

## How to run

```bash
cd Examples/SharpCoreDB.EFCoreCrudDemo
dotnet run
```

## Key Takeaway

This demo shows the **current best practice** for working with Guid-based relationships in SharpCoreDB.EntityFrameworkCore:

- Use simple queries + manual navigation wiring for reads (see `GetActiveWithVacanciesAsync`)
- This pattern is stable and recommended until deeper provider improvements (custom `IModificationCommandBatch`) are completed in a future release.

## Roadmap

A more "magical" EF experience (full `Include` + complex navigation queries for Guids on writes) is planned via Option B (custom batch implementation) in a later version.
