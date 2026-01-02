# SharpCoreDB Roadmap 2026

**Last Updated**: 2026-01-XX  
**Current Completion**: 82% ✅  
**Status**: Production-ready for core features  
**🔥 NEW PRIORITY**: Beat LiteDB in ALL operations (SELECT optimization)

---

## 🎯 **IMMEDIATE PRIORITY: Performance Dominance**

**Goal**: Make SharpCoreDB faster than LiteDB in **EVERY** operation  
**Timeline**: 6 weeks (Q1 2026)  
**Current Gap**: SELECT queries are 2x slower than LiteDB

### Current vs LiteDB Performance

| Operation | SharpCoreDB | LiteDB | Status |
|-----------|-------------|--------|--------|
| Analytics | 49.5µs | 17,029µs | ✅ **345x faster** |
| Inserts | 70.9ms | 148.7ms | ✅ **2.1x faster** |
| Batch Updates | 283ms | 437ms | ✅ **1.54x faster** |
| Memory | 54.4MB | 337.5MB | ✅ **6.2x less** |
| **SELECT** | **33.0ms** | **16.6ms** | ❌ **2x slower** |

**Target**: SELECT 6-8ms (2.1-2.8x faster than LiteDB) in 6 weeks

**Detailed Plan**: See [BEAT_LITEDB_PLAN.md](BEAT_LITEDB_PLAN.md)

---

## 📊 Quick Overview

| Phase | Timeline | Target Completion | Focus Area |
|-------|----------|-------------------|------------|
| **✅ Foundation** | Completed | 82% | Core database features |
| **🔄 Phase 1** | 4-6 weeks | 88% | Schema evolution |
| **📋 Phase 2** | 4-6 weeks | 94% | Data integrity |
| **🎯 Phase 3** | 8-12 weeks | 100% | Advanced SQL |

---

## ✅ What's Complete (82%)

### Core Database (100%)
- ✅ SQL operations (SELECT, INSERT, UPDATE, DELETE, CREATE TABLE)
- ✅ Storage engines (Columnar + PageBased with full table scan)
- ✅ Indexes (Hash O(1), B-Tree O(log n + k), Primary Key)
- ✅ Transactions (MVCC, WAL, GroupCommit)
- ✅ Encryption (AES-256-GCM, 0% overhead)
- ✅ Async/await operations
- ✅ Batch operations (10-50x speedup)
- ✅ Entity Framework Core provider
- ✅ Connection pooling & query caching

### Performance Optimizations (100%)
- ✅ SIMD analytics (345x faster than LiteDB)
- ✅ B-Tree range queries (2.8-3.8x speedup)
- ✅ ORDER BY optimization (8x speedup)
- ✅ Deferred batch updates (10-20x speedup)
- ✅ Zero-allocation serialization (Span<T>)

---

## 🔄 Phase 1: Schema Evolution (Next 4-6 Weeks)

**Goal**: Enable production schema migrations  
**Target Completion**: 88% overall  
**Priority**: **CRITICAL** - Blocking production migrations

### 1.1 ALTER TABLE ADD COLUMN ⏳
**Estimated Effort**: 3-5 days  
**Priority**: **P0 - CRITICAL**

**Requirements**:
```sql
ALTER TABLE users ADD COLUMN age INT;
ALTER TABLE users ADD COLUMN status TEXT DEFAULT 'active';
ALTER TABLE users ADD COLUMN created_at DATETIME DEFAULT CURRENT_TIMESTAMP;
```

**Implementation Tasks**:
1. Parse `ALTER TABLE table_name ADD COLUMN column_def` syntax
2. Update `Table.Columns`, `Table.ColumnTypes`, `Table.IsAuto` lists
3. Add column to existing row serialization (with default value)
4. Update metadata persistence (`meta.json`)
5. Rebuild indexes if needed
6. Add validation (prevent duplicate column names)

**Files to Modify**:
- `Services/SqlParser.cs` - Add ALTER TABLE parsing
- `Services/EnhancedSqlParser.DDL.cs` - Detailed ALTER parsing
- `DataStructures/Table.cs` - Add `AddColumn()` method
- `Core/Database.Core.cs` - Metadata update logic

**Test Cases**:
- [ ] Add column without DEFAULT
- [ ] Add column with DEFAULT value
- [ ] Add column with AUTO (ULID/GUID)
- [ ] Add column to table with existing data (backfill)
- [ ] Try to add duplicate column (should fail)
- [ ] Verify existing queries still work

**Acceptance Criteria**:
- [ ] ALTER TABLE ADD COLUMN parses correctly
- [ ] Column added to schema and persisted
- [ ] Existing rows preserve data
- [ ] New INSERTs include new column
- [ ] Metadata persists correctly
- [ ] Indexes remain valid

---

### 1.2 FOREIGN KEY Constraints ⏳
**Estimated Effort**: 7-10 days  
**Priority**: **P0 - CRITICAL**

**Requirements**:
```sql
CREATE TABLE orders (
    id INT PRIMARY KEY,
    user_id INT,
    FOREIGN KEY (user_id) REFERENCES users(id)
        ON DELETE CASCADE
        ON UPDATE CASCADE
);

-- Validation:
INSERT INTO orders VALUES (1, 999);  -- Should fail if user 999 doesn't exist
DELETE FROM users WHERE id = 1;     -- Should cascade delete orders
```

**Implementation Tasks**:
1. Parse FOREIGN KEY in CREATE TABLE
2. Store FK metadata in `Table.ForeignKeys` list
3. Validate FK on INSERT (referenced row must exist)
4. Validate FK on UPDATE (new value must exist)
5. Implement ON DELETE CASCADE
6. Implement ON DELETE SET NULL / RESTRICT
7. Implement ON UPDATE CASCADE
8. Add PRAGMA foreign_keys ON/OFF
9. Circular dependency detection

**Data Structure**:
```csharp
public class ForeignKeyConstraint
{
    public string ColumnName { get; set; }
    public string ReferencedTable { get; set; }
    public string ReferencedColumn { get; set; }
    public FkAction OnDelete { get; set; }  // CASCADE, SET NULL, RESTRICT
    public FkAction OnUpdate { get; set; }
}
```

**Files to Modify**:
- `Services/EnhancedSqlParser.DDL.cs` - Parse FK constraints
- `DataStructures/Table.cs` - Add `ForeignKeys` collection
- `DataStructures/Table.CRUD.cs` - Add FK validation
- `Core/Database.cs` - FK enforcement orchestration

**Test Cases**:
- [ ] Create table with FK
- [ ] Insert valid FK value (should succeed)
- [ ] Insert invalid FK value (should fail)
- [ ] Update FK to invalid value (should fail)
- [ ] Delete parent row with CASCADE (should delete children)
- [ ] Delete parent row with RESTRICT (should fail if children exist)
- [ ] Update parent row with CASCADE (should update children)
- [ ] Circular FK detection

**Acceptance Criteria**:
- [ ] FK constraints are parsed
- [ ] INSERT validates FK before commit
- [ ] UPDATE validates FK before commit
- [ ] DELETE CASCADE works correctly
- [ ] SET NULL works correctly
- [ ] Error messages are clear and helpful

---

### 1.3 DROP TABLE ⏳
**Estimated Effort**: 1-2 days  
**Priority**: **P0 - CRITICAL**

**Requirements**:
```sql
DROP TABLE users;
DROP TABLE IF EXISTS temp_table;
```

**Implementation Tasks**:
1. Parse `DROP TABLE [IF EXISTS] table_name`
2. Delete table data file (`.dat` or `.pages`)
3. Remove from `Database.tables` dictionary
4. Remove associated indexes
5. Update metadata (`meta.json`)
6. Handle IF EXISTS clause
7. Validate no FK dependencies

**Files to Modify**:
- `Services/SqlParser.cs` - Add DROP TABLE parsing
- `Core/Database.cs` - Add table removal logic
- `Core/Database.Core.cs` - Metadata cleanup

**Test Cases**:
- [ ] Drop existing table
- [ ] Drop non-existent table (should fail)
- [ ] DROP IF EXISTS (should not fail)
- [ ] Verify file deletion
- [ ] Verify metadata update
- [ ] Try to drop table with FK dependencies (should fail)

**Acceptance Criteria**:
- [ ] DROP TABLE removes table completely
- [ ] IF EXISTS prevents errors
- [ ] Files are deleted from disk
- [ ] Indexes are removed
- [ ] Metadata is updated
- [ ] FK dependencies are checked

---

### 1.4 UNIQUE Constraint (Table-Level) ⏳
**Estimated Effort**: 3-4 days  
**Priority**: **P0 - CRITICAL**

**Requirements**:
```sql
CREATE TABLE users (
    id INT PRIMARY KEY,
    email TEXT UNIQUE,
    username TEXT UNIQUE,
    UNIQUE (first_name, last_name)  -- Composite unique
);

INSERT INTO users VALUES (1, 'alice@test.com', 'alice', 'Alice', 'Smith');
INSERT INTO users VALUES (2, 'alice@test.com', 'bob', 'Bob', 'Jones');  -- Should fail!
```

**Implementation Tasks**:
1. Parse UNIQUE keyword in column definition
2. Parse table-level UNIQUE constraints
3. Auto-create unique index for each UNIQUE column
4. Validate uniqueness on INSERT
5. Validate uniqueness on UPDATE
6. Support composite UNIQUE (multiple columns)
7. Handle NULL values (NULL != NULL)

**Files to Modify**:
- `Services/EnhancedSqlParser.DDL.cs` - Parse UNIQUE
- `DataStructures/Table.cs` - Track unique constraints
- `DataStructures/Table.CRUD.cs` - Validate uniqueness
- `DataStructures/Table.Indexing.cs` - Auto-create unique indexes

**Test Cases**:
- [ ] Create table with UNIQUE column
- [ ] Insert duplicate value (should fail)
- [ ] Update to duplicate value (should fail)
- [ ] Composite UNIQUE constraint
- [ ] NULL values in UNIQUE column (allowed, multiple NULLs OK)
- [ ] UNIQUE + NOT NULL combination

**Acceptance Criteria**:
- [ ] UNIQUE column parsed correctly
- [ ] Table-level UNIQUE parsed correctly
- [ ] Uniqueness validated on INSERT
- [ ] Uniqueness validated on UPDATE
- [ ] Composite UNIQUE works
- [ ] NULL handling correct (multiple NULLs allowed)

---

### 1.5 Enhanced NOT NULL Enforcement ⏳
**Estimated Effort**: 2-3 days  
**Priority**: **HIGH** (P0)

**Requirements**:
```sql
CREATE TABLE users (
    id INT PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT NOT NULL
);

INSERT INTO users VALUES (1, NULL, 'test@test.com');  -- Should fail!
UPDATE users SET name = NULL WHERE id = 1;            -- Should fail!
```

**Implementation Tasks**:
1. Complete existing partial NOT NULL implementation
2. Validate on INSERT (all paths)
3. Validate on UPDATE (all paths)
4. Validate on ALTER TABLE ADD COLUMN
5. Clear error messages

**Files to Modify**:
- `DataStructures/Table.CRUD.cs` - Complete validation
- `DataStructures/Table.BatchUpdate.cs` - Batch validation
- `Services/EnhancedSqlParser.DDL.cs` - Parse NOT NULL

**Test Cases**:
- [ ] INSERT with NULL in NOT NULL column (should fail)
- [ ] UPDATE to NULL in NOT NULL column (should fail)
- [ ] Batch INSERT with NULL (should fail entire batch)
- [ ] ALTER TABLE ADD COLUMN NOT NULL (should fail if existing rows)
- [ ] Error messages are clear

**Acceptance Criteria**:
- [ ] NOT NULL validated on INSERT (all code paths)
- [ ] NOT NULL validated on UPDATE (all code paths)
- [ ] Clear error messages with column name
- [ ] Performance impact minimal

---

## 📋 Phase 2: Data Integrity (Following 4-6 Weeks)

**Goal**: Match SQLite constraint enforcement  
**Target Completion**: 94% overall  
**Priority**: **HIGH**

### 2.1 DEFAULT Values Enhancement ⏳
**Estimated Effort**: 3-4 days

**Requirements**:
```sql
CREATE TABLE users (
    id INT PRIMARY KEY,
    name TEXT DEFAULT 'Unknown',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status TEXT DEFAULT 'active',
    count INT DEFAULT 0
);
```

**Implementation**:
- Parse DEFAULT in column definition
- Support literals (strings, numbers, NULL)
- Support expressions (CURRENT_TIMESTAMP, NEWID(), etc.)
- Apply defaults when column omitted in INSERT

---

### 2.2 CHECK Constraints ⏳
**Estimated Effort**: 4-5 days

**Requirements**:
```sql
CREATE TABLE products (
    id INT PRIMARY KEY,
    price DECIMAL CHECK (price > 0),
    stock INT CHECK (stock >= 0),
    CHECK (price * stock < 1000000)
);
```

**Implementation**:
- Parse CHECK expressions
- Evaluate on INSERT/UPDATE
- Support comparison operators
- Support complex expressions

---

### 2.3 GROUP BY / HAVING Execution ⏳
**Estimated Effort**: 6-8 days

**Requirements**:
```sql
SELECT 
    dept,
    COUNT(*) as total,
    AVG(salary) as avg_salary
FROM employees
GROUP BY dept
HAVING COUNT(*) > 5;
```

**Implementation**:
- Group rows by column values
- Execute aggregate functions per group
- Apply HAVING filter
- Support multiple GROUP BY columns

---

### 2.4 String Functions ⏳
**Estimated Effort**: 4-6 days

**Requirements**:
```sql
SELECT UPPER(name), LOWER(email) FROM users;
SELECT SUBSTR(name, 1, 5) FROM users;
SELECT LENGTH(name), TRIM(name) FROM users;
SELECT REPLACE(email, '@', ' AT ') FROM users;
```

**Implementation**:
- Implement UPPER, LOWER
- Implement SUBSTR, LEFT, RIGHT
- Implement LENGTH, TRIM, LTRIM, RTRIM
- Implement REPLACE, CONCAT
- Integrate with SELECT execution

---

### 2.5 Subquery Execution ⏳
**Estimated Effort**: 5-7 days

**Requirements**:
```sql
SELECT * FROM users WHERE id IN (SELECT user_id FROM orders);
SELECT * FROM (SELECT * FROM users WHERE age > 18) AS adults;
```

**Implementation**:
- Execute subqueries in WHERE IN
- Execute derived table subqueries (FROM)
- Execute scalar subqueries (SELECT)
- Optimize nested execution

---

## 🎯 Phase 3: Advanced SQL (Optional, 8-12 Weeks)

**Goal**: Full SQL parity with SQLite  
**Target Completion**: 100% overall  
**Priority**: **NICE-TO-HAVE**

### 3.1 Views ⏳
**Estimated Effort**: 5-7 days

```sql
CREATE VIEW active_users AS
SELECT * FROM users WHERE is_active = 1;

SELECT * FROM active_users;
DROP VIEW active_users;
```

---

### 3.2 CTEs (WITH Clause) ⏳
**Estimated Effort**: 7-10 days

```sql
WITH high_earners AS (
    SELECT * FROM employees WHERE salary > 100000
)
SELECT * FROM high_earners WHERE dept = 'IT';
```

---

### 3.3 Window Functions ⏳
**Estimated Effort**: 10-14 days

```sql
SELECT 
    name,
    ROW_NUMBER() OVER (ORDER BY salary DESC) as rank,
    AVG(salary) OVER (PARTITION BY dept) as dept_avg
FROM employees;
```

---

### 3.4 Full-Text Search ⏳
**Estimated Effort**: 14-21 days

```sql
CREATE VIRTUAL TABLE documents USING fts5(content);
SELECT * FROM documents WHERE documents MATCH 'search term';
```

---

### 3.5 JSON Support ⏳
**Estimated Effort**: 7-10 days

```sql
SELECT json_extract(data, '$.name') FROM users;
```

---

## 📅 Release Schedule

### Version 1.1.0 (Phase 1 Complete)
**Target**: Q2 2026 (6-8 weeks from now)

**Features**:
- ✅ ALTER TABLE ADD COLUMN
- ✅ FOREIGN KEY constraints
- ✅ DROP TABLE improvements
- ✅ UNIQUE constraints
- ✅ Enhanced NOT NULL enforcement

**Breaking Changes**: None  
**Migration**: Automatic

---

### Version 1.2.0 (Phase 2 Complete)
**Target**: Q3 2026

**Features**:
- ✅ CHECK constraints
- ✅ DEFAULT with expressions
- ✅ GROUP BY / HAVING
- ✅ String functions
- ✅ Subqueries

**Breaking Changes**: None  
**Migration**: Automatic

---

### Version 2.0.0 (Phase 3 Complete)
**Target**: Q4 2026+

**Features**:
- ✅ Views
- ✅ CTEs
- ✅ Window functions
- ✅ Full-text search
- ✅ JSON support

**Breaking Changes**: Possible  
**Migration**: May require manual updates

---

## 📊 Progress Tracking

### Overall Completion

```
Phase 0 (Foundation):        ████████████████░░░░  82% (Complete) ✅
Phase 1 (Critical Schema):   ░░░░░░░░░░░░░░░░░░░░   0% (Next)     ⏳
Phase 2 (Data Integrity):    ░░░░░░░░░░░░░░░░░░░░   0% (Planned)  📋
Phase 3 (Advanced):          ░░░░░░░░░░░░░░░░░░░░   0% (Optional) 🎯

After Phase 1:               ██████████████████░░  88% ✅ PRODUCTION-READY
After Phase 2:               ███████████████████░  94% ✅ FEATURE-COMPLETE
After Phase 3:               ████████████████████ 100% ✅ FULL PARITY
```

### SQLite Feature Parity

```
Current:  ███████████████░░░░░ 82%
After P1: ██████████████████░░ 88%  (Production migrations enabled)
After P2: ███████████████████░ 94%  (Data integrity complete)
After P3: ████████████████████ 100% (Full SQLite alternative)
```

---

## 🎯 Success Metrics

### Phase 1 Success Criteria
- [ ] Can perform schema migrations (ALTER TABLE ADD COLUMN)
- [ ] Can enforce referential integrity (FOREIGN KEY)
- [ ] Can enforce uniqueness constraints (UNIQUE)
- [ ] Can drop tables cleanly (DROP TABLE)
- [ ] All existing 141+ tests still passing
- [ ] 50+ new tests for schema features
- [ ] Zero breaking changes to existing API
- [ ] Documentation complete and accurate

### Phase 2 Success Criteria
- [ ] Can enforce NOT NULL consistently
- [ ] Can use DEFAULT values with expressions
- [ ] Can execute subqueries
- [ ] Can GROUP BY with aggregates
- [ ] String functions work in SQL
- [ ] All Phase 1 features still working
- [ ] Zero performance regression

### Phase 3 Success Criteria
- [ ] Views create/query/drop
- [ ] Window functions calculate correctly
- [ ] CTEs work for recursive queries
- [ ] 90%+ SQLite feature parity achieved

---

## 🚀 Key Differentiators (Maintained)

Even as we add features, we maintain our core advantages:

- ✅ **Built-in encryption** (0% overhead)
- ✅ **SIMD analytics** (345x faster than LiteDB)
- ✅ **MVCC transactions**
- ✅ **B-Tree + Hash indexes**
- ✅ **Entity Framework Core**
- ✅ **.NET-first design**
- ✅ **Pure .NET** (no P/Invoke)
- ✅ **Async/await** throughout

---

## 🔗 Related Documentation

- [STATUS.md](STATUS.md) - Current feature status
- [KNOWN_ISSUES.md](KNOWN_ISSUES.md) - Current issues
- [ACTION_PLAN_2026.md](ACTION_PLAN_2026.md) - Implementation action plan
- [DOCUMENTATION_AUDIT_2026.md](DOCUMENTATION_AUDIT_2026.md) - Documentation audit

---

## 📞 Contributing

We welcome contributions! Priority areas for Phase 1:

1. **ALTER TABLE ADD COLUMN** (highest priority)
2. **FOREIGN KEY constraints**
3. **UNIQUE constraints**
4. **Test coverage**
5. **Documentation**

See [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.

---

**Timeline to Production-Ready**: **4-6 weeks** (Phase 1 only)  
**Timeline to Feature-Complete**: **10-14 weeks** (Phase 1 + 2)  
**Timeline to 100% Parity**: **20-26 weeks** (All phases)

**Last Updated**: 2026-01-XX  
**Next Review**: After Phase 1 completion (v1.1.0)
