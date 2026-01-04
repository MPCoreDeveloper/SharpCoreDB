# Development Documentation

This directory contains internal development documentation for SharpCoreDB contributors.

---

## 📁 Files

### **[SCDB_COMPILATION_FIXES.md](./SCDB_COMPILATION_FIXES.md)** 🇬🇧
**English** - Compilation error solutions for SCDB implementation.

**Contents:**
- All 41 compilation errors encountered
- Detailed solutions for each error
- Code examples (before/after)
- Technical explanations
- Lessons learned

**Read this if you:**
- Encounter compilation errors
- Want to understand the fixes
- Are debugging SCDB code
- Contributing to SCDB

### **[SCDB_COMPILATION_FIXES_NL.md](./SCDB_COMPILATION_FIXES_NL.md)** 🇳🇱
**Dutch** - Compilation error solutions (Nederlandse vertaling).

Same content as English version, translated to Dutch.

---

## 🎯 Purpose

This directory documents the development process, challenges, and solutions for SharpCoreDB implementation.

**Not user documentation** - For contributors and maintainers only.

---

## 📊 Compilation Fixes Summary

### Errors Fixed: **41 Total**

| File | Errors | Status |
|------|--------|--------|
| SingleFileStorageProvider.cs | 19 | ✅ Fixed |
| FreeSpaceManager.cs | 8 | ✅ Fixed |
| DatabaseExtensions.cs | 3 | ✅ Fixed |
| DatabaseOptions.cs | 3 | ✅ Fixed |
| BlockRegistry.cs | 3 | ✅ Fixed |
| WalManager.cs | 3 | ✅ Fixed |
| DirectoryStorageProvider.cs | 1 | ✅ Fixed |
| ScdbStructures.cs | 1 | ✅ Fixed |

### Categories of Fixes

1. **Type Casting** (2 errors)
   - `ulong` → `long` for MemoryMappedFile APIs

2. **Fixed Buffer Access** (5 errors)
   - Use `Span<byte>` instead of `fixed` for already-fixed buffers

3. **XML Documentation** (3 errors)
   - Added missing parameter documentation

4. **Readonly Structs** (2 errors)
   - Added constructor for `FreeExtent`

5. **Code Quality** (26 errors)
   - Async I/O outside locks
   - Unused variable cleanup
   - Exception type corrections
   - Warning suppressions

---

## 🔧 Common Issues

### 1. Cannot await in lock statement

**Problem:**
```csharp
lock (_lock) {
    await fileStream.WriteAsync(buffer); // ❌ CS1996
}
```

**Solution:**
```csharp
// Prepare data in lock
lock (_lock) {
    buffer = PrepareData();
}

// I/O outside lock
await fileStream.WriteAsync(buffer); // ✅
```

### 2. Fixed buffer address

**Problem:**
```csharp
fixed (byte* ptr = header.Nonce) // ❌ CS0213
{
    // Nonce is already fixed
}
```

**Solution:**
```csharp
var nonceSpan = new Span<byte>(header.Nonce, 12); // ✅
nonce.CopyTo(nonceSpan);
```

### 3. Readonly struct initialization

**Problem:**
```csharp
new FreeExtent { StartPage = 1, Length = 10 } // ❌ CS0191
```

**Solution:**
```csharp
// Add constructor
public FreeExtent(ulong startPage, ulong length) { ... }

// Use it
new FreeExtent(1, 10) // ✅
```

---

## 📖 Development Guidelines

### Build Process

```bash
# Build solution
dotnet build

# Run tests
dotnet test

# Check errors
dotnet build --no-incremental
```

### Coding Standards

1. **Use modern C# 14** patterns
2. **Zero-allocation** hot paths
3. **Async/await** for I/O
4. **XML documentation** for public APIs
5. **Thread-safe** concurrent access

### Performance

- Use `ArrayPool<byte>` for buffers
- Use `stackalloc` for small buffers (<256 bytes)
- Use `Span<T>` and `Memory<T>` for zero-copy
- Avoid LINQ in hot paths

---

## 🧪 Testing

### Unit Tests
```bash
dotnet test --filter Category=Unit
```

### Integration Tests
```bash
dotnet test --filter Category=Integration
```

### Performance Tests
```bash
dotnet run --project SharpCoreDB.Benchmarks
```

---

## 🚀 Contributing

### Setup

1. **Clone repository**
   ```bash
   git clone https://github.com/MPCoreDeveloper/SharpCoreDB.git
   cd SharpCoreDB
   ```

2. **Install .NET 10**
   ```bash
   dotnet --version # Should be 10.x
   ```

3. **Build**
   ```bash
   dotnet build
   ```

4. **Run tests**
   ```bash
   dotnet test
   ```

### Workflow

1. Create feature branch
2. Make changes
3. Run tests
4. Check compilation
5. Submit PR

### Before Committing

- ✅ All tests pass
- ✅ No compilation errors
- ✅ No compiler warnings
- ✅ XML documentation complete
- ✅ Code follows style guide

---

## 📝 Additional Resources

### Internal Docs
- [SCDB Implementation](../scdb/IMPLEMENTATION_STATUS.md)
- [Phase 1 Details](../scdb/PHASE1_IMPLEMENTATION.md)

### External Docs
- [Contributing Guide](../CONTRIBUTING.md)
- [Main README](../../README.md)

### Reference
- [.NET 10 Docs](https://learn.microsoft.com/en-us/dotnet/)
- [C# 14 Features](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)

---

## 🐛 Reporting Issues

Found a compilation error?

1. **Check this directory** - Solution might already be documented
2. **Search GitHub Issues** - May already be reported
3. **Create new issue** - Include error message and context

---

**Last Updated:** 2026-01-XX  
**Audience:** Contributors & Maintainers  
**License:** MIT
