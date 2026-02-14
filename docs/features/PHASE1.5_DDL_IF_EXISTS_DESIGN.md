# Phase 1.5: DDL IF EXISTS/IF NOT EXISTS Extensions — Design Document

**Phase:** 1.5 (DDL Extensions)  
**Status:** 🚧 In Progress  
**Priority:** High  
**Estimated Effort:** 4-6 hours  
**Dependencies:** Phase 1 (Core DDL)  

---

## 🎯 Objectives

Complete the SQL standard compliance for DDL operations by adding **IF EXISTS** and **IF NOT EXISTS** clauses to all remaining DDL statements.

### Current State

| Statement | IF NOT EXISTS | IF EXISTS | Status |
|-----------|---------------|-----------|--------|
| `CREATE TABLE` | ✅ Implemented | N/A | Complete |
| `DROP TABLE` | N/A | ✅ Implemented | Complete |
| `CREATE INDEX` | ❌ Missing | N/A | **Incomplete** |
| `DROP INDEX` | N/A | ❌ Missing | **Incomplete** |
| `CREATE PROCEDURE` | ❌ Missing | N/A | **Incomplete** |
| `DROP PROCEDURE` | N/A | ❌ Missing | **Incomplete** |
| `CREATE VIEW` | ❌ Missing | N/A | **Incomplete** |
| `DROP VIEW` | N/A | ❌ Missing | **Incomplete** |
| `CREATE TRIGGER` | ❌ Missing | N/A | **Incomplete** |
| `DROP TRIGGER` | N/A | ❌ Missing | **Incomplete** |
| `ALTER TABLE` | ❌ Missing | N/A | **Incomplete** |

### Target State

All DDL statements support appropriate IF EXISTS/IF NOT EXISTS clauses with consistent behavior.

---

## 📋 Requirements

### 1. CREATE INDEX IF NOT EXISTS

**Syntax:**
```sql
CREATE [UNIQUE] INDEX [IF NOT EXISTS] index_name 
ON table_name (column1, column2, ...) 
[USING BTREE|HASH]
```

**Behavior:**
- If index exists: **silently skip** (no error, no warning)
- If index doesn't exist: create normally
- If table doesn't exist: **throw error** (table must exist)

**Examples:**
```sql
-- Safe idempotent index creation
CREATE INDEX IF NOT EXISTS idx_users_age ON users(age);
CREATE UNIQUE INDEX IF NOT EXISTS idx_users_email ON users(email);
CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(created_at) USING BTREE;
```

### 2. DROP INDEX IF EXISTS

**Syntax:**
```sql
DROP INDEX [IF EXISTS] index_name [ON table_name]
```

**Behavior:**
- If index exists: drop normally
- If index doesn't exist: **silently skip** (no error)
- ON clause is optional (for SQLite compatibility)

**Examples:**
```sql
-- Safe cleanup
DROP INDEX IF EXISTS idx_users_age;
DROP INDEX IF EXISTS idx_users_age ON users;
```

### 3. DROP PROCEDURE IF EXISTS

**Syntax:**
```sql
DROP PROCEDURE [IF EXISTS] procedure_name
```

**Behavior:**
- If procedure exists: drop normally
- If procedure doesn't exist: **silently skip**

**Examples:**
```sql
DROP PROCEDURE IF EXISTS sp_calculate_total;
DROP PROCEDURE IF EXISTS legacy_proc;
```

### 4. DROP VIEW IF EXISTS

**Syntax:**
```sql
DROP VIEW [IF EXISTS] view_name
```

**Behavior:**
- If view exists: drop normally
- If view doesn't exist: **silently skip**

**Examples:**
```sql
DROP VIEW IF EXISTS vw_active_users;
DROP VIEW IF EXISTS vw_legacy_report;
```

### 5. DROP TRIGGER IF EXISTS

**Syntax:**
```sql
DROP TRIGGER [IF EXISTS] trigger_name
```

**Behavior:**
- If trigger exists: drop normally
- If trigger doesn't exist: **silently skip**

**Examples:**
```sql
DROP TRIGGER IF EXISTS trg_users_audit;
DROP TRIGGER IF EXISTS trg_orders_validate;
```

### 6. CREATE PROCEDURE IF NOT EXISTS

**Syntax:**
```sql
CREATE PROCEDURE [IF NOT EXISTS] procedure_name (parameters)
BEGIN
    -- statements
END
```

**Behavior:**
- If procedure exists: **silently skip** (do not replace)
- If procedure doesn't exist: create normally

**Note:** For **replacement** semantics, use `CREATE OR REPLACE PROCEDURE` (future Phase 1.6)

**Examples:**
```sql
CREATE PROCEDURE IF NOT EXISTS sp_init()
BEGIN
    -- initialization logic
END;
```

### 7. CREATE VIEW IF NOT EXISTS

**Syntax:**
```sql
CREATE VIEW [IF NOT EXISTS] view_name AS
SELECT ...
```

**Behavior:**
- If view exists: **silently skip**
- If view doesn't exist: create normally

**Examples:**
```sql
CREATE VIEW IF NOT EXISTS vw_active_users AS
SELECT * FROM users WHERE active = 1;
```

### 8. CREATE TRIGGER IF NOT EXISTS

**Syntax:**
```sql
CREATE TRIGGER [IF NOT EXISTS] trigger_name
BEFORE|AFTER INSERT|UPDATE|DELETE ON table_name
BEGIN
    -- statements
END
```

**Behavior:**
- If trigger exists: **silently skip**
- If trigger doesn't exist: create normally

**Examples:**
```sql
CREATE TRIGGER IF NOT EXISTS trg_users_audit
AFTER INSERT ON users
BEGIN
    INSERT INTO audit_log VALUES (NEW.id, 'INSERT', CURRENT_TIMESTAMP);
END;
```

### 9. ALTER TABLE IF EXISTS

**Syntax:**
```sql
ALTER TABLE [IF EXISTS] table_name ADD COLUMN column_name datatype
```

**Behavior:**
- If table exists: alter normally
- If table doesn't exist: **silently skip**

**Examples:**
```sql
ALTER TABLE IF EXISTS users ADD COLUMN last_login BIGINT;
ALTER TABLE IF EXISTS orders ADD COLUMN status TEXT DEFAULT 'pending';
```

---

## 🏗️ Implementation Strategy

### Phase 1.5.1: Index Operations (High Priority)

**Files to modify:**
- `src/SharpCoreDB/Services/SqlParser.DDL.cs`
  - `ExecuteCreateIndex()` - add IF NOT EXISTS detection
  - `ExecuteDropIndex()` - add IF EXISTS detection

**Pattern:**
```csharp
// CREATE INDEX IF NOT EXISTS pattern
private void ExecuteCreateIndex(string sql, string[] parts)
{
    bool ifNotExists = false;
    int nameIndex = 2;
    
    if (parts.Length >= 6 && parts[2].Equals("IF", OrdinalIgnoreCase)
        && parts[3].Equals("NOT", OrdinalIgnoreCase)
        && parts[4].Equals("EXISTS", OrdinalIgnoreCase))
    {
        ifNotExists = true;
        nameIndex = 5;
    }
    
    string indexName = parts[nameIndex];
    
    if (ifNotExists && this.indexes.ContainsKey(indexName))
    {
        return; // Skip creation
    }
    
    // ... existing creation logic
}

// DROP INDEX IF EXISTS pattern
private void ExecuteDropIndex(string sql, string[] parts)
{
    bool ifExists = false;
    int nameIndex = 2;
    
    if (parts.Length >= 5 && parts[2].Equals("IF", OrdinalIgnoreCase)
        && parts[3].Equals("EXISTS", OrdinalIgnoreCase))
    {
        ifExists = true;
        nameIndex = 4;
    }
    
    string indexName = parts[nameIndex];
    
    if (!this.indexes.ContainsKey(indexName))
    {
        if (ifExists)
            return; // Silently skip
        else
            throw new InvalidOperationException($"Index '{indexName}' does not exist");
    }
    
    // ... existing drop logic
}
```

### Phase 1.5.2: Procedure/View/Trigger Operations

**Files to modify:**
- `src/SharpCoreDB/Services/SqlParser.DDL.cs`
  - `ExecuteCreateProcedure()` - add IF NOT EXISTS
  - `ExecuteDropProcedure()` - add IF EXISTS
  - `ExecuteCreateView()` - add IF NOT EXISTS
  - `ExecuteDropView()` - add IF EXISTS
  - `ExecuteCreateTrigger()` - add IF NOT EXISTS
  - `ExecuteDropTrigger()` - add IF EXISTS

**Same pattern as indexes** with appropriate dictionary checks:
- Procedures: `this.procedures.ContainsKey(name)`
- Views: `this.views.ContainsKey(name)`
- Triggers: `this.triggers.ContainsKey(name)`

### Phase 1.5.3: ALTER TABLE IF EXISTS

**Files to modify:**
- `src/SharpCoreDB/Services/SqlParser.DDL.cs`
  - `ExecuteAlterTable()` - add IF EXISTS detection

**Pattern:**
```csharp
private void ExecuteAlterTable(string sql, string[] parts)
{
    bool ifExists = false;
    int nameIndex = 2;
    
    if (parts.Length >= 5 && parts[2].Equals("IF", OrdinalIgnoreCase)
        && parts[3].Equals("EXISTS", OrdinalIgnoreCase))
    {
        ifExists = true;
        nameIndex = 4;
    }
    
    string tableName = parts[nameIndex];
    
    if (!this.tables.ContainsKey(tableName))
    {
        if (ifExists)
            return; // Silently skip
        else
            throw new InvalidOperationException($"Table '{tableName}' does not exist");
    }
    
    // ... existing alter logic
}
```

---

## 🧪 Testing Strategy

### Test Coverage

**Files to create:**
- `tests/SharpCoreDB.Tests/Phase1_5_DDL_IfExistsTests.cs`

**Test cases:**

```csharp
[Fact]
public void CreateIndexIfNotExists_WhenIndexExists_ShouldSkip()
{
    // Arrange
    db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
    db.ExecuteSQL("CREATE INDEX idx_users_name ON users(name)");
    
    // Act - should not throw
    db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_users_name ON users(name)");
    
    // Assert - index still exists, no duplicate
    var indexes = db.GetIndexes("users");
    Assert.Single(indexes.Where(i => i.Name == "idx_users_name"));
}

[Fact]
public void CreateIndexIfNotExists_WhenIndexDoesNotExist_ShouldCreate()
{
    // Arrange
    db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
    
    // Act
    db.ExecuteSQL("CREATE INDEX IF NOT EXISTS idx_users_name ON users(name)");
    
    // Assert
    var indexes = db.GetIndexes("users");
    Assert.Contains(indexes, i => i.Name == "idx_users_name");
}

[Fact]
public void DropIndexIfExists_WhenIndexExists_ShouldDrop()
{
    // Arrange
    db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
    db.ExecuteSQL("CREATE INDEX idx_users_name ON users(name)");
    
    // Act
    db.ExecuteSQL("DROP INDEX IF EXISTS idx_users_name");
    
    // Assert
    var indexes = db.GetIndexes("users");
    Assert.DoesNotContain(indexes, i => i.Name == "idx_users_name");
}

[Fact]
public void DropIndexIfExists_WhenIndexDoesNotExist_ShouldSkip()
{
    // Act & Assert - should not throw
    db.ExecuteSQL("DROP INDEX IF EXISTS idx_nonexistent");
}

// Similar tests for PROCEDURE, VIEW, TRIGGER, ALTER TABLE
// Total: ~25 tests
```

---

## 📊 Acceptance Criteria

- [ ] All CREATE statements support IF NOT EXISTS
- [ ] All DROP statements support IF EXISTS
- [ ] ALTER TABLE supports IF EXISTS
- [ ] Consistent behavior across all DDL operations
- [ ] 25+ passing tests
- [ ] Zero breaking changes to existing code
- [ ] Documentation updated (README.md, USER_MANUAL.md)
- [ ] Build passes with 0 errors

---

## 🔄 Backwards Compatibility

**Zero breaking changes:**
- Existing DDL statements work identically
- IF EXISTS/IF NOT EXISTS is optional
- Default behavior unchanged

**Migration path:**
- No migration needed
- Optional adoption of new clauses

---

## 📈 Benefits

### For Users
1. **Idempotent Scripts**: Safe to run multiple times
2. **Simplified Deployment**: No "already exists" errors
3. **SQL Standard Compliance**: Matches PostgreSQL/MySQL/SQLite
4. **Safer Cleanup**: No errors on missing objects

### Example Use Case
```sql
-- Idempotent migration script
CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, name TEXT);
CREATE INDEX IF NOT EXISTS idx_users_name ON users(name);
CREATE PROCEDURE IF NOT EXISTS sp_cleanup() BEGIN DELETE FROM logs WHERE age > 30; END;

-- Safe cleanup
DROP VIEW IF EXISTS vw_legacy;
DROP TRIGGER IF EXISTS trg_old_validation;
DROP INDEX IF EXISTS idx_deprecated;
```

---

## 📅 Timeline

| Phase | Duration | Deliverables |
|-------|----------|--------------|
| **1.5.1**: Index Operations | 2 hours | CREATE/DROP INDEX IF [NOT] EXISTS |
| **1.5.2**: Procedure/View/Trigger | 2 hours | IF [NOT] EXISTS for all DDL |
| **1.5.3**: ALTER TABLE | 1 hour | ALTER TABLE IF EXISTS |
| **Testing** | 1 hour | 25+ comprehensive tests |
| **Documentation** | 1 hour | README, USER_MANUAL updates |
| **TOTAL** | **6-7 hours** | **Phase 1.5 Complete** |

---

## 🔗 Related Documents

- [Phase 1 Complete](../PHASE1_COMPLETE.md) - Core DDL implementation
- [SQL Standard Compliance](../SQL_STANDARDS.md) - SQL-92/99/2003 compliance
- [User Manual](../USER_MANUAL.md) - Complete API reference

---

**Next Phase:** Phase 9 (Locale-Specific Collations)
