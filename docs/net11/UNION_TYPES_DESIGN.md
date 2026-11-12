# Union Types in SharpCoreDB — Design Pass (v2.1 Preview)

> Status: **IMPLEMENTED at .NET 11 GA** (2026-11-12) — the opt-in `SharpCoreDB.Unions` namespace ships in
> `src/SharpCoreDB/Net11/Unions/UnionOutcomes.cs` with tests in
> `tests/SharpCoreDB.Tests/Net11/UnionOutcomeTests.cs`. This document stays as the design record; the
> shipped code uses the `union` keyword form (`public union QueryOutcome(QuerySuccess, QueryNotFound,
> QueryFailure);`) rather than the hand-written `[Union]` class shape proposed below, because the GA
> compiler generates that shape itself. Reference: [Union types — C# reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/union)

## Status

**Implemented and shipped** in this line (2026-11-12) as `SharpCoreDB.Unions`; the syntax was verified on
RC1 and the GA compiler (`11.0.100`) accepts the `union` keyword form unchanged. The runtime ships
`UnionAttribute`/`IUnion` since .NET 11 Preview 5 and they are GA in .NET 11.

**Decision as taken:** a **new opt-in namespace** `SharpCoreDB.Unions` (net11.0-only), existing result
types **never retrofitted** (`QueryResult` / `ExecuteResult` / `ExecuteSQL` signatures stay byte-identical),
and the `union` keyword rather than the hand-written `[Union]` class below — the compiler generates that
shape, so there is nothing to hand-maintain.

## Verified syntax (compiles + runs on RC1)

```csharp
public record class Cat(string Name);
public record class Dog(string Name);

// Modern union keyword: the compiler generates a `struct : IUnion` with a `Value` property and
// implicit conversions from each case type.
public union Pet(Cat, Dog);

// Class-based union (reference semantics / private constructors / factories):
[System.Runtime.CompilerServices.Union]
public class Result<T> : System.Runtime.CompilerServices.IUnion
{
    private readonly object? _value;
    public Result(T? value) { _value = value; }
    public object? Value => _value;
}

// Exhaustive switch: removing any case below is a COMPILE ERROR.
string Describe(Pet pet) => pet switch
{
    Cat c => $"Cat {c.Name}",
    Dog d => $"Dog {d.Name}",
};
```

Key semantics: cases can be classes, structs, interfaces, type parameters, nullable types or other unions;
value-type cases are boxed into `Value`; unions are structs without equality/cloning/deconstruction
(put those on the case types, e.g. `record class` cases).

## Proposed API: `SharpCoreDB.Unions` (additive, opt-in, net11.0-only)

```csharp
#if NET11_0_OR_GREATER
namespace SharpCoreDB.Unions;

using System.Runtime.CompilerServices;
using SharpCoreDB.DataStructures;

// Query outcome — compile-time exhaustive alternative to returning nullable/boolean-or-throw.
public record class QuerySuccess(IReadOnlyList<Dictionary<string, object?>> Rows);
public record class QueryNotFound;
public record class QueryFailure(string Message);
[Union]
public partial class QueryOutcome : IUnion
{
    private readonly object? _value;
    public QueryOutcome(QuerySuccess value) { _value = value; }
    public QueryOutcome(QueryNotFound value) { _value = value; }
    public QueryOutcome(QueryFailure value) { _value = value; }
    public object? Value => _value;
}

// Execute outcome (rows affected or failure).
public record class ExecuteSuccess(long RowsAffected);
public record class ExecuteFailure(string Message);
[Union]
public partial class ExecuteOutcome : IUnion
{
    private readonly object? _value;
    public ExecuteOutcome(ExecuteSuccess value) { _value = value; }
    public ExecuteOutcome(ExecuteFailure value) { _value = value; }
    public object? Value => _value;
}

// Table lookup (used with the extension indexer db["name"]).
public record class TableFound(TableInfo Table);
public record class TableMissing(string Name);
[Union]
public partial class TableLookup : IUnion
{
    private readonly object? _value;
    public TableLookup(TableFound value) { _value = value; }
    public TableLookup(TableMissing value) { _value = value; }
    public object? Value => _value;
}
#endif
```

Usage (opt-in, only on net11.0):

```csharp
using SharpCoreDB.Unions;

QueryOutcome outcome = ...;                    // returned by NEW opt-in methods only
string text = outcome switch
{
    QuerySuccess s => $"rows: {s.Rows.Count}",
    QueryNotFound  => "not found",
    QueryFailure f => $"error: {f.Message}",
};
```

## Backward compatibility

- The file is source-gated `#if NET11_0_OR_GREATER` → **compiled only for net11.0**; net10.0 consumers
  never see it.
- Existing `QueryResult` / `ExecuteResult` / `ExecuteSQL` signatures stay **byte-identical** on both TFMs.
- Union values can be inspected generically via `IUnion`:
  `if (outcome is IUnion { Value: null }) { ... }`.

## Performance notes

- Value-type cases are boxed into the `object? Value`.
- Unions are structs; for rich equality/deconstruction keep `record class` case types.
- The `QueryOutcome` design above uses `[Union]` **class** unions (reference semantics) to keep the
  API familiar; the `union` keyword equivalent (`public union QueryOutcome(QuerySuccess, QueryNotFound, QueryFailure);`)
  is the lighter-weight struct alternative.

## Test strategy

1. **Compile-time exhaustiveness** (the core value): a test that switches over `QueryOutcome` with all
   cases; add a 4th case type later and watch CI fail until every switch handles it.
2. **Runtime**: construct every case, switch-match it, verify `IUnion.Value` boxing, verify implicit
   conversion from each case type.
3. Existing suite stays green: net10.0 1679 / net11.0 1688 (new tests are net11-gated).

## Risk & decision

- Union types are still **C# 15 preview**; the implementation (visibility, `Value` boxing, member
  requirements) can shift between RC and GA.
- Because existing APIs are untouched and the new surface is opt-in, shipping early is low-risk, but
  frozen syntax is higher-value.
- **Decision:** keep `SharpCoreDB.Unions` as a design blueprint for the 2.1 preview; implement it once
  .NET 11 GA / final C# 15 confirms the syntax.
