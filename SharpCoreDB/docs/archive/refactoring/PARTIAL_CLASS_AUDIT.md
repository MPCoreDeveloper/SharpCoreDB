# Partial Class Audit - SharpCoreDB

**Audit Date**: 2025-12-23  
**Purpose**: Document all partial classes for C# 14 modernization and reorganization

## Current Structure

### Database Partial Classes (7 files)
Located in root SharpCoreDB/ directory:

1. **Database.Batch.cs** → `Database/Execution/`
   - ExecuteBatchSQL(), ExecuteBatchSQLAsync()
   - BulkInsertAsync(), BulkInsertOptimizedInternalAsync()
   - INSERT statement batching with SIMD optimization
   - **Dependencies**: storage, tables, config, queryCache

2. **Database.BatchUpdateDeferredIndexes.cs** → `Database/Transactions/` 
   - (Content needs verification - may be merged with BatchUpdateTransaction)
   
3. **Database.BatchUpdateTransaction.cs** → `Database/Transactions/`
   - BeginBatchUpdate(), EndBatchUpdate(), CancelBatchUpdate()
   - IsBatchUpdateActive property
   - Deferred index update logic
   - **Dependencies**: storage, tables, _batchUpdateActive flag

4. **Database.BatchWalOptimization.cs** → `Database/Optimization/`
   - WAL batching optimizations
   - Group commit logic
   - **Dependencies**: groupCommitWal, storage

5. **Database.Core.cs** → `Database/Core/`
   - Constructor, initialization, Load(), SaveMetadata()
   - Fields: storage, tables, config, queryCache, pageCache
   - IDisposable implementation
   - **Dependencies**: ALL (this is the foundation)

6. **Database.Execution.cs** → `Database/Execution/`
   - ExecuteSQL(), ExecuteSQLAsync()
   - ExecuteQuery(), ExecuteCompiled()
   - Prepared statement execution
   - **Dependencies**: SqlParser, tables, storage, queryCache

7. **Database.Metadata.cs** → `Database/Core/`
   - Metadata management logic
   - Schema versioning
   - **Dependencies**: storage, tables

8. **Database.PreparedStatements.cs** → `Database/Execution/`
   - Prepare(), ExecutePrepared(), ExecutePreparedAsync()
   - CachedQueryPlan management
   - **Dependencies**: _preparedPlans, tables, storage

9. **Database.Statistics.cs** → `Database/Core/`
   - GetQueryCacheStatistics(), GetPageCacheStatistics()
   - GetDatabaseStatistics()
   - **Dependencies**: queryCache, pageCache, tables

### Storage Partial Classes (5 files)
Located in Services/ directory:

1. **Storage.Advanced.cs** → `Services/Storage/Advanced/`
   - SIMD operations (ScanForPattern, ValidatePageIntegrity)
   - ComparePagesSimd(), SecureZeroPage()
   - **Dependencies**: SimdHelper, bufferPool

2. **Storage.Append.cs** → `Services/Storage/Operations/`
   - AppendBytes(), AppendBytesMultiple()
   - Append-only write operations
   - **Dependencies**: crypto, key, bufferPool

3. **Storage.Core.cs** → `Services/Storage/Core/`
   - Constructor, transaction management
   - BeginTransaction(), CommitAsync(), Rollback()
   - Core fields and initialization
   - **Dependencies**: crypto, config, pageCache

4. **Storage.PageCache.cs** → `Services/Storage/Cache/`
   - ReadBytesAt() with page caching
   - LoadPageFromDisk(), ReadBytesAtDirect()
   - **Dependencies**: pageCache, ComputePageId()

5. **Storage.ReadWrite.cs** → `Services/Storage/Operations/`
   - Write(), Read(), WriteBytes(), ReadBytes()
   - Basic I/O operations with encryption
   - **Dependencies**: crypto, key, noEncryption

### SqlParser Partial Classes (4 files)
Located in Services/ directory:

1. **SqlParser.Core.cs** → `Services/Parsing/Core/`
   - Constructor, Execute(), ExecuteQuery()
   - Core parsing logic
   - **Dependencies**: tables, storage, queryCache

2. **SqlParser.DDL.cs** → `Services/Parsing/DDL/`
   - ExecuteCreateTable(), ExecuteDropTable()
   - ExecuteCreateIndex(), ExecuteDropIndex()
   - ExecuteAlterTable()
   - **Dependencies**: tables, storage

3. **SqlParser.DML.cs** → `Services/Parsing/DML/`
   - ExecuteInsert(), ExecuteUpdate(), ExecuteDelete()
   - ExecuteSelect(), ExecuteAggregateQuery()
   - **Dependencies**: tables, storage

4. **SqlParser.Helpers.cs** → `Services/Parsing/Core/`
   - ParseValue(), EvaluateWhere(), ParseWhereColumns()
   - Helper methods for parsing
   - **Dependencies**: (utility methods)

### EnhancedSqlParser Partial Classes (5 files)
Located in Services/ directory:

1. **EnhancedSqlParser.cs** → `Services/Parsing/Enhanced/`
   - Main AST parser
   
2. **EnhancedSqlParser.DDL.cs** → `Services/Parsing/Enhanced/`
   - CREATE/DROP TABLE/INDEX parsing

3. **EnhancedSqlParser.DML.cs** → `Services/Parsing/Enhanced/`
   - INSERT/UPDATE/DELETE parsing

4. **EnhancedSqlParser.Expressions.cs** → `Services/Parsing/Enhanced/`
   - WHERE clause, expression parsing

5. **EnhancedSqlParser.Select.cs** → `Services/Parsing/Enhanced/`
   - SELECT statement parsing with JOINs

## Recommended Directory Structure

```
SharpCoreDB/
├── Database/
│   ├── Core/
│   │   ├── Database.Core.cs
│   │   ├── Database.Metadata.cs
│   │   └── Database.Statistics.cs
│   ├── Execution/
│   │   ├── Database.Execution.cs
│   │   ├── Database.Batch.cs
│   │   └── Database.PreparedStatements.cs
│   ├── Transactions/
│   │   ├── Database.BatchUpdateTransaction.cs
│   │   └── Database.BatchUpdateDeferredIndexes.cs (if needed)
│   └── Optimization/
│       └── Database.BatchWalOptimization.cs
│
├── Services/
│   ├── Storage/
│   │   ├── Core/
│   │   │   └── Storage.Core.cs
│   │   ├── Operations/
│   │   │   ├── Storage.ReadWrite.cs
│   │   │   └── Storage.Append.cs
│   │   ├── Cache/
│   │   │   └── Storage.PageCache.cs
│   │   └── Advanced/
│   │       └── Storage.Advanced.cs
│   │
│   └── Parsing/
│       ├── Core/
│       │   ├── SqlParser.Core.cs
│       │   └── SqlParser.Helpers.cs
│       ├── DDL/
│       │   └── SqlParser.DDL.cs
│       ├── DML/
│       │   └── SqlParser.DML.cs
│       └── Enhanced/
│           ├── EnhancedSqlParser.cs
│           ├── EnhancedSqlParser.DDL.cs
│           ├── EnhancedSqlParser.DML.cs
│           ├── EnhancedSqlParser.Expressions.cs
│           └── EnhancedSqlParser.Select.cs
```

## C# 14 Modernization Opportunities

### Priority 1: High-Impact
- [ ] Apply collection expressions (`[]` instead of `new List<>()`)
- [ ] Use primary constructors where applicable
- [ ] Apply file-scoped namespaces
- [ ] Convert to `is null` / `is not null` patterns
- [ ] Apply `ArgumentNullException.ThrowIfNull()`

### Priority 2: Performance
- [ ] Convert sync File I/O to async where applicable
- [ ] Apply `ValueTask<T>` for hot paths
- [ ] Use `Span<T>` and `Memory<T>` for zero-copy
- [ ] Apply `MethodImpl(AggressiveOptimization)`

### Priority 3: Maintainability
- [ ] Apply switch expressions
- [ ] Use target-typed new
- [ ] Apply init-only setters to config classes
- [ ] Modernize tuple usage patterns
- [ ] Add global usings

## Backward Compatibility Requirements

✅ **MUST MAINTAIN**:
- All public API signatures unchanged
- All 141+ existing tests must pass
- No breaking changes to interfaces
- Sync methods must coexist with async
- Configuration classes must support old constructors

✅ **CAN CHANGE**:
- Internal implementation details
- Private method signatures
- Partial class file locations
- Using directive organization
- Internal async patterns

## Dependencies Analysis

### Critical Dependencies (DO NOT BREAK)
1. **IDatabase** interface - all public methods must remain
2. **IStorage** interface - core storage contract
3. **ITable** interface - table operations
4. **DatabaseConfig** - configuration must remain compatible
5. **SecurityConfig** - security settings must remain

### Internal Dependencies (CAN REFACTOR)
1. SqlParser → tables, storage (internal)
2. Storage → crypto, key (internal)
3. Database partials → internal state (private/protected)

## Migration Strategy

### Phase 1: Preparation (Steps 1-2)
- ✅ Complete this audit
- Create directory structure
- Update .csproj with new paths

### Phase 2: Core Reorganization (Steps 3-8)
- Move Database.*.cs files to subdirectories
- Move Storage.*.cs files to subdirectories
- Move SqlParser.*.cs files to subdirectories
- Update XML headers with relocation notes

### Phase 3: Modernization (Steps 9-19)
- Apply C# 14 features incrementally
- Update one feature category at a time
- Run tests after each category
- Document each modernization

### Phase 4: Validation (Steps 20-24)
- Run full test suite (100% pass rate required)
- Performance benchmarks (no regression)
- Update documentation
- Create migration guide

## Success Criteria

- [ ] All files organized in logical subdirectories
- [ ] C# 14 language version set in .csproj
- [ ] All 141+ tests passing
- [ ] No performance regression
- [ ] Zero breaking changes to public API
- [ ] XML documentation complete
- [ ] Migration guide published
- [ ] README.md updated

## Notes

- **DO NOT** break backward compatibility
- **DO NOT** change public API signatures
- **DO** modernize internal implementations
- **DO** improve code organization
- **DO** add comprehensive documentation
- **DO** maintain sync + async method pairs

---

**Status**: ✅ Audit Complete - Ready for Phase 2 (Directory Structure)
