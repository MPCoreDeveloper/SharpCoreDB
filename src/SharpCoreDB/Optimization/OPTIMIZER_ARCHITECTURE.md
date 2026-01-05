# Query Optimizer Architecture - Design & Requirements

## Overview

A cost-based query optimizer with logical/physical plan separation for SharpCoreDB.

**Key Components**:
1. **CostEstimator** - Estimates execution costs and cardinality
2. **PredicatePushdown** - Moves filters closer to data sources
3. **SubqueryOptimizer** - Converts EXISTS/IN to joins
4. **JoinReorderer** - Reorders joins for optimal execution
5. **QueryOptimizer** - Orchestrates all optimization phases

## Optimization Pipeline

```
SELECT Statement
    ↓
Parse AST (EnhancedSqlParser)
    ↓
Logical Plan (operators, no execution decisions)
    ↓ Predicate Pushdown
Filtered logical plan (push WHERE below JOINs)
    ↓ Subquery Elimination
Join-based logical plan (EXISTS → SEMI-JOIN)
    ↓ Join Reordering
Reordered logical plan (optimal join order)
    ↓ Physical Planning
Physical Plan (execution steps)
    ↓ Execute (JoinExecutor, etc.)
Results
```

## Component Details

### 1. CostEstimator

**Purpose**: Estimate query execution cost and cardinality

**Cost Model**:
```
TableScan(T):         1.0 × row_count(T)
IndexScan(T, col):    1.0 + log₂(row_count(T))
HashJoin(L, R):       cost(L) + cost(R) + build(min(L,R)) + probe(max(L,R))
NestedLoop(L, R):     cost(L) + rows(L) × cost(R)
Filter(pred):         cost(input) × selectivity(pred)
Aggregate(GROUP):     cost(input) × 0.1 (assume 10% unique groups)
Sort:                 cost(input) + n × log₂(n)
```

**Cardinality Estimation**:
```
TableScan:            rows from table statistics
Filter:               input_rows × selectivity (default 0.1)
Join(INNER):          (left_rows × right_rows × 0.5) / max(distinct_cols)
Join(LEFT):           left_rows
Join(FULL):           left_rows + right_rows
```

### 2. PredicatePushdown

**Purpose**: Move WHERE clauses closer to data sources

**Transformations**:
```
BEFORE:
Scan(orders) → Filter(amount > 100 AND status = 'shipped') → Join(customers)

AFTER:
Filter(amount > 100 AND status = 'shipped') → Scan(orders) → Join(customers)
```

**Benefits**:
- Reduces rows flowing through joins
- Fewer comparisons in nested loops
- Better memory cache locality

**Rules**:
- ✅ Push simple column filters to scans
- ✅ Split AND conditions to push partial filters
- ❌ Don't push functions (UPPER, LOWER, etc)
- ❌ Don't push multi-table predicates (join conditions)

### 3. SubqueryOptimizer

**Purpose**: Convert subqueries to joins (more optimization opportunities)

**Conversions**:

```sql
-- EXISTS → SEMI-JOIN
WHERE EXISTS (SELECT 1 FROM B WHERE B.id = A.id)
→ A INNER JOIN B ON B.id = A.id (DISTINCT)

-- NOT EXISTS → ANTI-JOIN
WHERE NOT EXISTS (SELECT 1 FROM B WHERE B.id = A.id)
→ A LEFT JOIN B ON B.id = A.id WHERE B.id IS NULL

-- IN → HASH SEMI-JOIN
WHERE A.id IN (SELECT B.id FROM B WHERE ...)
→ A INNER JOIN B ON A.id = B.id (cached hash set)
```

**Benefits**:
- Enables join reordering
- Subquery caching (non-correlated)
- Standard join optimization applies

### 4. JoinReorderer

**Purpose**: Find optimal join execution order

**Strategy**: Small-to-large ordering

```
Query: A ⋈ B ⋈ C
Stats: |A| = 10M, |B| = 1K, |C| = 5M

Bad order:   (A ⋈ B) ⋈ C
  = (10M ⋈ 1K) ⋈ 5M = 10M rows ⋈ 5M (massive intermediate)

Good order:  (B ⋈ A) ⋈ C
  = (1K ⋈ 10M) ⋈ 5M = 10M rows ⋈ 5M (compact intermediate)
```

**Cost Estimation**:
- Try different orderings
- Estimate total cost
- Choose minimum cost

**Algorithms**:
- Current: Greedy (O(n) fast)
- Future: Selinger DP (O(n!) optimal but slow)

### 5. QueryOptimizer

**Purpose**: Orchestrate all optimization phases

**Phases**:
1. **Logical Planning**: AST → Logical operators
2. **Predicate Pushdown**: Optimize filters
3. **Subquery Elimination**: Convert to joins
4. **Join Reordering**: Optimize join order
5. **Physical Planning**: Logical → Physical steps

**Output**: PhysicalPlan (ordered execution steps)

## Plan Node Structures

### Logical Nodes

```csharp
abstract class LogicalOperator

class ScanOperator : LogicalOperator
{
    string TableName
    string? Alias
}

class JoinOperator : LogicalOperator
{
    JoinType Type
    LogicalOperator Left, Right
    Expression? OnCondition
}

class FilterOperator : LogicalOperator
{
    Expression Condition
    LogicalOperator Input
}

class AggregateOperator : LogicalOperator
{
    List<ColumnReference> GroupColumns
    LogicalOperator Input
}

class SortOperator : LogicalOperator
{
    List<OrderByItem> Items
    LogicalOperator Input
}
```

### Physical Plan

```csharp
class PhysicalPlan
{
    List<PhysicalStep> Steps          // Ordered execution
    double EstimatedCost
    long EstimatedRows
}

class PhysicalStep
{
    PhysicalStepType Type             // TableScan, Filter, HashJoin, etc
    // Type-specific properties:
    string? TableName                 // For scans
    Expression? FilterCondition       // For filters
    Expression? JoinCondition         // For joins
    List<OrderByItem>? SortItems      // For sorts
    List<ColumnNode>? ProjectColumns  // For projections
    int? Limit, Offset                // For LIMIT/OFFSET
}

enum PhysicalStepType
{
    TableScan, IndexScan, Filter,
    HashJoin, HashLeftJoin, NestedLoopJoin, CrossJoin,
    Aggregate, Sort, LimitOffset, Project
}
```

## Integration Points

### With QueryCache

```
QueryCache stores:
- Parsed AST
- Optimized PhysicalPlan
- Cost estimates

Reused on cache hit:
- No parsing
- No optimization
- Straight to execution
```

### With Execution Engine

```
PhysicalPlan.Steps → Executor

foreach (step in plan.Steps)
{
    result = switch(step.Type)
    {
        TableScan → TableScanExecutor
        Filter → FilterExecutor  
        HashJoin → JoinExecutor
        Aggregate → AggregateExecutor
        Sort → SortExecutor
        ... etc
    }
}
```

### With Statistics

```
Table Statistics:
- Row counts
- Column distinct counts
- Index information

Updated on:
- CREATE TABLE
- INSERT (batch)
- DELETE (batch)
- ANALYZE

Invalidates optimization cache when changed
```

## Performance Characteristics

### Optimization Time

```
Operation               Complexity      Time (typical)
────────────────────────────────────────────────────
Logical Planning        O(n)            <0.1ms
Predicate Pushdown      O(p)            <0.1ms
Subquery Elimination    O(s)            <0.1ms
Join Reordering         O(k! / 2)       <1ms (k≤5)
Physical Planning       O(n)            <0.1ms
────────────────────────────────────────────────────
Total                   O(n + k!)       <2ms (typical)

Where: n = operators, p = predicates, s = subqueries, k = joins
```

### Memory Usage

```
Logical Plan:          O(n) nodes
Physical Plan:         O(n) steps
Cost estimates:        O(k²) combinations
Total:                 ~1KB per query
```

### Execution Improvement

```
Without Optimization:           With Optimization:
─────────────────────          ──────────────────

Basic SELECT:                   No change
WHERE filter:                   Predicate pushdown (2-5x)
JOINS:                          Join reordering (5-20x)
EXISTS subquery:                Semi-join (10-100x)
Non-correlated scalar:          Cache + pushdown (100-1000x)

Combined worst-to-best:         50-1000x possible
```

## Configuration & Tuning

### Cost Model Tuning

```csharp
// Adjust selectivity estimates
const double DefaultSelectivity = 0.1;      // 10%
const double JoinSelectivity = 0.5;        // 50%
const double AggregateSelectivity = 0.1;  // 10%

// Adjust cost weights
const double ScanCost = 1.0;
const double CpuCost = 0.01;
const double IOCost = 10.0;
```

### Cache Settings

```csharp
// Plan cache size
const int MaxCachedPlans = 1000;

// Statistics invalidation
InvalidateOptimizationCache() when:
- INSERT/UPDATE/DELETE
- ANALYZE command
- Manual invalidation
```

## Future Enhancements

1. **Selectivity Learning**: ML-based predicate selectivity prediction
2. **Index Awareness**: Use index costs in decisions
3. **Partition Pruning**: Skip unneeded partitions
4. **Parallel Operators**: Multi-threaded join/aggregate
5. **Adaptive Execution**: Change join algorithm at runtime
6. **Query Rewriting**: Algebraic transformations
7. **Materialized Views**: Recognize patterns in queries
8. **Plan Hints**: Let users override optimizer decisions

## Usage Pattern

```csharp
// Setup
var costEstimator = new CostEstimator(tableStatistics);
var optimizer = new QueryOptimizer(
    costEstimator,
    new PredicatePushdown(),
    new SubqueryOptimizer(),
    new JoinReorderer());

// Optimize
var physicalPlan = optimizer.Optimize(selectNode);

// Execute
var results = new PhysicalPlanExecutor(physicalPlan).Execute();

// Cache for reuse
planCache[querySql] = physicalPlan;
```

## Debugging

```csharp
// Inspect logical plan
var logicalPlan = optimizer.BuildLogicalPlan(selectNode);
Console.WriteLine(logicalPlan.DebugString());

// Inspect physical plan
foreach (var step in physicalPlan.Steps)
{
    Console.WriteLine($"{step.Type}: {step.DebugString()}");
}

// Cost estimates
Console.WriteLine($"Estimated cost: {physicalPlan.EstimatedCost}");
Console.WriteLine($"Estimated rows: {physicalPlan.EstimatedRows}");
```

## HOT PATH Compliance

✅ **No allocations**:
- Struct-based plan nodes
- Stack-allocated small collections
- Reuse cached plans

✅ **No LINQ**:
- Manual iteration
- Direct algorithms
- Index-based loops

✅ **No async**:
- Optimization runs synchronously
- Fast enough (<2ms typical)

✅ **Streaming ready**:
- Physical steps are operators
- Can be executed incrementally
- Memory efficient

## Summary

The query optimizer is a modular, extensible system that:

- **Reduces execution cost** through intelligent plan reordering
- **Improves memory usage** by minimizing intermediate result sizes
- **Enables caching** via logical/physical separation
- **Stays fast** with zero-allocation struct nodes
- **Integrates seamlessly** with existing QueryCache
- **Scales** from simple SELECT to complex multi-table queries

Ready for implementation! 🚀
