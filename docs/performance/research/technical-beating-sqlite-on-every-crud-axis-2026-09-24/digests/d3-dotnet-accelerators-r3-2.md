# Digest — D3 .NET / runtime accelerators · round 3 · n=2

Sources: **this repository** (`src/`, `docs/net11/`), read 2026-10-09 — no external source supports any
statement below. Purpose: separate the levers the product **already ships** from the ones that are
**open**, so round 1–2's recommendations are not re-proposed as if they were new work.

Method: every line citation is read off the file named. No behaviour is inferred from an API's existence.

## Findings — shipped (do not re-recommend)

- **Zstd is live, on the stream path.** `src/SharpCoreDB/Services/BlockCompressor.cs:58` constructs
  `new ZstandardStream(output, new ZstandardCompressionOptions { Quality = …, AppendChecksum = true })`,
  and `docs/net11/UPGRADE.md:20-24` documents the same mapping (`Fastest → 1`, `Optimal → 5`,
  `SmallestSize → 19`, checksum appended so corruption is detectable). — repo · **high** · class =
  repo state. → *Round 2's "in-box Zstd removes the add-a-package objection" is already acted on.*
- **`AesGcm` is saturated in this codebase.** `src/SharpCoreDB/Services/AesGcmEncryption.cs` caches a
  cipher instance per thread (the file records the measured ~0.69 µs per-instance cost it avoids),
  uses `stackalloc` for nonce/tag and `ArrayPool` for ciphertext, and surfaces
  `AesGcm.IsSupported` as `IsHardwareAccelerated` (~line 214). There is no remaining `AesGcm` API to
  call. — repo · **high** · class = repo state. → *Confirms §3.2: the only crypto lever is layout.*
- **`RandomAccess` is already the I/O primitive.** It is used at 15 call sites across
  `src/SharpCoreDB/Services/Storage.Append.cs` and `src/SharpCoreDB/Services/Storage.PageCache.cs`
  (e.g. `Storage.PageCache.cs:93` inside a short-read loop). — repo · **high** · class = repo state.
  → *Round 3 is not "adopt `RandomAccess`"; it is "use the form of it we do not use".*
- **Runtime async is already the default posture** — `global.json` pins `11.0.100-rc.1.26425.128` and the
  project targets `net11.0` / C# 15 preview, so §3.1's runtime-async material is a *measurement*
  question (shallow surfaces), never an adoption question. — repo · **high** · class = repo state.

## Findings — open (the actual levers)

- **One logical write is issued as two adjacent calls.** `src/SharpCoreDB/Services/Storage.Append.cs:459-471`
  (`WriteRecordInPlace`) writes a 4-byte length prefix and then the record:
  `RandomAccess.Write(writeHandle, lengthPrefix, offset)` followed by
  `RandomAccess.Write(writeHandle, record, offset + 4)`. Both arguments are `ReadOnlySpan<byte>`, i.e.
  the single-buffer overload. — repo · **high** · class = repo state. → *This is the exact shape the
  list-taking `Write` overload takes; it is on the in-place patch path, which is the UPDATE cell arms
  B/C are measured short on.*
- **The block codec round-trips through a `MemoryStream` and copies twice per block.**
  `Services/BlockCompressor.cs`: compression writes into a `MemoryStream` and returns `ToArray()`;
  decompression does `data.ToArray()` before reading. — repo · **high** · class = repo state.
  → *Opportunity, **not a defect**: the bytes and the frame are correct; the cost is two full copies and
  two allocations per block, which `ZstandardEncoder` + `GetMaxCompressedLength` + `Reset()` (§n=1)
  addresses directly.*
- **The newer Zstd levers are unused anywhere in `src/`:** no match for `ZstandardDictionary`,
  `ZstandardEncoder`, `TargetBlockSize`, `EnableLongDistanceMatching` or `WindowLog2`. — repo ·
  **high** (absence verified by search over `src/`) · class = repo state.
- **No `Zstd` block is readable by a `net10` build, and it fails loudly.**
  `Services/BlockCompressor.cs:58-61` documents that its `Zstd` mode throws
  `PlatformNotSupportedException` on `net10` rather than misreading. — repo · **high** · class = repo
  state. → *This is the compatibility pattern any new format-affecting compression feature must copy.*
- **NativeAOT dispatch work is still blocked** on the absent VS C++ workload (the campaign's §9 row 6):
  this round found no reason to re-open it, and no new evidence either way. — repo/plan · **high** ·
  class = constraint. → *Round 3 therefore does not address it; it stays `BLOCKED`.*

## Synthesis (one line)
Round 1–2's three structural recommendations stand, and **two levers are cheaper than all of them and
are not format-affecting**: the list-form write at `Append.cs:469-470`, and a reusable span-based Zstd
encoder in `BlockCompressor`. Neither was in the round 1–2 report, because both are visible only when
the API surface (n=1) is read against the call sites (n=2).

## Looked for, did not find
- Any second site that issues one logical write as two calls — only `WriteRecordInPlace` does; the
  page-run and append paths already write contiguously in a single call.
- Any existing pool/reuse for `ZstandardEncoder`-shaped state — none; the only reuse in the area is the
  cached `SafeFileHandle` per path (`Storage.Append.cs:445-452`, `Storage.PageCache.cs:71-87`).
- An `IStorage`/`IStorageEngine` seam that would let the file-format writer choose the write form
  per backend — `RandomAccess` is used directly in the two files above, so the change is local.
