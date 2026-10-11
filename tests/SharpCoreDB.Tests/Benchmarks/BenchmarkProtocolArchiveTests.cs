namespace SharpCoreDB.Tests.Benchmarks;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

/// <summary>
/// Guards the protocol an archived benchmark run <i>claims</i> in its JSON, not the numbers themselves.
/// <para>
/// Why this exists (harness defect found 2026-10-11 while publishing the current fair-PK numbers):
/// <c>--pk</c> measures a median of <c>SHARPCOREDB_BENCH_REPS</c> runs (default 3) after
/// <c>SHARPCOREDB_WARMUP_REPS</c> discarded reps (default 8), and archives each arm as
/// <c>BenchmarkResult</c>. <c>RunPkMedian</c> built its median object without stamping <c>Reps</c>, so
/// every <c>pk_comparative_*.json</c> — the archive the performance plan publishes beside the fair-PK
/// table (§2 deliverable 1) so a reader can reproduce the protocol — reported <c>"Reps": 1</c>: it
/// described a 3-rep median as a single-shot reading. <c>BenchmarkResult.Reps</c> exists for exactly the
/// opposite purpose ("so a reader can tell a median from a single-shot reading without the console log").
/// </para>
/// <para>
/// Process-level on purpose: the harness is a standalone Exe
/// (<c>tests/benchmarks/SharpCoreDB.Benchmarks.Comparative</c>) that no test project references and that
/// <c>SharpCoreDB.CI.slnf</c> does not build, so this test is tagged <c>Manual</c> and skips when the
/// executable is absent rather than failing every CI run that never builds it. Run it locally after
/// <c>dotnet build tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -c Release -f net11.0</c>.
/// </para>
/// </summary>
[Trait("Category", "Manual")]
public sealed class BenchmarkProtocolArchiveTests
{
    /// <summary>
    /// The archived rep count must equal the rep count the run was asked for. Red on the pre-fix binary
    /// (all arms read <c>Reps: 1</c> for a 2-rep run), green once <c>RunPkMedian</c> stamps it.
    /// </summary>
    [Fact]
    public void PkComparisonArchive_RecordsTheMeasuredRepCount_NotTheSingleShotDefault()
    {
        // Arrange
        const int reps = 2;
        var root = FindRepositoryRoot();
        var executable = root is null ? null : FindHarnessExecutable(root);
        if (root is null || executable is null)
        {
            Assert.Skip("SharpCoreDB.Benchmarks.Comparative is not built; build it in Release/net11.0 to run this guard.");
        }

        var resultsDir = HarnessResultsDirectory(executable!);
        Directory.CreateDirectory(resultsDir);
        var startedAt = DateTime.UtcNow;
        var tempDir = Path.Combine(Path.GetTempPath(), $"SCDB_BenchProto_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var output = new List<string>();
        try
        {
            using var process = StartHarness(executable!, tempDir, reps, output);
            var exited = process.WaitForExit(TimeSpan.FromMinutes(5));
            Assert.True(exited, $"The benchmark harness did not finish within 5 minutes:{Environment.NewLine}{Joined(output)}");

            var archive = NewestArchive(resultsDir, startedAt);
            Assert.True(
                archive is not null,
                $"No pk_comparative_*.json was written after {startedAt:O} into {resultsDir}. Harness output:{Environment.NewLine}{Joined(output)}");

            // Assert — every archived arm carries the rep count it was measured over.
            using var document = JsonDocument.Parse(File.ReadAllText(archive!.FullName));
            var arms = document.RootElement.EnumerateObject().ToList();
            Assert.NotEmpty(arms);

            foreach (var arm in arms)
            {
                Assert.True(
                    arm.Value.TryGetProperty("Reps", out var archivedReps),
                    $"Arm '{arm.Name}' in {archive.Name} has no Reps field, so a reader cannot tell a median from a single shot.");
                Assert.True(
                    archivedReps.GetInt32() == reps,
                    $"Arm '{arm.Name}' in {archive.Name} claims Reps={archivedReps.GetInt32()}, but the run measured {reps} reps.");
            }
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    /// <summary>
    /// The arm the harness reports as <c>legacy variable-length</c> must actually run variable-length
    /// records.
    /// <para>
    /// Red on the pre-fix binary: <c>BuildConfig</c> set <c>AutoFixedWidthRecords = !fixedWidth</c>, so the
    /// non-forced arm kept the product's auto resolver — and that resolver turns any NEW table declaring a
    /// PRIMARY KEY into fixed-width records (<c>SqlParser.DDL.cs</c>). The arm the fair-PK table calls
    /// "legacy variable-length" therefore measured the *same* layout as the fixed-width arm (2026-10-11 run:
    /// 223,728 vs 230,591 INSERT ops/s, ~3 % apart, against a 2,3x UPDATE separation in the plan's last
    /// genuine variable-length reading), which makes both the label and the layout comparison false.
    /// Green once the arm opts out of <c>AutoFixedWidthRecords</c> — how the 2026-09-03 fair-PK report
    /// ("legacy variable-length (opt-out)") defines it.
    /// </para>
    /// </summary>
    [Fact]
    public void PkLegacyArm_ResolvesToVariableLengthRecords_AsItsBannerClaims()
    {
        // Arrange
        var root = FindRepositoryRoot();
        var executable = root is null ? null : FindHarnessExecutable(root);
        if (root is null || executable is null)
        {
            Assert.Skip("SharpCoreDB.Benchmarks.Comparative is not built; build it in Release/net11.0 to run this guard.");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"SCDB_BenchLayout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var output = new List<string>();
        try
        {
            using var process = StartHarness(executable!, tempDir, reps: 1, output);
            var exited = process.WaitForExit(TimeSpan.FromMinutes(5));
            Assert.True(exited, $"The benchmark harness did not finish within 5 minutes:{Environment.NewLine}{Joined(output)}");

            // Act — the harness prints the layout each arm actually resolved in its own [diag] line.
            var legacy = ResolvedLayoutFor(output, "legacy variable-length");
            var fixedWidth = ResolvedLayoutFor(output, "fixed-width, plaintext");

            // Assert
            Assert.True(legacy.HasValue, $"No layout diagnostic for the legacy arm in the harness output:{Environment.NewLine}{Joined(output)}");
            Assert.True(fixedWidth.HasValue, $"No layout diagnostic for the fixed-width arm in the harness output:{Environment.NewLine}{Joined(output)}");
            Assert.False(legacy!.Value, "The arm labelled 'legacy variable-length' resolved to FIXED-WIDTH records, so that label is false and the fair-PK table compares the same layout twice.");
            Assert.True(fixedWidth!.Value, "The arm labelled 'fixed-width' did not resolve to fixed-width records.");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static Process StartHarness(string executable, string tempDir, int reps, List<string> output)
    {
        var info = new ProcessStartInfo(executable, "--pk")
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.Environment["SHARPCOREDB_BENCH_REPS"] = reps.ToString(CultureInfo.InvariantCulture);
        info.Environment["SHARPCOREDB_WARMUP_REPS"] = "0"; // The protocol under test is the rep stamp, not the ramp.
        info.Environment["SHARPCOREDB_BENCH_TEMP"] = tempDir;

        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.Add(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.Add(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>
    /// The <c>IsFixedWidthRecords=</c> value the harness printed for the first arm run after the section
    /// banner containing <paramref name="bannerMarker"/>, or null when that diagnostic is absent.
    /// </summary>
    private static bool? ResolvedLayoutFor(List<string> output, string bannerMarker)
    {
        List<string> snapshot;
        lock (output)
        {
            snapshot = new List<string>(output);
        }

        var banner = snapshot.FindIndex(line => line.Contains(bannerMarker, StringComparison.Ordinal));
        if (banner < 0)
        {
            return null;
        }

        const string token = "IsFixedWidthRecords=";
        for (var i = banner + 1; i < snapshot.Count; i++)
        {
            var at = snapshot[i].IndexOf(token, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            var value = snapshot[i][(at + token.Length)..].Trim();
            var end = value.IndexOf(' ');
            if (end >= 0)
            {
                value = value[..end];
            }

            return bool.TryParse(value, out var parsed) ? parsed : null;
        }

        return null;
    }

    /// <summary>The newest <c>pk_comparative_*.json</c> written by the run this test just started.</summary>
    private static FileInfo? NewestArchive(string resultsDir, DateTime startedAt) =>
        new DirectoryInfo(resultsDir)
            .GetFiles("pk_comparative_*.json")
            .Where(f => f.LastWriteTimeUtc >= startedAt.AddSeconds(-1))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SharpCoreDB.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// The harness executable, or null when the project has not been built (the CI case).
    /// <para>
    /// Pinned to <c>net11.0</c> and to the shallowest match on purpose. A plain recursive search for the
    /// exe picks up leftovers from other target frameworks and RID/publish subdirectories: this box still
    /// carries a <c>bin\Release\net10.0\&lt;rid&gt;\</c> build of an older harness revision (its console
    /// table has three arms, not four), and *that* one writes its archive to
    /// <c>bin\Release\net10.0\results\</c>, because the harness resolves its results directory as "three
    /// levels up from AppContext.BaseDirectory". The shallowest <c>net11.0</c> exe resolves to the
    /// project's own <c>results\</c> folder, which is the tracked evidence location.
    /// </para>
    /// </summary>
    private static string? FindHarnessExecutable(string root)
    {
        var tfmDir = Path.Combine(root, "tests", "benchmarks", "SharpCoreDB.Benchmarks.Comparative", "bin", "Release", "net11.0");
        return Directory.Exists(tfmDir)
            ? Directory.EnumerateFiles(tfmDir, "SharpCoreDB.Benchmarks.Comparative.exe", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault()
            : null;
    }

    /// <summary>Mirrors the harness's own <c>ResultsDirectory()</c>, so the guard reads where it wrote.</summary>
    private static string HarnessResultsDirectory(string executable) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", "..", "results"));

    private static string Joined(List<string> output)
    {
        lock (output)
        {
            return string.Join(Environment.NewLine, output);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not a test failure.
        }
    }
}
