# Digest — D2 Record layout & in-place update · round 2 · n=2

Sources: `dev.mysql.com/doc/refman/8.4/en/innodb-row-format.html` (MySQL 8.4),
`github.com/facebook/rocksdb/wiki/Basic-Operations` (RocksDB wiki, last edited 2023-08-21)

## Findings

- InnoDB ships **four row formats** and the choice is per-table: `REDUNDANT`, `COMPACT`, `DYNAMIC`
  (default), `COMPRESSED`. The default is `dynamic` (`innodb_default_row_format`). — MySQL 8.4
  Reference Manual · accessed 2026-09-24 · **high** · class = layout.
- The formats differ in exactly the dimension that matters to us — how variable-length columns are
  referenced: `DYNAMIC` moves long variable-length columns **entirely off-page** (pointer only),
  while `COMPACT` keeps a prefix inline with an overflow pointer. — MySQL 8.4 Reference Manual ·
  accessed 2026-09-24 · **high** · class = layout.
- Changing format has a real cost surface: `REDUNDANT`/`COMPACT` cap index key prefix at **767
  bytes** vs **3072 bytes** for `DYNAMIC`/`COMPRESSED`, and a format mismatch between source and
  replica makes DDL succeed on one and fail on the other. — MySQL 8.4 Reference Manual · accessed
  2026-09-24 · **high** · class = constraint. *Transferable lesson: the "long value lives off-page"
  layout is not free — it buys in-page stability by spending index-key headroom and DDL
  compatibility. Any such change on our side needs the same explicit trade written down.*
- A row-format change via `ALTER TABLE`/`OPTIMIZE TABLE` is a **table-rebuilding** operation. — MySQL
  8.4 Reference Manual · accessed 2026-09-24 · high · class = constraint.
- RocksDB is a persistent **key-value** store: keys are arbitrary byte arrays, **ordered by a
  user comparator**, all contents in one directory. — RocksDB wiki · 2023-08-21 · accessed
  2026-09-24 · **high** · class = architecture.
- The wiki's own index of the *update/delete* machinery is the finding: `Single Delete`, `Merge`
  (read-modify-write), `DeleteRange`, `Compaction Filter`, `Write Stalls`, `Low Priority Write`,
  `TTL` — i.e. in an LSM, update and delete are **first-class bookkeeping problems** that need
  dedicated operators, tombstones and compaction policies. — RocksDB wiki · accessed 2026-09-24 ·
  **high** · class = architecture.
- `Write Stalls` is a named concept: the engine can *block writers* when compaction falls behind. —
  RocksDB wiki · accessed 2026-09-24 · medium (page title only in the fetched slice; not expanded) ·
  class = operational risk.

## Derived (medium) — the trade, stated for our decision
Two coherent designs exist and both are shipping in a major engine:

| | B-tree / heap page (SQLite, PostgreSQL, InnoDB) | LSM (RocksDB, LevelDB) |
|---|---|---|
| UPDATE/DELETE | edit/write a row version in a page; delete may need defrag/vacuum | append; delete = tombstone |
| Reclaim | deferred, explicit VACUUM / freelist | deferred, automatic compaction |
| Cost paid | page splits, free-space management, WAL of whole pages | read amplification, **write stalls** |

SharpCoreDB's *default* shape is the right-hand column (append new version, deferred compaction
behind `ColumnarAutoCompactionThreshold`) and its *fixed-width PK* shape is the left column. The
"0.24× UPDATE on the default shape" is therefore **not a bug — it is the LSM half of the trade**,
which is exactly why PostgreSQL added HOT rather than changing storage model.

Conclusion the pack supports: **closing the default-shape gap means giving the default shape the
left-hand column's in-page edit, not making the LSM faster.** The three published mechanisms for
that are (a) item-identifier indirection, (b) HOT's indexed-column test, (c) out-of-line values.

## Leads worth chasing
- `Write Stalls` detail (RocksDB wiki page) — do we have an equivalent (our `TryAutoCompact`)?
- InnoDB page fill / merge-threshold defaults for the "reserve slack" idea.

## Looked for, did not find
- Any MySQL statement about *in-place* single-row UPDATE within a page without a format change.
  Absence of evidence → do not claim InnoDB patches a growing column in place.
