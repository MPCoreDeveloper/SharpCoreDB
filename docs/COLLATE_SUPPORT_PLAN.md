# COLLATE Support Implementation Plan

**Feature:** SQL COLLATE clause and collation-aware string comparison  
**Author:** SharpCoreDB Team  
**Date:** 2026-02-10  
**Status:** Proposed  
**Priority:** High  
**Estimated Effort:** ~6 phases (incremental delivery)

---

## 1. Executive Summary

Add SQL-standard `COLLATE` support to SharpCoreDB, enabling case-insensitive and
locale-aware string comparisons at the column level, index level, and query level.

### Target SQL Syntax

```sql
-- Column-level collation in DDL
CREATE TABLE Users (
    Id INTEGER PRIMARY KEY AUTO,
    Name TEXT COLLATE NOCASE,
    Email TEXT COLLATE NOCASE
);

-- Index automatically inherits column collation
CREATE INDEX idx_users_name ON Users(Name); -- case-insensitive automatically

-- Explicit collation on index (future)
CREATE INDEX idx_name_ci ON users (name COLLATE "en_US" NOCASE);
CREATE INDEX idx_name_cs ON users (name);  -- default is case-sensitive (BINARY)

-- Query-level collation override
SELECT * FROM Users WHERE Name COLLATE NOCASE = @var;
SELECT * FROM Users WHERE LOWER(Name) = LOWER(@name);
```

### EF Core Integration (Future)

```csharp
modelBuilder.Entity<User>()
    .Property(u => u.Name)
    .UseCollation("NOCASE");
```

---

## 2. Current State Analysis

### Codebase Investigation Results

| File / Area | Current Behavior | Gap |
|---|---|---|
| `SqlParser.Helpers.cs` → `EvaluateOperator()` | `"=" => rowValueStr == value` (case-sensitive ordinal) | No collation awareness |
| `SqlParser.InExpressionSupport.cs` → `AreValuesEqual()` | Falls back to `StringComparison.OrdinalIgnoreCase` for strings | Inconsistent: always case-insensitive on fallback |
| `CompiledQueryExecutor.cs` → `CompareValues()` | `string.Compare(..., StringComparison.Ordinal)` | No collation awareness |
| `SqlAst.DML.cs` → `ColumnDefinition` | Has Name, DataType, IsPrimaryKey, IsNotNull, IsUnique, DefaultValue, CheckExpression, Dimensions | **No `Collation` property** |
| `ITable.cs` | Per-column lists: `IsAuto`, `IsNotNull`, `DefaultValues`, `UniqueConstraints`, `ForeignKeys` | **No `ColumnCollations` list** |
| `Table.cs` | Follows same per-column list pattern, has `Metadata` dict for extensible metadata | **No collation metadata** |
| `SqlParser.DDL.cs` → `ExecuteCreateTable()` | Parses NOT NULL, UNIQUE, PRIMARY KEY, AUTO, DEFAULT, CHECK, FOREIGN KEY | **No COLLATE parsing** |
| `EnhancedSqlParser.DDL.cs` → `ParseColumnDefinition()` | Parses PRIMARY KEY, AUTO, NOT NULL, UNIQUE, DEFAULT, CHECK | **No COLLATE parsing** |
| `HashIndex.cs` | Uses `SimdHashEqualityComparer` with binary string equality | **No collation-aware key normalization** |
| `GenericHashIndex.cs` | Uses `Dictionary<TKey, List<long>>` with default equality | **No collation-aware equality** |
| `BTree.cs` → `CompareKeys()` | `string.CompareOrdinal(str1, str2)` (binary) | **No collation-aware comparison** |
| `SimdWhereFilter.cs` | Integer/float SIMD filtering only | No string collation support (N/A for SIMD) |
| `SimdFilter.cs` (Query) | Integer/float SIMD filtering only | No string collation support (N/A for SIMD) |
| `Database.Core.cs` → `SaveMetadata()` | Serializes Columns, ColumnTypes, PrimaryKeyIndex, IsAuto, IsNotNull, DefaultValues, UniqueConstraints, ForeignKeys | **Missing collation serialization** |
| `Database.Metadata.cs` → `GetColumns()` | Returns `ColumnInfo` with Table, Name, DataType, Ordinal, IsNullable | **No collation in `ColumnInfo`** |
| `ColumnInfo.cs` | Record with Table, Name, DataType, Ordinal, IsNullable | **No `Collation` property** |
| `SharpCoreDBMigrationsSqlGenerator.cs` → `ColumnDefinition()` | Emits column name, type, NOT NULL, DEFAULT | **No COLLATE clause emission** |

### Key Observation

The codebase follows a consistent **per-column list pattern** for column metadata:
- `List<string> Columns`
- `List<DataType> ColumnTypes`
- `List<bool> IsAuto`
- `List<bool> IsNotNull`
- `List<object?> DefaultValues`
- `List<string?> DefaultExpressions`
- `List<string?> ColumnCheckExpressions`

Adding `List<CollationType> ColumnCollations` fits naturally into this pattern.

---

## 3. Collation Types

```
CollationType.Binary                 → Default. Byte-by-byte comparison (case-sensitive)
CollationType.NoCase                 → Ordinal case-insensitive (OrdinalIgnoreCase)
CollationType.RTrim                  → Like Binary but ignores trailing whitespace
CollationType.UnicodeCaseInsensitive → Culture-aware case-insensitive (future, locale-specific)
```

---

## 4. Implementation Phases

### Phase 1: Core Infrastructure (P0 — Foundation)

**Goal:** Define collation types and wire into column metadata across the entire stack.

#### New Files
| File | Purpose |
|---|---|
| `src/SharpCoreDB/CollationType.cs` | `CollationType` enum |

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB/Services/SqlAst.DML.cs` | Add `Collation` property to `ColumnDefinition` |
| `src/SharpCoreDB/Interfaces/ITable.cs` | Add `List<CollationType> ColumnCollations` property |
| `src/SharpCoreDB/DataStructures/Table.cs` | Add `List<CollationType> ColumnCollations` property with `[]` default |
| `src/SharpCoreDB/DataStructures/ColumnInfo.cs` | Add `string? Collation` property to metadata record |
| `src/SharpCoreDB/Database/Core/Database.Metadata.cs` | Include `ColumnCollations` in `GetColumns()` output |
| `src/SharpCoreDB/Database/Core/Database.Core.cs` | Include `ColumnCollations` in `SaveMetadata()` and `Load()` |
| `src/SharpCoreDB/Services/SqlParser.DML.cs` → `InMemoryTable` | Add stub `ColumnCollations` property |

#### Design Details

```csharp
// src/SharpCoreDB/CollationType.cs
namespace SharpCoreDB;

/// <summary>
/// Collation types for string comparison in SharpCoreDB.
/// Controls how TEXT values are compared, sorted, and indexed.
/// </summary>
public enum CollationType
{
    /// <summary>Default binary comparison (case-sensitive, byte-by-byte).</summary>
    Binary,

    /// <summary>Case-insensitive comparison using ordinal rules.</summary>
    NoCase,

    /// <summary>Like Binary but ignores trailing whitespace.</summary>
    RTrim,

    /// <summary>Culture-aware case-insensitive (future: locale-specific).</summary>
    UnicodeCaseInsensitive,
}
```

---

### Phase 2: DDL Parsing — `COLLATE` in `CREATE TABLE` (P0)

**Goal:** Parse `COLLATE NOCASE` / `COLLATE BINARY` in column definitions.

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB/Services/SqlParser.DDL.cs` → `ExecuteCreateTable()` | Parse `COLLATE <type>` in column definition loop (near line where `isNotNullCol`/`isUniqueCol` are detected) |
| `src/SharpCoreDB/Services/EnhancedSqlParser.DDL.cs` → `ParseColumnDefinition()` | Add `else if (MatchKeyword("COLLATE"))` branch after CHECK parsing |
| `src/SharpCoreDB/Services/SqlParser.DDL.cs` → `ParseColumnDefinitionFromSql()` | Add COLLATE case to constraint parser (for ALTER TABLE ADD COLUMN) |

#### DDL Parsing Logic (SqlParser.DDL.cs)

Inside `ExecuteCreateTable()` column parsing loop, after existing constraint detection:

```csharp
// Parse COLLATE clause
var columnCollations = new List<CollationType>();

// Inside the for loop per column definition:
var collation = CollationType.Binary; // default
var collateIdx = def.IndexOf("COLLATE", StringComparison.OrdinalIgnoreCase);
if (collateIdx >= 0)
{
    var collateType = def[(collateIdx + 7)..].Trim().Split(' ')[0].ToUpperInvariant();
    collation = collateType switch
    {
        "NOCASE" => CollationType.NoCase,
        "BINARY" => CollationType.Binary,
        "RTRIM" => CollationType.RTrim,
        _ => throw new InvalidOperationException(
            $"Unknown collation '{collateType}'. Valid: NOCASE, BINARY, RTRIM")
    };
}
columnCollations.Add(collation);
```

#### EnhancedSqlParser.DDL.cs

Add after the `else if (MatchKeyword("CHECK"))` block:

```csharp
else if (MatchKeyword("COLLATE"))
{
    var collationName = ConsumeIdentifier()?.ToUpperInvariant() ?? "BINARY";
    column.Collation = collationName switch
    {
        "NOCASE" => CollationType.NoCase,
        "BINARY" => CollationType.Binary,
        "RTRIM" => CollationType.RTrim,
        _ => CollationType.Binary
    };
}
```

---

### Phase 3: Query Execution — Collation-Aware Comparisons (P0)

**Goal:** Make WHERE filtering, JOIN conditions, and ORDER BY respect column collation.

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB/Services/SqlParser.Helpers.cs` → `EvaluateOperator()` | Add collation parameter and use `CompareWithCollation()` |
| `src/SharpCoreDB/Services/SqlParser.Helpers.cs` → `EvaluateJoinWhere()` | Thread collation through to comparison |
| `src/SharpCoreDB/Services/SqlParser.InExpressionSupport.cs` → `AreValuesEqual()` | Accept optional collation, default to current behavior |
| `src/SharpCoreDB/Services/CompiledQueryExecutor.cs` → `CompareValues()` | Add collation-aware string comparison branch |

#### Core Comparison Helper (new static method)

```csharp
/// <summary>
/// Compares two string values using the specified collation.
/// PERF: Hot path — uses Span-based comparison for NOCASE to avoid allocations.
/// </summary>
internal static int CompareWithCollation(
    ReadOnlySpan<char> left, ReadOnlySpan<char> right, CollationType collation)
{
    return collation switch
    {
        CollationType.Binary => left.SequenceCompareTo(right),
        CollationType.NoCase => left.CompareTo(right, StringComparison.OrdinalIgnoreCase),
        CollationType.RTrim => left.TrimEnd().SequenceCompareTo(right.TrimEnd()),
        CollationType.UnicodeCaseInsensitive
            => left.CompareTo(right, StringComparison.CurrentCultureIgnoreCase),
        _ => left.SequenceCompareTo(right),
    };
}
```

#### EvaluateOperator Impact

Current:
```csharp
"=" => rowValueStr == value,
```

After:
```csharp
"=" => CompareWithCollation(rowValueStr.AsSpan(), value.AsSpan(), collation) == 0,
```

The collation for a column needs to be resolved by the caller (SqlParser knows the table and column involved in the WHERE clause). For backward compatibility, default to `CollationType.Binary`.

---

### Phase 4: Index Integration (P1 — Performance Critical)

**Goal:** Indexes automatically respect column collation for key storage and lookup.

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB/DataStructures/HashIndex.cs` | Accept `CollationType` in constructor, normalize keys on Add/Lookup |
| `src/SharpCoreDB/DataStructures/HashIndex.cs` → `SimdHashEqualityComparer` | Collation-aware `Equals()` and `GetHashCode()` |
| `src/SharpCoreDB/DataStructures/GenericHashIndex.cs` | Accept optional `IEqualityComparer<TKey>` for collation |
| `src/SharpCoreDB/DataStructures/BTree.cs` → `CompareKeys()` | Collation-aware string comparison branch |
| `src/SharpCoreDB/DataStructures/Table.Indexing.cs` | Pass column collation when creating indexes |

#### Key Normalization Strategy

```csharp
internal static string NormalizeIndexKey(string value, CollationType collation)
{
    return collation switch
    {
        CollationType.NoCase => value.ToUpperInvariant(), // Canonical form
        CollationType.RTrim => value.TrimEnd(),
        _ => value // Binary = no normalization
    };
}
```

**HashIndex:** Normalize keys at `Add()` and `Find()` time:
```csharp
// In HashIndex.Add():
var normalizedKey = NormalizeIndexKey(key.ToString(), _collation);

// In HashIndex.Find():
var normalizedKey = NormalizeIndexKey(searchKey.ToString(), _collation);
```

**BTree:** Use collation-aware `CompareKeys()`:
```csharp
private static int CompareKeys(TKey key1, TKey key2, CollationType collation)
{
    if (typeof(TKey) == typeof(string) && key1 is string str1 && key2 is string str2)
    {
        return collation switch
        {
            CollationType.NoCase => string.Compare(str1, str2, StringComparison.OrdinalIgnoreCase),
            CollationType.RTrim => string.CompareOrdinal(str1.TrimEnd(), str2.TrimEnd()),
            _ => string.CompareOrdinal(str1, str2)
        };
    }
    return Comparer<TKey>.Default.Compare(key1, key2);
}
```

**Important:** When a `CREATE TABLE` has `Name TEXT COLLATE NOCASE`, and later
`CREATE INDEX idx_users_name ON Users(Name)` is executed, the index automatically
inherits the NOCASE collation from the column metadata. No extra syntax needed.

---

### Phase 5: Query-Level COLLATE Override (P2 — Power Users)

**Goal:** Allow per-expression collation override and built-in LOWER()/UPPER() functions.

#### Target Syntax
```sql
SELECT * FROM Users WHERE Name COLLATE NOCASE = @var;
SELECT * FROM Users WHERE LOWER(Name) = LOWER(@name);
```

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB/Services/EnhancedSqlParser.*.cs` | Parse `COLLATE` as unary expression modifier on column references |
| `src/SharpCoreDB/Services/SqlAst.Nodes.cs` | Add `CollateExpressionNode` AST node |
| `src/SharpCoreDB/Services/SqlParser.DML.cs` → `AstExecutor` | Evaluate `CollateExpressionNode` during WHERE filtering |
| Function evaluation system | Add `LOWER()`, `UPPER()` built-in function support |

#### New AST Node

```csharp
/// <summary>
/// Represents a COLLATE expression modifier (e.g., Name COLLATE NOCASE).
/// </summary>
public class CollateExpressionNode : ExpressionNode
{
    public required ExpressionNode Operand { get; set; }
    public required CollationType Collation { get; set; }
}
```

---

### Phase 6: Locale-Aware Collations (P3 — Future / Internationalization)

**Goal:** Culture-specific collation with ICU-based sorting.

#### Target Syntax
```sql
CREATE INDEX idx_name_ci ON users (name COLLATE "en_US" NOCASE);
CREATE INDEX idx_name_de ON users (name COLLATE "de_DE");
```

#### Design Considerations
- Collation registry: map collation names → `CultureInfo` + case rules
- ICU-based comparison via `CompareInfo.GetSortKey()` for index key materialization
- Sort key materialization for indexes (store `CompareInfo.GetSortKey()` bytes)
- Potential `CollationDefinition` class for custom collation registration
- Performance: culture-aware comparison is 10-100x slower than ordinal — cache sort keys

#### This phase requires:
- Collation name registry (e.g., "en_US", "de_DE", "tr_TR")
- Extended DDL syntax for quoted collation names
- Sort key storage in B-Tree nodes
- Careful handling of Turkish I problem, German ß, etc.

---

## 5. EF Core Integration (Separate Deliverable)

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB.EntityFrameworkCore/Migrations/SharpCoreDBMigrationsSqlGenerator.cs` → `ColumnDefinition()` | Emit `COLLATE <name>` after type and NOT NULL |
| `src/SharpCoreDB.EntityFrameworkCore/Storage/SharpCoreDBTypeMappingSource.cs` | Map `UseCollation()` to `CollationType` |

#### EF Core Fluent API

```csharp
modelBuilder.Entity<User>()
    .Property(u => u.Name)
    .UseCollation("NOCASE");

// Generates:
// Name TEXT COLLATE NOCASE
```

---

## 6. Test Plan

### Unit Tests

| Test | Phase | File |
|---|---|---|
| `CreateTable_WithCollateNoCase_ShouldStoreCollation` | 1-2 | `CollationDDLTests.cs` |
| `CreateTable_WithCollateBinary_ShouldBeDefault` | 1-2 | `CollationDDLTests.cs` |
| `CreateTable_WithInvalidCollation_ShouldThrow` | 2 | `CollationDDLTests.cs` |
| `Select_WithNoCaseColumn_ShouldMatchCaseInsensitive` | 3 | `CollationQueryTests.cs` |
| `Select_WithBinaryColumn_ShouldBeCaseSensitive` | 3 | `CollationQueryTests.cs` |
| `Select_WithRTrimColumn_ShouldIgnoreTrailingSpaces` | 3 | `CollationQueryTests.cs` |
| `HashIndex_WithNoCaseCollation_ShouldNormalizeKeys` | 4 | `CollationIndexTests.cs` |
| `BTreeIndex_WithNoCaseCollation_ShouldSortCaseInsensitive` | 4 | `CollationIndexTests.cs` |
| `QueryOverride_CollateNoCase_ShouldOverrideColumnCollation` | 5 | `CollationQueryTests.cs` |
| `LowerFunction_ShouldReturnLowercase` | 5 | `CollationQueryTests.cs` |
| `SaveMetadata_WithCollation_ShouldPersistAndReload` | 1 | `CollationPersistenceTests.cs` |
| `EFCore_UseCollation_ShouldEmitCollateDDL` | EF | `CollationEFCoreTests.cs` |

### Integration Tests

| Test | Phase |
|---|---|
| Create table with NOCASE → insert mixed-case → SELECT with exact case → should match | 3 |
| Create table with NOCASE → create index → lookup with different case → should find via index | 4 |
| Roundtrip: create table → save metadata → reload → verify collation preserved | 1 |

---

## 7. Backward Compatibility

- **Default behavior unchanged:** All existing tables default to `CollationType.Binary` (case-sensitive)
- **Metadata migration:** Existing databases without `ColumnCollations` in metadata will default to all-Binary
- **API backward compatible:** All new parameters are optional with Binary defaults
- **Index backward compatible:** Existing indexes continue to work with binary comparison

---

## 8. Performance Considerations

| Concern | Mitigation |
|---|---|
| Collation check in hot path (WHERE eval) | Single enum switch — zero allocation, ~2ns overhead |
| NOCASE key normalization in index | `ToUpperInvariant()` on insert/lookup — one-time per operation |
| Culture-aware comparison (Phase 6) | Cache `CompareInfo.GetSortKey()` in B-Tree nodes |
| Span-based comparison | `ReadOnlySpan<char>.CompareTo()` avoids string allocation |

---

## 9. Dependencies and Risks

| Risk | Mitigation |
|---|---|
| Breaking change to `ITable` interface | Add with default implementation or use adapter pattern |
| Metadata format change | Backward-compatible: missing `ColumnCollations` → all Binary |
| Performance regression on hot paths | Benchmark before/after with BenchmarkDotNet |
| Locale collation complexity (Phase 6) | Defer to P3; start with ordinal-based NOCASE only |

---

## 10. Delivery Timeline (Suggested)

| Phase | Deliverable | Can Ship With |
|---|---|---|
| Phase 1 + 2 | Core types + DDL parsing | Together as foundation |
| Phase 3 | Collation-aware WHERE | Immediately after Phase 2 |
| Phase 4 | Index integration | Can follow Phase 3 independently |
| Phase 5 | Query-level COLLATE | Separate release |
| Phase 6 | Locale-aware | Separate release, needs research |
| EF Core | UseCollation support | After Phase 2 minimum |

---

## 11. Files Summary (All Phases)

### New Files
| File | Phase |
|---|---|
| `src/SharpCoreDB/CollationType.cs` | 1 |
| `tests/SharpCoreDB.Tests/CollationDDLTests.cs` | 2 |
| `tests/SharpCoreDB.Tests/CollationQueryTests.cs` | 3 |
| `tests/SharpCoreDB.Tests/CollationIndexTests.cs` | 4 |
| `tests/SharpCoreDB.Tests/CollationPersistenceTests.cs` | 1 |

### Modified Files
| File | Phase |
|---|---|
| `src/SharpCoreDB/Services/SqlAst.DML.cs` | 1 |
| `src/SharpCoreDB/Interfaces/ITable.cs` | 1 |
| `src/SharpCoreDB/DataStructures/Table.cs` | 1 |
| `src/SharpCoreDB/DataStructures/ColumnInfo.cs` | 1 |
| `src/SharpCoreDB/Database/Core/Database.Core.cs` | 1 |
| `src/SharpCoreDB/Database/Core/Database.Metadata.cs` | 1 |
| `src/SharpCoreDB/Services/SqlParser.DML.cs` (InMemoryTable) | 1 |
| `src/SharpCoreDB/Services/SqlParser.DDL.cs` | 2 |
| `src/SharpCoreDB/Services/EnhancedSqlParser.DDL.cs` | 2 |
| `src/SharpCoreDB/Services/SqlParser.Helpers.cs` | 3 |
| `src/SharpCoreDB/Services/SqlParser.InExpressionSupport.cs` | 3 |
| `src/SharpCoreDB/Services/CompiledQueryExecutor.cs` | 3 |
| `src/SharpCoreDB/DataStructures/HashIndex.cs` | 4 |
| `src/SharpCoreDB/DataStructures/GenericHashIndex.cs` | 4 |
| `src/SharpCoreDB/DataStructures/BTree.cs` | 4 |
| `src/SharpCoreDB/DataStructures/Table.Indexing.cs` | 4 |
| `src/SharpCoreDB/Services/SqlAst.Nodes.cs` | 5 |
| `src/SharpCoreDB/Services/EnhancedSqlParser.*.cs` | 5 |
| `src/SharpCoreDB.EntityFrameworkCore/Migrations/SharpCoreDBMigrationsSqlGenerator.cs` | EF |
| `src/SharpCoreDB.EntityFrameworkCore/Storage/SharpCoreDBTypeMappingSource.cs` | EF |

---

**GitHub Issue:** See linked issue for tracking.  
**Last Updated:** 2025-07-14
