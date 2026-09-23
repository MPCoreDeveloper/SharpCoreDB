// <copyright file="SingleFileFileGrowthTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// The single-file (<c>.scdb</c>) file-size floor, and the knob that lowers it
/// (<see cref="DatabaseConfig.SingleFileMinExtensionBytes"/>, 2026-09-23).
/// <para>
/// The finding this pins: a small <c>.scdb</c> database is <b>14,7 MB whatever it holds</b>, because the file starts at
/// <b>1.037 pages</b> and the first extension that does not fit adds
/// <c>max(requiredPages, currentSize / 2, minExtensionPages)</c> with <c>minExtensionPages = 10 MiB / pageSize =
/// 2.560 pages</c> — i.e. the historical minimum decides, and the row count never enters the sum. That was measured on
/// the harness's <c>--scdb</c> arm as <b>14.733.312 B at 100, 500 and 2.000 rows</b> (both caller shapes, both inline
/// capacities), which is why these tests write a few hundred rows and assert the file size rather than a timing.
/// </para>
/// <para>
/// Both tests also read every row back after a reopen, and the first one reopens with a <b>different</b> value than it
/// wrote with: the size policy is not part of the on-disk format, so a file grown under one setting must extend and read
/// under another without conversion.
/// </para>
/// </summary>
public sealed class SingleFileFileGrowthTests : IDisposable
{
    private const string Password = "pw"; // NOSONAR:S2068 - throwaway local test credential
    private const int Rows = 400;

    /// <summary>1.037 initial pages + 2.560 minimum-extension pages, at the default 4 KiB page size.</summary>
    private const long HistoricalFloorBytes = 14_733_312;

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
    public void DefaultSettings_KeepTheHistoricalFloor_AndTheRowsSurviveADifferentSettingOnReopen()
    {
        var (fileBytes, rowsAfterWrite) = WriteAndMeasure(0);
        Assert.Equal(Rows, rowsAfterWrite);

        // The floor is the minimum extension, not the data: 400 rows of this schema fit inside it, and a ONE-row
        // database measures the same 14.733.312 B — so this asserts the exact page count (see HistoricalFloorBytes)
        // rather than a band: a deliberate change to the growth policy must move this number loudly, not silently.
        Assert.Equal(HistoricalFloorBytes, fileBytes);

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
    public void ConfiguredMinimumExtension_ShrinksTheSameDataset_AndTheRowsStayReadable()
    {
        var (fileBytes, rowsAfterWrite) = WriteAndMeasure(64 * 1024);
        Assert.Equal(Rows, rowsAfterWrite);
        Assert.Equal(0, fileBytes % 4096);

        // With the 10 MiB minimum out of the way the HALVING term decides on a fresh file
        // (`max(requiredPages, currentSize / 2, minExtensionPages)`), so the file lands at
        // 1.037 + 518 = 1.555 pages = 6.369.280 B on the harness's schema (measured) instead of 14,7 MB. This asserts
        // "strictly smaller and well under the old floor" rather than an exact figure, because how many pages the data
        // itself needs is the schema's business, not this knob's.
        Assert.True(
            fileBytes < HistoricalFloorBytes / 2,
            $"expected the configured 64 KiB minimum to halve the {HistoricalFloorBytes:N0} B floor, measured {fileBytes:N0} B");
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
