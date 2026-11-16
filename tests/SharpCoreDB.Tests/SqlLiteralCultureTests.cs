// <copyright file="SqlLiteralCultureTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// Values rendered into SQL text are culture-invariant, in every copy of the "value to SQL literal" switch.
/// <para>
/// Up to 2.1.4 the four copies — <c>ParameterBinder.FormatParameter</c> (the ADO.NET binder),
/// <c>CompiledQueryExecutor.FormatParameterValue</c>, <c>SqlFunctions.Quote</c> and <c>Table.FormatValue</c> —
/// formatted dates with the current culture's calendar (th-TH stored the year 2026 as 2569), rendered a
/// <c>DateTimeOffset</c>, <c>DateOnly</c> or <c>TimeOnly</c> through its culture-dependent <c>ToString()</c>
/// (<c>'1/1/2026 12:30:15 PM +10:00'</c> under en-US, without its milliseconds), three of them dropped the
/// milliseconds of a <c>DateTime</c>, and <c>Table.FormatValue</c> wrote a <c>double</c> as <c>1,5</c> on a
/// comma-decimal machine. Found through the linq2db adapter, whose <c>DateTimeOffset</c> column read back as 2569.
/// </para>
/// </summary>
public sealed class SqlLiteralCultureTests : IDisposable
{
    private static readonly DateTime SampleDateTime = new(2026, 1, 1, 12, 30, 15, 250);
    private static readonly DateTimeOffset SampleOffset = new(SampleDateTime, TimeSpan.FromHours(10));

    private readonly string _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_SqlLiteralCulture_{Guid.NewGuid():N}");

    public static TheoryData<string> Cultures => new() { "en-US", "th-TH", "sv-SE", "de-DE" };

    /// <summary>Every copy of the formatter, by name, so a failure names the copy that regressed.</summary>
    public static TheoryData<string, string> CulturesAndFormatters
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var culture in new[] { "en-US", "th-TH", "sv-SE", "de-DE" })
            {
                foreach (var formatter in Formatters.Keys)
                {
                    data.Add(culture, formatter);
                }
            }

            return data;
        }
    }

    private static readonly Dictionary<string, Func<object?, string>> Formatters = new()
    {
        ["ParameterBinder.FormatParameter"] = ParameterBinder.FormatParameter,
        ["CompiledQueryExecutor.FormatParameterValue"] = CompiledQueryExecutor.FormatParameterValue,
        ["SqlFunctions.Quote"] = SqlFunctions.Quote,
        ["Table.FormatValue"] = Table.FormatValue,
    };

    [Theory]
    [MemberData(nameof(CulturesAndFormatters))]
    public void Format_DateTime_IsInvariantWithMilliseconds(string culture, string formatter)
    {
        var literal = WithCulture(culture, () => Formatters[formatter](SampleDateTime));

        Assert.Equal("'2026-01-01 12:30:15.250'", literal);
    }

    [Theory]
    [MemberData(nameof(CulturesAndFormatters))]
    public void Format_DateTimeOffset_IsInvariantWithOffset(string culture, string formatter)
    {
        var literal = WithCulture(culture, () => Formatters[formatter](SampleOffset));

        Assert.Equal("'2026-01-01 12:30:15.250+10:00'", literal);
    }

    [Theory]
    [MemberData(nameof(CulturesAndFormatters))]
    public void Format_DateOnlyAndTimeOnly_AreInvariant(string culture, string formatter)
    {
        var (date, time) = WithCulture(culture, () => (
            Formatters[formatter](DateOnly.FromDateTime(SampleDateTime)),
            Formatters[formatter](TimeOnly.FromDateTime(SampleDateTime))));

        Assert.Equal("'2026-01-01'", date);
        Assert.Equal("'12:30:15.250'", time);
    }

    [Theory]
    [MemberData(nameof(CulturesAndFormatters))]
    public void Format_NegativeIntegers_UseAsciiMinus(string culture, string formatter)
    {
        var (i, l) = WithCulture(culture, () => (Formatters[formatter](-5), Formatters[formatter](-5L)));

        Assert.Equal("-5", i.Trim('\''));
        Assert.Equal("-5", l.Trim('\''));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TableFormatValue_Double_UsesInvariantDecimalPoint(string culture)
    {
        var literal = WithCulture(culture, () => Table.FormatValue(-1.5));

        Assert.Equal("-1.5", literal);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ExecuteSQL_WithTemporalParameters_StoresInvariantText(string culture)
    {
        var db = Open();

        WithCulture(culture, () =>
        {
            db.ExecuteSQL(
                "INSERT INTO t (id, at, at_offset, day, clock, delta) VALUES (@id, @at, @offset, @day, @clock, @delta)",
                new Dictionary<string, object?>
                {
                    ["@id"] = 1,
                    ["@at"] = SampleDateTime,
                    ["@offset"] = SampleOffset,
                    ["@day"] = DateOnly.FromDateTime(SampleDateTime),
                    ["@clock"] = TimeOnly.FromDateTime(SampleDateTime),
                    ["@delta"] = -5,
                });
            return 0;
        });

        var row = Assert.Single(db.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
        Assert.Equal("2026-01-01 12:30:15.250", Text(row["at"]));
        Assert.Equal("2026-01-01 12:30:15.250+10:00", Text(row["at_offset"]));
        Assert.Equal("2026-01-01", Text(row["day"]));
        Assert.Equal("12:30:15.250", Text(row["clock"]));
        Assert.Equal("-5", Text(row["delta"]));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dirPath))
            {
                Directory.Delete(_dirPath, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp directory.
        }
    }

    private IDatabase Open()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_dirPath);
        var db = factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, at TEXT, at_offset TEXT, day TEXT, clock TEXT, delta TEXT)");
        return db;
    }

    private static string? Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);

    private static T WithCulture<T>(string name, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
