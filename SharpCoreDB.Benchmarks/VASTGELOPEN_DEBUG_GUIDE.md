# ?? BENCHMARK VASTGELOPEN - DEBUG GUIDE

## ? PROBLEEM: Benchmark Vastgelopen op 5/10

**Symptomen**:
- Benchmark liep uren zonder resultaat
- Geen crash, maar ook geen progress
- Waarschijnlijk op benchmark 5/10 blijven hangen

**Meest waarschijnlijke oorzaak**: Een van de benchmarks hangt in een **infinite loop** of **deadlock**.

---

## ?? DIAGNOSE TOOLS

### Tool #1: Debug Console App (NIEUW - AANBEVOLEN!)

Ik heb een debug tool gemaakt die de EXACTE error laat zien:

**Hoe te gebruiken**:
```bash
cd D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.DebugBenchmark
dotnet run -c Release
```

**Wat het doet**:
- ? Runt EXACT dezelfde code als de benchmark
- ? Maar ZONDER BenchmarkDotNet overhead
- ? Toont EXACTE exception + stack trace
- ? Test elk benchmark method individueel
- ? Stopt bij de EERSTE error

**Expected output**:
```
Step 1: Running GlobalSetup...
? GlobalSetup SUCCESS

Step 2: Running IterationSetup...
? IterationSetup SUCCESS

Step 3: Testing SharpCoreDB (No Encryption)...
? SharpCoreDB_NoEncrypt_10K SUCCESS: 10000 records in 7695ms

Step 4: Testing SharpCoreDB (Encrypted)...
? ERROR: InvalidOperationException
Message: Database connection failed
Stack Trace: ...
```

---

### Tool #2: Check voor Hanging Processes

```powershell
# Check if benchmark process is still running
Get-Process | Where-Object {$_.ProcessName -like "*dotnet*" -or $_.ProcessName -like "*benchmark*"} | Format-Table ProcessName, Id, CPU, WorkingSet64

# If hung, kill it:
Stop-Process -Name "dotnet" -Force
```

---

### Tool #3: Check Temp Directory for Lock Files

```powershell
# Check for locked database files
$tempPath = [System.IO.Path]::GetTempPath()
Get-ChildItem $tempPath -Filter "dbBenchmark*" -Recurse -ErrorAction SilentlyContinue | Select-Object FullName, Length, LastWriteTime

# Clean up if needed:
Remove-Item "$tempPath\dbBenchmark*" -Recurse -Force -ErrorAction SilentlyContinue
```

---

## ?? WAARSCHIJNLIJKE OORZAKEN

### Oorzaak #1: SharpCoreDB Encrypted Hangs (MEEST WAARSCHIJNLIJK!)

**Probleem**: Encryption operations kunnen timeout hebben

**Bewijs**: Je zegt "vastgelopen op 5/10"
- Benchmark 1: SharpCoreDB (No Encryption) - WAARSCHIJNLIJK OK
- Benchmark 2: SharpCoreDB (Encrypted) - WAARSCHIJNLIJK HIER VASTGELOPEN! ?
- Benchmark 3-5: Nog niet bereikt

**Test**:
```bash
cd SharpCoreDB.DebugBenchmark
dotnet run -c Release
# Kijk of het stopt bij "Step 4: Testing SharpCoreDB (Encrypted)..."
```

---

### Oorzaak #2: Database File Lock

**Probleem**: SQLite of LiteDB file blijft locked na vorige run

**Fix**:
```powershell
# Kill all dotnet processes
Stop-Process -Name "dotnet" -Force

# Clean temp directory
$tempPath = [System.IO.Path]::GetTempPath()
Remove-Item "$tempPath\dbBenchmark*" -Recurse -Force

# Try again
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

---

### Oorzaak #3: Infinite Loop in SIMD Code

**Probleem**: SIMD operations kunnen infinite loop hebben bij bepaalde data

**Bewijs**: Zou consistent bij dezelfde benchmark vastlopen

**Test**: Run debug app en kijk welke step hangt

---

## ?? STAP-VOOR-STAP DEBUG PROCES

### Stap 1: Run Debug App

```bash
cd D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.DebugBenchmark
dotnet run -c Release
```

**Mogelijke uitkomsten**:

#### Scenario A: Hangt bij Step 4 (Encrypted)
```
Step 3: Testing SharpCoreDB (No Encryption)...
? SUCCESS

Step 4: Testing SharpCoreDB (Encrypted)...
[HANGT HIER]
```

**Conclusie**: Encryption heeft een probleem!

**Fix**: Disable encrypted benchmark tijdelijk:
```csharp
// In Quick10kComparison.cs, comment out:
// [Benchmark(Description = "SharpCoreDB (Encrypted): 10K Batch Insert")]
// public int SharpCoreDB_Encrypted_10K() { ... }
```

#### Scenario B: Hangt bij Step 1 (GlobalSetup)
```
Step 1: Running GlobalSetup...
[HANGT HIER]
```

**Conclusie**: Database initialization probleem

**Fix**: Check DI registration in BenchmarkDatabaseHelper

#### Scenario C: Geeft Exception
```
? ERROR: InvalidOperationException
Message: Primary key violation
```

**Conclusie**: ID conflict probleem (should be fixed already)

**Fix**: Check currentBaseId logic

#### Scenario D: Alles werkt!
```
ALL TESTS PASSED!
Results:
  SharpCoreDB (No Encryption): 10000 records
  SharpCoreDB (Encrypted): 10000 records
  SQLite (Memory): 10000 records
  ...
```

**Conclusie**: Bug is ALLEEN in BenchmarkDotNet infrastructure!

**Fix**: InProcess toolchain zou dit moeten oplossen

---

### Stap 2: Als Debug App Werkt, Probeer Benchmark Opnieuw

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release
# Kies optie 1
```

**Als het NOG STEEDS hangt**:
- Check voor background processes
- Clean temp directory
- Reboot machine (nuclear option!)

---

### Stap 3: Simplified Benchmark (Als Alles Faalt)

Maak een ULTRA-SIMPLE version met ALLEEN SQLite:

```csharp
[InProcess]
public class SimpleBenchmark
{
    [Benchmark]
    public void SQLite_Only()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        // Insert 1000 records
    }
}
```

Als DIT werkt, is het probleem in SharpCoreDB specifiek.

---

## ?? VERWACHTE TIMING

Als debug app werkt, verwacht:

| Step | Operation | Expected Time |
|------|-----------|---------------|
| 1 | GlobalSetup | ~2 seconds |
| 2 | IterationSetup | < 1ms |
| 3 | SharpCore (No Enc) | ~7-10 seconds |
| 4 | SharpCore (Encrypted) | ~40-50 seconds ?? |
| 5 | SQLite (Memory) | ~75ms |
| 6 | SQLite (File + WAL) | ~50ms |
| 7 | LiteDB | ~400ms |
| 8 | Cleanup | < 1 second |

**Total**: ~60 seconds voor volledige test

Als het langer duurt dan 2 minuten ? HET HANGT!

---

## ?? QUICK FIXES

### Fix #1: Skip Encrypted Benchmark

In `Quick10kComparison.cs`:
```csharp
// Comment this out:
/*
[Benchmark(Description = "SharpCoreDB (Encrypted): 10K Batch Insert")]
public int SharpCoreDB_Encrypted_10K()
{
    ...
}
*/
```

### Fix #2: Reduce Record Count

```csharp
// Change from 10,000 to 1,000 for faster debugging
private const int RecordCount = 1000;  // Was: 10000
```

### Fix #3: Add Timeout

```csharp
[Benchmark(Description = "...", Timeout = 60000)]  // 60 second timeout
public int MyBenchmark() { ... }
```

---

## ?? RAPPORTEER RESULTATEN

Na debug app run, deel dit:

```
Scenario: [A/B/C/D]
Hung at: [Step number]
Error (if any): [Exception type + message]
Time taken: [Seconds before hang/completion]
```

Dit helpt mij het probleem exact te identificeren!

---

## ? ACTIE PLAN

1. **RUN DEBUG APP** - `cd SharpCoreDB.DebugBenchmark && dotnet run -c Release`
2. **OBSERVEER** - Waar stopt/hangt het?
3. **RAPPORTEER** - Deel de output
4. **APPLY FIX** - Gebaseerd op scenario

---

**Created**: December 2025  
**Tool**: SharpCoreDB.DebugBenchmark  
**Status**: READY TO DEBUG  

?? **Run de debug app en laat me weten wat je ziet!**
