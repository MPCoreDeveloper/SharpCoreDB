// <copyright file="WritePathProfiler.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Diagnostics;

using System.Diagnostics;
using System.Globalization;
using System.Text;

/// <summary>
/// Opt-in, per-stage timings for the write path. The plan behind this is
/// <c>docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md</c> §2: before optimizing INSERT/UPDATE we
/// have to know whether the time goes into row encoding, index maintenance, the engine write or the
/// WAL flush — those have completely different fixes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zero cost when disabled.</b> When <see cref="Enabled"/> is false, <see cref="Stamp"/> returns 0
/// and <see cref="Add"/> returns immediately, so no timestamp is even read.
/// </para>
/// <para>
/// Enable in-process with <see cref="Enable"/>, or for a whole run with the environment variable
/// <c>SHARPCOREDB_WRITE_PROFILE=1</c> (probed lazily). Counters are per process and deliberately
/// simple: this is a diagnostic, not a metrics system.
/// </para>
/// </remarks>
public static class WritePathProfiler
{
    /// <summary>The stages the write path is measured in.</summary>
    public enum Stage
    {
        /// <summary>Row validation, defaults, auto-generation and NOT-NULL checks.</summary>
        Validate = 0,

        /// <summary>Encoding a row to its on-disk byte form.</summary>
        Encode = 1,

        /// <summary>Hash/B-tree index insertion or removal for a written row.</summary>
        IndexMaintenance = 2,

        /// <summary>Locating a row to update (index lookup / WHERE evaluation).</summary>
        RowLocate = 3,

        /// <summary>Attempting to patch fixed-width fields in place instead of appending a version.</summary>
        InPlacePatch = 4,

        /// <summary>The storage-engine call that actually writes (append / page write).</summary>
        EngineWrite = 5,

        /// <summary>Appending to the write-ahead log.</summary>
        WalAppend = 6,

        /// <summary>Flushing/fsync-ing the write-ahead log or the transaction buffer.</summary>
        WalFlush = 7,

        /// <summary>Committing a transaction.</summary>
        Commit = 8,

        /// <summary>
        /// Parsing a SQL statement and resolving its execution plan. Instrumented on the batch dispatcher
        /// (2026-09-14) because a stage report previously showed only the work *inside* the table, so its
        /// total was never wall time — the missing share was parse/plan/dispatch.
        /// </summary>
        Parse = 9,

        /// <summary>
        /// Decoding the indexed columns of a row to compute the keys for index maintenance. Separated from
        /// <see cref="IndexMaintenance"/> (2026-09-14) because a DELETE measured 82% in that bucket, and the
        /// question "is it the decode or the removal?" decides the fix: the decode of a TEXT column resolves
        /// an overflow-arena block (and decrypts it at rest), while the removal is a hashed bucket update.
        /// </summary>
        IndexDecode = 10,

        /// <summary>
        /// The whole <c>OverflowArena.Write</c> call: free-list claim, offset allocation, append and cache
        /// record, under the arena gate. Added (2026-09-15) because the multi-row INSERT profiler attributed
        /// 100% of its time to the coarse validate-and-serialize stamps, and the fixed-width layout routes
        /// every variable-length value through the arena — so this is the stage that says whether the arena
        /// is the cost.
        /// </summary>
        ArenaWrite = 11,

        /// <summary>
        /// The <c>IStorage.AppendBytes</c> call inside <see cref="ArenaWrite"/> — the part measured at
        /// 477.97 µs/record because it opens the file with <c>FileOptions.WriteThrough</c> per call.
        /// Separated from the rest of <see cref="ArenaWrite"/> so the free-list/lock/cache share of an arena
        /// write is visible without it.
        /// </summary>
        ArenaAppend = 12,

        /// <summary>
        /// Loading the arena file into the payload cache (<c>OverflowArena.EnsureLoaded</c>). It is guarded by
        /// a <c>_loaded</c> flag and so should appear once per arena instance — instrumented explicitly to
        /// prove that, because an O(arena) step per statement would explain the per-statement cost the
        /// multi-row INSERT benchmark measures, and "should be once" is not "is once".
        /// </summary>
        ArenaLoad = 13,

        /// <summary>
        /// Row validation alone — defaults, auto-generation, NOT NULL and type coercion — with serialization
        /// excluded. Added (2026-09-15) because <see cref="Validate"/> covered both and therefore could not
        /// answer which half of a 1.5 ms/row fixed-width INSERT was the cost.
        /// </summary>
        ValidateOnly = 14,

        /// <summary>
        /// Assembling a row dictionary from parsed SQL literals — the literal-to-typed-value conversion plus
        /// the dictionary writes — on the SQL INSERT path. Added (2026-09-15) as the ONE stage that wiring the
        /// batch-INSERT path needed beyond the ones that already existed: the statement-level work had no
        /// attribution, and putting it under <see cref="Parse"/> would have merged two different costs (the
        /// one-pass text scan and the per-row, per-column conversion) under a single number.
        /// </summary>
        RowBuild = 15,

        /// <summary>
        /// Non-unique hash-index maintenance — one <c>HashIndex.AddBatchKeys</c> call per loaded index per
        /// batch. Split out of <see cref="IndexMaintenance"/> (2026-09-15) because that stage at 18.2% of a
        /// multi-row INSERT covered three unrelated things (the per-row PK B-tree insert, this, and the
        /// B-tree bulk index) and the largest of them cannot be optimised without knowing which it is.
        /// </summary>
        HashIndexMaint = 16,

        /// <summary>
        /// Validating a SQL statement before dispatch — the per-statement security and parameter check.
        /// Added (2026-09-15) after a standalone-statement run measured the storage write at 0.8 µs and the WAL
        /// at 0.02 µs while ~75 % of a warm ~60 µs/statement stayed unattributed; this is one of the two phases
        /// below the table that had no stamp at all.
        /// </summary>
        StatementValidate = 17,

        /// <summary>
        /// The per-statement dispatch below classification: acquiring the write lock, fetching the shared parser
        /// and handing the statement to it. Added (2026-09-15) as the outer stamp for the last bucket on the
        /// standalone-statement shape — with `encode`, `parse`, `engine-write`, `row-build`, `validate`,
        /// `stmt-validate` and `wal-append` all attributed, ~31 µs of a 39.5 µs statement was still unaccounted
        /// for and the remaining candidates were lock acquisition plus the per-statement bookkeeping around the
        /// parser. Nested stages (`parse`) are included in it, so the delta between this and them is the point.
        /// </summary>
        Dispatch = 18,

        /// <summary>
        /// The <c>Table.InsertBatch</c> call on the SQL multi-row <c>INSERT … VALUES</c> path — and therefore the
        /// bracket around everything the table does for one statement. Added (2026-09-16) to split the last hole
        /// on that shape: with <see cref="Dispatch"/> (the statement envelope) and this (the table envelope) both
        /// stamped, each delta names its own residual — <c>dispatch</c> − (<c>table-batch</c> + <c>parse</c> +
        /// <c>row-build</c> + <c>stmt-validate</c>) is the statement glue, and <c>table-batch</c> − (<c>encode</c> +
        /// <c>index-maint</c> + <c>engine-write</c> + <c>commit</c> + <c>row-locate</c>) is the table-side glue
        /// (lock acquisition, the batch PK probe, the positions array, the cached-row-count update). Its children
        /// nest inside it, exactly as <see cref="Validate"/> nests <see cref="ValidateOnly"/> and
        /// <see cref="Encode"/>.
        /// </summary>
        TableBatch = 19,

        /// <summary>
        /// Deciding what kind of statement this is — the parser's statement classification and the dispatcher's
        /// post-call schema-change test. Added (2026-09-16) after the single-row-statement profile showed
        /// <c>dispatch</c> at 36.5 µs/statement with 26.2 µs of that unaccounted for: the dispatcher's
        /// <c>IsSchemaChangingCommand(sql)</c> and metadata flags run *after* the dispatch stamp closes, and the
        /// parser's own classification (prefix tests plus its regex fallbacks) runs inside <c>Execute</c> without a
        /// stamp. Both answer the same question, so they share one stage.
        /// </summary>
        Classify = 20,

        /// <summary>
        /// Tokenising a statement into its parts. Added (2026-09-16) alongside <see cref="Classify"/> to split the
        /// remainder of the single-row-statement profile: <c>Classify</c> measured 0.03 µs/statement and is excluded,
        /// so tokenisation (a whole-statement <c>Split</c> plus the query-cache lookup) and the word extraction inside
        /// <c>ExecuteInternal</c> are the remaining candidates for the un-stamped parser time.
        /// </summary>
        StmtSplit = 21,

        /// <summary>
        /// The per-call write-through <c>FileStream</c> open inside <c>Storage.AppendBytes</c>. Added (2026-09-16)
        /// because the write-through append path allocates ~64 KB per call in the multi-row arm's own allocation
        /// counter (76,467 B/row over a timed, profiler-free pass) and the code shows only a 4096-byte buffer, so the
        /// question this settles is whether the cost is the open or the writes.
        /// </summary>
        AppendOpen = 22,

        /// <summary>
        /// The length-prefix write, payload write and page-cache eviction that follow the open in
        /// <c>Storage.AppendBytes</c>; its sibling above covers the open itself.
        /// </summary>
        AppendWrite = 23,

        /// <summary>
        /// The PageBased engine's record read (<c>PageBasedEngine.Read</c>): decode the storage reference,
        /// resolve the page manager and copy the record bytes out of the page. Added (2026-09-21, plan §9
        /// priority 3) because the PageBased UPDATE profile attributed only ~11 % of its wall time and
        /// <c>row-locate</c> fired <b>once</b> for 10,000 updates — so the read the update path performs per
        /// row was invisible, and "is the page read the cost?" could not be answered from the report.
        /// </summary>
        PageRead = 24,

        /// <summary>
        /// The PageBased engine's record update (<c>PageBasedEngine.Update</c>): decode the storage reference,
        /// resolve the page manager and write the record into its page (in place, within-page relocation, or a
        /// cross-page relocation whose new reference the caller must honour). Split from
        /// <see cref="EngineWrite"/> so the page write's own share of a 4.4×-behind UPDATE column is measurable
        /// separately from the driver's per-row work around it.
        /// </summary>
        PageUpdate = 25,

        /// <summary>
        /// Building the SQL statement text for a batch in the harness/statement layer (the
        /// <c>string.Format</c> loop that produces the 10,000 <c>UPDATE … WHERE id = ?</c> strings). Added
        /// (2026-09-21) because that loop runs <b>inside</b> the profiled and timed window for the fair-PK DML
        /// arms, so leaving it unstamped charged harness text formatting to the engine.
        /// </summary>
        StmtBuild = 26,

        /// <summary>
        /// Decoding an existing record's bytes back into a row (<c>DeserializeRowFromSpan</c>) as the update
        /// path's first per-row step. Added (2026-09-21) because on the PageBased arm this ran once per row
        /// with no stamp while 84 % of the UPDATE wall time was unattributed.
        /// </summary>
        RowDecode = 27,

        /// <summary>
        /// The whole-file snapshot the per-row UPDATE reads are served from (<c>Table.CRUD.cs</c>'s
        /// <c>TryLoadWholeFileForRowAccess</c>). Added (2026-09-21, plan §5.5) because it runs once per batch
        /// UPDATE on the PK-less route and allocates the entire data file, so a per-operation cost was carrying
        /// batch-level invisibility — the same class of blind spot the <c>row-decode</c> stamp closed on the
        /// PageBased arm.
        /// </summary>
        RowSnapshot = 28,

        /// <summary>
        /// The per-operation row locate on the hash-predicate UPDATE route: the registered-index lookup plus the
        /// record read/slice for the matched position (<c>Table.CRUD.cs:2344-2390</c>). Added (2026-09-21, plan
        /// §5.5) to answer §5.3's question — what <c>UpdateMultiple</c>'s per-operation work actually is — because
        /// the only pre-existing <c>row-locate</c> stamp on that route covers the *batch-level* contiguous
        /// attempt, which never applies to a table without a primary key.
        /// </summary>
        RowLocateIndex = 29,

        /// <summary>
        /// The <c>storage.FlushTransactionBuffer()</c> half of the batch-commit pair
        /// (<c>Database.Batch.cs:1185-1189</c>), split out of <see cref="Commit"/> (2026-09-21) because
        /// <c>commit</c> is the largest single stage on the encrypted default-job UPDATE arm — one call whose cost
        /// ranges from 5.7 ms to 32.4 ms across reps against 4.8-12.8 ms for the same stamp on the plaintext arm,
        /// so the two calls it covers have to be separated before either can be attacked. <see cref="Commit"/>
        /// now measures <c>CommitSync()</c> alone (the buffered in-place overwrite flush lives inside it,
        /// <c>Storage.Append.cs:1310-1338</c>).
        /// </summary>
        CommitBuffer = 30,

        /// <summary>
        /// The buffered in-place overwrite flush inside <c>FlushBufferedAppendsAndOverwrites</c>
        /// (<c>Storage.Append.cs:1211</c> → <c>FlushBufferedOverwrites</c>), split out (2026-09-21) because the
        /// commit is the largest single stage on the encrypted default-job UPDATE arm and this is where a
        /// 10,000-row UPDATE batch's write-behind records are actually written: either the batched page path
        /// (<c>TryFlushBufferedOverwritesBatched</c>) or two <c>WriteRecordInPlace</c> syscalls per row.
        /// </summary>
        CommitOverwrites = 31,

        /// <summary>
        /// The *preparation* half of the buffered-overwrite flush: collecting the valid entries, sorting them by
        /// offset and bucketing them per storage page (`Storage.Append.cs`'s <c>TryFlushBufferedOverwritesBatched</c>).
        /// Split out (2026-09-21) because coalescing the I/O loop's page writes changed nothing (31.2 → 29.8 ms
        /// while 640 syscalls became 4), which leaves whatever both shapes do identically — this block is the only
        /// per-entry work outside the I/O loop, over 10,000 entries.
        /// </summary>
        CommitOverwritesPrep = 32,

        /// <summary>
        /// The <c>RandomAccess.Write</c> half of the buffered-overwrite flush's page loop (`Storage.Append.cs`,
        /// `FlushOverwritePages`), split out (2026-09-21) so the read half can be derived as
        /// <see cref="CommitOverwrites"/> − <see cref="CommitOverwritesPrep"/> − this: the write handle is cached and
        /// opened with <c>FileOptions.None</c>, so write-through is not available as an explanation and the split has
        /// to be measured rather than assumed.
        /// </summary>
        CommitOverwritesWrite = 33,
    }

    private const int StageCount = 34;

    private static readonly long[] ElapsedTicks = new long[StageCount];
    private static readonly long[] CallCounts = new long[StageCount];

    /// <summary>
    /// Allocated bytes attributed to each stage. Measured with
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> over the same regions as the timings, because the
    /// multi-row INSERT was measured allocating <b>6.2 KB of garbage per row</b> (124 MB per 20,000-row
    /// pass, 17 gen0 collections) while the stages involved only accounted for ~1.8 KB — and every
    /// hand-measured suspect for the remainder (the PK key's <c>ToString</c>, the per-key index list, the
    /// UTF-8 key buffer, per-record encryption) measured small or nil. Adding bytes to the existing stamps
    /// is what turns that into attribution.
    /// </summary>
    private static readonly long[] AllocBytes = new long[StageCount];

    /// <summary>
    /// Per-thread stack of allocation checkpoints, pushed by <see cref="Stamp"/> and popped by
    /// <see cref="Add"/>. A stack rather than a single slot because stages nest (<c>validate</c> contains
    /// <c>encode</c> contains the arena stages), and each <see cref="Add"/> must close the checkpoint that
    /// its own <see cref="Stamp"/> opened. Every call site is Stamp→Add inside one method on one thread, so
    /// the pairing is LIFO; <see cref="Report"/> surfaces a leftover depth rather than hiding a mismatch.
    /// </summary>
    [ThreadStatic]
    private static long[]? _allocCheckpoints;

    [ThreadStatic]
    private static int _allocDepth;
    private static readonly string[] StageNames =
    [
        "validate", "encode", "index-maint", "row-locate", "in-place-patch",
        "engine-write", "wal-append", "wal-flush", "commit", "parse", "index-decode",
        "arena-write", "arena-append", "arena-load", "validate-only", "row-build",
        "hash-index", "stmt-validate", "dispatch", "table-batch", "classify", "stmt-split",
        "append-open", "append-write",
        "page-read", "page-update",
        "stmt-build", "row-decode",
        "row-snapshot", "row-locate-index",
        "commit-buffer",
        "commit-overwrites",
        "commit-ovw-prep",
        "commit-ovw-write",
    ];

    private static int _enabled;
    private static int _probed;
    private static int _explicitlyDisabled;

    /// <summary>Gets a value indicating whether accumulation is active.</summary>
    public static bool Enabled => Volatile.Read(ref _enabled) != 0;

    /// <summary>Starts accumulating stage timings.</summary>
    public static void Enable()
    {
        Volatile.Write(ref _explicitlyDisabled, 0);
        Volatile.Write(ref _enabled, 1);
    }

    /// <summary>
    /// Stops accumulating. Counters keep their values until <see cref="Reset"/>. An explicit disable
    /// wins over the <c>SHARPCOREDB_WRITE_PROFILE</c> environment variable: otherwise the next
    /// <see cref="Stamp"/> silently re-armed the profiler, so "Disable" meant "stop until the next
    /// write" — which broke the disable assertion in <c>WritePathProfilerTests</c> for anyone running
    /// the suite with that variable set.
    /// </summary>
    public static void Disable()
    {
        Volatile.Write(ref _enabled, 0);
        Volatile.Write(ref _explicitlyDisabled, 1);
    }

    /// <summary>Clears every counter.</summary>
    public static void Reset()
    {
        Array.Clear(ElapsedTicks);
        Array.Clear(CallCounts);
        Array.Clear(AllocBytes);
        _allocDepth = 0; // drop any checkpoint a stage opened and never closed
    }

    /// <summary>
    /// Takes a start timestamp for a stage, or returns 0 when profiling is off. Pass the result to
    /// <see cref="Add"/> when the stage completes; a 0 start is ignored there, so call sites need no
    /// branch of their own.
    /// </summary>
    public static long Stamp()
    {
        if (Volatile.Read(ref _enabled) == 0 &&
            (Volatile.Read(ref _explicitlyDisabled) != 0 || !TryAutoEnableFromEnvironment()))
        {
            return 0L;
        }

        // Allocation checkpoint for this stage, taken at the same moment as the timestamp so the two can
        // never disagree about which stage is open. The buffer grows with the stage nesting depth, never per
        // call, so this allocates nothing on the hot path.
        var checkpoints = _allocCheckpoints;
        if (checkpoints is null)
        {
            checkpoints = new long[64];
            _allocCheckpoints = checkpoints;
        }
        else if (_allocDepth == checkpoints.Length)
        {
            Array.Resize(ref checkpoints, checkpoints.Length * 2);
            _allocCheckpoints = checkpoints;
        }

        checkpoints[_allocDepth++] = GC.GetAllocatedBytesForCurrentThread();
        return Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Records the time and the allocated bytes spent since <paramref name="startTicks"/> in
    /// <paramref name="stage"/>, and closes the allocation checkpoint that <see cref="Stamp"/> opened.
    /// </summary>
    public static void Add(Stage stage, long startTicks)
    {
        if (startTicks == 0L || Volatile.Read(ref _enabled) == 0)
        {
            return;
        }

        int index = (int)stage;
        if ((uint)index >= StageCount)
        {
            return;
        }

        Interlocked.Add(ref ElapsedTicks[index], Stopwatch.GetTimestamp() - startTicks);
        Interlocked.Increment(ref CallCounts[index]);

        // Close this stage's allocation checkpoint. GetAllocatedBytesForCurrentThread reads a thread-local
        // counter, so only the calling thread's allocations are attributed: exact for the serial write path,
        // and an under-count for the Parallel.For batch serialisation — which is why the column is labelled
        // "B/call" and read as a floor rather than a total.
        if (_allocDepth > 0 && _allocCheckpoints is not null)
        {
            long allocStart = _allocCheckpoints[--_allocDepth];
            long delta = GC.GetAllocatedBytesForCurrentThread() - allocStart;
            if (delta > 0)
            {
                Interlocked.Add(ref AllocBytes[index], delta);
            }
        }
    }

    /// <summary>Per-stage totals: elapsed milliseconds and call count, in stage order.</summary>
    public static IReadOnlyList<(string Stage, double TotalMs, long Calls)> Snapshot()
    {
        var rows = new List<(string, double, long)>(StageCount);
        for (int i = 0; i < StageCount; i++)
        {
            rows.Add((
                StageNames[i],
                Volatile.Read(ref ElapsedTicks[i]) * 1000.0 / Stopwatch.Frequency,
                Volatile.Read(ref CallCounts[i])));
        }

        return rows;
    }

    /// <summary>
    /// Per-stage allocated bytes and call count, in stage order. Kept separate from <see cref="Snapshot"/>
    /// so that method's tuple shape — and every existing caller — stays unchanged.
    /// </summary>
    public static IReadOnlyList<(string Stage, long AllocBytes, long Calls)> AllocationSnapshot()
    {
        var rows = new List<(string, long, long)>(StageCount);
        for (int i = 0; i < StageCount; i++)
        {
            rows.Add((
                StageNames[i],
                Volatile.Read(ref AllocBytes[i]),
                Volatile.Read(ref CallCounts[i])));
        }

        return rows;
    }

    /// <summary>
    /// Human-readable table of the accumulated stages, largest total first, with each stage's share of
    /// the measured total. Stages that were never hit are omitted.
    /// </summary>
    public static string Report()
    {
        var rows = Snapshot()
            .Where(r => r.Calls > 0)
            .OrderByDescending(r => r.TotalMs)
            .ToList();

        if (rows.Count == 0)
        {
            return "WritePathProfiler: no stages recorded (is it enabled?).";
        }

        var allocByStage = AllocationSnapshot().ToDictionary(r => r.Stage, r => r.AllocBytes);

        double total = rows.Sum(r => r.TotalMs);
        var sb = new StringBuilder();
        sb.Append("WritePathProfiler: ")
          .Append(total.ToString("F1", CultureInfo.InvariantCulture))
          .AppendLine(" ms measured across stages");
        sb.AppendLine("  stage              total ms     calls   share     alloc MB      B/call");

        foreach ((string stage, double ms, long calls) in rows)
        {
            long alloc = allocByStage.TryGetValue(stage, out var bytes) ? bytes : 0;
            sb.Append("  ")
              .Append(stage.PadRight(16))
              .Append(ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(10))
              .Append(calls.ToString("N0", CultureInfo.InvariantCulture).PadLeft(10))
              .Append((total <= 0 ? 0 : ms * 100.0 / total).ToString("F1", CultureInfo.InvariantCulture).PadLeft(8))
              .Append('%')
              .Append((alloc / (1024.0 * 1024.0)).ToString("N1", CultureInfo.InvariantCulture).PadLeft(13))
              .Append((calls <= 0 ? 0 : alloc / calls).ToString("N0", CultureInfo.InvariantCulture).PadLeft(12))
              .AppendLine();
        }

        // A non-zero depth means a Stamp had no matching Add on this thread, so the byte column is short by
        // that stage's allocation. Say so rather than silently mis-attributing the next stage's bytes.
        if (_allocDepth != 0)
        {
            sb.Append("  ⚠ allocation checkpoints left open: ").Append(_allocDepth)
              .AppendLine(" — some Stamp had no matching Add on this thread");
        }

        return sb.ToString();
    }

    private static bool TryAutoEnableFromEnvironment()
    {
        if (Volatile.Read(ref _probed) != 0)
        {
            return false;
        }

        Volatile.Write(ref _probed, 1);
        string? value = Environment.GetEnvironmentVariable("SHARPCOREDB_WRITE_PROFILE");
        if (!string.IsNullOrEmpty(value) && value != "0" && !value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            Enable();
            return true;
        }

        return false;
    }
}
