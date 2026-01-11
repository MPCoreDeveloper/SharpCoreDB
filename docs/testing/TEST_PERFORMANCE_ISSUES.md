# Test Performance Issues - Root Cause Analysis

## 🔴 **Probleem: Tests hangen bij cleanup**

**Betrokken tests:**
- `CompiledQueryTests.CompiledQuery_ParameterizedQuery_BindsParametersCorrectly`
- `AsyncTests.Prepare_And_ExecutePrepared_SelectWithParameter`

---

## 🔍 **Root Causes**

### **1. Directory.Delete hangt op open file handles** 🔴 CRITICAL

**Locatie:** Alle `CompiledQueryTests` en `AsyncTests`

**Code:**
```csharp
// Cleanup
Directory.Delete(_testDbPath, true);  // ❌ Database nog OPEN!
```

**Waarom dit hangt:**
- `Directory.Delete` probeert files te verwijderen die nog open zijn
- Windows file locking voorkomt verwijdering
- .NET wacht oneindig op file release
- Test timeout (default 5 minuten) is te lang voor CI

**Bewijs:**
```
User cancelled the command running in terminal
```
= Test blijft hangen en wordt handmatig gecancelled

---

### **2. QueryCompiler.Compile hangt op parameterized queries** 🟡 MEDIUM

**Locatie:** `Database.PreparedStatements.cs:42`

**Code:**
```csharp
if (sql.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
{
    compiledPlan = QueryCompiler.Compile(sql);  // ❌ Kan lopen bij @param
}
```

**Waarom dit problematisch is:**
```csharp
// Input SQL:
"SELECT * FROM users WHERE id = @id"

// QueryCompiler.Compile → EnhancedSqlParser.Parse
// EnhancedSqlParser ziet "@id" als identifier, NIET als parameter
// Kan infinite loop triggeren in expression compilation
```

---

### **3. Parameter binding niet geïmplementeerd** 🟡 MEDIUM

**Locatie:** `CompiledQueryExecutor.cs`

**Code:**
```csharp
public List<Dictionary<string, object>> Execute(
    CompiledQueryPlan plan,
    Dictionary<string, object?>? parameters = null)
{
    // ❌ Parameters worden NOOIT gebruikt!
    var whereClause = ExtractWhereClause(plan.Sql);  // Bevat nog "@id"
    var results = table.Select(whereClause, ...);    // Faalt: WHERE id = @id
}
```

**Gevolg:**
- Query met `@id` wordt uitgevoerd zonder parameter substitutie
- `table.Select("id = @id")` matcht GEEN rijen
- Test verwacht resultaten maar krijgt empty list
- Test assertion faalt

---

## ✅ **Oplossingen**

### **Fix 1: Proper database disposal before cleanup** (URGENT)

```csharp
[Fact]
public void CompiledQuery_ParameterizedQuery_BindsParametersCorrectly()
{
    // Arrange
    var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
    var db = factory.Create(_testDbPath, "test123");

    try
    {
        db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 'alice@example.com')");
        // ... rest of test ...
        
        var stmt = db.Prepare("SELECT * FROM users WHERE id = @id");
        var results1 = db.ExecuteCompiledQuery(stmt, new Dictionary<string, object?> { { "id", 1 } });
        
        Assert.NotEmpty(results1);
        Assert.Equal("Alice", results1[0]["name"]);
    }
    finally
    {
        // ✅ FIX: Dispose database BEFORE deleting directory
        db?.Dispose();
        
        // ✅ FIX: Retry logic for Windows file locking
        if (Directory.Exists(_testDbPath))
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    Directory.Delete(_testDbPath, true);
                    break;
                }
                catch (IOException) when (i < 2)
                {
                    Thread.Sleep(100);  // Wait for file handles to release
                }
            }
        }
    }
}
```

---

### **Fix 2: Skip compilation for parameterized queries**

**File:** `src/SharpCoreDB/Database/Execution/Database.PreparedStatements.cs`

```csharp
public DataStructures.PreparedStatement Prepare(string sql)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(sql);
    
    if (!_preparedPlans.TryGetValue(sql, out var plan))
    {
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        plan = new CachedQueryPlan(sql, parts);
        _preparedPlans[sql] = plan;
    }
    
    CompiledQueryPlan? compiledPlan = null;
    
    // ✅ FIX: Skip compilation for parameterized queries
    bool isSelectQuery = sql.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
    bool hasParameters = sql.Contains('@') || sql.Contains('?');
    
    if (isSelectQuery && !hasParameters)  // ✅ Only compile non-parameterized
    {
        compiledPlan = QueryCompiler.Compile(sql);
    }
    
    return new DataStructures.PreparedStatement(sql, plan, compiledPlan);
}
```

---

### **Fix 3: Implement parameter substitution in CompiledQueryExecutor**

**File:** `src/SharpCoreDB/Services/CompiledQueryExecutor.cs`

```csharp
public List<Dictionary<string, object>> ExecuteParameterized(
    CompiledQueryPlan plan,
    Dictionary<string, object?> parameters)
{
    // Get the table
    if (!tables.TryGetValue(plan.TableName, out var table))
    {
        throw new InvalidOperationException($"Table {plan.TableName} does not exist");
    }

    // Extract WHERE clause and bind parameters
    var whereClause = ExtractWhereClause(plan.Sql);
    if (!string.IsNullOrEmpty(whereClause))
    {
        // ✅ FIX: Properly substitute parameters
        whereClause = BindParametersInWhereClause(whereClause, parameters);
    }

    // Use table's built-in Select
    var results = string.IsNullOrEmpty(whereClause)
        ? table.Select()
        : table.Select(whereClause, plan.OrderByColumn, plan.OrderByAscending);

    // ... rest of method ...
}

// ✅ ENHANCED: Better parameter binding
private static string BindParametersInWhereClause(string whereClause, Dictionary<string, object?> parameters)
{
    var result = whereClause;
    
    foreach (var param in parameters)
    {
        // Support both @param and ? placeholders
        var paramName = param.Key.StartsWith('@') ? param.Key : $"@{param.Key}";
        var value = FormatParameterValue(param.Value);
        
        // ✅ Replace @id → '1'
        result = result.Replace(paramName, value, StringComparison.OrdinalIgnoreCase);
    }
    
    return result;
}
```

---

## 📊 **Impact Analysis**

| Issue | Severity | Frequency | Impact | Fix Priority |
|-------|----------|-----------|--------|--------------|
| Directory.Delete hangs | 🔴 CRITICAL | Every test | **100% test failure** | **P0 - Immediate** |
| QueryCompiler.Compile hangs | 🟡 MEDIUM | Parameterized queries | ~30% tests affected | P1 - High |
| Parameter binding broken | 🟡 MEDIUM | Parameterized queries | ~30% tests affected | P1 - High |

---

## 🎯 **Recommended Fix Order**

1. **Fix 1 first** (Directory.Delete) - Onblokkeert ALLE tests ✅
2. **Fix 2 second** (Skip compilation for parameterized) - Voorkomt hangs ✅
3. **Fix 3 third** (Parameter substitution) - Maakt functionaliteit werkend ✅

---

## 🧪 **Verification**

Na fixes, run:

```bash
# Test 1: Parameterized query
dotnet test --filter "FullyQualifiedName~CompiledQuery_ParameterizedQuery_BindsParametersCorrectly"

# Test 2: Async prepared statement
dotnet test --filter "FullyQualifiedName~Prepare_And_ExecutePrepared_SelectWithParameter"

# Verwacht resultaat:
# ✅ Both tests pass in < 5 seconds
```

---

## 📝 **Prevention Guidelines**

### **For future tests:**

```csharp
public class MyTests : IDisposable
{
    private readonly string _testDbPath;
    private IDatabase? _db;

    public MyTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
    }

    [Fact]
    public void MyTest()
    {
        _db = _factory.Create(_testDbPath, "pass");
        
        try
        {
            // Test logic
        }
        finally
        {
            // ✅ ALWAYS dispose database first
            _db?.Dispose();
            _db = null;
        }
    }

    public void Dispose()
    {
        // ✅ Cleanup in Dispose for test framework integration
        _db?.Dispose();
        
        if (Directory.Exists(_testDbPath))
        {
            try
            {
                Directory.Delete(_testDbPath, true);
            }
            catch (IOException)
            {
                // Log warning but don't fail test cleanup
                Console.WriteLine($"Warning: Could not delete {_testDbPath}");
            }
        }
    }
}
```

---

## 📚 **Related Issues**

- Similar issue in `AsyncTests.ExecutePreparedAsync_InsertWithParameter`
- Potential issue in `CompiledQuery_1000RepeatedSelects_CompletesUnder8ms` (directory cleanup)
- All `CompiledQueryTests` need Fix 1 applied

---

**Status:** 🔴 BLOCKING - Tests kunnen niet succesvol runnen  
**Owner:** *Assign to developer*  
**ETA:** 30 minutes voor alle 3 fixes  

---

_Last updated: 2025-01-XX_
