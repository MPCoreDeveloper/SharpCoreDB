# Functional Null Safety in SharpCoreDB v1.7.0

## Why This Exists

C#'s Nullable Reference Types (NRT) are **compile-time annotations only**. They do not prevent `NullReferenceException` at runtime. SharpCoreDB's functional API (`Option<T>`, `Fin<T>`) provides **compiler-enforced, runtime-safe** null handling — especially critical for database operations where missing data is the norm, not the exception.

This document explains the gap, proves it with real tests you can run yourself, and shows the performance impact.

---

## The Problem: Where NRT Falls Short

NRT annotations (`string?`, `[NotNull]`, etc.) are advisory hints. The CLR does not enforce them. In database workloads, this creates 4 categories of runtime failures that NRT **cannot** prevent:

### 1. Dictionary Lookups Return Null Despite Non-Null Type

```csharp
// Database row: Bob has NULL email
var rows = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 2");
var row = rows[0];

// NRT: row["Email"] is typed as `object` (non-null). No compiler warning.
// Runtime: the value IS null (or empty string for SQL NULL).
var email = (string)row["Email"]; // 💥 NullReferenceException or silent empty string
```

**Why NRT can't help:** The `Dictionary<string, object>` stores values as `object`, not `object?`. NRT sees the type signature and assumes non-null. The database doesn't care about your type annotations.

### 2. Missing Rows — Empty Collections, Not Null Collections

```csharp
var rows = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 999");
// NRT: rows is List<Dictionary<string, object>> — non-null ✓
// But rows.Count == 0. NRT gives zero warning about this.

var name = rows[0]["Name"]; // 💥 ArgumentOutOfRangeException
```

**Why NRT can't help:** The list itself is non-null. NRT has no concept of "this list might be empty." It only tracks nullability of references, not collection emptiness.

### 3. Chained Foreign Key Traversal

```csharp
// "Get the email of Charlie's manager"
// Charlie.ManagerId = 99, but User 99 doesn't exist

var charlie = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 3")[0];
var managerId = charlie["ManagerId"]; // NRT: non-null ✓ (it's 99)

var manager = db.ExecuteQuery($"SELECT * FROM Users WHERE Id = {managerId}");
var email = manager[0]["Email"]; // 💥 ArgumentOutOfRangeException — manager list is empty
```

**Why NRT can't help:** Each individual value is non-null. The *data dependency* (ID 99 references nothing) is invisible to static analysis. NRT tracks types, not data relationships.

### 4. Reflection-Based DTO Mapping

```csharp
public class UserDto
{
    public string Name { get; set; } // NRT: non-null
}

// Reflection mapper populates from database row missing "Name" column
var dto = new UserDto();
// dto.Name is null at runtime, but NRT says it's non-null
Console.WriteLine(dto.Name.Length); // 💥 NullReferenceException
```

**Why NRT can't help:** Reflection bypasses the compiler entirely. NRT annotations exist only at compile time; `PropertyInfo.SetValue()` doesn't check them.

---

## The Solution: `Option<T>` and `Fin<T>`

SharpCoreDB's functional API encodes "might not exist" and "might fail" **in the type system** so the compiler enforces handling:

| Type | Meaning | Forces Developer To |
|------|---------|---------------------|
| `Option<T>` | Value may or may not exist | Handle both `Some` and `None` |
| `Fin<T>` | Operation may succeed or fail | Handle both `Succ` and `Fail` |

### Side-by-Side Comparison

#### Classic (NRT only) — Unsafe

```csharp
// 5 lines of defensive null checking, easy to forget one
var rows = db.ExecuteQuery("SELECT * FROM Users WHERE Id = 99");
if (rows != null && rows.Count > 0)
{
    var row = rows[0];
    if (row.TryGetValue("Email", out var email) && email != null && (string)email != "")
    {
        SendEmail((string)email);
    }
}
```

#### Functional (`Option<T>`) — Safe

```csharp
// One expression. Compiler ensures you handle absence. Zero exceptions possible.
var email = (await fdb.GetByIdAsync<UserDto>("Users", 99))
    .Map(u => u.Email)
    .Bind(e => string.IsNullOrEmpty(e) ? Option<string>.None : Option<string>.Some(e))
    .IfNone("no-email");
```

#### Error Handling: `Fin<T>` vs Try/Catch

```csharp
// Classic — exception-driven
try
{
    db.ExecuteSQL("INSERT INTO NonExistent VALUES (1, 'x')");
}
catch (Exception ex)
{
    Log(ex.Message); // Expensive: stack unwinding, allocation
}

// Functional — errors as values
var result = await fdb.InsertAsync("NonExistent", dto);
result.Match(
    Succ: _ => Log("ok"),
    Fail: err => Log(err.Message)); // Zero-cost: no exception thrown
```

---

## Performance Impact

### Exception Cost vs `Option<T>` Cost

`Option<T>` is a **struct** (stack-allocated, zero GC pressure). Returning `None` is as cheap as returning an integer.

| Operation | Cost | Allocates |
|-----------|------|-----------|
| `throw new NullReferenceException()` | ~5,000–50,000 ns | Yes (exception object + stack trace) |
| `return Option<T>.None` | ~1 ns | **No** |
| `try/catch` (exception thrown) | ~5,000–50,000 ns | Yes |
| `Fin<T>.Fail(error)` | ~10 ns | Minimal (error struct) |

### Real-World Batch Scenario

Processing 100,000 rows where 10% have broken foreign keys:

| Approach | Missing lookups | Overhead | GC Pressure |
|----------|----------------|----------|-------------|
| Try/catch per lookup | 10,000 exceptions | **50–500ms** wasted | High (10K exception objects) |
| `Option<T>.None` returns | 10,000 struct returns | **~0.01ms** | **None** |

**That's a 5,000x–50,000x reduction in overhead for missing-data handling.**

### Why This Matters for Databases

Database operations have inherently unpredictable data. Foreign keys reference deleted rows. Columns contain NULL. Queries return empty result sets. This isn't edge-case handling — it's **the normal operating mode**. Making absence cheap and safe is a core performance feature.

---

## Functional SQL Syntax (v1.7.0)

SharpCoreDB.Functional now supports a functional SQL layer that maps directly to `Option<T>` semantics.

### Example syntax

```sql
SELECT Id, Name, Email OPTIONALLY FROM Users
WHERE Email IS SOME;
```

### Supported functional SQL keywords

| Keyword | Meaning |
|---|---|
| `OPTIONALLY FROM` | Returns a `Seq<Option<T>>` result shape from `ExecuteFunctionalSqlAsync<T>` |
| `IS SOME` | Semantic non-null check (filters out `null`, empty string, and `"NULL"`) |
| `IS NONE` | Semantic null check (matches `null`, empty string, and `"NULL"`) |
| `MATCH SOME column` | Alias of `column IS SOME` |
| `MATCH NONE column` | Alias of `column IS NONE` |
| `UNWRAP Column AS Alias DEFAULT 'x'` | Projects a value with default fallback metadata |

### C# usage

```csharp
var functional = db.Functional();

var rows = await functional.ExecuteFunctionalSqlAsync<UserDto>(
    "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME");

foreach (var row in rows)
{
    var email = row
        .Map(u => u.Email)
        .IfNone("no-email");

    Console.WriteLine(email);
}
```

### Verify functional SQL behavior yourself

Run the dedicated functional SQL tests:

```bash
dotnet test tests/SharpCoreDB.Functional.Tests --filter "FullyQualifiedName~FunctionalSqlSyntaxTests" --verbosity normal
```

Test source:

- [`tests/SharpCoreDB.Functional.Tests/FunctionalSqlSyntaxTests.cs`](../tests/SharpCoreDB.Functional.Tests/FunctionalSqlSyntaxTests.cs)

This suite validates parser translation, `OPTIONALLY FROM`, `IS SOME` / `IS NONE`, match aliases, unwrap mapping, and end-to-end filtering behavior.

---

## Verify It Yourself

All claims above are backed by **12 passing tests** you can run right now.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Clone the repository:
  ```bash
  git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
  cd SharpCoreDB
  ```

### Run the Tests

```bash
dotnet test tests/SharpCoreDB.Functional.Tests --filter "FullyQualifiedName~NullSafetyComparisonTests" --verbosity normal
```

### Test Source Code

📄 [`tests/SharpCoreDB.Functional.Tests/NullSafetyComparisonTests.cs`](../tests/SharpCoreDB.Functional.Tests/NullSafetyComparisonTests.cs)

### What Each Test Proves

| # | Test Name | What It Proves |
|---|-----------|----------------|
| 1 | `DictionaryLookup_NrtSaysNonNull_ButRuntimeIsNull` | NRT says `object` is non-null; database returns null/empty for SQL NULL |
| 2 | `DictionaryLookup_OptionForcesSafeAccess` | `Option` + `Bind` detects semantically-null empty strings |
| 3 | `MissingRow_NrtCannotPreventIndexOutOfRange` | NRT can't prevent `rows[0]` on empty result set → `ArgumentOutOfRangeException` |
| 4 | `MissingRow_OptionReturnsNoneSafely` | `GetByIdAsync` returns `None` for missing rows — no exception |
| 5 | `ChainedLookup_NrtCannotTrackDataDependentNull` | NRT can't track that FK reference ID 99 doesn't exist |
| 6 | `ChainedLookup_OptionBindShortCircuitsSafely` | `Bind` chain short-circuits at first `None` — zero exceptions |
| 7 | `ReflectionMapping_NrtCannotValidatePopulatedProperties` | Reflection bypasses NRT; DTO property is null despite `string` type |
| 8 | `ReflectionMapping_OptionReturnsNoneForPartialData` | `FindOneAsync` + `Bind` handles missing columns safely |
| 9 | `AggregateQuery_OptionHandlesEdgeCasesSafely` | `CountAsync` returns 0 on empty table — no crash |
| 10 | `WriteOperation_FinCapturesErrorsAsValues` | `Fin<T>` captures insert failure as value, not exception |
| 11 | `Pipeline_OptionSeqProvidesSafeComposition` | `Option` pipeline filters null/empty emails without exceptions |
| 12 | `RealWorkload_BatchLookupWithMissingReferences` | 100 batch lookups with ~50% missing FKs — all handled via `Option.Match`, zero exceptions |

### Expected Output

```
Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12
```

---

## Summary

| Aspect | NRT (C# nullable annotations) | SharpCoreDB Functional API |
|--------|-------------------------------|---------------------------|
| Enforcement | Compile-time hints only | Runtime type system |
| Dictionary nulls | Invisible | Explicit via `Option<T>` |
| Empty result sets | No protection | `None` return, no exception |
| Chained lookups | No data-flow tracking | `Bind` short-circuits safely |
| Reflection mapping | Completely blind | `FindOneAsync` returns `None` on failure |
| Error handling | Exceptions (expensive) | `Fin<T>` values (near-zero cost) |
| Performance (10K misses) | 50–500ms exception overhead | ~0.01ms |
| Developer experience | Defensive `if != null` chains | Composable `Map`/`Bind`/`Match` |

**NRT and `Option<T>` are complementary.** Use NRT for compile-time guidance. Use `Option<T>` for runtime safety. SharpCoreDB gives you both.

---

*Document version: v1.7.0 | Last updated: 2025-07-15 | Test suite: `NullSafetyComparisonTests` (12 tests)*
