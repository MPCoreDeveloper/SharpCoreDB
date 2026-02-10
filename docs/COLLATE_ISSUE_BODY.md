## Feature: SQL COLLATE Support for Case-Insensitive and Locale-Aware String Comparisons

### Summary

Add SQL-standard `COLLATE` support to SharpCoreDB, enabling case-insensitive and locale-aware string comparisons at the column level, index level, and query level.

### Motivation

Currently, all string comparisons in SharpCoreDB are binary (case-sensitive). Users need the ability to:
- Define case-insensitive columns (e.g., `Name TEXT COLLATE NOCASE`)
- Have indexes automatically respect collation (case-insensitive lookups)
- Override collation at query time
- Eventually support locale-aware sorting (e.g., German ß, Turkish İ)

### Target SQL Syntax

```sql
-- Column-level collation in DDL
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTO,
    Name TEXT COLLATE NOCASE,
    Email TEXT COLLATE NOCASE
);

-- Index automatically inherits column collation
CREATE INDEX idx_users_name ON Users(Name);  -- case-insensitive automatically

-- Query-level override (future)
SELECT * FROM Users WHERE Name COLLATE NOCASE = @var;
SELECT * FROM Users WHERE LOWER(Name) = LOWER(@name);

-- Locale-aware indexes (future)
CREATE INDEX idx_name_ci ON users (name COLLATE "en_US" NOCASE);
CREATE INDEX idx_name_cs ON users (name);  -- default is case-sensitive
```

### EF Core Integration (Future)

```csharp
modelBuilder.Entity<User>()
    .Property(u => u.Name)
    .UseCollation("NOCASE");
```

### Implementation Plan

📄 **Full plan:** [`docs/COLLATE_SUPPORT_PLAN.md`](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/COLLATE_SUPPORT_PLAN.md)

### Phases

| Phase | Description | Priority | Impact |
|-------|-------------|----------|--------|
| **Phase 1** | Core types (`CollationType` enum), ITable/Table metadata, persistence | P0 | Foundation — 7 files |
| **Phase 2** | DDL parsing (`COLLATE` in `CREATE TABLE` and `ALTER TABLE ADD COLUMN`) | P0 | `SqlParser.DDL.cs`, `EnhancedSqlParser.DDL.cs` |
| **Phase 3** | Collation-aware WHERE filtering, JOIN conditions, ORDER BY | P0 | `SqlParser.Helpers.cs`, `CompiledQueryExecutor.cs` |
| **Phase 4** | Index integration — HashIndex/BTree key normalization | P1 | `HashIndex.cs`, `BTree.cs`, `GenericHashIndex.cs` |
| **Phase 5** | Query-level `COLLATE` override + `LOWER()`/`UPPER()` functions | P2 | Enhanced parser + AST nodes |
| **Phase 6** | Locale-aware collations (ICU-based, culture-specific) | P3 | Future/research |
| **EF Core** | `UseCollation()` fluent API + DDL emission | Separate | `SharpCoreDBMigrationsSqlGenerator.cs` |

### Codebase Impact (from investigation)

**20+ files** across core engine, SQL parsers, indexes, metadata, and EF Core provider.

Key touchpoints identified:
- `EvaluateOperator()` — currently uses `rowValueStr == value` (binary only)
- `CompareKeys()` in BTree — uses `string.CompareOrdinal()` (binary only)
- `HashIndex` — uses `SimdHashEqualityComparer` (binary only)
- `ColumnDefinition` — missing `Collation` property
- `ITable` / `Table` — missing `ColumnCollations` per-column list
- `SaveMetadata()` — missing collation serialization
- `ColumnInfo` — missing collation in metadata discovery

### Backward Compatibility

- ✅ Default behavior unchanged (all existing tables default to `Binary`)
- ✅ Metadata migration: missing `ColumnCollations` → all Binary
- ✅ All new parameters are optional with Binary defaults
- ✅ Existing indexes continue to work

### Labels

`enhancement`, `sql-engine`, `roadmap`
