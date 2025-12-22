# SELECT Benchmark - COUNT(*) Key Access Fix

## 🔧 Issue Fixed

**Error**:
```
The given key 'COUNT(*)' was not present in the dictionary.
at line 85: Console.WriteLine($"  Inserted records: {countResult[0]["COUNT(*)"]}");
```

## 🔍 Root Cause

The `COUNT(*)` aggregate function may return results with different column names depending on the SQL parser implementation:
- Could be `"COUNT(*)"` (literal)
- Could be `"count(*)"` (lowercase)
- Could be `"col1"` or `"column1"` (generic name)
- Could be empty key `""`

## ✅ Solution

Instead of accessing by exact key name, use the first value from the dictionary:

```csharp
// ❌ BEFORE: Assumes exact key name
var countResult = db1.ExecuteQuery("SELECT COUNT(*) FROM users");
Console.WriteLine($"  Inserted records: {countResult[0]["COUNT(*)"]}");

// ✅ AFTER: Access first value regardless of key name
var countResult = db1.ExecuteQuery("SELECT COUNT(*) FROM users");
if (countResult.Count > 0 && countResult[0].Count > 0)
{
    var firstValue = countResult[0].Values.FirstOrDefault();
    Console.WriteLine($"  Inserted records: {firstValue}");
}
else
{
    Console.WriteLine("  Warning: Could not verify record count");
}
```

## 📊 Why This Works

The `ExecuteQuery` returns:
```
List<Dictionary<string, object>>
```

For `SELECT COUNT(*) FROM users`:
- Returns 1 row: `List` with 1 element
- Each row has 1 column: `Dictionary` with 1 key-value pair
- The key name varies, but there's only 1 value
- `Values.FirstOrDefault()` gets that value regardless of key name

## 🎯 Benefit

- ✅ **Robust**: Works regardless of column name format
- ✅ **Safe**: Null-checks prevent exceptions
- ✅ **Fallback**: Shows warning if count unavailable
- ✅ **Generic**: Can be used for any single-column query

## 🚀 Status

**Build**: ✅ Successful  
**Fix Applied**: ✅ Line 85 updated  
**Ready to Run**: ✅ Yes

Now the benchmark will work regardless of how the SQL parser names aggregate columns!
