# Query Optimizer - Implementation Guide

> **Verified against SharpCoreDB 2.1.0.** The optimizer on the engine's query path is
> `SharpCoreDB.Optimization.QueryOptimizer` (`src/SharpCoreDB/Optimization/QueryOptimizer.cs`),
> constructed with `Optimization.CostEstimator` from `Services/QueryCompiler.cs`. The `Planning/`
> types (`QueryOptimizer`, `CardinalityEstimator`, `PredicatePushdown`) and `Query/CostBasedOptimizer.cs`
> are real, compiling components that **no shipped call site constructs** — building blocks, not the
> active path. Sections that used to describe components which do not exist at all
> (`SubqueryOptimizer`, `JoinReorderer`, `OptimizerCache`, `QueryOptimizerExtensions`,
> `OptimizerIntegration`, `InvalidateOptimizationCache()`) are corrected below and marked as such.

## Overview

A lightweight, cost-based query optimizer for SharpCoreDB with logical/physical plan separation.

**Key Characteristics**:
- ✅ Cost-based plan selection
- ✅ `readonly struct` plan nodes, no LINQ and no async on the plan path
- ✅ Plans cached per compiled query (`Database/Caching/Database.PlanCaching.cs`)
- ⚠️ Join reordering and subquery elimination are **not** implemented on this path — see §4 and §6

## Architecture

### Optimization pipeline (what `Optimization.QueryOptimizer.Optimize` does)

```
Parse AST → Logical view (shape flags, estimated rows) → Costed plan → Physical steps → Execute
             one pass, no rewrite phases
```

`Optimize(SelectNode)` runs three steps, in this order:

**1. Logical view** (`BuildLogicalPlan`) — derives shape flags from the AST and an estimated row count
from `CostEstimator.EstimateScanCost`. It does **not** rewrite the query:
`PredicatesPushed = hasWhere || hasHaving` ("pushdown planned"), `JoinsReordered = hasJoins`
("placeholder flag"), `SubqueriesEliminated = !hasSubquery` ("trivially eliminated"),
`Cacheable = !hasSubquery`.

**2. Costing** (`EstimateCost`) — accumulates a heuristic cost per recognised clause (base scan, then
joins, filters, aggregates, sort) in clause order.

**3. Physical steps** (`BuildPhysicalSteps`) — emits one `PhysicalStep` per clause it recognises:
`TableScan`, `Filter`, `Aggregate`, `Sort`, `Project`.

> **Not part of this pipeline** (they were described here as phases 2-4 in earlier revisions):
> predicate pushdown as a *rewrite*, `EXISTS`/`NOT EXISTS`/`IN (SELECT)` conversion to semi/anti-joins,
> and join reordering. The first is only flagged, the second has no implementation (`SemiJoin` and
> `AntiJoin` do not exist in the source), and the third has algorithms on two components the engine
> does not construct. §"Optimization rules" is the authoritative table.

### Component interaction

```
Optimization.QueryOptimizer            ← the engine's query path (Services/QueryCompiler.cs)
├── Optimization.CostEstimator         (scan/filter/join/aggregate/sort costs)
└── Optimization.PhysicalStepType      (TableScan, Filter, Aggregate, Sort, Project)

Database/Caching/Database.PlanCaching.cs ← compiled-plan cache (normalised SQL + parameter shape)
└── Services.QueryPlanCache            (ConcurrentDictionary + LRU)

Planning/{QueryOptimizer,CardinalityEstimator,PredicatePushdown}  ← building blocks, not wired in
Query/CostBasedOptimizer                                          ← building block, not wired in
Execution/{JoinExecutor,SubqueryCache,AggregationOptimizer}        ← execution side
```

## Components

### 1. `Optimization.QueryOptimizer`

**Main entry point**: `QueryOptimizer.Optimize(SelectNode query)` → `PhysicalPlan`
**Constructed as**: `new QueryOptimizer(new CostEstimator(stats))` in `Services/QueryCompiler.cs`

**Key types** (all in `src/SharpCoreDB/Optimization/QueryOptimizer.cs`):
- `LogicalPlan` (`readonly struct`) — `Query`, `PredicatesPushed`, `JoinsReordered`,
  `SubqueriesEliminated`, `EstimatedRows`, `Cacheable`
- `PhysicalPlan` (`readonly struct`) — `Logical`, `Steps` (`PhysicalStep[]`), `EstimatedCost`
- `PhysicalStep` — one execution step: `Type`, `TableName`, `Alias`
- `PhysicalStepType` — `TableScan`, `Filter`, `Aggregate`, `Sort`, `Project`

**What it actually does**: walks the parsed `SelectNode` and emits one physical step per clause it
recognises (scan, filter, aggregate, sort, project), pricing the plan through `CostEstimator`. Two
`LogicalPlan` flags describe the query's *shape*, not the result of a rewrite:
`JoinsReordered = hasJoins` and `SubqueriesEliminated = !hasSubquery` (the assignments in `Optimize`
carry the comment "placeholder flag" / "trivially eliminated").

**Example**:
```csharp
var optimizer = new QueryOptimizer(new CostEstimator(tableStats));
var physicalPlan = optimizer.Optimize(selectNode);

Console.WriteLine($"cost={physicalPlan.EstimatedCost} steps={physicalPlan.Steps.Length}");
foreach (var step in physicalPlan.Steps)
{
    Console.WriteLine($"{step.Type}: {step.TableName ?? "N/A"}");
}
// Steps follow the statement's clause order, e.g.
// TableScan(orders) → Filter(id > 5) → Aggregate → Sort → Project
```

### 2. `Optimization.CostEstimator`

**Purpose**: prices a plan and predicts its output size.

**Real API** (`src/SharpCoreDB/Optimization/CostEstimator.cs`):

```csharp
var estimator = new CostEstimator(statistics);   // IReadOnlyDictionary<string, TableStatistics>

EstimatedCost scan   = estimator.EstimateScanCost("orders");
EstimatedCost filter = estimator.EstimateFilterCost(inputCost);
EstimatedCost join   = estimator.EstimateJoinCost(leftCost, rightCost, /* … */);
EstimatedCost agg    = estimator.EstimateAggregateCost(inputCost);
EstimatedCost sort   = estimator.EstimateSortCost(inputCost);

Console.WriteLine($"cost={scan.Cost} rows={scan.OutputRows}");   // EstimatedCost { double Cost; long OutputRows; }
```

The coefficients that turn each operator into `Cost`/`OutputRows` are in the source file — read them
there rather than trusting a copied table; they are the numbers the planner compares.

### 3. Predicate pushdown

**Purpose**: apply filters as close to the storage layer as possible, and evaluate the checklist of
predicates most-selective-first.

> **Correction:** this is a *building block*, not a wired-in rewrite. `Planning/PredicatePushdown.cs`
> exposes static helpers and is referenced by no shipped call site; on the compiled path the optimizer
> only records the *intent* — `LogicalPlan.PredicatesPushed = hasWhere || hasHaving`, commented
> "pushdown planned" in `Optimization/QueryOptimizer.cs`. The compiled plan carries the actual filter
> delegates (`whereFilter`, `whereFilterIndexed`) built in `Services/QueryCompiler.cs`.

**Real API** (`src/SharpCoreDB/Planning/PredicatePushdown.cs`, all `static`):

```csharp
// Order predicates most-selective-first before evaluating them.
List<PredicateInfo> ordered = PredicatePushdown.OptimizePredicateOrder(predicates);

// Can this predicate be evaluated at this level (are its columns available)?
bool pushable = PredicatePushdown.CanPushDown(predicate, availableColumns);

// Evaluate pushed predicates against columnar data and return the surviving row indices.
// Integrates with the SIMD filter path (column encoding aware).
int[] rows = PredicatePushdown.ExecutePushedPredicates(
    predicates, totalRows, getColumnData, getColumnEncoding);

// Rewrite a predicate for columnar access; combine AND-connected predicates.
PredicateInfo rewritten = PredicatePushdown.RewriteForColumnar(predicate, /* … */);
List<PredicateInfo> combined = PredicatePushdown.CombinePredicates(predicates);
```

The `PushdownPredicates(logicalPlan)` / `new PredicatePushdown()` shape shown in earlier revisions of
this page does not exist.

### 4. Subqueries — classified, planned, executed and cached (not rewritten)

> **Correction:** there is **no** `SubqueryOptimizer` and no `EliminateSubqueries(...)`; the types
> `SemiJoin` and `AntiJoin` do not exist anywhere in the source. SharpCoreDB does **not** rewrite
> `EXISTS` / `NOT EXISTS` / `IN (SELECT)` into semi-joins or anti-joins. `LogicalPlan.SubqueriesEliminated`
> is `!hasSubquery` — a shape flag, commented "non-subquery sources are trivially eliminated".

What does exist (`src/SharpCoreDB/Execution/`):

| Type | Role |
|------|------|
| `SubqueryClassifier` | classifies the subquery (shape/type) |
| `SubqueryPlanner` | turns it into an execution plan |
| `SubqueryExecutor` | executes it |
| `SubqueryCache` | caches the **result**, with per-table invalidation |

```csharp
var cached = cache.GetOrExecute(/* … */);      // result cache
cache.Invalidate("orders");                    // drop entries for one table
cache.Clear();
var (hits, misses, hitRate, count) = cache.GetStatistics();
```

Non-correlated subqueries therefore benefit from result caching rather than from a plan rewrite;
correlated ones are executed as the plan dictates.

### 5. Join ordering

> **Correction:** there is no `JoinReorderer` type and no `ReorderJoins(...)` method. The compiled
> path does **not** reorder joins: `LogicalPlan.JoinsReordered = hasJoins`, commented "placeholder
> flag". Joins execute through `Execution/JoinExecutor.cs`.

`OptimizeJoinOrder(...)` does exist, on two components that no shipped call site constructs:

```csharp
// Planning/QueryOptimizer.cs — statistics-driven, accepts TableInfo{Name, RowCount}
List<string> order = plannerOptimizer.OptimizeJoinOrder(tables, joinConditions);

// Query/CostBasedOptimizer.cs — table-name list + registered TableStatistics
List<string> order2 = costBasedOptimizer.OptimizeJoinOrder(tableNames, joins);
```

**Execution reality** (`src/SharpCoreDB/Execution/JoinExecutor.cs`): the join executor selects between a
hash join and a nested-loop join on row count, but the hash-join threshold is currently
`HashJoinThreshold = int.MaxValue` with the comment *"Disable hash join until key hashing is
implemented"* — so in practice joins run as **nested-loop** joins today.

### 6. Plan caching (there is no `OptimizerCache`)

> **Correction:** `OptimizerCache`, `QueryOptimizerExtensions`, `OptimizerIntegration.GetOrOptimize(...)`
> and `InvalidateOptimizationCache()` do not exist. Plan caching is done by the compiled-plan cache.

```
Database.ExecuteSQL / ExecuteQuery
        │
        ▼
Database/Caching/Database.PlanCaching.cs   ← GetOrAddPlan / TryGetCachedPlan /
        │                                       NormalizeSqlForCaching / BuildCacheKey
        ▼
Services/QueryPlanCache.cs                 ← ConcurrentDictionary<string, CacheEntry>
        │                                       (+ LRU eviction, capacity configurable)
        ▼
CacheEntry { CachedPlan, CompiledPlan, OptimizedPlan, OptimizedCost, CachedAtUtc, AccessCount }
```

**Configuration** (`DatabaseConfig`, defaults shown):

```csharp
var config = new DatabaseConfig
{
    EnableCompiledPlanCache = true,    // default
    CompiledPlanCacheCapacity = 2048,  // default; LRU eviction past this
    NormalizeSqlForPlanCache = true,   // collapse whitespace so equal queries share an entry
};
```

**Cache key** = normalised SQL + parameter shape + command type (`SqlCommandType`), so an `INSERT` and
a `SELECT` over the same text never share an entry.

**Diagnostics and clearing are `internal`** — `Database.GetPlanCacheStats()` (`(Hits, Misses, HitRate,
Count)`) and `Database.ClearPlanCache()` are visible to the `SharpCoreDB.Tests` /
`SharpCoreDB.Benchmarks` assemblies through `InternalsVisibleTo`, but **not callable from application
code**. Applications that host their own `Services.QueryPlanCache` instance can use its public
`GetStatistics()` / `Clear()`.

## Cost model characteristics

The compiled path's planner is deliberately light: `BuildLogicalPlan` reads the AST's *shape* (a few
flags and `EstimatedRows` from `CostEstimator.EstimateScanCost`), `EstimateCost` accumulates a heuristic
cost for each recognised clause, and `BuildPhysicalSteps` emits the step array. There is no
combinatorial search — no join-order enumeration and no subquery rewrite — so planning here is
O(clauses).

> Earlier revisions of this page published an "Execution Impact" table (2-5× pushdown, 5-20× join
> reordering, 50-1000× combined). Those multipliers were never measured against this code path and have
> been removed. For measured, reproducible numbers — including the planner's contribution —
> use the benchmark harness and the reports indexed in
> [`docs/benchmarks/BENCHMARK_METHOD.md`](../benchmarks/BENCHMARK_METHOD.md) and
> [`docs/manual/performance.md`](../manual/performance.md).

## Usage examples

### Planning a statement

```csharp
var estimator = new CostEstimator(tableStatistics);   // IReadOnlyDictionary<string, TableStatistics>
var optimizer = new QueryOptimizer(estimator);

PhysicalPlan plan = optimizer.Optimize(selectNode);
double cost = plan.EstimatedCost;
LogicalPlan logical = plan.Logical;                   // shape flags + EstimatedRows

foreach (var step in plan.Steps)
{
    Console.WriteLine($"{step.Type}: {step.TableName ?? "N/A"}");
}
```

### Estimating clause costs

```csharp
var estimator = new CostEstimator(statistics);

EstimatedCost scan = estimator.EstimateScanCost("orders");
EstimatedCost filtered = estimator.EstimateFilterCost(scan);

Console.WriteLine($"scan cost={scan.Cost} rows={scan.OutputRows} | " +
                  $"filter cost={filtered.Cost} rows={filtered.OutputRows}");
```

### Where plan caching happens

Callers do not wire caching up. `Database.ExecuteSQL` / `ExecuteQuery` go through
`Database.PlanCaching.cs`, which normalises the SQL and asks `Services.QueryPlanCache` for an existing
entry before compiling a new plan. Configuration and the (internal) diagnostics are in §6.

## From plan to execution

`PhysicalPlan.Steps` describes the statement's clause order; it is not a second execution engine:

```
PhysicalStep(TableScan, "orders")   → rows from the table
PhysicalStep(Filter, "id > 100")    → predicate evaluation (compiled whereFilter / §3 helper)
PhysicalStep(Aggregate)             → grouping and aggregation
PhysicalStep(Sort)                  → ORDER BY
PhysicalStep(Project)               → projection / selected columns
```

Executors that exist under `src/SharpCoreDB/Execution/`: `JoinExecutor` (with
`JoinExecutor.Diagnostics` and `JoinConditionEvaluator`) for joins, `AggregationOptimizer` for
aggregates, and the subquery trio from §4. **`FilterExecutor` and `TableScanExecutor` are not types in
this codebase** — earlier revisions named them — and `PhysicalStepType` has no `HashJoin` member, because
joins are not a plan-step type here.

## Optimization rules — what is actually applied

| Rule | Status on the engine's query path | Where |
|------|-----------------------------------|-------|
| Cost-based plan shape | ✅ applied — heuristic cost per recognised clause | `Optimization/QueryOptimizer.EstimateCost` |
| Predicate pushdown (as a rewrite) | ❌ not applied — `LogicalPlan.PredicatesPushed` is `hasWhere \|\| hasHaving` ("pushdown planned") | `Optimization/QueryOptimizer.BuildLogicalPlan` |
| Predicate evaluation helper (`CanPushDown`, most-selective-first ordering, SIMD-aware row filtering) | ✅ available, not wired into the compiled path | `Planning/PredicatePushdown` (static) |
| Join reordering | ❌ not applied — `LogicalPlan.JoinsReordered = hasJoins` ("placeholder flag") | `Optimization/QueryOptimizer.BuildLogicalPlan` |
| Join-order algorithms | ✅ exist on unused components | `Planning.QueryOptimizer.OptimizeJoinOrder`, `Query.CostBasedOptimizer.OptimizeJoinOrder` |
| Subquery → semi/anti-join conversion | ❌ does not exist (`SemiJoin`/`AntiJoin` absent from the source) | — |
| Subquery result caching with per-table invalidation | ✅ applied | `Execution/SubqueryCache` |
| Compiled-plan caching (normalised SQL + parameter shape + command type) | ✅ applied, on by default | `Database/Caching/Database.PlanCaching.cs` |
| Hash join selection | ❌ disabled — threshold is `int.MaxValue` ("until key hashing is implemented") | `Execution/JoinExecutor.cs` |

## Roadmap

What is *not* on the path yet, in rough priority order:

1. **Join reordering** — the flags exist; the search does not. `OptimizeJoinOrder` implementations are
   already written and tested on two unused components.
2. **Subquery→join rewriting** — needed before `SubqueriesEliminated` can mean anything beyond
   "the source is not a subquery".
3. **Hash joins** — requires key hashing in `JoinExecutor`; today's joins are nested-loop.
4. **Index-aware plan choice on the compiled path** (`Query/CostBasedOptimizer.ChooseBestIndex` exists,
   unused).
5. Deeper selectivity statistics, partition pruning, and parallel operator execution — none implemented.

## Notes

- **Plan shape**: `LogicalPlan` and `PhysicalPlan` are `readonly struct`s, and `PhysicalStep` is a small
  struct array — the plan path is allocation-light, but "zero allocation" would overstate it: each
  `Optimize` call builds a `PhysicalStep[]`.
- **Not a search engine**: no Selinger-style dynamic programming, no plan enumeration beyond the single
  costed plan this class builds.
- **Honesty over multipliers**: this page deliberately carries no per-rule speedup table; the numbers
  that are published for this engine are measured end-to-end and live in `docs/benchmarks/` and
  `docs/manual/performance.md`.
- **.NET 11 / C# 15**: the implementation uses `required` members, `init` properties and pattern
  matching, consistent with the rest of the codebase.
