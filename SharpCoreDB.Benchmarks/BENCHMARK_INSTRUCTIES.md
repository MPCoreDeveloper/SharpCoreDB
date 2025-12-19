# ?? Benchmark Instructies

## ? Stap-voor-stap Guide

### 1?? Run Benchmarks (HANDMATIG)

```powershell
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

**In het menu:**
- Kies `1` voor **PageBasedStorageBenchmark** (Baseline vs Optimized)
- Kies `2` voor **StorageEngineComparisonBenchmark** (vs SQLite & LiteDB)

**Wacht tot voltooid** (kan 5-15 minuten duren)

---

### 2?? Analyseer Resultaten

**Optie A: PowerShell Script** (AANBEVOLEN)
```powershell
.\AnalyzeResults.ps1
```

Dit script:
- ? Vindt automatisch de meest recente resultaten
- ? Toont formatted tabel
- ? Checkt voor issues
- ? Biedt aan om HTML report te openen
- ? Kan analyse opslaan naar .txt file

**Optie B: Handmatig**
```powershell
# Open results folder
explorer BenchmarkDotNet.Artifacts\results

# Bekijk markdown files
code BenchmarkDotNet.Artifacts\results\*-report-github.md

# Open HTML report (interactief)
start BenchmarkDotNet.Artifacts\results\*.html
```

---

### 3?? Deel Resultaten

**Voor analyse door AI:**
```powershell
# Run analyse script en save to file
.\AnalyzeResults.ps1
> Kies optie 1
> Save analysis? Y

# Upload de .txt file
```

**OF gewoon de markdown file uploaden:**
```
BenchmarkDotNet.Artifacts\results\SharpCoreDB.Benchmarks.PageBasedStorageBenchmark-report-github.md
```

---

## ?? Output Files

Na het draaien van benchmarks vind je:

```
BenchmarkDotNet.Artifacts\results\
??? *-report-github.md       # ? Markdown tabel (best voor Git/issues)
??? *-report.html             # ? Interactieve HTML (grafieken!)
??? *-report.csv              # ? Excel-compatible
??? *-report-full.json        # ? Volledige data
??? *-measurements.csv        # ? Ruwe metingen
```

---

## ?? Verwachte Resultaten

### PageBasedStorageBenchmark (10K records)

| Operatie | Baseline | Optimized | Target Speedup | Doel |
|----------|----------|-----------|----------------|------|
| UPDATE 5K | ~600-800ms | ~120-180ms | **3-5x** | ? |
| SELECT Scan | ~150-200ms | ~20-40ms | **5-10x** | ? |
| DELETE 2K | ~400-500ms | ~80-120ms | **3-5x** | ? |
| Mixed 5K | ~1200-1500ms | ~250-400ms | **3-4x** | ? |

### StorageEngineComparisonBenchmark (10K records)

| Operatie | SQLite (target) | LiteDB | PAGE_BASED | AppendOnly |
|----------|-----------------|--------|------------|------------|
| INSERT 10K | ~40-60ms ?? | ~120-180ms | ~200-300ms | ~500-700ms |
| UPDATE 5K | ~80-120ms ?? | ~180-250ms | ~120-180ms ? | ~400-600ms |
| SELECT (cached) | ~30-50ms | ~80-120ms | **~4ms** ?? | ~100-150ms |
| Mixed OLTP | ~180ms ?? | ~450ms | ~250-400ms ? | ~1000ms+ |

**Key Insights:**
- ? PAGE_BASED **10x sneller** bij cached SELECT dan SQLite
- ? PAGE_BASED **bijna even snel** als SQLite bij UPDATE
- ?? SQLite **6x sneller** bij raw INSERT (maar geen encryption)

---

## ?? Troubleshooting

### "NA" in resultaten
```powershell
# Benchmark is gefaald, check log:
Get-Content BenchmarkDotNet.Artifacts\*.log -Tail 50
```

### Benchmarks lopen vast
- ? Gebruik ALLEEN handmatige run (niet via script)
- ? Wacht geduldig (10K records = 5-15 min)
- ? Check CPU usage (moet >80% zijn tijdens run)

### Geen resultaten gevonden
```powershell
# Check of results folder bestaat:
Test-Path BenchmarkDotNet.Artifacts\results

# Als niet, run benchmarks eerst!
```

---

## ?? Configuratie

### RecordCount aanpassen

**PageBasedStorageBenchmark.cs:**
```csharp
private const int RecordCount = 10_000; // Verander naar 1_000 voor snelle test
```

**StorageEngineComparisonBenchmark.cs:**
```csharp
private const int RecordCount = 10_000; // Verander naar 1_000 voor snelle test
```

### Warmup/Iterations aanpassen

**Program.cs:**
```csharp
var job = Job.InProcess
    .WithWarmupCount(1)      // 0 = skip warmup (sneller maar minder accuraat)
    .WithIterationCount(3)   // 1 = 1 run (snelste)
    .WithLaunchCount(1);
```

---

## ? Checklist voor Succesvolle Run

- [ ] Build in Release mode: `dotnet build -c Release`
- [ ] Start benchmark app: `dotnet run -c Release`
- [ ] Kies optie 1 of 2
- [ ] Wacht tot "Benchmark Complete!" verschijnt
- [ ] Run `.\AnalyzeResults.ps1`
- [ ] Save analysis to file
- [ ] Upload analysis.txt voor review

---

## ?? Hulp Nodig?

**Check logs:**
```powershell
# Laatste benchmark log
Get-ChildItem BenchmarkDotNet.Artifacts\*.log | 
    Sort-Object LastWriteTime -Descending | 
    Select-Object -First 1 | 
    Get-Content -Tail 100
```

**Clean resultaten:**
```powershell
# Verwijder oude results voor fresh start
Remove-Item -Path BenchmarkDotNet.Artifacts -Recurse -Force
```

---

**Ready to benchmark!** ??
