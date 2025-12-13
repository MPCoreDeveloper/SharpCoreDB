# ? Parameter Count Mismatch - OPLOSSING

**Datum:** 11 December 2024  
**Status:** ? **DIAGNOSE COMPLEET**  
**Type:** Parameter binding bug  

---

## ?? Probleem Beschrijving

Je ziet tijdens benchmarks deze warning:
```
?? Parameter count mismatch: 6 provided but 0 found
```

**Dit betekent:**
- De SQL query heeft **6 placeholders** (`?`)
- Maar de parameters dictionary heeft **0 (of verkeerde) keys**

---

## ?? Root Cause

### De Code (SqlParser.cs, regel 795-832):

```csharp
private static string BindParameters(string sql, Dictionary<string, object?> parameters)
{
    var result = sql;
    
    // Handle named parameters (@paramName or @param0, @param1, etc.)
    foreach (var param in parameters)
    {
        var paramName = param.Key;
        var valueStr = FormatValue(param.Value);
        
        // Try matching with @ prefix
        if (paramName.StartsWith('@'))
        {
            result = result.Replace(paramName, valueStr);
        }
        else
        {
            // Also try with @ prefix added
            result = result.Replace("@" + paramName, valueStr);
        }
    }
    
    // Handle positional parameters (?)
    var paramIndex = 0;
    var index = 0;
    while ((index = result.IndexOf('?', index)) != -1)
    {
        if (paramIndex >= parameters.Count)  // ? HIER GAAT HET FOUT!
        {
            throw new InvalidOperationException("Not enough parameters provided for SQL query.");
        }

        var paramKey = paramIndex.ToString();  // "0", "1", "2", etc.
        if (!parameters.TryGetValue(paramKey, out var value))
        {
            // Try finding any unused parameter
            var unusedParam = parameters.FirstOrDefault(p => !result.Contains(p.Key));
            if (unusedParam.Key != null)
            {
                value = unusedParam.Value;
            }
            else
            {
                throw new InvalidOperationException($"Parameter '{paramKey}' not found.");
            }
        }

        var valueStr = FormatValue(value);
        result = result.Remove(index, 1).Insert(index, valueStr);
        index += valueStr.Length;
        paramIndex++;
    }

    return result;
}
```

---

## ?? Waarom Gebeurt Dit?

### Scenario 1: Named Parameters Verwarring

```csharp
// BENCHMARK CODE:
var parameters = new Dictionary<string, object?> 
{
    ["@id"] = 1,
    ["@name"] = "Alice",
    ["@email"] = "alice@test.com",
    // ... 6 parameters met @ prefix
};

// SQL:
"INSERT INTO users (id, name, email, ...) VALUES (?, ?, ?, ?, ?, ?)"
//                                                 ?  ?  ?  ?  ?  ?
//                                                 6× ?

// WAT GEBEURT ER:
// 1. Named parameters loop ? vervangt @id, @name, etc. (MAAR SQL HEEFT GEEN @!)
// 2. Positional parameters loop ? zoekt naar "0", "1", "2", etc. (NIET GEVONDEN!)
// 3. paramIndex = 0, maar parameters.Count = 6
// 4. paramKey = "0" ? NOT FOUND!
// 5. ERROR: "6 provided but 0 found"
```

### Scenario 2: Keys Mismatch

```csharp
// BENCHMARK CODE (FOUT):
var parameters = new Dictionary<string, object?> 
{
    ["id"] = 1,        // ? Key is "id", niet "0"!
    ["name"] = "Alice", // ? Key is "name", niet "1"!
    // ...
};

// SQL:
"INSERT INTO users VALUES (?, ?, ?, ?, ?, ?)"

// RESULT: Keys "id", "name" != "0", "1", "2", ...
// ? NOT FOUND!
```

---

## ? OPLOSSING

### Fix #1: Gebruik Correct Parameter Formaat

**Als je positional parameters (`?`) gebruikt:**

```csharp
// ? GOED:
var parameters = new Dictionary<string, object?> 
{
    ["0"] = 1,           // ? Key is string "0"
    ["1"] = "Alice",
    ["2"] = "alice@test.com",
    ["3"] = 30,
    ["4"] = "Engineering",
    ["5"] = 100000m
};

database.ExecutePrepared(stmt, parameters);
```

**Als je named parameters (`@name`) gebruikt:**

```csharp
// ? GOED:
var sql = "INSERT INTO users (id, name, email, age, dept, salary) " +
          "VALUES (@id, @name, @email, @age, @dept, @salary)";

var parameters = new Dictionary<string, object?> 
{
    ["@id"] = 1,           // ? Include @ in key
    ["@name"] = "Alice",
    ["@email"] = "alice@test.com",
    ["@age"] = 30,
    ["@dept"] = "Engineering",
    ["@salary"] = 100000m
};

database.ExecuteSQL(sql, parameters);
```

---

### Fix #2: Update BenchmarkDatabaseHelper

**File:** `SharpCoreDB.Benchmarks\Infrastructure\BenchmarkDatabaseHelper.cs`

**Vind de methode waar parameters worden gemaakt en fix dit:**

```csharp
// VOOR (FOUT):
public static void InsertUsersBatch(this Database database, List<(int, string, string, int, string, decimal)> users)
{
    var stmt = database.Prepare("INSERT INTO users (id, name, email, age, department, salary) " +
                                 "VALUES (?, ?, ?, ?, ?, ?)");

    foreach (var (id, name, email, age, dept, salary) in users)
    {
        var parameters = new Dictionary<string, object?> 
        {
            ["id"] = id,        // ? FOUT! Moet "0" zijn
            ["name"] = name,    // ? FOUT! Moet "1" zijn
            ["email"] = email,  // ? FOUT! Moet "2" zijn
            ["age"] = age,
            ["department"] = dept,
            ["salary"] = salary
        };
        
        database.ExecutePrepared(stmt, parameters);
    }
}

// NA (GOED):
public static void InsertUsersBatch(this Database database, List<(int, string, string, int, string, decimal)> users)
{
    var stmt = database.Prepare("INSERT INTO users (id, name, email, age, department, salary) " +
                                 "VALUES (?, ?, ?, ?, ?, ?)");

    foreach (var (id, name, email, age, dept, salary) in users)
    {
        var parameters = new Dictionary<string, object?> 
        {
            ["0"] = id,      // ? GOED! Positional key
            ["1"] = name,
            ["2"] = email,
            ["3"] = age,
            ["4"] = dept,
            ["5"] = salary
        };
        
        database.ExecutePrepared(stmt, parameters);
    }
}
```

---

### Fix #3: Alternatief - Gebruik Named Parameters

**Wijzig SQL om named parameters te gebruiken:**

```csharp
// OPTIE 2: Gebruik @param syntax in SQL
public static void InsertUsersBatch(this Database database, List<(int, string, string, int, string, decimal)> users)
{
    var stmt = database.Prepare("INSERT INTO users (id, name, email, age, department, salary) " +
                                 "VALUES (@id, @name, @email, @age, @dept, @salary)");
    //                               ?     ?      ?       ?     ?       ?
    //                               Named parameters!

    foreach (var (id, name, email, age, dept, salary) in users)
    {
        var parameters = new Dictionary<string, object?> 
        {
            ["@id"] = id,           // ? Match SQL placeholders
            ["@name"] = name,
            ["@email"] = email,
            ["@age"] = age,
            ["@dept"] = dept,
            ["@salary"] = salary
        };
        
        database.ExecutePrepared(stmt, parameters);
    }
}
```

---

## ?? Snelle Fix Stappen

### Stap 1: Zoek Waar Parameters Worden Aangemaakt

```bash
cd SharpCoreDB.Benchmarks
grep -r "Dictionary<string, object?>" Infrastructure/
```

### Stap 2: Check De Keys

Kijk of ze **"0", "1", "2"** zijn (voor `?`) of **"@name"** (voor `@name`).

### Stap 3: Match SQL Placeholders

**SQL heeft `?`** ? Keys moeten **"0", "1", "2", ...** zijn  
**SQL heeft `@name`** ? Keys moeten **"@name", "@email", ...** zijn

### Stap 4: Build & Test

```bash
dotnet build -c Release
dotnet run -c Release --filter "*Insert*"
```

---

## ?? Voorbeeld Fixes

### Voorbeeld 1: ComparativeInsertBenchmarks.cs

```csharp
// Zoek naar deze code:
[Benchmark]
public void SharpCoreDB_BulkInsert()
{
    // ...
    var parameters = new Dictionary<string, object?>(); // ? CHECK DEZE!
    // ...
}
```

**Fix dit naar:**

```csharp
[Benchmark]
public void SharpCoreDB_BulkInsert()
{
    // ...
    for (int i = 0; i < recordCount; i++)
    {
        var user = _testData[i];
        var parameters = new Dictionary<string, object?> 
        {
            ["0"] = user.Id,        // ? Positional
            ["1"] = user.Name,
            ["2"] = user.Email,
            ["3"] = user.Age,
            ["4"] = user.Department,
            ["5"] = user.Salary
        };
        sharpCoreDb.ExecutePrepared(stmt, parameters);
    }
}
```

---

### Voorbeeld 2: BenchmarkDatabaseHelper.cs

```csharp
public static class BenchmarkDatabaseHelper
{
    public static void InsertUser(this Database db, int id, string name, string email, int age, string dept, decimal salary)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["0"] = id,      // ? Match positional placeholders
            ["1"] = name,
            ["2"] = email,
            ["3"] = age,
            ["4"] = dept,
            ["5"] = salary
        };
        
        db.ExecuteSQL("INSERT INTO users VALUES (?, ?, ?, ?, ?, ?)", parameters);
    }
}
```

---

## ?? Verificatie

### Test 1: Check Parameter Keys

```csharp
var parameters = new Dictionary<string, object?> 
{
    ["0"] = 1,
    ["1"] = "test"
};

// Verify:
Console.WriteLine($"Has key '0': {parameters.ContainsKey("0")}");  // Should be true
Console.WriteLine($"Has key '1': {parameters.ContainsKey("1")}");  // Should be true
```

### Test 2: Check SQL Placeholders

```csharp
var sql = "INSERT INTO users VALUES (?, ?, ?)";
int placeholders = sql.Count(c => c == '?');
Console.WriteLine($"SQL has {placeholders} placeholders");  // Should match parameter count
```

### Test 3: Run Minimal Test

```csharp
var db = new Database(/* ... */);
db.ExecuteSQL("CREATE TABLE test (id INTEGER, name TEXT)");

var parameters = new Dictionary<string, object?> 
{
    ["0"] = 1,
    ["1"] = "Alice"
};

db.ExecuteSQL("INSERT INTO test VALUES (?, ?)", parameters);  // Should work!
```

---

## ? Checklist

- [ ] Zoek alle `Dictionary<string, object?>` declaraties
- [ ] Check of SQL `?` of `@name` gebruikt
- [ ] Match parameter keys met SQL placeholders:
  - `?` ? keys: "0", "1", "2", ...
  - `@name` ? keys: "@name", "@email", ...
- [ ] Test met 1 record insert
- [ ] Test met batch insert
- [ ] Run benchmarks

---

## ?? Samenvatting

**Probleem:**  
Parameter keys ("id", "name") matchen niet met SQL placeholders (`?` verwacht "0", "1")

**Oplossing:**  
Gebruik **"0", "1", "2", ...** als keys voor positional parameters (`?`)  
OF gebruik **"@name", "@email", ...** voor named parameters (`@name`)

**Impact:**  
? Geen warnings meer  
? Parameters worden correct gebonden  
? Benchmarks werken!

---

**Status:** ? DIAGNOSE COMPLEET  
**Tijd om te fixen:** 5-10 minuten  
**Moeilijkheid:** Makkelijk (zoek & vervang)

**Ga fixen! ??**
