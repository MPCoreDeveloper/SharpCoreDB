# Digest — D1 SQLite mechanics · round 1 · n=3

Sources: `sqlite.org/pragma.html`, `sqlite.org/speed.html` (vendor marks this page **obsolete**: "very
very old … the numbers here have become meaningless")

## Findings — the tuning surface (this is the fair-comparison control list)

| Pragma | What it changes | Documented tradeoff | Status |
|---|---|---|---|
| `synchronous` | fsync frequency | durability vs speed; **`nosync` is roughly 2× on write tests** in the vendor's own obsolete benchmark | **two-source (vendor pages)** |
| `journal_mode=WAL` | rollback journal → WAL | "significantly faster in most scenarios" | vendor, high |
| `cache_size` | page cache size | memory vs I/O | vendor, high |
| `mmap_size` | bytes mapped for mmap I/O | memory vs syscalls | vendor, high |
| `locking_mode=EXCLUSIVE` | never releases file locks | "number of system calls for filesystem operations is reduced, possibly resulting in a small performance increase" | vendor, high |
| `page_size` | page size (power of two) | must be set outside WAL mode; affects split/overflow rates | vendor, high |
| `temp_store` | where temp objects live | memory vs disk for sorts | vendor, high |
| `wal_autocheckpoint` | checkpoint frequency | WAL growth vs write pauses | vendor, high |

- Vendor's own historical benchmark: on UPDATE/DELETE tests, **`nosync` was 1.4–1.7× faster than
  full-sync** (e.g. 25000 text UPDATEs: 2.408 s sync vs 1.725 s nosync) and in one DELETE test 4.004 s
  → 0.560 s. — sqlite.org/speed.html · page marked obsolete/by the vendor · accessed 2026-09-24 ·
  **low** (explicitly retracted by the publisher) · class = performance. **Do not cite as current.**
- The vendor's own conclusion on workload shape: *"SQLite works best if you group multiple
  operations together into a single transaction."* — sqlite.org/speed.html · obsolete page ·
  accessed 2026-09-24 · medium (mechanism still current) · class = mechanism.
- `-DNDEBUG=1` "roughly doubles the speed of SQLite" by removing `assert()`s — an explicit admission
  that assert-bearing builds halve throughput. Mirrors our own "check the profiler is not armed"
  pitfall. — sqlite.org/speed.html · obsolete page · accessed 2026-09-24 · low–medium · class =
  methodology.

## Derived (medium) — the fairness rule this pack forces
Any published "we beat SQLite" number is only meaningful if the SQLite arm is documented at the
**same tuning level** as ours. The pragma table above is the checklist: an untuned SQLite
(`synchronous=FULL`, rollback journal, default 2 MB cache, no mmap) is a *different engine* from a
tuned one, and the vendor's own page shows a >2× swing from `synchronous` and journal mode alone.
Consequence for us: publish **both** an untuned-defaults comparison *and* a tuned-vs-tuned comparison,
and never mix the two — the repo's existing "ratios only" rule is the same rule seen from the other side.

## Looked for, did not find
- Any current (2024+) vendor curve for `synchronous` on modern NVMe. Absence of evidence — treat the
  2× figure as obsolete-direction-only.
