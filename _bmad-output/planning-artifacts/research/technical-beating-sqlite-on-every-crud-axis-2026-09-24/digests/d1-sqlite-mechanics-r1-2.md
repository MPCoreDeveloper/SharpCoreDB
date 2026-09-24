# Digest — D1 SQLite mechanics · round 1 · n=2

Sources: `sqlite.org/wal.html` (updated 2026-08-25), `sqlite.org/faq.html` (updated 2024-11-26)

## Findings

- WAL advantages, stated by the vendor: *"WAL is significantly faster in most scenarios"*; readers do
  not block writers and writers do not block readers; **disk I/O tends to be more sequential**;
  **"WAL uses many fewer fsync() operations"**. — sqlite.org/wal.html · 2026-08-25 · accessed
  2026-09-24 · **high** · class = performance mechanism.
- WAL is "very slightly slower (perhaps **1% or 2%**)" than rollback journal for read-mostly
  workloads. — sqlite.org/wal.html · 2026-08-25 · accessed 2026-09-24 · high · class = performance.
- **WAL works best with smaller transactions**; for transactions above ~100 MB the rollback journal
  is likely faster; >1 GB WAL may fail. — sqlite.org/wal.html · 2026-08-25 · accessed 2026-09-24 ·
  high · class = performance. (Direction matters for our batch-inserter sizing.)
- `page_size` **cannot** be changed once in WAL mode (including via VACUUM/backup restore). —
  sqlite.org/wal.html · 2026-08-25 · accessed 2026-09-24 · high · class = constraint.
- Read-only WAL databases cannot be opened unless `-shm`/`-wal` exist or are creatable, or the db is
  immutable — a real end-user integration cost of WAL. — sqlite.org/wal.html · 2026-08-25 · accessed
  2026-09-24 · high · class = constraint.
- A **corruption bug** ("WAL-reset") was present in every release **3.7.0 (2010-07-21) through
  3.51.2 (2026-01-09)**, fixed in **3.51.3 (2026-03-13)**; needs ≥2 connections on the same file
  writing/checkpointing simultaneously; developers could not reproduce it organically until Aug 2026.
  — sqlite.org/wal.html · 2026-08-25 · accessed 2026-09-24 · **high** · class = reliability.
  *This is honest-differentiation material, not a speed lever — record it, do not build a claim on
  "SQLite is unsafe".*
- FAQ entry **"INSERT is really slow - I can only do few dozen INSERTs per second"** exists as a
  named question: SQLite's own framing is that the fix is transaction batching / `synchronous`
  configuration, not the engine. — sqlite.org/faq.html · 2024-11-26 · accessed 2026-09-24 · high ·
  class = performance.
- *"I deleted a lot of data but the database file did not get any smaller"* is a documented FAQ
  answer — i.e. SQLite also defers physical reclamation (freelist + explicit VACUUM). Implication:
  our append+compaction design is **not** inferior by construction; SQLite does an equivalent
  deferred reclaim, just inside pages. — sqlite.org/faq.html · 2024-11-26 · accessed 2026-09-24 ·
  high · class = parity argument.

## Leads worth chasing
- WAL checkpoint defaults / `wal_autocheckpoint` interaction with per-statement throughput.
- `PRAGMA synchronous` levels and their measured effect (round 1 n=3 covered the index only).

## Looked for, did not find
- Any vendor figure for single-threaded UPDATE/DELETE ops/s. sqlite.org publishes no CRUD benchmark;
  our comparison numbers must therefore be our own measurements, and the *tuning surface* is the
  only fair-comparison lever we control.
