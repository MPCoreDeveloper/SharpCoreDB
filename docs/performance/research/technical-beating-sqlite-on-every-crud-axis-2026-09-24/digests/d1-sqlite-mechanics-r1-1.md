# Digest — D1 SQLite mechanics · round 1 · n=1

Sources: `sqlite.org/arch.html` (updated 2025-05-31), `sqlite.org/fileformat2.html`

## Findings (claim / source / pub / accessed / confidence / class)

- SQLite compiles SQL text into bytecode and runs it on a VM; `sqlite3_prepare_v2()` is the
  compile step and `sqlite3_stmt` holds the program. Re-preparing per call therefore re-pays
  compilation — the documented reason to prepare once and reuse. — sqlite.org/arch.html · pub unknown
  (page updated 2025-05-31) · accessed 2026-09-24 · **high** · class = mechanism.
- The tokenizer calls the parser (not the reverse) explicitly *because it runs faster* and can be
  made threadsafe. — sqlite.org/arch.html · 2025-05-31 · accessed 2026-09-24 · high · class = mechanism.
- Separate B-trees per table and per index, all in **one** disk file. Interface between b-tree and
  pager is `btree.h`. — sqlite.org/arch.html · 2025-05-31 · accessed 2026-09-24 · high · class = mechanism.
- Page size default **4096 B**, any power of two **512–65536**. — sqlite.org/arch.html ·
  accessed 2026-09-24 · high · class = layout.
- Page cache supplies the "rollback and **atomic commit abstraction**" and the file locking; the
  b-tree driver *notifies* the pager when it modifies pages / wants to commit or roll back. This is
  the key separation: durability/atomicity is centralized, so per-page writes stay cheap. —
  sqlite.org/arch.html · accessed 2026-09-24 · high · class = architecture.
- Page numbering from 1; max page number 2^32−2; max database ≈ **281 TB**; minimum database is a
  single 512-byte page. — sqlite.org/fileformat2.html · accessed 2026-09-24 · high · class = limits.
- Header fields that matter to us: page size at offset 16; reserved bytes per page; payload
  fractions; free page list; **suggested cache size**; incremental-vacuum settings; text encoding. —
  sqlite.org/fileformat2.html · accessed 2026-09-24 · high · class = layout.
- Record format (schema layer §2.1) — a *record* is the row body; its b-tree **key** is the rowid
  for ordinary tables, the PRIMARY KEY for `WITHOUT ROWID`. Index keys are the indexed columns
  followed by the row key, so every index entry is unique and self-locating. —
  sqlite.org/fileformat2.html · accessed 2026-09-24 · high · class = layout.
- `WITHOUT ROWID` tables are stored as an index b-tree **whose record is the content**, and
  redundant PK columns are *suppressed* from secondary-index key suffixes. —
  sqlite.org/fileformat2.html · accessed 2026-09-24 · high · class = layout.
- B-tree pages carry cells + freeblocks + a cell pointer array, and cells carry a **payload** that
  spills to overflow pages above a computed fraction of the page. There is an explicit **freelist**
  and a **ptrmap** for auto/incremental vacuum. — sqlite.org/fileformat2.html · accessed
  2026-09-24 · high · class = layout. **Note:** the fetched slice did not include the raw §1.6/§1.7
  numbers; the payload-fraction formula and overflow threshold are *not* quoted here — round 2
  target.
- Interpretation constraint: because an index entry *is* `(indexed cols → row key)`, an UPDATE that
  changes an indexed column **must** delete+reinsert the index entry, but an UPDATE that changes only
  non-indexed payload does not touch any index at all. This is the single most transferable
  structural fact for our own index maintenance. — derived from the above two citations, labelled
  **derived** (medium) · accessed 2026-09-24 · class = inference.

## Leads worth chasing
- The exact cell payload/overflow rule (§1.7) and the freeblock coalescing rule.
- Whether SQLite ever *moves* a row within a page on UPDATE (it does: cell size change → freeblock
  fragmentation → defragment on next write). Need the source-level statement.

## Looked for, did not find
- Any statement about in-place vs copy-on-write at the *row* level; arch.html and the fetched slice of
  fileformat2 stop at the page/cell level.
