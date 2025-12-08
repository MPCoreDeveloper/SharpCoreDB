# PageCache Benchmark Resultaten - Verwacht vs Werkelijk

## Overzicht

Deze benchmark vergelijkt de **moderne PageCache** (CLOCK + lock-free) met een **traditionele implementatie** (LRU + Dictionary + lock).

## Verwachte Resultaten

### 1. Sequential Access (Basis Operaties)

**Scenario:** 1000 opeenvolgende toegangen tot pagina's in de cache

**Verwacht:**
```
Traditional: ~2,000 ns  (2 µs per operatie)
Modern:      ~1,200 ns  (1.2 µs per operatie)
Speedup:     1.7x sneller
```

**Reden:** Lock-free ConcurrentDictionary vs Dictionary met lock

---

### 2. Cache Hits (100% Hit Rate)

**Scenario:** 1000 toegangen tot dezelfde pagina (pure cache hits)

**Verwacht:**
```
Traditional: ~1,500 ns  (Dictionary lookup + lock)
Modern:      ~800 ns    (ConcurrentDictionary lookup)
Speedup:     1.9x sneller
```

**Reden:** Geen lock contention, pure lookup snelheid

---

### 3. Pin/Unpin Operations

**Scenario:** 1000 pin/unpin cycles op één pagina

**Verwacht:**
```
Traditional: ~1,200 ns  (lock + counter increment)
Modern:      ~500 ns    (Interlocked.Increment/Decrement)
Speedup:     2.4x sneller
```

**Reden:** Atomic operations vs lock overhead

---

### 4. Mark Dirty Operations

**Scenario:** 100 pagina's markeren als dirty

**Verwacht:**
```
Traditional: ~800 ns per page   (lock + flag set)
Modern:      ~200 ns per page   (Volatile.Write)
Speedup:     4x sneller
```

**Reden:** Lock-free flag update vs lock acquisition

---

### 5. Flush Operations

**Scenario:** 100 dirty pagina's flushen

**Verwacht:**
```
Traditional: ~15,000 ns  (lock per page + flush)
Modern:      ~12,000 ns  (latch + flush)
Speedup:     1.25x sneller
```

**Reden:** Lightweight latch vs full lock, maar I/O domineert

---

### 6. Eviction Performance

**Scenario:** Cache vol maken en nieuwe pagina's laden (triggert evictions)

**Verwacht:**
```
Traditional (LRU):  ~5,000 ns per eviction  (linked list manipulation + lock)
Modern (CLOCK):     ~3,000 ns per eviction  (array scan + CAS latch)
Speedup:            1.7x sneller
```

**Reden:** CLOCK is O(n) maar sneller in praktijk dan LRU linked list

---

### 7. Random Access Pattern

**Scenario:** 1000 random page accesses (mix van hits en misses)

**Verwacht:**
```
Traditional: ~3,500 ns per access
Modern:      ~2,000 ns per access
Speedup:     1.75x sneller
```

**Reden:** Combinatie van alle voordelen

---

### 8. Concurrent Access - 4 Threads

**Scenario:** 4 threads die elk 250 pagina's benaderen

**Verwacht:**
```
Traditional: ~800,000 ns  (lock contention)
Modern:      ~350,000 ns  (lock-free)
Speedup:     2.3x sneller
```

**Reden:** Minimale contention door lock-free design

---

### 9. Concurrent Access - 8 Threads

**Scenario:** 8 threads die elk 125 pagina's benaderen

**Verwacht:**
```
Traditional: ~1,200,000 ns  (hoge lock contention)
Modern:      ~400,000 ns    (bijna lineaire scalability)
Speedup:     3x sneller
```

**Reden:** Lock contention wordt exponentieel erger, modern schaalt lineair

---

### 10. Concurrent Access - 16 Threads

**Scenario:** 16 threads die elk 62 pagina's benaderen

**Verwacht:**
```
Traditional: ~2,500,000 ns  (zeer hoge contention)
Modern:      ~500,000 ns    (nog steeds goede scalability)
Speedup:     5x sneller
```

**Reden:** Modern design schaalt bijna lineair, traditional valt volledig uit elkaar

---

### 11. Hot Page Contention (Zelfde Pagina, 8 Threads)

**Scenario:** 8 threads benaderen dezelfde hot page

**Verwacht:**
```
Traditional: ~1,500,000 ns  (serialization door lock)
Modern:      ~600,000 ns    (Interlocked contention maar geen lock)
Speedup:     2.5x sneller
```

**Reden:** Interlocked is veel sneller dan lock bij contention

---

### 12. Mixed Workload

**Scenario:** Combinatie van reads, writes, flushes

**Verwacht:**
```
Traditional: ~25,000 ns
Modern:      ~15,000 ns
Speedup:     1.7x sneller
```

**Reden:** Alle voordelen gecombineerd

---

### 13. Working Set Scan

**Scenario:** 10 iteraties over 500 pagina's (working set scan)

**Verwacht:**
```
Traditional: ~120,000 ns
Modern:      ~80,000 ns
Speedup:     1.5x sneller
```

**Reden:** Efficiëntere cache access

---

## Samenvatting Verwachte Speedups

| Scenario | Speedup | Belangrijkste Factor |
|----------|---------|---------------------|
| Sequential | 1.7x | Lock-free lookup |
| Cache Hits | 1.9x | ConcurrentDictionary |
| Pin/Unpin | 2.4x | Interlocked vs Lock |
| Mark Dirty | 4x | Volatile vs Lock |
| Flush | 1.25x | Latch vs Lock |
| Eviction | 1.7x | CLOCK vs LRU |
| Random | 1.75x | Combined |
| 4 Threads | 2.3x | Low contention |
| 8 Threads | 3x | Medium contention |
| 16 Threads | **5x** | High contention |
| Hot Page | 2.5x | Interlocked contention |
| Mixed | 1.7x | Combined |
| Working Set | 1.5x | Efficient access |

## Memory Allocatie Verwachtingen

### Traditional Cache
```
Per GetPage (miss):
  - Dictionary allocation: ~48 bytes
  - LinkedList node: ~32 bytes
  - LRU node dict entry: ~16 bytes
  - Page buffer: 4096 bytes
  Total: ~4,192 bytes

Per 1000 operations: ~400 KB allocaties
```

### Modern Cache
```
Per GetPage (miss):
  - PageFrame allocation: ~72 bytes (struct overhead)
  - MemoryPool buffer: 4096 bytes (reused!)
  Total: ~72 bytes nieuwe allocatie

Per 1000 operations: ~7 KB allocaties

Improvement: 98% minder allocaties!
```

## GC Pressure Verwachtingen

### Traditional
- **Gen0 Collections:** ~15 per 10,000 operaties
- **Gen1 Collections:** ~3 per 10,000 operaties
- **GC Pause Time:** ~50 ms totaal

### Modern
- **Gen0 Collections:** ~1 per 10,000 operaties
- **Gen1 Collections:** 0
- **GC Pause Time:** ~3 ms totaal

**Improvement:** 95% minder GC druk

## Threading Diagnostics Verwachtingen

### Lock Contention (Traditional)
```
Lock Acquisitions: ~10,000 per test
Lock Contentions: ~2,000 per test (20%)
Average Wait Time: ~50 µs
```

### CAS Failures (Modern)
```
CAS Operations: ~10,000 per test
CAS Failures: ~200 per test (2%)
Retry Spins: ~3 gemiddeld
```

**Improvement:** 10x minder contention

## Hoe Resultaten Interpreteren

### 1. Kijk naar de Ratio Column
```
|                Method |      Mean | Ratio | Allocated |
|---------------------- |----------:|------:|----------:|
| Traditional_Sequential|  2,000 ns |  1.00 |    128 B  |  ← Baseline
| Modern_Sequential     |  1,200 ns |  0.60 |      0 B  |  ← 40% sneller!
```

**Ratio < 1.0 = sneller**
**Ratio > 1.0 = langzamer**

### 2. Check Memory Allocations
```
Modern: 0 B  ✅ Geen allocaties!
Traditional: 128 B ❌ Veel allocaties
```

### 3. Threading Diagnostics
```
Lock Contentions: 2,000 ❌ Veel contention
Completed Work Items: 8 ✅ Alle threads efficient
```

## Wat Te Doen Als Resultaten Afwijken

### Als Modern LANGZAMER is:
1. Check CPU - is Turbo Boost enabled?
2. Check background processes - close alles
3. Run benchmark opnieuw met `--filter "*SequentialAccess*"` eerst
4. Check of .NET 10 correct geïnstalleerd is

### Als Speedup HOGER is dan verwacht:
🎉 **Gefeliciteerd!** De implementatie is nog beter dan verwacht.

### Als Speedup LAGER is dan verwacht:
- Check CacheSize parameter (grotere cache = minder evictions)
- Check HitRatePercentage (hogere hit rate = meer voordeel)
- Run meerdere keren voor consistente resultaten

## Conclusie

De moderne PageCache zou consistent **1.5x - 5x sneller** moeten zijn, afhankelijk van het scenario:

- **Best Case:** 16 threads concurrent (5x speedup)
- **Average Case:** Random mixed workload (1.7x speedup)
- **Worst Case:** Pure sequential (1.5x speedup)

Plus: **98% minder memory allocaties** en **95% minder GC druk**!

---

**Run de benchmark met:**
```powershell
.\run-pagecache-benchmark.ps1
```

Of handmatig:
```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release --filter "*PageCache*"
```
