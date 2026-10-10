# Digest — D3/D4 crypto framing & fair comparison · round 2 · n=2

Sources: `learn.microsoft.com/…/api/system.security.cryptography.aesgcm` (references
`Microsoft.Bcl.Cryptography v11.0.0-rc.1.26425.128`), plus the round-1 pragma table as the comparison
control.

## Findings — the AES-GCM API shape *is* the encryption tax

- `AesGcm` exposes exactly two data operations, both **whole-buffer, single-call**:
  `Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext,
  Span<byte> tag, ReadOnlySpan<byte> associatedData)` and the matching `Decrypt(...)`. —
  MS Learn · accessed 2026-09-24 · **high** · class = API surface.
- The authentication **tag is a separate output buffer** (`TagByteSizes`, `TagSizeInBytes`), and the
  **nonce is per-message input** (`NonceByteSizes`) — so per-record framing means per-record nonce
  and per-record tag. — MS Learn · accessed 2026-09-24 · high · class = API surface.
- **There is no incremental / streaming / seekable update API.** Every mutation of any byte of the
  plaintext requires re-running the whole encryption over the whole plaintext. — derived from the
  complete method list on the page (Constructor/Properties/`Encrypt`/`Decrypt`/`Dispose` only),
  labelled **derived** · **high** (the page enumerates the full public surface) · class = API surface.
- **Consequence, and it is a structural one:** our measured
  `commit-overwrites` **19.5 ms / 1.60 MB (encrypted) vs 3.4 ms / 0.55 MB (raw)** — "3× the bytes"
  — and `in-place-patch` **8.3 ms vs 1.2 ms** at *identical* 175 B/call is **not** an implementation
  defect, it is the **direct consequence of the API contract**. You cannot patch one field of an
  AEAD record; you must re-encrypt the record.
- Therefore **the only way to make encrypted UPDATE cheap is to make the encrypted unit small and
  constant-size** — i.e. the record layout decision (D2: line-pointer indirection + out-of-line
  values + fixed-width slots), exactly as D2 concluded from the storage-engine side. The two
  independent lenses agree, which is the strongest cross-dimension signal in this run.
- Version note: the doc names `Microsoft.Bcl.Cryptography v11.0.0-rc.1.26425.128` — the same
  `26425.128` build as the pinned SDK in `global.json`, i.e. the API on the page is the API we
  compile against. — MS Learn · accessed 2026-09-24 · high · class = version.

## Findings — the fairness control (from D1 n=3, restated as a rule)
- SQLite's own documented tuning surface is large enough to swing its numbers by **>2×**: journal
  mode ("significantly faster in most scenarios"), `synchronous` (the historical `nosync` win was
  1.4–1.7× on write tests), `cache_size`, `mmap_size`, `page_size`, `locking_mode=EXCLUSIVE`
  ("possibly resulting in a small performance increase"), `temp_store`, `wal_autocheckpoint`.
  — sqlite.org/pragma.html · accessed 2026-09-24 · high · class = comparison control.
- `locking_mode=EXCLUSIVE` is the pragma that most resembles our own always-open, single-process
  handle: SQLite can only claim it by giving up multi-process access, which our generator **must
  not** do. This is the concrete place where "we beat tuned SQLite" needs a *shape* qualifier, not
  just a ratio.
- **Rule this run proposes:** every published comparison carries a regime banner naming *both*
  sides' tuning — SQLite's pragma set **and** our `SHARPCOREDB_*` switches — because the repo's
  existing `REGIME:` banner already does half of this and the SQLite half is currently implicit.

## Looked for, did not find
- Any Microsoft guidance on *field-level* or *page-level* encryption granularity. The crypto docs
  stop at the API; the layout decision is ours.
- A vendor-published, current CRUD benchmark from SQLite — there is none (see D1 n=2/n=3).
