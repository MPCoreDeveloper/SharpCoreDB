# Additional File Reorganization - SharpCoreDB

**Date**: December 23, 2025  
**Purpose**: Move misplaced files to correct locations

---

## 🔍 Files Needing Relocation

### 1. TestPageBasedSelect.csx
**Current Location**: `SharpCoreDB/TestPageBasedSelect.csx` (root of main project)  
**Issue**: Test script in production code  
**Solution**: Move to `SharpCoreDB.Tests/Scripts/TestPageBasedSelect.csx`  
**Reason**: Test scripts should be with test project, not in main codebase

### 2. Ulid.cs
**Current Location**: `SharpCoreDB/Ulid.cs` (root)  
**Issue**: Data structure in root directory  
**Solution**: Move to `SharpCoreDB/DataStructures/Ulid.cs`  
**Reason**: 
- Ulid is a data structure (record type)
- Belongs with other data structures like TableInfo, ColumnInfo, PreparedStatement
- Consistent with project organization

---

## 📋 Action Plan

### Step 1: Move Ulid.cs to DataStructures/
**From**: `Ulid.cs`  
**To**: `DataStructures/Ulid.cs`  
**Modernizations**:
- Already uses modern C# features (record type, Span<T>)
- Add relocation header comment
- Verify namespace unchanged (SharpCoreDB)

### Step 2: Move TestPageBasedSelect.csx to Tests
**From**: `TestPageBasedSelect.csx`  
**To**: `../SharpCoreDB.Tests/Scripts/TestPageBasedSelect.csx`  
**Note**: Create Scripts/ directory if needed

---

## ✅ Expected Directory Structure After

```
SharpCoreDB/
├── DataStructures/
│   ├── Table.cs
│   ├── TableInfo.cs
│   ├── ColumnInfo.cs
│   ├── PreparedStatement.cs
│   └── Ulid.cs ✅ (moved here)
│
└── (no test scripts in root) ✅

SharpCoreDB.Tests/
├── Scripts/
│   └── TestPageBasedSelect.csx ✅ (moved here)
└── UlidTests.cs (already exists)
```

---

## 🎯 Benefits

1. **Clear Separation**: Test code separated from production code
2. **Logical Grouping**: Data structures together
3. **Better Discovery**: Easier to find related code
4. **Professional Structure**: Follows .NET conventions

---

**Status**: Ready to execute file moves
