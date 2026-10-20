# Digest — D3 .NET / runtime accelerators · round 3 · n=3

Sources: consolidation of r3-1 (API surface, `[17]`–`[22]` in the report) and r3-2 (repo state), plus the
frame-format implementation source `[23]` read in the same-day follow-up — the one place this digest cites a
source directly, because the format cost is the decision it exists to record. Read 2026-10-09.

Purpose: turn the round-3 evidence into the decision the plan needs — **what to do, in what order, and
what each item costs the on-disk format** — and to state the compatibility ruling explicitly, because
three of the levers look free and are not.

## The lever ledger

"Format" = does it change bytes a *reader* must be able to interpret. "Compat" = can an older build
(this product at `net10`, or an earlier `net11` RC) still open the file.

| # | Lever | Where it acts | Status | Format / compat | Falsifiable prediction |
|---:|---|---|---|---|---|
| 1 | List-form `RandomAccess.Write` for prefix + record | `Services/Storage.Append.cs:469-470` (in-place patch → UPDATE) | **open** (two single-buffer calls today) | **none** — same bytes at the same offsets | One syscall per in-place record instead of two; must show up as an UPDATE-cell ratio shift, and if it does not, the "one call = one syscall" assumption is wrong and the item is dropped |
| 2 | Reusable span-based `ZstandardEncoder` + `GetMaxCompressedLength` + pooled output | `Services/BlockCompressor.cs` | **open** (stream path only) | **none** — identical zstd frame, checksum and level unchanged | Removes 2 full buffer copies + 1 stream + 1 encoder allocation per block; measurable as alloc/op on the compression arms, and must not change round-trip bytes |
| 3 | `TargetBlockSize` / `WindowLog2` / `EnableLongDistanceMatching` | same | **open**; property existence verified | **none for readability** (legal frame fields) — but *which* land in the header is undocumented | Needs an A/B on our block sizes; if under ~2 % it is dropped rather than kept "because it is free" |
| 4 | `ZstandardDictionary` (trained) **or** `SetPrefix` | same, and only if a block is still variable after the structural item | **open — format-affecting** | **breaks** old readers: the frame's `Dictionary_ID` flags the requirement `[23]` and .NET needs the dictionary handed back `[22]`, so it must live in our file header behind a magic/version gate | Only worth a format bump if dictionary gain on small, self-similar blocks beats its own header cost and the migration work — to be measured, never assumed |
| 5 | `AesGcm` API surface | `Services/AesGcmEncryption.cs` | **closed — saturated** | n/a | No lever remains; re-opening this is a known waste of a session |
| 6 | Span-based `ParseValue` / prepared-statement binding | row codec + statement binding | **open** | **none** (source-level only) | C# 15 labeled `break`/`continue` can remove scanner sentinels; measure before claiming, per the campaign's own NEXT item |
| 7 | NativeAOT interface dispatch | every row, every arm | **BLOCKED** — VS C++ workload absent (plan §9 row 6) | none | Unchanged from rounds 1–2; not to be re-opened on the strength of this round |

## The compatibility ruling

1. **Exactly one lever in this round touches the file: a Zstd dictionary or prefix (#4).** #1, #2, #3 and
   #6 write the same bytes, a legal frame, or the same IL.
2. **#4 is not backwards compatible, including with an older `net11` RC build of this product**, and the
   mechanism is now sourced on both sides. The zstd frame header carries a **`Dictionary_ID`**, sized by
   the low two bits of `Frame_Header_Descriptor` (1, 2 or 4 bytes; absent when that field is 0), with bit 5
   the `Single_Segment_flag` and bit 3 reserved and rejected when set `[23]`; and the decompression API
   takes the dictionary as *caller-supplied* state — `ZstandardDecompressionOptions` has exactly `Dictionary`
   and `MaxWindowLog2` `[22]`, and the decoder constructors take a `ZstandardDictionary` `[18]`. The
   dictionary is therefore never reconstructed from the file: any dictionary mode is a **new format
   version**, gated the way `BlockCompressionMode.Zstd` already is — a loud `PlatformNotSupportedException`
   on a build that cannot decode it, never a silent misread. *Still open:* the exception a .NET decoder
   raises on a missing or mismatched dictionary (no Remarks section states it `[19][22]`).
3. **The non-format levers stay inside every repo rail**: no durability change (WAL ordering and the
   write offsets are untouched — identical bytes at identical offsets), no encryption change, no
   fixed-width-default flip (brief §8 trap 3). **#1 and #2 may therefore land ahead of the structural
   item**, which is the only differently-ordered conclusion round 3 reaches.

## What this round changes in the round 1–2 report
- **Adds** two zero-format-risk items to `Recommendations` (the ledger's #1/#2) and reorders the
  recommended sequence to put them first.
- **Does not overturn** the layout conclusion: `ZstandardStream`'s seekability does not make a
  compressed block patchable, and no .NET 11 API found in this round lets a variable-length row be
  updated in place without changing the layout.
- **Refuses** to promote any Zstd tuning property (#3) or the dictionary (#4) into a recommendation
  before measurement — round 3's evidence proves the call exists, not that it wins.
- **Leaves** the AesGcm and NativeAOT verdicts exactly as rounds 1–2 left them, with the same reasons.

## Looked for, did not find
- A runtime primitive that removes the need for the structural (stable-slot) item — none.
- Any GC/allocator claim for .NET 11 — the fetched slices still do not cover GC.
- Any statement, in the API pages, quantifying a benefit of the Zstd object model over the stream
  model — the object model has to be measured on our harness, not read off a page.
