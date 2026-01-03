# Phase 1: Schema Evolution - Implementation Plan

**Goal**: Enable production schema migrations in SharpCoreDB  
**Timeline**: 4-6 weeks (February - March 2026)  
**Target Completion**: 88% overall  
**Priority**: CRITICAL - Blocking production deployments

---

## 🎯 **Executive Summary**

Phase 1 focuses on **schema evolution** - the ability to modify database schemas after creation without data loss. This is the #1 blocker preventing production use of SharpCoreDB.

**Key Deliverables:**
- ✅ ALTER TABLE ADD COLUMN (highest priority)
- ✅ FOREIGN KEY constraints (data integrity)
- ✅ DROP TABLE (cleanup)
- ✅ UNIQUE constraints (uniqueness)
- ✅ Enhanced NOT NULL enforcement

**Success Criteria:**
- Production applications can perform schema migrations
- Referential integrity is enforced
- All existing functionality remains intact
- Zero breaking changes to existing API

---

## 📋 **Weekly Implementation Plan**

### **Week 1: ALTER TABLE ADD COLUMN (5 days)**

#### **Day 1: SQL Parser Extension**
**Objective**: Parse ALTER TABLE ADD COLUMN syntax

**Tasks:**
1. Extend `Services/SqlParser.cs` to recognize ALTER TABLE statements
2. Add `ALTER TABLE table_name ADD COLUMN column_def` grammar
3. Create `Services/EnhancedSqlParser.DDL.cs` for detailed ALTER parsing
4. Support column definitions with DEFAULT values
5. Support AUTO columns (ULID/GUID)

**Files to Create/Modify:**
- `Services/SqlParser.cs` - Add ALTER TABLE token recognition
- `Services/EnhancedSqlParser.DDL.cs` - Detailed ALTER TABLE parsing
- `Services/SqlAst.cs` - Add AlterTableStatement AST node

**Test Cases:**
```csharp
[Test]
public void ParseAlterTableAddColumn_Simple()
{
    var sql = "ALTER TABLE users ADD COLUMN age INT";
    var statement = parser.Parse(sql);
    Assert.IsType<AlterTableStatement>(statement);
    Assert.Equal("users", statement.TableName);
    Assert.Equal("age", statement.Column.Name);
    Assert.Equal(DataType.Integer, statement.Column.Type);
}
```

#### **Day 2: Table Schema Modification**
**Objective**: Update table metadata and schema

**Tasks:**
1. Add `AddColumn()` method to `DataStructures/Table.cs`
2. Update `Table.Columns`, `Table.ColumnTypes`, `Table.IsAuto` lists
3. Add column to existing row serialization logic
4. Handle DEFAULT values for existing rows (backfill)
5. Update metadata persistence (`meta.json`)

**Files to Modify:**
- `DataStructures/Table.cs` - Add `AddColumn()` method
- `Core/Database.Core.cs` - Metadata update logic
- `DataStructures/Table.Serialization.cs` - Row serialization updates

#### **Day 3: Data Backfill & Migration**
**Objective**: Handle existing data when adding columns

**Tasks:**
1. Implement backfill logic for existing rows
2. Read all existing data from storage engine
3. Add default values to each row
4. Rewrite data with new schema
5. Handle large tables efficiently (streaming)

**Files to Modify:**
- `DataStructures/Table.cs` - `BackfillExistingRows()` method
- `Storage/Hybrid/StorageEngine.cs` - Bulk data operations

#### **Day 4: Index Updates & Validation**
**Objective**: Ensure indexes work with new columns

**Tasks:**
1. Update hash indexes if new column is indexed
2. Rebuild B-tree indexes if necessary
3. Validate existing queries still work
4. Add validation to prevent duplicate column names

**Files to Modify:**
- `DataStructures/Table.Indexing.cs` - Index update logic
- `DataStructures/Table.cs` - Validation methods

#### **Day 5: Testing & Documentation**
**Objective**: Comprehensive testing and documentation

**Tasks:**
1. Unit tests for ALTER TABLE parsing
2. Integration tests for schema changes
3. Performance tests for large table migrations
4. Update API documentation
5. Add examples to demo applications

---

### **Week 2: FOREIGN KEY Constraints (7 days)**

#### **Days 6-7: FK Parser & Metadata**
**Objective**: Parse and store FOREIGN KEY constraints

**Tasks:**
1. Extend CREATE TABLE parser for FOREIGN KEY syntax
2. Add `ForeignKeyConstraint` data structure
3. Store FK metadata in `Table.ForeignKeys` collection
4. Support ON DELETE CASCADE/SET NULL/RESTRICT
5. Support ON UPDATE CASCADE

**Data Structure:**
```csharp
public class ForeignKeyConstraint
{
    public string ColumnName { get; set; }
    public string ReferencedTable { get; set; }
    public string ReferencedColumn { get; set; }
    public FkAction OnDelete { get; set; }
    public FkAction OnUpdate { get; set; }
}

public enum FkAction
{
    Restrict,
    Cascade,
    SetNull
}
```

**Files to Modify:**
- `Services/EnhancedSqlParser.DDL.cs` - FK parsing
- `DataStructures/Table.cs` - Add ForeignKeys collection
- `Core/Database.Core.cs` - FK metadata persistence

#### **Days 8-9: FK Validation Logic**
**Objective**: Enforce referential integrity

**Tasks:**
1. Validate FK on INSERT (referenced row must exist)
2. Validate FK on UPDATE (new value must exist)
3. Implement cascading deletes
4. Implement cascading updates
5. Handle circular dependencies

**Files to Modify:**
- `DataStructures/Table.CRUD.cs` - FK validation in Insert/Update
- `DataStructures/Table.Delete.cs` - Cascading delete logic

#### **Days 10-11: Cascading Operations**
**Objective**: Implement CASCADE and SET NULL actions

**Tasks:**
1. Implement ON DELETE CASCADE (delete child rows)
2. Implement ON DELETE SET NULL (set FK to null)
3. Implement ON UPDATE CASCADE (update child rows)
4. Handle multiple levels of cascading
5. Prevent infinite loops

**Files to Modify:**
- `DataStructures/Table.Delete.cs` - Cascading delete
- `DataStructures/Table.Update.cs` - Cascading update

#### **Day 12: FK Testing & Edge Cases**
**Objective**: Comprehensive FK testing

**Tasks:**
1. Test all FK actions (CASCADE, SET NULL, RESTRICT)
2. Test circular dependency detection
3. Test performance impact
4. Test error messages
5. Integration tests with existing features

---

### **Week 3: DROP TABLE & UNIQUE Constraints (7 days)**

#### **Days 13-14: DROP TABLE Implementation**
**Objective**: Clean table removal

**Tasks:**
1. Parse `DROP TABLE [IF EXISTS] table_name`
2. Delete table data files (`.dat`, `.pages`, indexes)
3. Remove from `Database.tables` dictionary
4. Check for FK dependencies before dropping
5. Update metadata

**Files to Modify:**
- `Services/SqlParser.cs` - DROP TABLE parsing
- `Core/Database.cs` - Table removal logic
- `Core/Database.Core.cs` - Metadata cleanup

#### **Days 15-16: UNIQUE Constraints**
**Objective**: Enforce uniqueness

**Tasks:**
1. Parse UNIQUE in column definitions
2. Parse table-level UNIQUE constraints
3. Auto-create unique indexes
4. Validate uniqueness on INSERT/UPDATE
5. Handle NULL values (multiple NULLs allowed)

**Files to Modify:**
- `Services/EnhancedSqlParser.DDL.cs` - UNIQUE parsing
- `DataStructures/Table.cs` - Track unique constraints
- `DataStructures/Table.CRUD.cs` - Uniqueness validation

#### **Days 17-18: Composite UNIQUE**
**Objective**: Support multi-column uniqueness

**Tasks:**
1. Parse `UNIQUE (col1, col2, col3)` syntax
2. Create composite unique indexes
3. Validate composite uniqueness
4. Handle partial NULLs in composite keys

#### **Day 19: Integration Testing**
**Objective**: Test all schema features together

---

### **Week 4: Enhanced NOT NULL & Final Polish (7 days)**

#### **Days 20-21: NOT NULL Enhancement**
**Objective**: Complete NOT NULL enforcement

**Tasks:**
1. Complete existing partial NOT NULL implementation
2. Validate on all INSERT paths
3. Validate on all UPDATE paths
4. Clear error messages with column names
5. Minimal performance impact

**Files to Modify:**
- `DataStructures/Table.CRUD.cs` - Complete validation
- `DataStructures/Table.BatchUpdate.cs` - Batch validation

#### **Days 22-23: Performance Optimization**
**Objective**: Ensure schema operations are fast

**Tasks:**
1. Optimize ALTER TABLE for large tables
2. Optimize FK validation performance
3. Optimize UNIQUE constraint checking
4. Memory-efficient backfill operations

#### **Days 24-25: Comprehensive Testing**
**Objective**: Full test coverage

**Tasks:**
1. 50+ new unit tests
2. Integration tests for all features
3. Performance regression tests
4. Edge case testing

#### **Day 26: Documentation & Release Prep**
**Objective**: Complete documentation

**Tasks:**
1. Update API documentation
2. Add examples and tutorials
3. Update README and guides
4. Prepare release notes

---

## 📊 **Progress Tracking**

### **Daily Progress Dashboard**

| Day | Feature | Status | Tests | Notes |
|-----|---------|--------|-------|-------|
| 1 | ALTER TABLE parser | ⏳ | 0/5 | Basic parsing |
| 2 | Table schema updates | ⏳ | 0/8 | AddColumn method |
| 3 | Data backfill | ⏳ | 0/6 | Large table handling |
| 4 | Index updates | ⏳ | 0/4 | Hash/B-tree updates |
| 5 | ALTER TABLE testing | ⏳ | 0/12 | Full integration |
| 6-7 | FK parser & metadata | ⏳ | 0/8 | Constraint storage |
| 8-9 | FK validation | ⏳ | 0/10 | Insert/Update checks |
| 10-11 | Cascading operations | ⏳ | 0/12 | CASCADE/SET NULL |
| 12 | FK testing | ⏳ | 0/15 | Edge cases |
| 13-14 | DROP TABLE | ⏳ | 0/6 | Clean removal |
| 15-16 | UNIQUE constraints | ⏳ | 0/8 | Single column |
| 17-18 | Composite UNIQUE | ⏳ | 0/6 | Multi-column |
| 19 | Integration testing | ⏳ | 0/20 | All features |
| 20-21 | NOT NULL enhancement | ⏳ | 0/8 | Complete enforcement |
| 22-23 | Performance optimization | ⏳ | 0/5 | Large table handling |
| 24-25 | Comprehensive testing | ⏳ | 0/30 | Full coverage |
| 26 | Documentation & release | ⏳ | 0/5 | Complete docs |

### **Weekly Milestones**

**Week 1 (ALTER TABLE):**
- [ ] ALTER TABLE ADD COLUMN fully working
- [ ] Backfill logic for existing data
- [ ] Index compatibility verified
- [ ] 20+ unit tests passing

**Week 2 (FOREIGN KEYS):**
- [ ] FK constraints parsed and stored
- [ ] Validation on INSERT/UPDATE
- [ ] Cascading operations working
- [ ] 25+ FK tests passing

**Week 3 (DROP & UNIQUE):**
- [ ] DROP TABLE with dependency checks
- [ ] UNIQUE constraints (single & composite)
- [ ] 15+ new tests passing

**Week 4 (Polish & Test):**
- [ ] Enhanced NOT NULL enforcement
- [ ] Performance optimizations
- [ ] 50+ total new tests
- [ ] Documentation complete

---

## 🔧 **Technical Architecture**

### **Schema Metadata Storage**

```json
{
  "tables": {
    "users": {
      "columns": ["id", "name", "email", "age"],
      "types": ["INTEGER", "STRING", "STRING", "INTEGER"],
      "isAuto": [false, false, false, false],
      "primaryKey": 0,
      "foreignKeys": [
        {
          "column": "department_id",
          "references": "departments.id",
          "onDelete": "CASCADE",
          "onUpdate": "RESTRICT"
        }
      ],
      "uniqueConstraints": [
        ["email"],
        ["name", "department_id"]
      ],
      "notNullColumns": [0, 1, 2]
    }
  }
}
```

### **Constraint Validation Flow**

```
INSERT/UPDATE Request
    ↓
Validate NOT NULL constraints
    ↓
Validate UNIQUE constraints
    ↓
Validate FOREIGN KEY constraints
    ↓
Execute operation
    ↓
Apply cascading actions if needed
```

### **Backfill Process for ALTER TABLE**

```
1. Parse ALTER TABLE statement
2. Validate new column definition
3. Create backup of existing data
4. Read all existing rows
5. Add default values to each row
6. Write rows with new schema
7. Update indexes
8. Update metadata
9. Cleanup old data files
```

---

## 🧪 **Testing Strategy**

### **Unit Tests (30 tests)**
- Parser tests for all DDL syntax
- Schema modification tests
- Constraint validation tests
- Error handling tests

### **Integration Tests (20 tests)**
- End-to-end schema migrations
- FK relationship tests
- Cascading operation tests
- Large table performance tests

### **Performance Tests (5 tests)**
- ALTER TABLE on 100K rows
- FK validation on bulk inserts
- UNIQUE constraint checking
- Memory usage during migrations

### **Regression Tests**
- All existing 141+ tests still pass
- No breaking changes to existing API
- Performance regressions <5%

---

## 🚨 **Risks & Mitigations**

### **High-Risk Items**

1. **Data Loss During Migration**
   - **Risk**: ALTER TABLE could corrupt data
   - **Mitigation**: Comprehensive backups, transaction safety, extensive testing

2. **Performance Impact**
   - **Risk**: Schema operations slow on large tables
   - **Mitigation**: Streaming processing, chunked operations, progress reporting

3. **FK Complexity**
   - **Risk**: Cascading operations could cause deadlocks
   - **Mitigation**: Careful ordering, deadlock detection, rollback on failure

### **Contingency Plans**

- **Rollback Strategy**: Ability to revert schema changes
- **Partial Implementation**: Can ship without composite UNIQUE if needed
- **Performance Fallbacks**: Synchronous operations if async fails

---

## 📈 **Success Metrics**

### **Functional Completeness**
- [ ] ALTER TABLE ADD COLUMN works for all data types
- [ ] FOREIGN KEY constraints enforced
- [ ] UNIQUE constraints work (single & composite)
- [ ] DROP TABLE removes all traces
- [ ] NOT NULL fully enforced

### **Quality Assurance**
- [ ] 50+ new tests passing
- [ ] All existing tests still pass
- [ ] Performance impact <5%
- [ ] Memory usage reasonable
- [ ] Error messages clear

### **Production Readiness**
- [ ] Schema migrations work in production
- [ ] No data loss during migrations
- [ ] Transaction safety maintained
- [ ] Documentation complete
- [ ] Examples provided

---

## 🎯 **Deliverables**

### **Code Changes**
- 8+ files modified/created
- 1000+ lines of new code
- Zero breaking changes

### **Documentation**
- Updated API docs
- Migration guides
- Performance benchmarks
- Example applications

### **Testing**
- 50+ new unit tests
- Integration test suite
- Performance regression tests

### **Release Package**
- Version 1.1.0
- NuGet package
- Release notes
- Migration guide

---

## 🚀 **Post-Phase 1 Benefits**

After completing Phase 1, SharpCoreDB becomes **production-ready**:

- ✅ **Schema Migrations**: Applications can evolve their data models
- ✅ **Data Integrity**: Referential integrity enforced
- ✅ **Production Deployments**: Safe to use in production environments
- ✅ **Enterprise Features**: Meets enterprise requirements

**This transforms SharpCoreDB from a "cool prototype" to a "serious database alternative".**

---

**Phase 1 Implementation Plan Complete** ✅  
**Ready for implementation starting February 2026**
