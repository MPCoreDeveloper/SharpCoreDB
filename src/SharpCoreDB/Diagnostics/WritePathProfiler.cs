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
    }

    private const int StageCount = 9;

    private static readonly long[] ElapsedTicks = new long[StageCount];
    private static readonly long[] CallCounts = new long[StageCount];
    private static readonly string[] StageNames =
    [
        "validate", "encode", "index-maint", "row-locate", "in-place-patch",
        "engine-write", "wal-append", "wal-flush", "commit",
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

        return Stopwatch.GetTimestamp();
    }

    /// <summary>Records the time spent since <paramref name="startTicks"/> in <paramref name="stage"/>.</summary>
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

        double total = rows.Sum(r => r.TotalMs);
        var sb = new StringBuilder();
        sb.Append("WritePathProfiler: ")
          .Append(total.ToString("F1", CultureInfo.InvariantCulture))
          .AppendLine(" ms measured across stages");
        sb.AppendLine("  stage              total ms     calls   share");

        foreach ((string stage, double ms, long calls) in rows)
        {
            sb.Append("  ")
              .Append(stage.PadRight(16))
              .Append(ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(10))
              .Append(calls.ToString("N0", CultureInfo.InvariantCulture).PadLeft(10))
              .Append((total <= 0 ? 0 : ms * 100.0 / total).ToString("F1", CultureInfo.InvariantCulture).PadLeft(8))
              .AppendLine("%");
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
