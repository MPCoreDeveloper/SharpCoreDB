# Missing Features Implementation Roadmap

**Last Updated**: 2025-12-13  
**Purpose**: Prioritized implementation plan to reach SQLite feature parity  
**Target**: 90% feature completeness (from current 75%)

---

## Implementation Phases

### ✅ Phase 0: Foundation (COMPLETE)

**Status**: ✅ **DONE** - All core features implemented

**Completed Features**:
- ✅ Basic SQL (SELECT, INSERT, UPDATE, DELETE)
- ✅ PRIMARY KEY indexes (B-Tree)
- ✅ Hash indexes (O(1) lookups)
- ✅ LEFT/INNER JOINs
- ✅ Transactions (MVCC with snapshot isolation)
- ✅ GroupCommit WAL
- ✅ Encryption (AES-256-GCM)
- ✅ Entity Framework Core provider
- ✅ Connection pooling
- ✅ Query caching

---

## 📋 Phase 1: Critical Schema Features (P0)

**Timeline**: 2-4 weeks  
**Goal**: Enable schema migrations and data integrity  
**Completion**: 0/5 features

### Feature 1.1: ALTER TABLE ADD COLUMN ⏳

**Priority**: **CRITICAL** (P0)  
**Estimated Effort**: 3-5 days  
**Complexity**: Medium

**Requirements**:
```sql
-- Support basic column addition:
ALTER TABLE users ADD COLUMN age INT;
ALTER TABLE users ADD COLUMN status TEXT DEFAULT 'active';
ALTER TABLE users ADD COLUMN created_at DATETIME DEFAULT CURRENT_TIMESTAMP;
```

**Implementation Tasks**:
1. Parse `ALTER TABLE table_name ADD COLUMN column_def` syntax
2. Update `Table.Columns`, `Table.ColumnTypes`, `Table.IsAuto` lists
3. Add column to existing row serialization (with default value)
4. Update metadata persistence
5. Rebuild indexes if needed
6. Add validation (prevent duplicate column names)

**Files to Modify**:
- `Services/SqlParser.cs` - Add ALTER TABLE parsing
- `DataStructures/Table.cs` - Add `AddColumn()` method
- `Services/EnhancedSqlParser.DDL.cs` - Add ALTER parsing

**Test Cases**:
- Add column without DEFAULT
- Add column with DEFAULT value
- Add column with AUTO (ULID/GUID)
- Add column to table with existing data
- Try to add duplicate column (should fail)

**Acceptance Criteria**:
- [ ] ALTER TABLE ADD COLUMN parses correctly
- [ ] Column added to schema
- [ ] Existing rows preserve data
- [ ] New INSERTs include new column
- [ ] Metadata persists correctly
- [ ] Indexes remain valid

---

### Feature 1.2: DROP TABLE ⏳

**Priority**: **CRITICAL** (P0)  
**Estimated Effort**: 1-2 days  
**Complexity**: Low

**Requirements**:
```sql
DROP TABLE users;
DROP TABLE IF EXISTS temp_table;
```

**Implementation Tasks**:
1. Parse `DROP TABLE [IF EXISTS] table_name`
2. Delete table data file
3. Remove from `tables` dictionary
4. Remove associated indexes
5. Update metadata
6. Handle IF EXISTS clause

**Files to Modify**:
- `Services/SqlParser.cs` - Add DROP TABLE parsing
- `Database.cs` - Add table removal logic

**Test Cases**:
- Drop existing table
- Drop non-existent table (should fail)
- DROP IF EXISTS (should not fail)
- Verify file deletion
- Verify metadata update

**Acceptance Criteria**:
- [ ] DROP TABLE removes table
- [ ] IF EXISTS prevents errors
- [ ] Files are deleted
- [ ] Indexes are removed
- [ ] Metadata is updated

---

### Feature 1.3: FOREIGN KEY Constraints ⏳

**Priority**: **CRITICAL** (P0)  
**Estimated Effort**: 7-10 days  
**Complexity**: High

**Requirements**:
```sql
CREATE TABLE orders (
    id INT PRIMARY KEY,
    user_id INT,
    FOREIGN KEY (user_id) REFERENCES users(id)
        ON DELETE CASCADE
        ON UPDATE CASCADE
);

-- Validation on INSERT:
INSERT INTO orders VALUES (1, 999);  -- Should fail if user 999 doesn't exist

-- Cascade delete:
DELETE FROM users WHERE id = 1;  -- Should delete all orders for user 1
```

**Implementation Tasks**:
1. Parse FOREIGN KEY in CREATE TABLE
2. Store FK metadata (referencing table, column, action)
3. Validate FK on INSERT (referenced row must exist)
4. Validate FK on UPDATE (new value must exist)
5. Implement ON DELETE CASCADE
6. Implement ON DELETE SET NULL
7. Implement ON UPDATE CASCADE
8. Add PRAGMA foreign_keys ON/OFF

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

public enum FkAction { Cascade, SetNull, Restrict, NoAction }
```

**Files to Modify**:
- `Services/EnhancedSqlParser.DDL.cs` - Parse FK constraints
- `DataStructures/Table.cs` - Add FK list
- `DataStructures/Table.CRUD.cs` - Add FK validation
- `Database.cs` - FK enforcement

**Test Cases**:
- Create table with FK
- Insert valid FK value (should succeed)
- Insert invalid FK value (should fail)
- Update FK to invalid value (should fail)
- Delete parent row with CASCADE (should delete children)
- Delete parent row with RESTRICT (should fail if children exist)
- Update parent row with CASCADE

**Acceptance Criteria**:
- [ ] FK constraints are parsed
- [ ] INSERT validates FK
- [ ] UPDATE validates FK
- [ ] DELETE CASCADE works
- [ ] SET NULL works
- [ ] Error messages are clear

---

### Feature 1.4: UNIQUE Constraint (Table-Level) ⏳

**Priority**: **CRITICAL** (P0)  
**Estimated Effort**: 3-4 days  
**Complexity**: Medium

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

**Files to Modify**:
- `Services/EnhancedSqlParser.DDL.cs` - Parse UNIQUE
- `DataStructures/Table.cs` - Track unique constraints
- `DataStructures/Table.CRUD.cs` - Validate uniqueness

**Test Cases**:
- Create table with UNIQUE column
- Insert duplicate value (should fail)
- Update to duplicate value (should fail)
- Composite UNIQUE constraint
- NULL values in UNIQUE column (allowed)

**Acceptance Criteria**:
- [ ] UNIQUE column parsed
- [ ] Table-level UNIQUE parsed
- [ ] Uniqueness validated on INSERT
- [ ] Uniqueness validated on UPDATE
- [ ] Composite UNIQUE works
- [ ] NULL handling correct

---

### Feature 1.5: DROP INDEX ⏳

**Priority**: **HIGH** (P0)  
**Estimated Effort**: 1-2 days  
**Complexity**: Low

**Requirements**:
```sql
DROP INDEX idx_users_email;
DROP INDEX IF EXISTS idx_temp;
```

**Implementation Tasks**:
1. Parse `DROP INDEX [IF EXISTS] index_name`
2. Remove index from table's index collections
3. Update metadata
4. Handle IF EXISTS clause

**Files to Modify**:
- `Services/SqlParser.cs` - Add DROP INDEX parsing
- `DataStructures/Table.Indexing.cs` - Add RemoveIndex() method

**Test Cases**:
- Drop existing index
- Drop non-existent index (should fail)
- DROP IF EXISTS
- Verify queries still work (fall back to scan)

**Acceptance Criteria**:
- [ ] DROP INDEX works
- [ ] IF EXISTS prevents errors
- [ ] Metadata updated
- [ ] Queries still execute

---

## 📋 Phase 2: Data Integrity & Validation (P1)

**Timeline**: 4-6 weeks  
**Goal**: Enforce constraints and add SQL functions  
**Completion**: 0/6 features

### Feature 2.1: NOT NULL Enforcement ⏳

**Priority**: **HIGH** (P1)  
**Estimated Effort**: 2-3 days  
**Complexity**: Low

**Requirements**:
```sql
CREATE TABLE users (
    id INT PRIMARY KEY,
    name TEXT NOT NULL,
    email TEXT NOT NULL
);

INSERT INTO users VALUES (1, NULL, 'test@test.com');  -- Should fail!
```

**Implementation**:
1. Track NOT NULL columns
2. Validate on INSERT
3. Validate on UPDATE
4. Clear error messages

**Files**: `Table.CRUD.cs`, `SqlParser.cs`

**Acceptance Criteria**:
- [ ] NOT NULL validated on INSERT
- [ ] NOT NULL validated on UPDATE
- [ ] Clear error messages

---

### Feature 2.2: DEFAULT Values ⏳

**Priority**: **HIGH** (P1)  
**Estimated Effort**: 3-4 days  
**Complexity**: Medium

**Requirements**:
```sql
CREATE TABLE users (
    id INT PRIMARY KEY,
    name TEXT DEFAULT 'Unknown',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status TEXT DEFAULT 'active',
    count INT DEFAULT 0
);

INSERT INTO users (id) VALUES (1);  -- Should use defaults
```

**Implementation**:
1. Parse DEFAULT in column definition
2. Store default values per column
3. Apply defaults when column omitted in INSERT
4. Support literals (strings, numbers, NULL)
5. Support SQL expressions (CURRENT_TIMESTAMP, etc.)

**Files**: `EnhancedSqlParser.DDL.cs`, `Table.CRUD.cs`

**Acceptance Criteria**:
- [ ] DEFAULT values parsed
- [ ] Defaults applied on INSERT
- [ ] Literals work
- [ ] CURRENT_TIMESTAMP works

---

### Feature 2.3: CHECK Constraints ⏳

**Priority**: **MEDIUM** (P1)  
**Estimated Effort**: 4-5 days  
**Complexity**: Medium

**Requirements**:
```sql
CREATE TABLE products (
    id INT PRIMARY KEY,
    price DECIMAL CHECK (price > 0),
    stock INT CHECK (stock >= 0)
);
```

**Implementation**:
1. Parse CHECK expressions
2. Evaluate on INSERT/UPDATE
3. Support comparison operators
4. Support IN clause

**Files**: `EnhancedSqlParser.DDL.cs`, `Table.CRUD.cs`

**Acceptance Criteria**:
- [ ] CHECK parsed
- [ ] Validation on INSERT
- [ ] Validation on UPDATE
- [ ] Clear error messages

---

### Feature 2.4: Subquery Execution ⏳

**Priority**: **MEDIUM** (P1)  
**Estimated Effort**: 5-7 days  
**Complexity**: High

**Requirements**:
```sql
SELECT * FROM users WHERE id IN (SELECT user_id FROM orders);
SELECT * FROM (SELECT * FROM users WHERE age > 18) AS adults;
```

**Implementation**:
1. Execute subqueries in WHERE IN
2. Execute derived table subqueries (FROM)
3. Execute scalar subqueries (SELECT)
4. Optimize nested execution

**Files**: `SqlParser.cs`, `EnhancedSqlParser.Select.cs`

**Acceptance Criteria**:
- [ ] IN subqueries work
- [ ] FROM subqueries work
- [ ] Scalar subqueries work
- [ ] Performance acceptable

---

### Feature 2.5: GROUP BY / HAVING Execution ⏳

**Priority**: **HIGH** (P1)  
**Estimated Effort**: 6-8 days  
**Complexity**: High

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
1. Group rows by column values
2. Execute aggregate functions per group
3. Apply HAVING filter
4. Support multiple GROUP BY columns

**Files**: `SqlParser.cs`, `Table.CRUD.cs`

**Acceptance Criteria**:
- [ ] GROUP BY works
- [ ] Aggregates calculated per group
- [ ] HAVING filters groups
- [ ] Multiple GROUP BY columns

---

### Feature 2.6: String Functions ⏳

**Priority**: **MEDIUM** (P1)  
**Estimated Effort**: 4-6 days  
**Complexity**: Medium

**Requirements**:
```sql
SELECT UPPER(name), LOWER(email) FROM users;
SELECT SUBSTR(name, 1, 5) FROM users;
SELECT LENGTH(name), TRIM(name) FROM users;
```

**Implementation**:
1. Implement UPPER, LOWER
2. Implement SUBSTR
3. Implement LENGTH, TRIM
4. Implement REPLACE
5. Integrate with SELECT execution

**Files**: `Services/SqlFunctions.cs`, `SqlParser.cs`

**Acceptance Criteria**:
- [ ] UPPER/LOWER work
- [ ] SUBSTR works
- [ ] LENGTH/TRIM work
- [ ] Functions work in WHERE clauses

---

## 📋 Phase 3: Advanced Features (P2)

**Timeline**: 6-12 weeks  
**Goal**: Add nice-to-have features for completeness  
**Completion**: 0/5 features

### Feature 3.1: Views ⏳

**Priority**: **MEDIUM** (P2)  
**Estimated Effort**: 5-7 days  
**Complexity**: Medium

**Requirements**:
```sql
CREATE VIEW active_users AS
SELECT * FROM users WHERE is_active = 1;

SELECT * FROM active_users;
DROP VIEW active_users;
```

**Implementation**:
1. Parse CREATE VIEW
2. Store view definition
3. Expand view on query
4. Support DROP VIEW

---

### Feature 3.2: Window Functions ⏳

**Priority**: **LOW** (P2)  
**Estimated Effort**: 10-14 days  
**Complexity**: Very High

**Requirements**:
```sql
SELECT 
    name,
    ROW_NUMBER() OVER (ORDER BY salary DESC) as rank,
    AVG(salary) OVER (PARTITION BY dept) as dept_avg
FROM employees;
```

**Implementation**: Complex - requires window frame processing

---

### Feature 3.3: CTEs (WITH Clause) ⏳

**Priority**: **LOW** (P2)  
**Estimated Effort**: 7-10 days  
**Complexity**: High

**Requirements**:
```sql
WITH high_earners AS (
    SELECT * FROM employees WHERE salary > 100000
)
SELECT * FROM high_earners WHERE dept = 'IT';
```

---

### Feature 3.4: Full-Text Search ⏳

**Priority**: **LOW** (P2)  
**Estimated Effort**: 14-21 days  
**Complexity**: Very High

**Requirements**:
```sql
CREATE VIRTUAL TABLE documents USING fts5(content);
SELECT * FROM documents WHERE documents MATCH 'search term';
```

---

### Feature 3.5: JSON Support ⏳

**Priority**: **LOW** (P2)  
**Estimated Effort**: 7-10 days  
**Complexity**: Medium

**Requirements**:
```sql
SELECT json_extract(data, '$.name') FROM users;
```

---

## Progress Tracking

### Overall Completion

```
Phase 0 (Foundation):        ████████████████████ 100% (20/20) ✅
Phase 1 (Critical Schema):   ░░░░░░░░░░░░░░░░░░░░   0% (0/5)  ⏳
Phase 2 (Data Integrity):    ░░░░░░░░░░░░░░░░░░░░   0% (0/6)  ⏳
Phase 3 (Advanced):          ░░░░░░░░░░░░░░░░░░░░   0% (0/5)  ⏳

Total Progress:              ████░░░░░░░░░░░░░░░░ 55% (20/36)
```

### SQLite Feature Parity

```
Current:  ███████████████░░░░░ 75%
After P1: ██████████████████░░ 90%
After P2: ████████████████████ 100%
```

---

## Implementation Priorities

### Must Have (P0 - Critical)
1. ✅ ALTER TABLE ADD COLUMN - Schema migrations
2. ✅ FOREIGN KEY - Referential integrity
3. ✅ UNIQUE constraints - Data integrity
4. ✅ DROP TABLE/INDEX - Schema management

### Should Have (P1 - Important)
5. ✅ NOT NULL enforcement - Validation
6. ✅ DEFAULT values - Convenience
7. ✅ Subqueries - Advanced queries
8. ✅ GROUP BY/HAVING - Analytics
9. ✅ String functions - Data manipulation

### Nice to Have (P2 - Advanced)
10. ⚠️ Views - Abstraction
11. ⚠️ Window functions - Analytics
12. ⚠️ CTEs - Complex queries
13. ⚠️ Full-text search - Text search
14. ⚠️ JSON support - Semi-structured data

---

## Success Metrics

### Phase 1 Success Criteria
- [ ] Can perform schema migrations (ALTER TABLE)
- [ ] Can enforce referential integrity (FOREIGN KEY)
- [ ] Can enforce uniqueness constraints
- [ ] Can drop tables and indexes
- [ ] All existing tests pass
- [ ] New tests for P0 features pass

### Phase 2 Success Criteria
- [ ] Can enforce NOT NULL
- [ ] Can use DEFAULT values
- [ ] Can execute subqueries
- [ ] Can GROUP BY with aggregates
- [ ] String functions work in SQL
- [ ] All Phase 1 features still work

### Phase 3 Success Criteria
- [ ] Views work
- [ ] Window functions work (if implemented)
- [ ] CTEs work (if implemented)
- [ ] 90%+ SQLite feature parity achieved

---

## Risk Mitigation

### Technical Risks

**Risk 1: Breaking Changes**
- **Impact**: High
- **Mitigation**: Extensive test coverage, versioned releases

**Risk 2: Performance Regression**
- **Impact**: Medium
- **Mitigation**: Benchmark before/after, profiling

**Risk 3: Complex Features (Window Functions)**
- **Impact**: Medium
- **Mitigation**: Phase 3 is optional, can defer

### Resource Risks

**Risk 1: Time Estimation**
- **Impact**: Medium
- **Mitigation**: Add 20% buffer to estimates

**Risk 2: Feature Scope Creep**
- **Impact**: Low
- **Mitigation**: Strict prioritization, defer to later phases

---

## Release Strategy

### Version 1.1 (Phase 1 Complete)
- **Target**: Q2 2025
- **Features**: ALTER TABLE, FOREIGN KEY, UNIQUE, DROP TABLE/INDEX
- **Breaking Changes**: None expected
- **Migration**: Automatic

### Version 1.2 (Phase 2 Complete)
- **Target**: Q3 2025
- **Features**: NOT NULL, DEFAULT, Subqueries, GROUP BY, String functions
- **Breaking Changes**: None expected
- **Migration**: Automatic

### Version 2.0 (Phase 3 Complete)
- **Target**: Q4 2025+
- **Features**: Views, Window functions, CTEs, FTS, JSON
- **Breaking Changes**: Possible
- **Migration**: May require manual updates

---

## Contribution Guidelines

### For Contributors

**Priority Areas**:
1. Phase 1 (P0) features - Most impactful
2. Documentation - Always needed
3. Test coverage - Critical for stability

**How to Contribute**:
1. Pick a feature from Phase 1 or 2
2. Create issue with implementation plan
3. Submit PR with tests
4. Update documentation

**Code Standards**:
- Follow existing code style
- Add XML documentation
- Include unit tests
- Update CHANGELOG.md

---

## Conclusion

**Current State**: SharpCoreDB is **75% feature-complete** vs SQLite

**After Phase 1**: Will be **90% feature-complete** - suitable for most production use cases

**After Phase 2**: Will be **95% feature-complete** - competitive with SQLite for .NET apps

**After Phase 3**: Will be **100% feature-complete** - full SQLite alternative

**Recommended Approach**: 
1. Focus on Phase 1 (2-4 weeks) to reach 90% parity
2. Ship v1.1 with critical schema features
3. Evaluate Phase 2 based on user feedback
4. Consider Phase 3 as long-term roadmap

**Key Differentiators to Maintain**:
- ✅ Built-in encryption
- ✅ MVCC transactions
- ✅ Hash indexes
- ✅ Entity Framework Core
- ✅ .NET-first design

**Timeline to 90% Parity**: **2-4 weeks** (Phase 1 only)  
**Timeline to 100% Parity**: **12-20 weeks** (All phases)

---

**Last Updated**: 2025-12-13  
**Next Review**: After Phase 1 completion
