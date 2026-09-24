// <copyright file="SingleFileFileGrowthTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// The single-file (<c>.scdb</c>) file-size floor, the knob that moves it
/// (<see cref="DatabaseConfig.SingleFileMinExtensionBytes"/>) and the 2026-09-24 owner decision that lowered the default.
/// <para>
/// The finding these tests pin: a small <c>.scdb</c> database used to be <b>14,7 MB whatever it held</b>, because the file
/// starts at <b>1.037 pages</b> and the first extension that does not fit adds
/// <c>max(requiredPages, currentSize / 2, minExtensionPages)</c> — where a 10 MiB minimum is 2.560 pages at a 4 KiB page
/// size, so the minimum decided and the row count never entered the sum. That was measured on the harness's
/// <c>--scdb</c> arm as <b>14.733.312 B at 1, 100, 500 and 2.000 rows</b> (both caller shapes, both inline capacities).
/// With the default at <b>1 MiB</b> the minimum no longer binds on a fresh file and the <b>halving term</b> decides:
/// 1.037 + 518 = 1.555 pages = <b>6.369.280 B</b>. Three tests hold that: the default lands on 6.369.280 B, an explicit
/// 10 MiB reproduces the historical 14.733.312 B exactly (the decision is reversible in one config line), and a value
/// below the halving term changes nothing.
/// </para>
/// <para>
/// Every variant also reads all rows back, and the default test reopens with a <b>different</b> value than it wrote with:
/// the size policy is not part of the on-disk format, so a file grown under one setting must extend and read under
/// another without conversion.
/// </para>
/// </summary>
public sealed class SingleFileFileGrowthTests : IDisposable
{
    private const string Password = "pw"; // NOSONAR:S2068 - throwaway local test credential
    private const int Rows = 400;

    /// <summary>1.037 initial pages + 2.560 minimum-extension pages, i.e. the historical 10 MiB minimum at a 4 KiB page size.</summary>
    private const long HistoricalFloorBytes = 14_733_312;

    /// <summary>1.037 initial pages + 518 pages from the halving term (<c>currentSize / 2</c>) — where the default lands since 2026-09-24.</summary>
    private const long HalvingTermFloorBytes = 6_369_280;

    private readonly DatabaseFactory _factory;
    private readonly string _path;

    public SingleFileFileGrowthTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _path = Path.Combine(Path.GetTempPath(), $"SCDB_SfFileGrowth_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    private static DatabaseOptions Options(long minExtensionBytes)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.DatabaseConfig = new DatabaseConfig { SingleFileMinExtensionBytes = minExtensionBytes };
        return options;
    }

    [Fact]
    public void DefaultSettings_LandOnTheHalvingTermFloor_AndTheRowsSurviveADifferentSettingOnReopen()
    {
        var (fileBytes, rowsAfterWrite) = WriteAndMeasure(0);
        Assert.Equal(Rows, rowsAfterWrite);

        // The default is 1 MiB since the 2026-09-24 decision, which no longer binds on a fresh 1.037-page file: the
        // halving term decides and the file lands here instead of on the historical 14,7 MB floor. A ONE-row database
        // measured the same value on the harness schema, so this is a floor and not growth — which is why it is pinned
        // exactly rather than banded: a deliberate change to the growth policy must move this number loudly.
        Assert.Equal(HalvingTermFloorBytes, fileBytes);
        Assert.Equal(0, fileBytes % 4096);

        // Reopen with a much smaller minimum and add a row: the setting is read per open and the file keeps reading.
        var options = Options(64 * 1024);
        var db = _factory.CreateWithOptions(_path, Password, options);
        try
        {
            db.ExecuteSQL($"INSERT INTO g VALUES ({Rows}, 'added-after-reopen', 'third row of payload text')");
            db.Flush();
            Assert.Equal(Rows + 1, db.ExecuteQuery("SELECT * FROM g").Count);
        }
        finally
        {
            db.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void ExplicitHistoricalTenMiBMinimum_StillProducesTheOldFloor()
    {
        // The compatibility half of the decision: the 14,7 MB floor is not gone, it is one configuration line away —
        // so a caller who pinned it (or wants the old growth pattern) can have it byte for byte.
        var (fileBytes, rowsAfterWrite) = WriteAndMeasure(10 * 1024 * 1024);
        Assert.Equal(Rows, rowsAfterWrite);
        Assert.Equal(HistoricalFloorBytes, fileBytes);
    }

    [Fact]
    public void MinimumBelowTheHalvingTerm_MakesNoDifference_AndTheRowsStayReadable()
    {
        // Anything at or below ~2 MiB lands in the same place, because the halving term decides a fresh file — pinned so
        // that a future reader cannot conclude from a 64 KiB experiment that the knob scales all the way down.
        var (fileBytes, rowsAfterWrite) = WriteAndMeasure(64 * 1024);
        Assert.Equal(Rows, rowsAfterWrite);
        Assert.Equal(HalvingTermFloorBytes, fileBytes);
    }

    /// <summary>
    /// Writes <see cref="Rows"/> rows in one batched call (one flush per table, so the file grows once rather than once
    /// per statement), then reopens and counts them.
    /// </summary>
    private (long FileBytes, int RowsReadBack) WriteAndMeasure(long minExtensionBytes)
    {
        var options = Options(minExtensionBytes);

        var db = _factory.CreateWithOptions(_path, Password, options);
        try
        {
            db.ExecuteSQL("CREATE TABLE g (id INTEGER PRIMARY KEY, name TEXT NOT NULL, payload TEXT)");

            var statements = new List<string>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                statements.Add($"INSERT INTO g VALUES ({i}, 'row{i}', 'some text payload for row {i}')");
            }

            db.ExecuteBatchSQL(statements);
            db.Flush();
        }
        finally
        {
            db.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        var fileBytes = new FileInfo(_path).Length;

        var reopened = _factory.CreateWithOptions(_path, Password, options);
        try
        {
            return (fileBytes, reopened.ExecuteQuery("SELECT * FROM g").Count);
        }
        finally
        {
            reopened.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
