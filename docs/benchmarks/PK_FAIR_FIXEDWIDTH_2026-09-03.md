# Fair PK harness — fixed-width default & contiguous UPDATE/DELETE (2026-09-03)

Machine: Windows, SDK 11 preview (builds net10.0). AppendOnly engine, `--pk` scenario:
100k inserts / 10k point reads / 10k PK updates / 10k PK deletes on
`docs(id INTEGER PRIMARY KEY, name TEXT, email TEXT, age INTEGER, score REAL, data TEXT)`
with `CREATE INDEX idx_docs_name ON docs(name)`; SQLite run on the identical schema.
Values are single-run samples from this machine and vary with load (one run under load dropped
SQLite itself from ~230-270K UPDATE to ~103K); use them as ranges, not exact deltas.

## Legacy vs fixed-width vs SQLite (AppendOnly)

| DB | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB legacy variable-length (opt-out) | 75-96K | 55-63K | 39-63K | 54-94K |
| SharpCoreDB default (fixed-width) before contiguous paths | 128K | 74K | 51K | 75K |
| SharpCoreDB default (fixed-width) after B8/B9 contiguous UPDATE/DELETE | 134-140K | 70-83K | 79-84K | 135K |
| SQLite | 119-124K | 79-93K | 230-270K | 279-353K |

Gap vs SQLite (representative, default fixed-width): INSERT ~1.0x, READ ~1.2x, UPDATE ~2.9-3.2x,
DELETE ~2.1x.

## What changed to get there

- #359 fixed-width default for new columnar PK tables (`AutoFixedWidthRecords`), incl. fixing the
  SQL batch-INSERT fast path to serialize fixed-width records.
- #360 single-pass contiguous UPDATE (one contiguous range read + in-memory patch).
- #361 single-pass contiguous DELETE + BTree separator-delete corruption fix.
- #363 gate widened to default-config plaintext databases (runtime `AreRecordsEncrypted`).
- #364 batched hash-index re-points (one lock per index per batch).

Raw per-run JSON results land in `results/pk_comparative_*.json` when running
`SharpCoreDB.Benchmarks.Comparative --pk`.
