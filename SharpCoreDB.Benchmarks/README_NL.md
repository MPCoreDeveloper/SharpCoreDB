# ? HOE BENCHMARKS TE RUNNEN - SIMPEL!

## ?? Vanuit Visual Studio (MAKKELIJKST!)

1. **Open** `SharpCoreDB.Benchmarks` project in Visual Studio
2. **Klik** op de **groene play knop** (of druk F5)
3. **Kies** optie uit het menu

**Menu opties**:
```
1. Quick 10K Test (RECOMMENDED - 2-3 min) ?  ? KIES DIT!
2. Quick Comparison
3. Full Comprehensive Suite
4. INSERT Benchmarks Only
5. SELECT Benchmarks Only
6. UPDATE/DELETE Benchmarks Only
7. C# 14 Modernization Benchmark
8. Quick Performance Comparison
```

---

## ?? Vanuit Terminal (ook goed!)

```bash
cd D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Benchmarks
dotnet run -c Release
```

Dan kies je gewoon een nummer!

---

## ? Direct een specifieke test runnen

```bash
# Quick 10K test (aanbevolen!)
cd SharpCoreDB.Benchmarks
dotnet run -c Release
# Kies optie 1

# Of via command line:
dotnet run -c Release -- --quick
```

---

## ?? Wat test de Quick 10K Test?

- **10,000 record inserts** tegen:
  - ? SharpCoreDB (No Encryption)
  - ? SharpCoreDB (Encrypted)
  - ? SQLite (Memory)
  - ? SQLite (File + WAL + FullSync)
  - ? LiteDB

**Duur**: 2-3 minuten
**Output**: BenchmarkDotNet report met HTML/CSV/JSON

---

## ?? Resultaten vinden

Na het runnen:
```
BenchmarkDotNet.Artifacts/
??? results/
    ??? Quick10kComparison-report.html  ? Open dit in browser!
    ??? Quick10kComparison-report.csv   ? Open in Excel
    ??? Quick10kComparison-report.json
```

---

## ? DAT IS HET!

**Geen scripts, geen gedoe, gewoon:**
1. F5 in Visual Studio
2. Kies optie 1
3. Wacht 2-3 minuten
4. Klaar!

??
