# PageBased storage — crash safety, checksums and encryption: design note

**Status:** proposal for owner decision (2026-11-17). No code in this note is implemented yet. It covers known issues
§14 (unencrypted pages), §18 (uncommitted pages reaching disk), §19 (torn pages undetected) and §20 (the directory-mode
WAL), and records why §15 (autocommit durability) was fixed without them.

## 1. What holds today, and what does not

Measured by killing a child process (no Dispose, no flush) and reopening, and by damaging files the way a torn write
would (`CrashDurabilityTests`, the investigation recorded in known issues §14–§20):

| Situation | Columnar | PageBased |
|---|---|---|
| Autocommit statement / `ExecuteBatchSQL` that returned, then crash | kept | kept (since §15 was fixed: the page cache is flushed at the statement's end) |
| Committed transaction, then crash | kept | kept |
| Uncommitted transaction, then crash | not applied | not applied — **unless** a dirty page was evicted first (§18) |
| Torn write inside a file | the incomplete trailing record is lost, the rest reads | **rows vanish silently**, writes continue on the damaged page (§19) |
| Power loss after a commit | kept (appends written through, in-place writes synced since §17) | kept under `FullSync` (page file synced at flush) |
| Data at rest | AES-256-GCM per record | **plaintext** (§14) |

The PageBased engine (`PageBasedEngine` → one `PageManager` per table → `table_<id>.pages`, 8 KB pages) writes pages in
place. Its only atomicity mechanism is the in-memory undo images of §13 (`PageManager.BeginUndoScope`), which survive a
rollback but not a crash.

## 2. The page as it is on disk

```
offset 0   TableId      uint32
offset 4   Type         byte
offset 5   RecordCount  uint16
offset 7   FreeSpace    uint16
offset 9   — unused (23 bytes, always written as zero) —
offset 32  data area, growing upwards
...        slot directory at the end: 4 bytes per slot (offset uint16, length uint16; length 0 = deleted since §15's fix)
```

The 23 unused header bytes are the design's main lever: a checksum and a log sequence number fit **without moving the
data area or the slot directory**, so the page format stays readable by the current reader and an old page stays
readable by the new one (all-zero fields mean "written before checksums").

## 3. Torn pages — a checksum per page (§19)

**Proposal.** Header bytes 9–16: `uint64` XxHash64 (System.IO.Hashing) of the page with these 8 bytes zeroed; byte 17:
format flag `0x01` = checksummed. `WritePage` computes it; `ReadPage` verifies it when the flag is set and throws a
`PageCorruptedException` naming the table and page id when it does not match. A page with flag `0x00` (written before)
is read unverified, exactly as today.

- *Cost:* one 8 KB hash per page write and read — XxHash64 runs at several GB/s, i.e. around 1 µs per page; measure on
  the PageBased benchmark (`--engine=pagebased`) and the gate.
- *What it buys:* a torn page becomes a loud error instead of silently missing rows. It does not repair the page —
  that needs §4's log.
- *Compatibility:* none broken; downgrade (unsupported by policy) would read checksummed pages fine, since an old reader
  ignores bytes 9–31.

This piece is independent of the others and could land first.

## 4. Crash atomicity — a page redo log (§18, and the real fix for §15)

**Options considered.**

| Option | How | Pros | Cons |
|---|---|---|---|
| A. Pin dirty pages of an open transaction | Never evict a page that has an undo image | Small change; closes §18 for transactions that fit in the cache | A transaction larger than the cache (1,024 pages = 8 MB by default) must spill or fail; a crash *during* the commit's multi-page flush still tears the transaction |
| B. Page redo log (WAL) | Before a commit's pages are written in place, append their after-images plus a commit record to `table_<id>.wal` and sync it; replay committed records on open; truncate after a checkpoint | Atomic and durable commits, torn-page repair (the log holds a full image), autocommit becomes one sequential append + one sync instead of N random page writes | New file, recovery code, checkpointing; the most work |
| C. Shadow paging | Write changed pages to new locations, then switch a page table atomically | No log replay | Needs a page-table indirection the engine does not have; fragmentation; largest change |

**Recommendation: B, with A as an interim step if wanted.**

Design of B:

1. **Log file** per table: `table_<id>.wal`, records `[LSN uint64][kind byte][page id uint64][length uint32][payload][XxHash64 of record]`, kinds `PageImage` (8 KB after-image) and `Commit` (transaction id). Encrypted with the same key as the pages once §14 lands.
2. **Commit** (`EndBatchUpdate`, `ExecuteBatchSQL`, the autocommit boundary): append the after-image of every dirty page and a `Commit` record, sync the log once — this is the durability point — then write the pages in place lazily (or at once) and stamp each page's LSN (header bytes 18–25).
3. **Eviction during a transaction** may write a dirty page in place only after its after-image is in the log (the
   "write-ahead" rule); with the undo images of §13 the in-memory rollback stays as it is. On a crash, replay restores
   only committed images; an uncommitted page written early is overwritten by the last committed image of that page
   (the log keeps the before-image's committed predecessor) — or, simpler, Option A's pinning keeps uncommitted pages
   out of the file entirely and the log needs no undo.
4. **Recovery on open**: scan the log, collect page images of transactions that have a `Commit` record, and write each
   page whose header LSN is older than the logged one. Torn pages (§3 checksum fails) are repaired from the log when it
   holds their image.
5. **Checkpoint**: after a flush has written and synced all pages up to an LSN, truncate the log. Trigger by size
   (e.g. 16 MB) and on `Dispose`.

*Cost to measure:* autocommit PageBased writes today pay a random 8 KB page write plus an fsync per touched page file
(~750 µs per statement, measured with §15's fix); a log append + one sync is typically cheaper than that, so B may be
faster than today's §15 fix. Batches pay one sequential log write per commit.

## 5. Encryption at rest (§14)

AES-256-GCM per page needs a 12-byte nonce and a 16-byte tag per page — 28 bytes, more than the 23 free header bytes.
Options:

1. **Take 28 bytes from the data area** (usable page 8,160 → 8,132 bytes): a format change — existing pages keep the
   current layout (flag `0x00`), new or rewritten pages use the encrypted layout (flag `0x02`); a background or
   on-open rewrite converts old pages. Same key derivation as the columnar records.
2. **XTS-style length-preserving encryption** (no tag) using the 23 free bytes for a per-page tweak/counter: no layout
   change, but no integrity — the §3 checksum would have to be computed over the ciphertext.

Recommendation: option 1 (authenticated, matches the columnar records' AES-GCM), landed together with the checksum flag
so one format bump covers both. Needs the owner's decision: it is an on-disk format change, and a mixed-format file must
stay readable until it is rewritten.

## 6. The directory-mode `GroupCommitWAL` (§20)

It is created when `UseGroupCommitWal` is set, but nothing writes to it or replays it. Two honest outcomes:

- **Remove it from directory mode** and the WAL settings' claims, since the columnar engine's durability comes from
  written-through appends and §17's syncs, and the PageBased engine would get its own per-table log (§4).
- **Or** make §4's log the implementation behind `UseGroupCommitWal` for directory mode (group commit = several
  transactions' records synced together).

Recommendation: the second — one documented WAL, used by the engine that needs it — but it is the owner's call because
it changes what a public configuration flag means.

## 7. Suggested order and test plan

1. §3 checksums (no format break, independent) — tests: damage a page's tail → `PageCorruptedException` names it; a
   page written before (flag 0) reads unverified; reopen round-trip matrix stays green.
2. Option A pinning (§18 interim) — test: the eviction crash case of the investigation (`tx-uncommitted-then-big-read`)
   leaves the committed state.
3. §4 log + recovery — tests: kill the child process at each step of the commit (after the log sync, between page
   writes) via a test hook; replay restores exactly the committed state; torn page repaired from the log; checkpoint
   truncates.
4. §5 encryption with the format flag — tests: no plaintext in the page file; mixed old/new pages read; rewrite converts.

Every step runs the write-path gate and the PageBased benchmark (`--engine=pagebased`), and adds its crash cases to
`CrashDurabilityTests`.

## 8. Decisions needed from the owner

- §14: approve the page format bump (encrypted layout, flag byte) and the conversion strategy for existing files.
- §20: remove the directory-mode `GroupCommitWAL`, or make it the PageBased log.
- Whether PageBased should be documented as not production-ready (the configuration already says "NOT yet OLTP-ready")
  until §14 and §18 land.
