# Digest — D3 .NET / runtime accelerators · round 3 · n=1

Sources: `learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardcompressionoptions`
(`?view=net-11.0`), `…/system.io.compression.zstandardencoder`, `…/system.io.compression.zstandarddictionary`,
`…/system.io.compression.zstandardstream`, `…/system.io.compression.zstandarddecompressionoptions`,
`…/system.randomaccess` — all accessed **2026-10-09**. (The frame-format implementation source added in the
same-day follow-up is carried by `…-r3-3.md`, because it is a *format* input rather than an API surface.)

Scope note: rounds 1–2 established *that* .NET 11 ships Zstd, vectored-I/O and runtime async. Round 3
reads the **exact API surface** of the storage-relevant types, because "Zstd is in the BCL" is not an
engineering lever until the object model behind it is known.

## Findings

- **`ZstandardEncoder` is a reusable, span-based one-shot compressor — not just a stream wrapper.**
  Sealed, `IDisposable`. Six constructors: `()`, `(int)`, `(int, int)`, `(ZstandardCompressionOptions)`,
  `(ZstandardDictionary)`, `(ZstandardDictionary, int)`. Members: `Compress(ReadOnlySpan<byte>,
  Span<byte>, int, int, bool)`, `TryCompress(…)` (three overloads), `Flush(Span<byte>, int)`,
  **`GetMaxCompressedLength(long)`**, **`Reset()`**, `SetPrefix(ReadOnlyMemory<byte>)`,
  `SetSourceLength(long)`, `Dispose()`. — MS Learn · rc.1 · accessed 2026-10-09 · **high** (complete
  enumeration of the sealed type's public surface) · class = api surface.
  → *`GetMaxCompressedLength` + a destination span + `Compress` is a poolable, allocation-free block
  codec; `Reset()` is what makes one instance reusable across blocks. Together they remove the
  `MemoryStream` round-trip and the per-block encoder allocation our block compressor performs today.*
- **`ZstandardDictionary` can be created and trained in-box.** Sealed, `IDisposable`; property `Data`;
  methods `Create(ReadOnlySpan<byte>)`, `Create(ReadOnlySpan<byte>, int)`,
  `Train(ReadOnlySpan<byte>, ReadOnlySpan<int>, int)`. — MS Learn · rc.1 · accessed 2026-10-09 ·
  **high** · class = api surface. → *No external `zstd` CLI or P/Invoke is needed to train a dictionary,
  so a dictionary-based mode stays inside the product's "pure .NET, no P/Invoke" constraint.*
- **`ZstandardDecompressionOptions` is the caller-side decompression state.** Sealed, one parameterless
  constructor, exactly two properties: **`Dictionary`** and `MaxWindowLog2`. **No Remarks section** —
  nothing states what a decoder does when the frame's dictionary does not match the one supplied. — MS
  Learn · rc.1 · accessed 2026-10-09 · **high** (complete property list) · class = api surface.
  → *This is the other half of the format ruling: the dictionary is handed to the decoder, never read out
  of the file.*
- **`ZstandardCompressionOptions` is a sealed 12-property options bag.** `AppendChecksum`, `Quality`,
  `MinQuality`, `MaxQuality`, `DefaultQuality`, `WindowLog2`, `MinWindowLog2`, `MaxWindowLog2`,
  `DefaultWindowLog2`, **`TargetBlockSize`**, **`Dictionary`**, `EnableLongDistanceMatching`; one
  parameterless constructor. **The page has no Remarks section** — nothing states which properties
  affect the frame header (and therefore what a *reader* must reproduce) versus only the encoder's
  effort. — MS Learn · rc.1 · accessed 2026-10-09 · **high** for the property list, **unverified** for
- **`ZstandardStream` is seekable and takes encoder/decoder/dictionary objects directly.** Nine
  constructors, including `(Stream, ZstandardCompressionOptions, bool)`, `(Stream, ZstandardEncoder,
  bool)`, `(Stream, ZstandardDecoder, bool)` and `(Stream, CompressionMode, ZstandardDictionary,
  bool)`; members include `Seek(long, SeekOrigin)`, `SetLength(long)` and **`SetSourceLength(long)`**,
  and `CanSeek` is therefore part of the contract. — MS Learn · rc.1 · accessed 2026-10-09 · **high** ·
  class = api surface. → *Seekability does not make a compressed block patchable — reaching a byte
  inside a zstd frame still means decoding the frame — so this does not weaken round 1–2's layout
  conclusion.*
- **Scatter/gather file I/O is a documented first-class API, not a P/Invoke.** `System.RandomAccess`
  exposes `Read`/`Write` and `ReadAsync`/`WriteAsync` in two shapes each: a single
  `Span<byte>`/`ReadOnlySpan<byte>`/`Memory<byte>`/`ReadOnlyMemory<byte>`, **and a list of buffers**
  (`IReadOnlyList<Memory<byte>>` for reads, `IReadOnlyList<ReadOnlyMemory<byte>>` for writes). Class
  summary: *"Provides offset-based APIs for reading and writing files in a thread-safe manner."*
  Remarks: *"Only regular disk files are supported. Unseekable files, like pipes, are not supported."* —
  MS Learn · net-11.0 · accessed 2026-10-09 · **high** for the signatures and the two quoted remarks ·
  class = api surface.
- **Honest limit on the vectored form (a *negative* finding, and it matters).** The page does **not**
  state how many OS calls either form issues; there is no mention of `pwritev`/`WriteFileGather` or of
  batching semantics. "One vectored write = one syscall" is a **hypothesis**, not a citation, and must be
  measured (see the report's Open questions). — methodological · **assumption**.
- **Deployment constraint:** all three Zstd types carry `[UnsupportedOSPlatform("browser")]` and
  `[UnsupportedOSPlatform("wasi")]`. — MS Learn · rc.1 · accessed 2026-10-09 · high · class =
  compatibility. → *Zstd is a desktop/server story; a browser or WASI target must keep a non-Zstd mode
  selectable, which our `BlockCompressionMode` enum already allows.*
- **Prerelease caveat applies to every finding above**, verbatim from the pages: *"Some information
  relates to prerelease product that may be substantially modified before it's released."* — MS Learn ·
  rc.1 · accessed 2026-10-09 · high · class = version/compatibility. → *Re-read all four API pages after
  GA (expected November 2026), the same bar the report's Staleness map already sets for `[9]` and `[5]`.*

## Contradiction / caution worth reporting
Every page in this digest is an **API reference, not a performance claim**: no throughput, latency,
syscall count or allocation figure appears on any of the five. The digest therefore proves *what can be
called*, never *what it wins*. Round 3's predictions are written as falsifiable hypotheses for our own
harness, and none of them may be published as a result.

## Looked for, did not find
- **Syscall count / batching semantics of the vectored `RandomAccess` overloads** — absent; the biggest
  single gap in this round, because it decides whether the write-path lever is real.
- **Whether `ZstandardEncoder.Reset()` preserves the options and dictionary**, and whether an instance
  is thread-confined — `Reset()` and `SetSourceLength` exist with no remarks; semantics unstated.
- **Which frame field carries a dictionary ID**, and what a decoder does without the dictionary — not on
  the `ZstandardDictionary` or `ZstandardCompressionOptions` pages.
- **`ZstandardDecoder`'s own surface** — its existence is confirmed only via the stream constructors;
  the page was not fetched in this round.
- **GC/allocation changes in .NET 11** — still not covered by any fetched slice (unchanged from round 1).

