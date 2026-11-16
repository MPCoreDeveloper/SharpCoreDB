// <copyright file="SqlFunctionsDateFamilyTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// The public <see cref="SqlFunctions.EvaluateFunction"/> answers SQLite's date and time functions with SQLite's
/// argument order and format language, through the engine's one implementation, while keeping its legacy
/// <c>(DateTime, .NET format)</c> shape for <c>STRFTIME</c> and <c>DATE</c>.
/// <para>
/// Up to 2.1.4 <c>STRFTIME</c> and <c>DATE</c> only answered for a <see cref="DateTime"/> argument (anything else was
/// NULL), <c>TIME</c>, <c>DATETIME</c>, <c>JULIANDAY</c> and <c>UNIXEPOCH</c> threw <see cref="NotSupportedException"/>,
/// and <see cref="SqlFunctions.StrFTime"/> formatted with the current culture, so th-TH printed the year 2026 as 2569.
/// </para>
/// </summary>
public sealed class SqlFunctionsDateFamilyTests
{
    [Theory]
    [InlineData("STRFTIME", new object[] { "%Y-%m", "2026-03-10" }, "2026-03")]
    [InlineData("DATE", new object[] { "2026-03-10 10:00:00" }, "2026-03-10")]
    [InlineData("TIME", new object[] { "2026-03-10 10:11:12" }, "10:11:12")]
    [InlineData("DATETIME", new object[] { "2026-03-10", "+1 day" }, "2026-03-11 00:00:00")]
    [InlineData("UNIXEPOCH", new object[] { "1970-01-02" }, "86400")]
    [InlineData("JULIANDAY", new object[] { "2000-01-01 12:00:00" }, "2451545")]
    public void EvaluateFunction_DateFamilyWithSqliteArguments_AnswersAsSqlite(string name, object[] args, string expected)
    {
        var value = SqlFunctions.EvaluateFunction(name, new List<object?>(args));

        Assert.Equal(expected, Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void EvaluateFunction_StrftimeWithLegacyDateTimeAndDotNetFormat_KeepsItsBehaviour()
    {
        var value = SqlFunctions.EvaluateFunction("STRFTIME", [new DateTime(2026, 3, 10), "yyyy/MM"]);

        Assert.Equal("2026/03", value);
    }

    [Fact]
    public void StrFTime_UnderANonGregorianCulture_UsesTheGregorianYear()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
        try
        {
            Assert.Equal("2026-03-10", SqlFunctions.StrFTime(new DateTime(2026, 3, 10), "yyyy-MM-dd"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
