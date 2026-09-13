// <copyright file="WritePathProfilerTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Diagnostics;
using SharpCoreDB.Interfaces;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Guards <see cref="WritePathProfiler"/>: it must be free when disabled, and it must actually
/// attribute time to stages when enabled — that is the whole point of the write-path plan
/// (docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md §2).
/// </summary>
public sealed class WritePathProfilerTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dir;

    public WritePathProfilerTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dir = Path.Combine(Path.GetTempPath(), $"SCDB_WritePath_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        WritePathProfiler.Disable();

        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Disabled_RecordsNothing_AndStampIsFree()
    {
        WritePathProfiler.Disable();
        WritePathProfiler.Reset();

        long stamp = WritePathProfiler.Stamp();
        WritePathProfiler.Add(WritePathProfiler.Stage.EngineWrite, stamp);

        Assert.False(WritePathProfiler.Enabled);
        Assert.Equal(0L, stamp);
        Assert.All(WritePathProfiler.Snapshot(), row => Assert.Equal(0, row.Calls));
        Assert.Contains("no stages recorded", WritePathProfiler.Report(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabled_AttributesTimeToStages_OfARealWriteWorkload()
    {
        WritePathProfiler.Reset();
        WritePathProfiler.Enable();

        await using (IDatabase db = _factory.Create(
            _dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true }))
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            for (int i = 0; i < 50; i++)
            {
                db.ExecuteSQL($"INSERT INTO docs VALUES ({i}, 'user{i}', {i * 0.5})");
            }

            db.ExecuteBatchSQL([.. Enumerable.Range(0, 50).Select(i =>
                $"UPDATE docs SET score = {i * 1.5} WHERE id = {i}")]);

            db.Flush();
        }

        var snapshot = WritePathProfiler.Snapshot();
        long engineWriteCalls = snapshot.Single(r => r.Stage == "engine-write").Calls;
        long indexCalls = snapshot.Single(r => r.Stage == "index-maint").Calls;

        // The update path is instrumented, so a 50-row update batch must show up here. Other tests
        // running in parallel may add more calls; the assertion is a floor, not an exact count.
        Assert.True(engineWriteCalls > 0, $"expected engine-write calls, got {engineWriteCalls}");
        Assert.True(indexCalls > 0, $"expected index-maint calls, got {indexCalls}");

        string report = WritePathProfiler.Report();
        Assert.Contains("engine-write", report, StringComparison.Ordinal);
        Assert.Contains("index-maint", report, StringComparison.Ordinal);

        // Disabling stops accumulation: the counters must not move.
        WritePathProfiler.Disable();
        long afterDisable = WritePathProfiler.Snapshot().Single(r => r.Stage == "engine-write").Calls;
        WritePathProfiler.Add(WritePathProfiler.Stage.EngineWrite, WritePathProfiler.Stamp());
        Assert.Equal(afterDisable, WritePathProfiler.Snapshot().Single(r => r.Stage == "engine-write").Calls);
    }

    [Fact]
    public void Report_OrdersByTotalTimeDescending_AndShowsShares()
    {
        WritePathProfiler.Reset();
        WritePathProfiler.Enable();

        long slow = WritePathProfiler.Stamp();
        Thread.Sleep(2);
        WritePathProfiler.Add(WritePathProfiler.Stage.EngineWrite, slow);

        long fast = WritePathProfiler.Stamp();
        WritePathProfiler.Add(WritePathProfiler.Stage.Validate, fast);

        WritePathProfiler.Disable();

        string report = WritePathProfiler.Report();
        int engine = report.IndexOf("engine-write", StringComparison.Ordinal);
        int validate = report.IndexOf("validate", StringComparison.Ordinal);

        Assert.True(engine >= 0 && validate >= 0, report);
        Assert.True(engine < validate, $"engine-write should sort first:\n{report}");
        Assert.Contains("share", report, StringComparison.Ordinal);
    }
}
