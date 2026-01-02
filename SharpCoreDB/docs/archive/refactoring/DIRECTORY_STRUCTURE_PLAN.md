# Directory Structure Creation Plan - SharpCoreDB

**Date**: December 2025  
**Purpose**: Reorganize partial classes into logical subdirectories

## Proposed Directory Structure

```
SharpCoreDB/
│
├── GlobalUsings.cs                          ✅ CREATED
│
├── Database/                                📁 TO CREATE
│   ├── Core/
│   │   ├── Database.Core.cs                (move from root)
│   │   ├── Database.Metadata.cs            (move from root)
│   │   └── Database.Statistics.cs          (move from root)
│   │
│   ├── Execution/
│   │   ├── Database.Execution.cs           (move from root)
│   │   ├── Database.Batch.cs               (move from root)
│   │   └── Database.PreparedStatements.cs  (move from root)
│   │
│   ├── Transactions/
│   │   ├── Database.BatchUpdateTransaction.cs        (move from root)
│   │   └── Database.BatchUpdateDeferredIndexes.cs    (move from root)
│   │
│   └── Optimization/
│       └── Database.BatchWalOptimization.cs (move from root)
│
├── Services/
│   ├── Storage/                             📁 TO CREATE
│   │   ├── Core/
│   │   │   └── Storage.Core.cs             (move from Services/)
│   │   │
│   │   ├── Operations/
│   │   │   ├── Storage.ReadWrite.cs        (move from Services/)
│   │   │   └── Storage.Append.cs           (move from Services/)
│   │   │
│   │   ├── Cache/
│   │   │   └── Storage.PageCache.cs        (move from Services/)
│   │   │
│   │   └── Advanced/
│   │       └── Storage.Advanced.cs         (move from Services/)
│   │
│   └── Parsing/                             📁 TO CREATE
│       ├── Core/
│       │   ├── SqlParser.Core.cs           (move from Services/)
│       │   └── SqlParser.Helpers.cs        (move from Services/)
│       │
│       ├── DDL/
│       │   └── SqlParser.DDL.cs            (move from Services/)
│       │
│       ├── DML/
│       │   └── SqlParser.DML.cs            (move from Services/)
│       │
│       └── Enhanced/
│           ├── EnhancedSqlParser.cs        (move from Services/)
│           ├── EnhancedSqlParser.DDL.cs    (move from Services/)
│           ├── EnhancedSqlParser.DML.cs    (move from Services/)
│           ├── EnhancedSqlParser.Expressions.cs  (move from Services/)
│           └── EnhancedSqlParser.Select.cs (move from Services/)
│
├── Interfaces/                              (existing - no changes)
├── DataStructures/                          (existing - no changes)
├── Constants/                               (existing - no changes)
├── Core/                                    (existing - no changes)
├── Optimizations/                           (existing - no changes)
└── docs/                                    (existing - documentation)
```

## Implementation Steps

### Phase 1: Create Directory Structure ✅
- [x] Create `GlobalUsings.cs`
- [ ] Create `Database/Core/`
- [ ] Create `Database/Execution/`
- [ ] Create `Database/Transactions/`
- [ ] Create `Database/Optimization/`
- [ ] Create `Services/Storage/Core/`
- [ ] Create `Services/Storage/Operations/`
- [ ] Create `Services/Storage/Cache/`
- [ ] Create `Services/Storage/Advanced/`
- [ ] Create `Services/Parsing/Core/`
- [ ] Create `Services/Parsing/DDL/`
- [ ] Create `Services/Parsing/DML/`
- [ ] Create `Services/Parsing/Enhanced/`

### Phase 2: Move Files (Steps 3-8 of main plan)
Each file move will:
1. Copy file to new location
2. Update XML header with relocation note
3. Remove old file
4. Verify namespaces unchanged
5. Test compilation

### Phase 3: Update Project References
The `.csproj` already uses wildcard patterns:
```xml
<Compile Include="**/*.cs" Exclude="..." />
```
This means subdirectories are automatically included! ✅

### Phase 4: Verification
- [ ] Run `dotnet build` - must succeed
- [ ] Run `dotnet test` - all 141+ tests must pass
- [ ] Verify no namespace changes
- [ ] Verify intellisense works

## File Move Commands (for automation)

### Database Partials:
```bash
# Core
mkdir -p Database/Core
git mv Database.Core.cs Database/Core/
git mv Database.Metadata.cs Database/Core/
git mv Database.Statistics.cs Database/Core/

# Execution
mkdir -p Database/Execution
git mv Database.Execution.cs Database/Execution/
git mv Database.Batch.cs Database/Execution/
git mv Database.PreparedStatements.cs Database/Execution/

# Transactions
mkdir -p Database/Transactions
git mv Database.BatchUpdateTransaction.cs Database/Transactions/
git mv Database.BatchUpdateDeferredIndexes.cs Database/Transactions/

# Optimization
mkdir -p Database/Optimization
git mv Database.BatchWalOptimization.cs Database/Optimization/
```

### Storage Partials:
```bash
# Storage subdirectories
mkdir -p Services/Storage/Core
mkdir -p Services/Storage/Operations
mkdir -p Services/Storage/Cache
mkdir -p Services/Storage/Advanced

# Move files
git mv Services/Storage.Core.cs Services/Storage/Core/
git mv Services/Storage.ReadWrite.cs Services/Storage/Operations/
git mv Services/Storage.Append.cs Services/Storage/Operations/
git mv Services/Storage.PageCache.cs Services/Storage/Cache/
git mv Services/Storage.Advanced.cs Services/Storage/Advanced/
```

### SqlParser Partials:
```bash
# Parsing subdirectories
mkdir -p Services/Parsing/Core
mkdir -p Services/Parsing/DDL
mkdir -p Services/Parsing/DML
mkdir -p Services/Parsing/Enhanced

# Move files
git mv Services/SqlParser.Core.cs Services/Parsing/Core/
git mv Services/SqlParser.Helpers.cs Services/Parsing/Core/
git mv Services/SqlParser.DDL.cs Services/Parsing/DDL/
git mv Services/SqlParser.DML.cs Services/Parsing/DML/

# Enhanced parser
git mv Services/EnhancedSqlParser.cs Services/Parsing/Enhanced/
git mv Services/EnhancedSqlParser.DDL.cs Services/Parsing/Enhanced/
git mv Services/EnhancedSqlParser.DML.cs Services/Parsing/Enhanced/
git mv Services/EnhancedSqlParser.Expressions.cs Services/Parsing/Enhanced/
git mv Services/EnhancedSqlParser.Select.cs Services/Parsing/Enhanced/
```

## Benefits of New Structure

### 1. **Logical Organization**
- Related functionality grouped together
- Clear separation of concerns
- Easier to find code

### 2. **Better Maintainability**
- Smaller directories to navigate
- Clear file purposes
- Easier onboarding for new contributors

### 3. **Build System Advantages**
- Faster incremental builds (fewer files per directory)
- Better parallel compilation
- Cleaner project structure

### 4. **IDE Experience**
- Better file tree navigation
- Faster IntelliSense
- Clearer project structure in Solution Explorer

### 5. **Future Extensibility**
- Easy to add new partial classes
- Clear conventions for new features
- Scalable structure

## Backward Compatibility

✅ **GUARANTEED**:
- All namespaces remain unchanged
- No public API changes
- File relocation only (no code changes)
- All tests will pass after reorganization

## Testing Strategy

After each file move:
1. `dotnet build` - verify compilation
2. `dotnet test --filter FullyQualifiedName~Database` - test Database classes
3. `dotnet test --filter FullyQualifiedName~Storage` - test Storage classes
4. `dotnet test` - run full suite

After all moves complete:
1. Full test suite (100% pass required)
2. Performance benchmarks (no regression)
3. Visual Studio solution reload test
4. Git history verification (files tracked correctly)

## Rollback Plan

If issues arise:
```bash
# Rollback using Git
git reset --hard HEAD

# Or revert specific commits
git revert <commit-hash>
```

All file moves will be committed incrementally so rollback is easy.

---

**Status**: ✅ Ready for Phase 1 Execution (Directory Creation)
**Next Step**: Create all directories, then begin file moves in Phase 2
