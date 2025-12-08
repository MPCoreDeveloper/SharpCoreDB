# Benchmarks Not Running? Quick Fix Guide

## Problem
Je ziet deze output:
```
Statistics:
  Total Benchmarks: 3
  Total Reports: 0
```

Dit betekent dat de benchmarks **NIET** echt uitgevoerd zijn.

## Snelle Test

Run eerst deze test om te zien of BenchmarkDotNet werkt:

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release -- --test
```

Dit runt een simpele benchmark die altijd moet werken.

### Verwachte Output (goed):
```
? SimpleBenchmark Setup completed with N=10
... benchmark output ...
? BenchmarkDotNet is working correctly!
```

### Als test NIET werkt:
1. Check of je in Release mode bent: `-c Release`
2. Check of .NET 10 SDK geïnstalleerd is: `dotnet --version`
3. Check logs: `cat BenchmarkDotNet.Artifacts/logs/*.log`

## Als Test Werkt Maar Comparative Benchmarks Niet

### Mogelijke Oorzaken

#### 1. **Database Initialization Fails**

Test handmatig:
```csharp
// Voeg toe aan Program.cs Main, voor benchmarks:
Console.WriteLine("Testing database helper...");
try
{
    var testPath = Path.Combine(Path.GetTempPath(), "test_db");
    Directory.CreateDirectory(testPath);
    
    using (var helper = new BenchmarkDatabaseHelper(testPath))
    {
        Console.WriteLine("  ? Helper created");
        helper.CreateUsersTable();
        Console.WriteLine("  ? Table created");
        helper.InsertUser(1, "Test", "test@test.com", 30, DateTime.Now, true);
        Console.WriteLine("  ? Insert works");
    }
    
    Directory.Delete(testPath, true);
    Console.WriteLine("? Database helper works perfectly!");
}
catch (Exception ex)
{
    Console.WriteLine($"? Database helper failed: {ex.Message}");
    return;
}
```

#### 2. **Temp Directory Issues**

Windows kan temp directory permissions hebben. Test:
```csharp
var tempPath = Path.GetTempPath();
Console.WriteLine($"Temp path: {tempPath}");
Console.WriteLine($"Can write: {Directory.Exists(tempPath)}");

var testDir = Path.Combine(tempPath, $"test_{Guid.NewGuid()}");
Directory.CreateDirectory(testDir);
File.WriteAllText(Path.Combine(testDir, "test.txt"), "test");
Directory.Delete(testDir, true);
Console.WriteLine("? Temp directory access OK");
```

#### 3. **SQLite/LiteDB Issues**

Test of dependencies werken:
```csharp
// Test SQLite
using var sqliteConn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
sqliteConn.Open();
Console.WriteLine("? SQLite works");

// Test LiteDB  
using var liteDb = new LiteDB.LiteDatabase(":memory:");
Console.WriteLine("? LiteDB works");
```

## Workarounds

### Workaround 1: Run Each Benchmark Separately

```bash
# Test alleen Insert
dotnet run -c Release -- --filter Insert

# Test alleen Select
dotnet run -c Release -- --filter Select

# Test alleen Update/Delete
dotnet run -c Release -- --filter Update
```

### Workaround 2: Reduce Test Sizes

Edit benchmark files en verander `[Params]`:

```csharp
// In ComparativeInsertBenchmarks.cs
[Params(1, 10)]  // Instead of [Params(1, 10, 100, 1000)]
public int RecordCount { get; set; }
```

Dit maakt benchmarks VEEL sneller voor testing.

### Workaround 3: Skip SharpCoreDB Temporarily

Comment out SharpCoreDB benchmarks om te testen of SQLite/LiteDB werken:

```csharp
// In ComparativeInsertBenchmarks.cs
// [Benchmark(Description = "SharpCoreDB: Bulk Insert")]
// public void SharpCoreDB_BulkInsert()
// {
//     // ... commented out
// }
```

## Debug Mode

Voor meer informatie, run in Debug mode:

```bash
dotnet run -c Debug
```

Dit geeft meer output en betere error messages.

## Check Logs

BenchmarkDotNet schrijft alles naar logs:

```bash
# Windows
type BenchmarkDotNet.Artifacts\logs\*.log

# Linux/Mac
cat BenchmarkDotNet.Artifacts/logs/*.log
```

Zoek naar "ERROR" of "Exception" in de logs.

## ThreadingDiagnoser Warning

Deze warning is NORMAAL en niet het probleem:
```
* ThreadingDiagnoser supports only .NET Core 3.0+
```

Dit is een bug in BenchmarkDotNet - het werkt WEL op .NET 10, maar print deze warning.
Ik heb het al verwijderd in de nieuwe code.

## Hulp Nodig?

Als niets werkt:

1. Run `dotnet run -c Release -- --test` en stuur output
2. Check `BenchmarkDotNet.Artifacts/logs/*.log` en stuur errors
3. Run deze diagnostic:

```csharp
Console.WriteLine($"Current Dir: {Directory.GetCurrentDirectory()}");
Console.WriteLine($"Temp Path: {Path.GetTempPath()}");
Console.WriteLine($".NET Version: {Environment.Version}");
Console.WriteLine($"OS: {Environment.OSVersion}");
```

## Expected Timeline

Als benchmarks WEL werken:
- Insert benchmarks: 5-10 minuten
- Select benchmarks: 3-5 minuten  
- Update/Delete benchmarks: 5-8 minuten
- **Total: 15-25 minuten**

Als het in 1 seconde klaar is = niet echt uitgevoerd!
