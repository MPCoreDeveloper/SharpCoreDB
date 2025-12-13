# PageCache Performance Testing

## Overzicht

Deze directory bevat benchmarks om de performance van de nieuwe PageCache implementatie te testen en te vergelijken met een traditionele aanpak.

## Files

- **`PageCacheBenchmarks.cs`** - Complete BenchmarkDotNet suite (13 scenarios)
- **`PageCacheQuickTest.cs`** - Snelle validatie test (geen BenchmarkDotNet nodig)
- **`run-pagecache-benchmark.ps1`** - PowerShell script om benchmarks te draaien
- **`PAGECACHE_BENCHMARK_EXPECTATIONS.md`** - Verwachte resultaten en analyse guide

## Quick Start

### Optie 1: Quick Test (2 minuten)

Run de quick test voor een snelle validatie:

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release PageCacheQuickTest
```

**Verwachte Output:**
```
==============================================
  PageCache Quick Performance Test
==============================================

1. Sequential Access Test...
   - 10,000 operaties in 12 ms
   - 833,333 ops/sec
   - 1.2 ticks per operatie
   - Hit Rate: 99.0%
   ✅ Sequential access werkt!

2. Concurrent Access Test (8 threads)...
   - 10,000 operaties (8 threads) in 15 ms
   - 666,667 ops/sec
   - Hit Rate: 98.0%
   ✅ Concurrent access werkt!

3. Memory Allocation Test...
   - Cache size: 1000 pages x 4096 bytes = 4 MB
   - Actual memory used: 4.12 MB
   - Overhead: 120 KB
   - Gen0 collections tijdens 10K ops: 0
   ✅ Memory usage is efficient!

4. Statistics & Diagnostics Test...
   - Total Hits: 50
   - Total Misses: 150
   - Hit Rate: 25.0%
   - Evictions: 50
   - Current Size: 100/100
   ✅ Statistics tracking werkt!

==============================================
  ✅ Alle tests geslaagd!
==============================================
```

### Optie 2: Full Benchmark Suite (15-30 minuten)

Run de volledige BenchmarkDotNet suite:

```powershell
.\run-pagecache-benchmark.ps1
```

Of handmatig:
```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release --filter "*PageCache*"
```

## Benchmark Scenarios

De volledige suite test 13 verschillende scenarios:

### Basis Operaties
1. **Sequential Access** - Opeenvolgende page accesses
2. **Cache Hits** - Pure hit performance (100% hit rate)
3. **Pin/Unpin** - Pin count management
4. **Mark Dirty** - Dirty flag operations
5. **Flush Pages** - Write-back performance

### Eviction & Working Set
6. **Eviction Stress** - CLOCK vs LRU eviction
7. **Random Access** - Random page access pattern
8. **Working Set Scan** - Repeated scan over working set

### Concurrency Tests
9. **4 Threads Concurrent** - Low contention
10. **8 Threads Concurrent** - Medium contention
11. **16 Threads Concurrent** - High contention
12. **Hot Page Contention** - Alle threads dezelfde page

### Mixed Workload
13. **Mixed Workload** - Realistische mix van operations

## Verwachte Resultaten

### Performance Speedups

| Scenario | Verwachte Speedup | Reden |
|----------|-------------------|-------|
| Sequential | 1.7x | Lock-free lookup |
| Cache Hits | 1.9x | ConcurrentDictionary |
| Pin/Unpin | 2.4x | Interlocked vs Lock |
| Mark Dirty | 4x | Volatile vs Lock |
| 4 Threads | 2.3x | Low contention |
| 8 Threads | 3x | Medium contention |
| **16 Threads** | **5x** | **High contention** |
| Hot Page | 2.5x | Interlocked contention |

### Memory & GC

- **Memory Allocations:** 98% reductie
- **GC Collections:** 95% reductie
- **GC Pause Time:** 90% reductie

Zie `PAGECACHE_BENCHMARK_EXPECTATIONS.md` voor gedetailleerde verwachtingen.

## Results Interpreteren

### 1. Check de Ratio Column

```
|                Method |      Mean | Ratio | Allocated |
|---------------------- |----------:|------:|----------:|
| Traditional_Sequential|  2,000 ns |  1.00 |    128 B  |  ← Baseline
| Modern_Sequential     |  1,200 ns |  0.60 |      0 B  |  ← 1.7x sneller!
```

**Ratio < 1.0 betekent sneller!**

### 2. Check Memory Allocations

```
Modern: 0 B        ✅ Zero allocations!
Traditional: 128 B  ❌ Veel allocaties
```

### 3. Check Threading Diagnostics

```
Completed Work Items: 16  ✅ Alle threads efficient
Lock Contentions: 0       ✅ Geen contention
```

## Troubleshooting

### Build Errors

Als je build errors krijgt:

```bash
dotnet restore
dotnet build -c Release
```

### Benchmark Crashes

Als de benchmark crasht:
1. Check of je genoeg geheugen hebt (min 4 GB vrij)
2. Close andere applicaties
3. Run met kleinere CacheSize: `[Params(100)]`

### Inconsistent Results

Als resultaten variëren:
1. Disable Turbo Boost voor consistente CPU clocks
2. Run benchmark meerdere keren
3. Check background processes
4. Use `[InvocationCount(1000)]` voor meer iteraties

### Modern is Langzamer?!

Als Modern onverwacht langzamer is:
1. Check of je in Release mode build (niet Debug!)
2. Check .NET version: `dotnet --version` (moet 10.0+ zijn)
3. Check CPU - is AVX2 supported?
4. Run alleen Sequential test eerst voor baseline

## Expected Output Voorbeeld

```
// * Summary *

BenchmarkDotNet v0.13.12, Windows 11
Intel Core i7-10700K CPU 3.80GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.0
  [Host]     : .NET 10.0.0, X64 RyuJIT AVX2
  Job-QWERTY : .NET 10.0.0, X64 RyuJIT AVX2

|                Method | CacheSize |      Mean |    Ratio | Allocated |
|---------------------- |---------- |----------:|---------:|----------:|
| Traditional_Sequential|      1000 |  2.123 µs |     1.00 |     128 B |
| Modern_Sequential     |      1000 |  1.245 µs |     0.59 |       0 B |
|                       |           |           |          |           |
| Traditional_Concurrent|      1000 |  1.234 ms |     1.00 |   4,280 B |
| Modern_Concurrent     |      1000 |  0.412 ms |     0.33 |      72 B |

// Modern is 3x sneller bij concurrency! ✅
```

## Next Steps

Na successful benchmark runs:

1. **Analyze Results** - Open HTML report in `BenchmarkDotNet.Artifacts/results/`
2. **Compare with Expectations** - Zie `PAGECACHE_BENCHMARK_EXPECTATIONS.md`
3. **Document Findings** - Update project documentation
4. **Integrate** - Use PageCache in SharpCoreDB Database class

## Meer Info

- **Implementation Details:** `../SharpCoreDB/PAGE_CACHE_IMPLEMENTATION.md`
- **API Reference:** `../SharpCoreDB/PAGE_CACHE_SUMMARY.md`
- **Architecture:** `../SharpCoreDB/Core/Cache/`

---

**Status:** ✅ Ready to Run

**Laatste Update:** December 2025
