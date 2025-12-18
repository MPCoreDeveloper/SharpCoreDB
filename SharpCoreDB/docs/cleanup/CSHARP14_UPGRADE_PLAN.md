# 🔧 C# 14 Upgrade Plan - SharpCoreDB Main Project

## 🎯 Target Features

### **1. File-Scoped Namespaces** ✅ (Already Done in Most Files)
```csharp
// Old
namespace SharpCoreDB
{
    public class Database { }
}

// New (C# 14)
namespace SharpCoreDB;

public class Database { }
```
**Status**: Most files already use this! ✅

---

### **2. Primary Constructors** 🆕
```csharp
// Old
public class Service
{
    private readonly ILogger logger;
    public Service(ILogger logger) => this.logger = logger;
}

// New (C# 14)
public class Service(ILogger logger)
{
    // logger is automatically a field
}
```
**Impact**: Reduce boilerplate in 50+ classes

---

### **3. Collection Expressions** 🆕
```csharp
// Old
var list = new List<string> { "a", "b", "c" };
var array = new[] { 1, 2, 3 };
var empty = Array.Empty<int>();

// New (C# 14)
var list = ["a", "b", "c"];
var array = [1, 2, 3];
var empty = [];
```
**Impact**: Cleaner collection initialization in 100+ places

---

### **4. Pattern Matching Enhancements** ✅ (Partially Done)
```csharp
// Old
if (obj != null)
if (value is not null)

// New (C# 14 - both are modern)
if (obj is not null)  // ✅ Preferred
```
**Status**: Many files use this ✅

---

### **5. Switch Expressions** ✅ (Partially Done)
```csharp
// Old
switch (type)
{
    case "int": return typeof(int);
    case "string": return typeof(string);
    default: throw new Exception();
}

// New (C# 14)
return type switch
{
    "int" => typeof(int),
    "string" => typeof(string),
    _ => throw new Exception()
};
```
**Status**: Many files use this ✅

---

### **6. Target-Typed New** ✅ (Partially Done)
```csharp
// Old
Dictionary<string, int> dict = new Dictionary<string, int>();

// New (C# 14)
Dictionary<string, int> dict = new();
```
**Status**: Many files use this ✅

---

### **7. ArgumentNullException.ThrowIfNull** ✅ (Partially Done)
```csharp
// Old
if (arg == null) throw new ArgumentNullException(nameof(arg));

// New (C# 14)
ArgumentNullException.ThrowIfNull(arg);
```
**Status**: Some files use this ✅

---

### **8. Required Properties** 🆕
```csharp
// Old
public class Config
{
    public string Name { get; set; } = null!;
}

// New (C# 14)
public class Config
{
    public required string Name { get; init; }
}
```
**Impact**: Safer initialization in DTOs/configs

---

## 📊 Files to Upgrade

### **High Priority** (Core Classes - Most Impact)

1. **Storage/** - Page management, engines
   - `PageManager.cs` - ✅ Already modern
   - `Storage.cs` - Check for old patterns
   - Engines/*.cs - Most already modern

2. **Services/** - SQL parsing, crypto
   - `SqlParser.*.cs` - Check switch statements
   - `CryptoService.cs` - Check patterns
   - `UserService.cs` - Check null checks

3. **DataStructures/** - Table, Index
   - `Table.cs` - Check collections
   - `HashIndex.cs` - Check patterns

4. **Core/** - Database core
   - `Database.*.cs` - Check everywhere

### **Medium Priority** (Extensions)

5. **SharpCoreDB.Extensions/** - EF Core, LINQ
   - Check all extension methods
   - Upgrade LINQ providers

### **Low Priority** (Already Modern)

6. **Interfaces/** - Most are already clean
7. **Constants/** - No changes needed

---

## 🎯 Upgrade Strategy

### **Phase 1: Quick Wins** (No Breaking Changes)
1. ✅ Collection expressions `[]` everywhere
2. ✅ ArgumentNullException.ThrowIfNull
3. ✅ Pattern matching (`is not null`)
4. ✅ Target-typed new

### **Phase 2: Refactoring** (Requires Testing)
1. Primary constructors (DI classes)
2. Required properties (DTOs)
3. Switch expressions (remaining switch statements)

### **Phase 3: Advanced** (Optional)
1. Inline arrays (if beneficial)
2. Ref readonly parameters
3. Unsafe code improvements

---

## ✅ Already Modern (Good Job!)

These are already using C# 14 features:
- ✅ File-scoped namespaces (90%+ files)
- ✅ Target-typed new (many files)
- ✅ Pattern matching (many files)
- ✅ Switch expressions (many files)
- ✅ Null-conditional operators `?.`
- ✅ Expression-bodied members

---

## 📝 Execution Plan

Due to the large codebase (200+ files), I'll focus on:

1. **High-Impact Files** (20-30 files):
   - Core database classes
   - Storage engines
   - SQL parser
   - Main services

2. **Low-Risk Changes**:
   - Collection expressions
   - ArgumentNullException.ThrowIfNull
   - Target-typed new

3. **Skip**:
   - Already modern files
   - Files with minimal benefit
   - Risky refactorings (primary constructors in complex classes)

---

## 🎯 Expected Outcome

**Before**: Mix of C# 10-14 features  
**After**: Consistent C# 14 throughout high-impact files

**Benefits**:
- ✅ Cleaner, more readable code
- ✅ Less boilerplate
- ✅ Better null safety
- ✅ Modern C# patterns

**Risk**: ⚠️ LOW (focusing on non-breaking changes)

---

**Next Step**: Scan top 20-30 files and apply safe C# 14 upgrades
