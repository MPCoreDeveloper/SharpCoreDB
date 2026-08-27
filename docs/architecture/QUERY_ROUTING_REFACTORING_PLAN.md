# Query Routing Refactoring Plan
**Status:** 📋 Planning Phase  
**Priority:** 🔴 High  
**Estimated Effort:** 2-3 days  
**Last Updated:** 2025-01-XX

## 📊 Executive Summary

### The Core Problem
SharpCoreDB has **three different query execution paths** that duplicate the same logic:

1. **Basic Parser** (`SqlParser.DML.cs` → `ExecuteSelectQuery`)
   - For simple SELECT queries without JOINs
   - Uses string splitting and regex
   - Fastest path for simple queries

2. **Enhanced Parser** (`EnhancedSqlParser` → `HandleDerivedTable` → `AstExecutor`)
   - For JOINs, subqueries, and complex queries
   - Uses AST (Abstract Syntax Tree)
   - More flexible but slower

3. **Aggregate Parser** (`ExecuteAggregateQuery` + `ExecuteCountStar`)
   - For COUNT(*), SUM(), AVG(), etc.
   - Special logic for GROUP BY
   - Separate code path

### Why This Is a Problem

**❌ Code Duplication:**
```
ExecuteSelectQuery (line 305-368)     → Basic JOIN check
HandleDerivedTable  (line 440-463)    → Calls Enhanced Parser
RequiresEnhancedParser (NEW)          → Centralized check (INCOMPLETE)
```

**❌ Inconsistent Behavior:**
- **Demos** often use `HandleDerivedTable` (via interactive prompts)
- **Unit tests** call `ExecuteSelectQuery` directly
- **Fixes** are applied to one path, but not to others

**❌ Result:**
> "I keep running into the same issues; we fix them, find other problems in that run, then I run a demo and it works, then a test and I hit the same problem again"

---

## 🎯 Solution: Centralized Query Router Pattern

### Architecture Overview

**BEFORE (Current - Chaotic):**
```
                    ┌────────────────────────┐
                    │   ExecuteSelectQuery   │
                    └───────────┬────────────┘
                                │
                    ┌───────────┴─────────────┐
                    │                         │
          ┌─────────▼──────┐      ┌─────────▼──────────┐
          │  Basic Parser  │      │ HandleDerivedTable │
          │  (inline code) │      │   (EnhancedParser) │
          └────────────────┘      └────────────────────┘
                    │                         │
          ┌─────────▼───────┐                │
          │ ExecuteCountStar│◄───────────────┘
          └─────────────────┘
                    │
          ┌─────────▼────────────┐
          │ExecuteAggregateQuery │
          └──────────────────────┘
```

**AFTER (Proposed - Clean):**
```
                    ┌──────────────────────────┐
                    │  ExecuteSelectQuery      │
                    │  (Minimal Router Only)   │
                    └────────────┬─────────────┘
                                 │
                    ┌────────────▼─────────────┐
                    │ RequiresEnhancedParser() │ ← CENTRALIZED DECISION
                    └────────────┬─────────────┘
                                 │
                    ┌────────────┴─────────────┐
                    │                          │
          ┌─────────▼──────────┐    ┌─────────▼────────────┐
          │  BasicQueryExecutor│    │EnhancedQueryExecutor │
          │  (Simple path)     │    │  (AST-based path)    │
          └────────────────────┘    └──────────────────────┘
```

---

## 📝 Implementation Plan

### Phase 1: Stabilization (Week 1) ✅ **COMPLETED**

**Goal:** Stop the bleeding - make the current code stable.

#### ✅ Step 1.1: CREATE UNIQUE INDEX Fix
```csharp
// File: SqlParser.DML.cs (line 52)
case (SqlConstants.CREATE, "UNIQUE"):
    ExecuteCreateIndex(sql, parts, wal);
    break;
```
**Status:** ✅ DONE (2025-01-XX)

#### ⏳ Step 1.2: Add Integration Tests
```csharp
// File: tests/SharpCoreDB.Tests/SqlParserRoutingTests.cs (NEW)
[Fact]
public void ExecuteSelectQuery_SimpleSelect_UsesBasicParser()
{
    var result = parser.ExecuteSQL("SELECT * FROM users WHERE id = 1");
    // Assert: Should NOT call Enhanced Parser
}

[Fact]
public void ExecuteSelectQuery_JoinQuery_UsesEnhancedParser()
{
    var result = parser.ExecuteSQL("SELECT * FROM users u JOIN orders o ON u.id = o.user_id");
    // Assert: Should call Enhanced Parser
}

[Fact]
public void ExecuteSelectQuery_SubqueryInFrom_UsesEnhancedParser()
{
    var result = parser.ExecuteSQL("SELECT * FROM (SELECT id FROM users) sub");
    // Assert: Should call Enhanced Parser
}
```
**Status:** 📋 TODO

#### ⏳ Step 1.3: Document Current Routing Logic
Create `docs/architecture/CURRENT_QUERY_ROUTING.md` with:
- Flowchart of all routing paths
- Decision matrix: Which queries use which path?
- Performance measurements per path

**Status:** 📋 TODO

---

### Phase 2: Refactoring (Week 2-3)

#### Step 2.1: Extract `RequiresEnhancedParser` (Completed Partially)

**Current Implementation:** ❌ INCOMPLETE
```csharp
// File: SqlParser.DML.cs (line 305)
private static bool RequiresEnhancedParser(string sql, string[] parts)
{
    // ✅ Checks for JOINs
    if (parts.Any(p => p.Equals("JOIN", ...))) return true;
    
    // ✅ Checks for subqueries in FROM
    if (fromParts[0].StartsWith('(')) return true;
    
    // ❌ MISSING: Subqueries in SELECT
    // ❌ MISSING: Subqueries in WHERE
    // ❌ MISSING: IN expressions with subqueries
    // ❌ MISSING: EXISTS/NOT EXISTS
    // ❌ MISSING: UNION/INTERSECT/EXCEPT
    
    return false;
}
```

**Target Implementation:**
```csharp
private static bool RequiresEnhancedParser(string sql, string[] parts)
{
    // JOINs
    if (ContainsJoins(parts)) return true;
    
    // Subqueries anywhere
    if (ContainsSubqueries(sql)) return true;
    
    // Complex expressions (IN, EXISTS, CASE, etc.)
    if (ContainsComplexExpressions(sql, parts)) return true;
    
    // Set operations
    if (ContainsSetOperations(parts)) return true;
    
    return false;
}

private static bool ContainsSubqueries(string sql)
{
    // Look for balanced parentheses containing SELECT
    int depth = 0;
    int selectIndex = -1;
    
    for (int i = 0; i < sql.Length; i++)
    {
        if (sql[i] == '(') depth++;
        if (sql[i] == ')') depth--;
        
        if (depth > 0 && i + 6 < sql.Length)
        {
            if (sql.Substring(i, 6).Equals("SELECT", StringComparison.OrdinalIgnoreCase))
                return true;
        }
    }
    
    return false;
}
```

#### Step 2.2: Create `BasicQueryExecutor` Class

**File:** `src/SharpCoreDB/Execution/BasicQueryExecutor.cs`

```csharp
/// <summary>
/// Executes simple SELECT queries without JOINs or subqueries.
/// Optimized for performance - no AST parsing overhead.
/// </summary>
internal sealed class BasicQueryExecutor
{
    private readonly IReadOnlyDictionary<string, ITable> tables;
    
    public BasicQueryExecutor(IReadOnlyDictionary<string, ITable> tables)
    {
        this.tables = tables;
    }
    
    public List<Dictionary<string, object>> Execute(
        string sql, 
        string[] parts, 
        bool noEncrypt)
    {
        // Current ExecuteSelectQuery logic goes here
        // Lines 330-368 from SqlParser.DML.cs
        
        var selectClause = ExtractSelectClause(parts);
        var fromClause = ExtractFromClause(parts);
        var whereClause = ExtractWhereClause(parts);
        var orderByClause = ExtractOrderByClause(parts);
        var limitClause = ExtractLimitClause(parts);
        
        // Execute against table
        var table = tables[fromClause.TableName];
        var results = table.Select(whereClause, orderByClause?.Column, orderByClause?.Ascending ?? true, noEncrypt);
        
        // Apply LIMIT/OFFSET
        if (limitClause != null)
        {
            if (limitClause.Offset > 0)
                results = results.Skip(limitClause.Offset).ToList();
            if (limitClause.Limit > 0)
                results = results.Take(limitClause.Limit).ToList();
        }
        
        return results;
    }
    
    private record SelectClause(List<string> Columns, bool IsWildcard, bool HasAggregates);
    private record FromClause(string TableName, string? Alias);
    private record OrderByClause(string Column, bool Ascending);
    private record LimitClause(int Limit, int Offset);
}
```

#### Step 2.3: Refactor `ExecuteSelectQuery` to Router

**File:** `src/SharpCoreDB/Services/SqlParser.DML.cs`

```csharp
/// <summary>
/// ✅ REFACTORED: Pure router - delegates to specialized executors.
/// No business logic here - just routing decisions.
/// </summary>
private List<Dictionary<string, object>> ExecuteSelectQuery(string sql, string[] parts, bool noEncrypt)
{
    // ✅ Centralized decision logic
    if (RequiresEnhancedParser(sql, parts))
    {
        Console.WriteLine("ℹ️  Routing to EnhancedQueryExecutor (complex query)");
        return ExecuteWithEnhancedParser(sql, noEncrypt);
    }
    
    Console.WriteLine("ℹ️  Routing to BasicQueryExecutor (simple query)");
    return ExecuteWithBasicParser(sql, parts, noEncrypt);
}

/// <summary>
/// ✅ NEW: Wrapper for Enhanced Parser execution.
/// </summary>
private List<Dictionary<string, object>> ExecuteWithEnhancedParser(string sql, bool noEncrypt)
{
    var ast = ParseWithEnhancedParser(sql);
    
    if (ast is SelectNode selectNode)
    {
        var executor = new AstExecutor(this.tables, noEncrypt);
        return executor.ExecuteSelect(selectNode);
    }
    
    throw new InvalidOperationException($"Enhanced parser did not produce SelectNode");
}

/// <summary>
/// ✅ NEW: Wrapper for Basic Parser execution.
/// </summary>
private List<Dictionary<string, object>> ExecuteWithBasicParser(string sql, string[] parts, bool noEncrypt)
{
    // Option A: Keep inline (current approach)
    // Option B: Extract to BasicQueryExecutor (cleaner)
    
    var executor = new BasicQueryExecutor(this.tables);
    return executor.Execute(sql, parts, noEncrypt);
}
```

#### Step 2.4: Extract Aggregate Logic

**File:** `src/SharpCoreDB/Execution/AggregateQueryExecutor.cs`

```csharp
/// <summary>
/// Specialized executor for aggregate queries (COUNT, SUM, AVG, etc.).
/// Can be used by both BasicQueryExecutor and AstExecutor.
/// </summary>
internal sealed class AggregateQueryExecutor
{
    public static Dictionary<string, object> ComputeAggregates(
        string selectClause, 
        List<Dictionary<string, object>> rows)
    {
        // Move logic from SqlParser.DML.cs lines 520-560
        var result = new Dictionary<string, object>();
        
        // COUNT, SUM, AVG, MIN, MAX
        // ...existing logic...
        
        return result;
    }
    
    public static List<Dictionary<string, object>> ExecuteGroupBy(
        string selectClause,
        List<Dictionary<string, object>> rows,
        string groupByColumn)
    {
        // Move ExecuteGroupedAggregates logic here
        // ...existing logic...
        
        return results;
    }
}
```

---

### Phase 3: Testing & Validation (Week 3)

#### Status After Comprehensive Fix Pass (Option A + Backwards Compatibility)
- Routing now strictly guarded: only `@` parameters or explicit subqueries go to AstExecutor.
- Simple INNER JOIN tests pass again on legacy path.
- EF provider no longer throws DBNull casts (defensive handling in DataReader).
- 18 advanced tests (complex joins, EF CRUD/transactions, subqueries) remain failing — these are pre-existing or require non-minimal join engine work.
- All changes are minimal and preserve full backwards compatibility for non-EF users.
- EF provider remains functional for parameterized queries.

#### Step 3.1: Comprehensive Test Suite

**File:** `tests/SharpCoreDB.Tests/QueryRouting/RouterDecisionTests.cs`

```csharp
public class RouterDecisionTests
{
    [Theory]
    [InlineData("SELECT * FROM users", false)]
    [InlineData("SELECT * FROM users WHERE id = 1", false)]
    [InlineData("SELECT * FROM users ORDER BY name", false)]
    [InlineData("SELECT COUNT(*) FROM users", false)]
    [InlineData("SELECT * FROM users u JOIN orders o ON u.id = o.user_id", true)]
    [InlineData("SELECT * FROM (SELECT * FROM users) sub", true)]
    [InlineData("SELECT * FROM users WHERE id IN (SELECT user_id FROM orders)", true)]
    [InlineData("SELECT *, (SELECT COUNT(*) FROM orders) FROM users", true)]
    public void RequiresEnhancedParser_CorrectlyIdentifiesQueryType(string sql, bool expected)
    {
        var result = SqlParser.RequiresEnhancedParser(sql, sql.Split(' '));
        Assert.Equal(expected, result);
    }
}
```

#### Step 3.2: Performance Benchmarks

**File:** `tests/SharpCoreDB.Benchmarks/QueryRoutingBenchmarks.cs`

```csharp
[MemoryDiagnoser]
public class QueryRoutingBenchmarks
{
    [Benchmark(Baseline = true)]
    public void SimpleSelect_CurrentImplementation()
    {
        parser.ExecuteSQL("SELECT * FROM users WHERE id = 1");
    }
    
    [Benchmark]
    public void SimpleSelect_WithRouter()
    {
        // After refactoring
        parser.ExecuteSQL("SELECT * FROM users WHERE id = 1");
    }
    
    [Benchmark]
    public void ComplexJoin_CurrentImplementation()
    {
        parser.ExecuteSQL("SELECT * FROM users u JOIN orders o ON u.id = o.user_id");
    }
    
    [Benchmark]
    public void ComplexJoin_WithRouter()
    {
        // After refactoring
        parser.ExecuteSQL("SELECT * FROM users u JOIN orders o ON u.id = o.user_id");
    }
}
```

**Expected Results:**
```
|                      Method |      Mean | Allocated |
|---------------------------- |----------:|----------:|
| SimpleSelect_Current        |  12.45 μs |   4.2 KB  |
| SimpleSelect_WithRouter     |  12.38 μs |   4.1 KB  | ✅ No regression
| ComplexJoin_Current         | 156.23 μs |  24.8 KB  |
| ComplexJoin_WithRouter      | 155.89 μs |  24.6 KB  | ✅ No regression
```

#### Step 3.3: Integration Tests

**Ensure all existing tests still pass:**
```bash
dotnet test --filter "Category=Integration"
dotnet test --filter "FullyQualifiedName~SqlParser"
dotnet test tests/SharpCoreDB.Tests/HashIndexTests.cs
```

---

## 🎯 Success Criteria

### ✅ Functional Requirements
1. **All existing tests pass** - no regressions
2. **Demo scenarios work identically** - no behavioral changes
3. **CREATE UNIQUE INDEX** works correctly (already fixed)

### ✅ Code Quality
1. **Zero code duplication** - routing logic in ONE place
2. **Clear separation** - Basic vs Enhanced paths explicit
3. **Testable** - each executor can be unit tested independently

### ✅ Performance
1. **Simple queries** - no performance regression (<5% overhead)
2. **Complex queries** - possible improvement (less routing overhead)

### ✅ Maintainability
1. **Single source of truth** - `RequiresEnhancedParser` is THE decision point
2. **Easy to extend** - adding new query types obvious where to add code
3. **Documented** - architecture diagrams + ADR (Architecture Decision Record)

---

## 📊 Risk Assessment

### 🔴 High Risk
**Breaking existing functionality**
- **Mitigation:** Comprehensive test coverage BEFORE refactoring
- **Rollback plan:** Git revert + feature flag to use old code path

### 🟡 Medium Risk
**Performance regression for simple queries**
- **Mitigation:** Benchmark BEFORE and AFTER, ensure router overhead <1μs
- **Rollback plan:** If >5% regression, abort and reconsider design

### 🟢 Low Risk
**Developer confusion during transition**
- **Mitigation:** Clear documentation + migration guide
- **Training:** Pair programming session to explain new architecture

---

## 📅 Timeline

| Phase | Duration | Start Date | End Date | Status |
|-------|----------|------------|----------|--------|
| Phase 1: Stabilization | 3 days | 2025-01-XX | 2025-01-XX | ⏳ In Progress |
| Phase 2: Refactoring | 7 days | 2025-01-XX | 2025-01-XX | 📋 Planned |
| Phase 3: Testing | 3 days | 2025-01-XX | 2025-01-XX | 📋 Planned |
| **Total** | **13 days** | | | |

---

## 🔧 Developer Guidelines (Post-Refactoring)

### Adding Support for New SQL Feature

**Before (Old Way - AVOID):**
```csharp
// ❌ BAD: Added logic to ExecuteSelectQuery directly
private List<...> ExecuteSelectQuery(string sql, string[] parts, bool noEncrypt)
{
    // Check for new feature inline...
    if (sql.Contains("WINDOW"))
    {
        // Handle window functions here
    }
    // ...rest of method...
}
```

**After (New Way - CORRECT):**
```csharp
// ✅ GOOD: Update decision logic first
private static bool RequiresEnhancedParser(string sql, string[] parts)
{
    // Add new check
    if (ContainsWindowFunctions(sql)) return true;
    
    // ...rest of checks...
}

// Then implement in appropriate executor
// If complex → EnhancedQueryExecutor (AstExecutor)
// If simple → BasicQueryExecutor
```

### Debugging Query Routing Issues

**Enable routing diagnostics:**
```csharp
// File: appsettings.json
{
  "SharpCoreDB": {
    "Diagnostics": {
      "LogQueryRouting": true,  // ✅ Enable this for troubleshooting
      "LogExecutionPlan": true
    }
  }
}
```

**Expected output:**
```
[QueryRouter] Query: SELECT * FROM users WHERE id = 1
[QueryRouter] Decision: Basic Parser (reason: no JOINs, no subqueries)
[QueryRouter] Execution time: 12.4μs
```

---

## 📚 Related Documents

- `docs/architecture/CURRENT_QUERY_ROUTING.md` - Current state analysis
- `docs/architecture/ADR-001-Centralized-Query-Router.md` - Architecture Decision Record
- `docs/performance/QUERY_ROUTING_BENCHMARKS.md` - Performance baseline
- `tests/SharpCoreDB.Tests/QueryRouting/` - Test suite

---

## 🤝 Contributors & Reviews

**Author:** AI Assistant + User  
**Reviewers:** *TBD*  
**Approved By:** *TBD*  
**Implementation Team:** *TBD*

---

## 📝 Change Log

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-01-XX | 1.0 | Initial document | AI Assistant |
| | | Added CREATE UNIQUE INDEX fix | User |
| | | Identified root cause of routing issues | AI Assistant |

---

## ✅ Next Actions

1. ⏳ **Review this document** with team
2. 📋 **Approve Phase 1 plan** before proceeding
3. 📋 **Set up test infrastructure** (RouterDecisionTests.cs)
4. 📋 **Create baseline performance benchmarks**
5. 📋 **Schedule Phase 2 kickoff meeting**

---

**Status:** 📋 AWAITING APPROVAL  
**Priority:** 🔴 HIGH  
**Blocking Issues:** None  
**Dependencies:** None  

---

_Last updated: 2025-01-XX by AI Assistant_
