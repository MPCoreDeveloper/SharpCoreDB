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

**Goal:** Full collation support in the EF Core provider — DDL generation, query translation,
`EF.Functions.Collate()`, and `string.Equals(x, StringComparison)` translation.

See also **Section 12** for the ORM-vs-DB collation mismatch problem this solves.

#### Modified Files
| File | Change |
|---|---|
| `src/SharpCoreDB.EntityFrameworkCore/Migrations/SharpCoreDBMigrationsSqlGenerator.cs` → `ColumnDefinition()` | Emit `COLLATE <name>` after type and NOT NULL |
| `src/SharpCoreDB.EntityFrameworkCore/Storage/SharpCoreDBTypeMappingSource.cs` | Map `UseCollation()` to `CollationType` |
| `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBStringMethodCallTranslator.cs` | Translate `string.Equals(string, StringComparison)` → `COLLATE` SQL |
| `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBQuerySqlGenerator.cs` | Emit `COLLATE <name>` expression in SQL visitor |
| `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBMethodCallTranslatorPlugin.cs` | Register collate translator |
| New: `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBCollateTranslator.cs` | Translate `EF.Functions.Collate()` calls to SQL |

#### 5.1 EF Core Fluent API — DDL Generation

```csharp
modelBuilder.Entity<User>()
    .Property(u => u.Name)
    .UseCollation("NOCASE");

// Generates:
// Name TEXT COLLATE NOCASE
```

#### 5.2 EF.Functions.Collate() — Query-Level Override

```csharp
// Explicit collation override (standard EF Core pattern)
var users = await context.Users
    .Where(u => EF.Functions.Collate(u.Name, "NOCASE") == "john")
    .ToListAsync();

// Generated SQL:
// SELECT * FROM Users WHERE Name COLLATE NOCASE = 'john'
```

#### 5.3 string.Equals(string, StringComparison) Translation (SharpCoreDB-Specific)

Other EF Core providers silently drop the `StringComparison` parameter.
SharpCoreDB can do better because we control both sides:

```csharp
// C# idiomatic case-insensitive comparison
var users = db.Users
    .Where(u => u.Name.Equals("john", StringComparison.OrdinalIgnoreCase))
    .ToList();

// SharpCoreDB generates:
// SELECT * FROM Users WHERE Name COLLATE NOCASE = 'john'
//
// Other EF providers would generate:
// SELECT * FROM Users WHERE Name = 'john'  ← WRONG if column is CS!
```

**StringComparison → SQL mapping:**
| C# `StringComparison` | Generated SQL |
|---|---|
| `Ordinal` | `WHERE Name = 'value'` (no COLLATE — uses column default) |
| `OrdinalIgnoreCase` | `WHERE Name COLLATE NOCASE = 'value'` |
| `CurrentCultureIgnoreCase` | `WHERE Name COLLATE UNICODE_CI = 'value'` (Phase 6) |
| `InvariantCultureIgnoreCase` | `WHERE Name COLLATE NOCASE = 'value'` |

**Implementation in `SharpCoreDBStringMethodCallTranslator.cs`:**
```csharp
private static readonly MethodInfo _equalsWithComparisonMethod =
    typeof(string).GetRuntimeMethod(nameof(string.Equals),
        [typeof(string), typeof(StringComparison)])!;

// In Translate():
if (method == _equalsWithComparisonMethod && instance is not null)
{
    var comparisonArg = arguments[1];
    if (comparisonArg is SqlConstantExpression { Value: StringComparison comparison })
    {
        var collation = comparison switch
        {
            StringComparison.OrdinalIgnoreCase => "NOCASE",
            StringComparison.InvariantCultureIgnoreCase => "NOCASE",
            StringComparison.CurrentCultureIgnoreCase => "UNICODE_CI",
            _ => null // No COLLATE for case-sensitive comparisons
        };

        if (collation is not null)
        {
            // Emit: column COLLATE NOCASE = @value
            return _sqlExpressionFactory.Equal(
                _sqlExpressionFactory.Collate(instance, collation),
                arguments[0]);
        }

        // Case-sensitive: standard equality
        return _sqlExpressionFactory.Equal(instance, arguments[0]);
    }
}
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
| `EFCore_StringEqualsIgnoreCase_ShouldEmitCollateNoCase` | EF | `CollationEFCoreTests.cs` |
| `EFCore_StringEqualsOrdinal_ShouldNotEmitCollate` | EF | `CollationEFCoreTests.cs` |
| `EFCore_EFFunctionsCollate_ShouldEmitCollateClause` | EF | `CollationEFCoreTests.cs` |
| `EFCore_NoCaseColumn_SimpleEquals_ShouldReturnBothCases` | EF | `CollationEFCoreTests.cs` |
| `EFCore_CSColumn_IgnoreCase_ShouldLogDiagnosticWarning` | EF | `CollationEFCoreTests.cs` |

### Integration Tests

| Test | Phase |
|---|---|
| Create table with NOCASE → insert mixed-case → SELECT with exact case → should match | 3 |
| Create table with NOCASE → create index → lookup with different case → should find via index | 4 |
| Roundtrip: create table → save metadata → reload → verify collation preserved | 1 |
| **ORM mismatch scenario:** CS column + `Equals(x, OrdinalIgnoreCase)` → returns both rows | EF |
| **ORM mismatch scenario:** NOCASE column + simple `== "john"` → returns both rows | EF |

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
| `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBStringMethodCallTranslator.cs` | EF |
| `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBQuerySqlGenerator.cs` | EF |
| `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBMethodCallTranslatorPlugin.cs` | EF |
| New: `src/SharpCoreDB.EntityFrameworkCore/Query/SharpCoreDBCollateTranslator.cs` | EF |

---

## 12. Critical Use Case: ORM-vs-Database Collation Mismatch

> **Source:** LinkedIn discussion (Dave Callan / Dmitry Maslov / Shay Rojansky — EF Core team)

### The Problem

There is a **fundamental semantic contradiction** between how C# LINQ and SQL handle
string comparisons when collation is involved:

```csharp
// Developer writes this C# LINQ query:
var users = db.Users
    .Where(u => u.Name.Equals("john", StringComparison.OrdinalIgnoreCase))
    .ToList();

// Developer EXPECTS: 2 records ("John" and "john")
// EF Core DEFAULT behavior: generates  WHERE Name = 'john'
// If column is COLLATE CS (case-sensitive): returns ONLY "john" → 1 record!
```

The database was created with a case-sensitive collation:
```sql
CREATE TABLE Users (
    Id  INT IDENTITY PRIMARY KEY,
    Name NVARCHAR(50) COLLATE Latin1_General_CS_AS   -- case-sensitive!
);

INSERT INTO Users (Name) VALUES ('John'), ('john');
```

The C# code says "compare case-insensitively" but the database has a case-sensitive
collation on the column. **The ORM cannot resolve this contradiction silently** because:

1. EF Core translates `.Equals("john", OrdinalIgnoreCase)` to `WHERE Name = 'john'`
   by default — it drops the `StringComparison` parameter entirely
2. The SQL engine then applies the column's collation (`CS_AS`) → case-sensitive match
3. Result: only 1 record instead of the expected 2

### Why This Is Hard (Industry-Wide)

As the EF Core team (Shay Rojansky) has noted, this is an unsolvable problem from
the ORM side alone:
- The ORM doesn't know the column's collation at query translation time
- `StringComparison` in C# doesn't map 1:1 to SQL collations
- Different databases have different collation systems
- Silently adding `COLLATE` to every string comparison would break indexes

### SharpCoreDB Advantage: We Control Both Sides

Unlike generic EF Core providers, **we own both the ORM provider AND the SQL engine**.
This gives us three strategies that other databases can't offer:

#### Strategy A: `EF.Functions.Collate()` — Explicit Query-Level Override (Recommended)

The standard EF Core approach. Developer explicitly requests collation in the query:

```csharp
// ✅ EXPLICIT: Developer knows what they want
var users = await context.Users
    .Where(u => EF.Functions.Collate(u.Name, "NOCASE") == "john")
    .ToListAsync();

// Generated SQL:
// SELECT * FROM Users WHERE Name COLLATE NOCASE = 'john'
```

**Implementation:** Add `EF.Functions.Collate()` translation to the
`SharpCoreDBStringMethodCallTranslator`.

#### Strategy B: `string.Equals(x, StringComparison)` → COLLATE Translation

SharpCoreDB-specific: we can translate the `StringComparison` overload since we
know our collation system:

```csharp
// ✅ C# idiomatic — SharpCoreDB translates the StringComparison
var users = db.Users
    .Where(u => u.Name.Equals("john", StringComparison.OrdinalIgnoreCase))
    .ToList();

// Generated SQL (SharpCoreDB-specific):
// SELECT * FROM Users WHERE Name COLLATE NOCASE = 'john'
```

Mapping table:
| `StringComparison` | SharpCoreDB SQL |
|---|---|
| `Ordinal` | `= 'value'` (no COLLATE, uses column default) |
| `OrdinalIgnoreCase` | `COLLATE NOCASE = 'value'` |
| `CurrentCultureIgnoreCase` | `COLLATE UNICODE_CI = 'value'` (Phase 6) |
| `InvariantCultureIgnoreCase` | `COLLATE NOCASE = 'value'` |

**Implementation:** Add `string.Equals(string, StringComparison)` overload to
`SharpCoreDBStringMethodCallTranslator.cs`.

#### Strategy C: Column Collation Awareness at Translation Time

Since we control the provider, we can read column metadata during query translation
and emit a **warning** when the C# comparison semantics conflict with the column collation:

```
⚠️ SharpCoreDB Warning: Column 'Users.Name' has COLLATE BINARY (case-sensitive),
but query uses StringComparison.OrdinalIgnoreCase. Consider using
EF.Functions.Collate() or setting .UseCollation("NOCASE") on the property.
```

### SharpCoreDB Resolution: The "No Surprise" Approach

For SharpCoreDB, we recommend the following behavior:

1. **Column defined with `COLLATE NOCASE`** → All comparisons on that column are
   case-insensitive by default. `WHERE Name = 'john'` matches both `'John'` and `'john'`.
   No mismatch possible.

2. **Column defined with `COLLATE BINARY` (default)** + C# `OrdinalIgnoreCase` →
   The EF Core provider emits `COLLATE NOCASE` in the generated SQL to honor the
   developer's intent. This is safe because SharpCoreDB's query engine evaluates
   `COLLATE` per-expression (Phase 5).

3. **`EF.Functions.Collate()`** → Always available as the explicit escape hatch,
   matching EF Core conventions.

### Test Cases for This Scenario

| Test | Expected Behavior |
|---|---|
| `CS_Column_EqualsIgnoreCase_ShouldEmitCollateNoCase` | `Name.Equals("john", OrdinalIgnoreCase)` → SQL contains `COLLATE NOCASE` |
| `NOCASE_Column_SimpleEquals_ShouldMatchBothCases` | Column is NOCASE → `WHERE Name = 'john'` returns both 'John' and 'john' |
| `EFCollateFunction_ShouldEmitCollateClause` | `EF.Functions.Collate(u.Name, "NOCASE")` → SQL contains `Name COLLATE NOCASE` |
| `CS_Column_OrdinalEquals_ShouldNotAddCollate` | `Name.Equals("john", Ordinal)` → no COLLATE in SQL (honor DB collation) |
| `MismatchWarning_CS_Column_IgnoreCase_ShouldLogWarning` | CS column + IgnoreCase → diagnostic warning logged |

### Files Impacted (Additional to existing plan)

| File | Change | Phase |
|---|---|---|
| `SharpCoreDBStringMethodCallTranslator.cs` | Add `string.Equals(string, StringComparison)` overload + `EF.Functions.Collate()` | EF Core |
| `SharpCoreDBQuerySqlGenerator.cs` | Emit `COLLATE <name>` expression in SQL output | EF Core |
| `SharpCoreDBMethodCallTranslatorPlugin.cs` | Register collate translator | EF Core |
| New: `SharpCoreDBCollateTranslator.cs` | Translate `EF.Functions.Collate()` calls | EF Core |
| `SqlAst.Nodes.cs` → `CollateExpressionNode` | Already in Phase 5 | 5 |

---

**GitHub Issue:** See linked issue for tracking.  
**Last Updated:** 2025-07-14
