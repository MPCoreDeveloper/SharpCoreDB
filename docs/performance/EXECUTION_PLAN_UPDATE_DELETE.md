# Uitvoerplan — UPDATE/DELETE-achterstand (combined)

**Datum:** 2026-09-03
**Bron:** eigen metingen/root-causes (sessie: PR #367–#370) + `PERFORMANCE_DEEP_DIVE.md` (Grok/xAI, second opinion).
**Status:** actief. Werkwijze: meet eerst (D2), bouw daarna (A1/A2), valideer met median-of-runs + full suite + CI.

## 0. Wat al klaar is (deze sessie)
| PR | Wat | Effect |
|---|---|---|
| #367 | In-place tombstones (directe deletes) | DELETE duurzaam in O(delete), geen flush-rewrite |
| #368 | **Commit-time tombstones** (transactionele/batch deletes) | **SQL DELETE 0,82 s → 0,24 s** (~12K → ~41-58K ops/s) — de grote sprong |
| #369 | C4 (batch markers + evict-dedup) + B3 (structured delete, geen dubbele parse) | veilig; neutraal binnen ruis op benchmark |
| #370 | B1 (key-only decode: alleen PK + hash-indexkolommen) | veilig; neutraal binnen ruis op small-row benchmark |
| B5-bulk (open) | **Bulk aflopende PK-delete** (`DeleteRecordsCore` verzamelt PK-sleutels eenmalig; `IIndex.DeleteBulk`/`BTree.DeleteBulk` sorteert aflopend → rechter-bladpad, minder separator-promoties) | correct (identieke keyset, één bezoek per key); fair-PK legacy-DELETE ~69-72K ops/s (binnen ruis op geordende batches) — winst bij ongeordende keysets |

**Root cause (niet in Grok-doc):** `ExecuteBatchSQL` draait elke batch in een storage-transactie; zonder #368 deed batch-DELETE nog steeds de #366 full-file compactie (~690 ms in `tableFlushLoop`). Daardoor waren eerdere “winst”-metingen niet-duurzaam/logisch-only.

## 1. Baselines & doel
| | SCDB Direct | SCDB SQL | SQLite | LiteDB |
|---|---:|---:|---:|---:|
| INSERT | ~130-187K | ~80-112K | ~130-150K | ~72K |
| READ | ~127K | ~71K | ~95K | ~15K |
| UPDATE | ~40-49K | ~34-40K | ~240-275K | ~10K |
| DELETE | ~55-58K | ~41K | ~325-375K | ~14K |

Cijfers variëren per machine/run (±10-20%). Verdict (beide analyses): niet verloren; **UPDATE/DELETE ~3x achter** is structureel maar aanpakbaar. INSERT/READ + analytics/vector/encryptie zijn al sterke punten.

## 2. Bottlenecks (gecombineerd)
1. AppendOnly versie-appends: UPDATE/DELETE = pread+pwrite per rij + indexonderhoud (SQLite: in-place leaf + WAL-batch).
2. Batch-DELETE zat onterecht op flush-compactie → **opgelost (#368)**.
3. Per-rij punt-I/O + B-tree/hash per operatie (C4/B3/B1 raken de rand, niet de kern — gemeten neutraal).
4. Volledige (de)serialisatie op in-place paden met leidende variabele-lengte kolommen (deels geraakt door B1).
5. Contiguïteit wordt alleen voor fixed-width benut (B8/B9); de benchmark-docs-tabel is variabele-lengte met fysiek oplopende posities.
6. Grove `rwLock` + per-op index-locks.
7. WAL/fsync-duurzaamheid (bevestigd met split-flush-meting: fsync-tail ~0,5-0,7 s op het oude pad; weg door #368).
8. Managed/GC + Dictionary/boxing in non-Direct paden.
9. AES-GCM per record (toggle: `NoEncryptMode`).

## 3. Werkwijze per stap (vóór elke bouw: meten)
- **D2-first:** profile UPDATE/DELETE hot paths (`DeleteMultipleKeys`/`UpdateMultiple`/`DeleteRecordsCore`/commit-tombstones) met `dotnet-trace` of env-gated fase-timers; bepaal of de tijd zit in resolutie (hash-lookups), per-rij `engine.Read`+decode, indexonderhoud (B-tree/hash), of de commit-markers.
- Bouw alleen wat het profiel aanwijst. Elke stap: eigen branch → median-of-N benchmark → full suite + 4 CI-filter-suites → doc-update.

## 4. Uitvoeringsfasen
### Fase A — Quick wins (P0)
- **A1:** contiguous variabele-lengte single-pass voor UPDATE/DELETE (prefix-walk over oplopende posities; 1 range-read, markers/patches in 1 schrijfpassage). Doel: docs-DELETE/UPDATE ~80-120K. Alleen na D2-bevestiging dat range-I/O de bottleneck is.
- **A2:** in-place UPDATE waar de slot-lengte gelijk blijft (geen append/stale-versie) voor niet-fastPatch-gevallen.
- **A3:** batched index re-point per index over de hele batch (deels aanwezig).

### Fase B — Structuur (P1, kern ~3x-achterstand)
- **B1:** `FixedWidthTable`/in-place page-engine volwassen (vaste offsets, slot-free-space, tombstone+vacuum). Doel: ~120-200K ops/s → ~80-110% van SQLite.
- **B2:** PageBased als aanbevolen OLTP-engine + storage-engine selector (OLTP→PageBased+FixedWidth; analytics/eventsourcing→AppendOnly/Columnar).
- **B3:** source-generated typed/ref-struct accessors (geen Dictionary op hot paths) + `PreparedCommand` met herbruikbare buffers.
- **B4:** fijnmaziger locking (per-page/per-index) + optimistische concurrency.

### Fase C — Platform (P2, .NET 11)
- **C1:** Native AOT + `Span<T>` + Runtime Async; SIMD lane-API's/AVX-VNNI-512 voor index-lookups.
- **C2:** optionele “SQLite-compat mode” (NoEncrypt + fixed-width default).

### Fase D — Hygiëne & observability (doorlopend)
- **D1:** median-of-N + warm-up in de comparative-harness.

## 7. D2-bevindingen (env-gated fase-timers, 2026-09-03)
DELETE 10K op de docs-tabel (comparative, Release) — attributie in de structured batch path:

| Fase | SQL | Direct |
|---|---:|---:|
| per-rij read+decode (`engine.Read`+`DeserializeDeleteKeyRow`) | 62-82 ms | ~66 ms |
| commit-tombstones (markers, pread+pwrite per rij) | ~50 ms | ~48-50 ms |
| delete core (index-onderhoud, B-tree/hash) | 23-56 ms | ~32 ms |
| hash-index lookup | 5-10 ms | ~5 ms |

**Ontdekte bug (gefixed):** `TryScanCanonicalDml`'s DELETE-tak consumente nooit de whitespace/het `WHERE`-keyword na de tabelnaam → elke canonieke `DELETE ... WHERE col = literal` viel terug op de regex-path → `DeleteMultipleKeys` + B1/W1 (PR #369/#370) waren **dead code in de benchmark-harness**. Fix + diagnostische teller `Database.CanonicalDeleteStatementsParsed` + regressietest `CanonicalBatchDelete_EngagesStructuredPath` (deze PR).


## 8. Eindstatus 2026-09-03 (alles gemerged op master `0a9b9fc1`)
Gemeten op master (Release, zelfde machine; median van 3 runs voor de comparative, 2 voor `--pk`):

| Operatie | SCDB SQL | SCDB Direct | SQLite | opmerking |
|---|---:|---:|---:|---|
| INSERT 100K | ~98-102K | ~103K | ~126-138K | al competitief |
| UPDATE 10K | ~44K | ~63K | ~228K | voor sessie: SQL ~35K / Direct ~49K |
| DELETE 10K | ~59K | ~86K | ~294K | **voor sessie: SQL ~12K / Direct ~16K** |
| `--pk` DELETE legacy / FW | 68K / 93K | — | ~320K | FW was ~78K |

**Merged deze sessie (chronologisch):** #367 tombstones · #368 commit-time tombstones · #369 C4+B3 · #370 B1 · #371 canonieke DELETE-scanner-fix + W1 · #372 whole-file DELETE-resolutie · #373 marker range-read · #374 whole-file UPDATE-resolutie.

### Bewuste vervolgstappen (niet in deze sessie, zie secties 3-4)
1. **Batch-PK stale/lazy-rebuild** na grote delete-batches — grootste open post; vereist eerst PK-index-refresh-infra (`Table.Index` is een plain property zonder lazy rebuild). Ontwerp nodig; daarna kan de per-rij read in DELETE vervallen.
2. **Commit-marker schrijfbatching** (writes blijven per-offset; range-read zit er al in via #373).
3. **Fase B (structureel):** fixed-width in-place engine + PageBased als OLTP-default — de weg naar ~1,2-1,5× van SQLite op UPDATE/DELETE.
4. **Fase C/D (platform):** AOT/R2R als aparte meet-as, median-of-N in de harness, `dotnet-trace` per Fase-B-stap.

**Gevolg voor de attributie:** de per-rij read blijft nodig zolang de delete-core de auto-`rowid`-PK-waarde per rij moet wissen (gate-onderzoek: docs heeft PK-achtige index >1 geregistreerd). Grootste resterende hefbomen, nu met cijfers onderbouwd:
1. **PK-onderhoud vervangen door één stale/lazy-rebuild** na een grote delete-batch (i.p.v. per-rij `Index.Delete`) → verwijdert ~30-50% van `core` én maakt de per-rij read overbodig (grootste winst op deze workload).
2. **Marker-batching over een range-read** (commit-tombstones ~50 ms → enkele ms) als vervolg op C4.
3. W1 (key-only, geen read) vuurt alleen bij een tabel met exact één geregistreerde index en zonder PK-tree — daar direct ~60-80 ms winst per 10K deletes.

- **D2:** `dotnet-trace`/fase-timers op `UpdateAffectedRows`, `DeleteMultipleKeys`, `DeleteRecordsCore`, commit-tombstones.
- **D3:** AOT/R2R expliciet als aparte meet-as (we meten nu managed JIT + tiered PGO).
- **D4:** `docs/manual/performance.md` + dit document bijwerken na elke stap.

## 5. Beslisboom (storage-engine)
- Veel UPDATE/DELETE (OLTP) → **PageBased + FixedWidth** (in-place).
- Veel appends/analytics/eventsourcing → **AppendOnly/Columnar** (blijft dominant).
- Pure-throughput scenario’s → `NoEncryptMode`; encryptie blijft default-differentiator.

## 6. Volgende acties
1. D2-profiel op de huidige DELETE/UPDATE hot paths.
2. A1/A2 implementeren op basis van het profiel (branch bovenop #370).
3. Fase B (fixed-width in-place + PageBased default) als aparte roadmap-track.
