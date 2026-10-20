# Digest — D2 Record layout & in-place update · round 2 · n=1

Sources: `postgresql.org/docs/current/storage-page-layout.html` (PostgreSQL 18),
`…/storage-toast.html`, `…/storage-hot.html`

## Findings — the three mechanisms that matter to us

### 1. Indirected slots: the page holds `(offset, length)` pointers, not fixed rows
- A PostgreSQL page has five parts; **`ItemIdData` is "Array of item identifiers pointing to the
  actual items. Each entry is an (offset, length) pair. 4 bytes per item."** Free space sits between
  the pointer array (grows from the start) and the items (grows from the end); `pd_lower` /
  `pd_upper` / `pd_special` mark the boundaries. — postgresql.org · PostgreSQL 18 · accessed
  2026-09-24 · **high** · class = layout.
- **Why this is the single most important fact in this round:** the *pointer* is stable while the
  *item bytes* may move within the page (or be rewritten at a different offset) without any index or
  external reference becoming stale. Indexes and other pages reference the **item identifier**, not a
  byte offset. — postgresql.org · accessed 2026-09-24 · high · class = layout (**derived**
  consequence, labelled).
- Row header is **23 bytes** on most machines + an optional **null bitmap of one bit per column** +
  optional OID; user data starts at `t_hoff`, aligned to `MAXALIGN`. Column offsets are *not* stored
  — they are recomputed by walking `attlen`/`attalign` from `pg_attribute`, and the docs note "there
  is no way to directly get a particular attribute, **except when there are only fixed width fields
  and no null values**." — postgresql.org · accessed 2026-09-24 · **high** · class = layout.
  *Direct analogue: our `TryOverwriteFieldsInPlaceActual` has exactly the documented weakness — it
  must walk the encoded fields to find an offset unless the layout is fixed-width.*

### 2. Out-of-line storage keeps the in-page tuple small and stable
- PostgreSQL requires tuples not to span pages, so oversized values are "compressed and/or broken up
  into multiple physical rows" (TOAST), transparently. — postgresql.org · accessed 2026-09-24 ·
  **high** · class = mechanism.
- The `varlena` length word has **two encodings**: a 4-byte header (4-byte aligned), and a
  **1-byte header for values shorter than 127 bytes**, which is *not* aligned — "this omission of
  alignment padding provides additional space savings that is significant compared to short values."
  — postgresql.org · accessed 2026-09-24 · **high** · class = layout. *Transferable: a
  size-class-aware header is a real, cheap win for short-string-heavy rows.*
- An out-of-line value is stored as a **pointer datum** (no data inline), with the pointer type and
### 3. HOT = the update optimization, and its two exact preconditions
- HOT exists because "updates require new versions of rows to be added to tables. This can also
  require new index entries for each updated row, and removal of old versions of rows and their
  index entries can be expensive." — postgresql.org · accessed 2026-09-24 · **high** · class = mechanism.
- HOT **qualifies only when**: (a) "the update does not modify any columns referenced by the table's
  indexes", and (b) "there is **sufficient free space on the page** containing the old row". —
  postgresql.org · accessed 2026-09-24 · **high** · class = mechanism.
- When it qualifies: **no new index entries** are needed, and intermediate row versions can be
  removed during normal operation instead of by periodic VACUUM. Mechanism: "Indexes always refer to
  the page item identifier of the original row version. The tuple data associated with that row
  version is removed, and its item identifier is converted to a **redirect** that points to the
  oldest version that may still be visible to some concurrent transaction." — postgresql.org ·
  accessed 2026-09-24 · **high** · class = mechanism.
- Mitigation for the free-space precondition: lower the table's `fillfactor`; and "if you don't, HOT
  updates will still happen because new rows will naturally migrate to new pages". Observability:
  `pg_stat_all_tables` exposes HOT vs non-HOT update counts. — postgresql.org · accessed 2026-09-24 ·
  high · class = practice.

## Derived (medium) — mapped onto our code
1. **We already have the pointer half.** Our fixed-width Columnar layout gives a stable storage
   reference and `OverwriteRecordAtSameLength`; the missing half for the *default* shape is exactly
   PostgreSQL's `ItemIdData`: an indirection so a variable-length row's *bytes* can relocate while
   its *reference* stays valid. Today our variable-length UPDATE relocates by **appending** and
   re-pointing indexes (`UpdateColumnarRow` → `engine.Insert`), which is the LSM behaviour, and it is
   precisely the 0.24×/0.31× default-shape gap.
2. **HOT's condition (a) is directly actionable for us**: an UPDATE that touches no indexed column
   should not pay index maintenance at all. Our delete paths already try to be key-only; the update
   side should branch on "does any updated column participate in a loaded index?" *before* it touches
   any index structure.
3. **HOT's condition (b) says reserve free space.** SQLite packs pages; PostgreSQL deliberately
   leaves slack (`fillfactor`). For a write-heavy profile that slack is a throughput feature — and it
   is a knob we do not currently have.
4. **TOAST answers "variable-length fields make the record size unstable".** Combined with (1), a
   record whose only variable part is an off-page pointer has a **constant in-page size**, which
   makes the in-place patch — and therefore the per-record AEAD re-encryption — O(1) with constant
   cost.

## Leads worth chasing
- The `fillfactor` default and its tuned range for write-heavy tables (round 3 if the slack idea
  survives).
- Whether SQLite has any equivalent of HOT's indexed-column test in its `UPDATE` code path.

## Looked for, did not find
- A vendor-published number for HOT's speedup. PostgreSQL ships the *mechanism* and the
  `pg_stat_all_tables` counters, not a headline figure — so any "HOT is N× faster" claim would be
  ours to measure, not theirs to cite.
