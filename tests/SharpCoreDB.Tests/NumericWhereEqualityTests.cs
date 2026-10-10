// <copyright file="NumericWhereEqualityTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

/// <summary>
/// Plan §3-1h: a simple WHERE equality on a numeric column used to compare TEXT — the row value went
/// through <c>ToString()</c> and the SQL literal was compared as a string. <c>WHERE score = 5.0</c>
/// therefore missed a stored 5.0 (double.ToString() yields "5"), while <c>WHERE score = 5</c> matched
/// by accident, and the ordering operators parsed the literal with the machine's CURRENT culture (so a
/// decimal literal failed to parse under a comma-decimal culture). Numbers are now compared numerically
/// against an invariant-culture parse of the literal; non-numeric values keep the string comparison.
/// </summary>
public sealed class NumericWhereEqualityTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public NumericWhereEqualityTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_NumWhere_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    /// <summary>Rows: id, name, score, age — see the literals the tests assert against.</summary>
    private static readonly (long Id, string Name, double Score, long Age)[] Seed =
    [
        (1, "alpha", 4.5, 25),
        (2, "beta", 5.0, 30),
        (3, "gamma", 5.5, 25),
        (4, "delta", 19.99, 40),
        (5, "epsilon", 100.0, 25),
    ];

    private IDatabase Create(string suffix, bool atRest = false, bool fixedWidth = true)
    {
        var dir = Path.Combine(_dirPath, suffix);
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig
        {
            NoEncryptMode = !atRest,
            EnableAtRestRecordEncryption = atRest,
            AutoFixedWidthRecords = fixedWidth,
        });

        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL, age INTEGER)");
        foreach (var (id, name, score, age) in Seed)
        {
            // Invariant formatting on purpose: the SQL literal must not depend on the test host culture.
            db.ExecuteSQL(string.Format(CultureInfo.InvariantCulture,
                "INSERT INTO docs VALUES ({0}, '{1}', {2}, {3})", id, name, score, age));
        }

        db.Flush();
        return db;
    }

    private static List<long> Ids(IDatabase db, string where) =>
        db.ExecuteQuery($"SELECT id FROM docs WHERE {where}")
          .ConvertAll(r => Convert.ToInt64(r["id"]));

    [Theory]
    [InlineData("5")]
    [InlineData("5.0")]
    [InlineData("5.00")]
    [InlineData("5.000")]
    public void RealEquality_MatchesEveryEquivalentLiteralForm(string literal)
    {
        var db = Create($"forms-{literal.Replace('.', '_')}");
        try
        {
            Assert.Equal([2L], Ids(db, $"score = {literal}"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void RealEquality_MatchesLiteralWithDecimals()
    {
        var db = Create("decimals");
        try
        {
            Assert.Equal([4L], Ids(db, "score = 19.99"));
            Assert.Empty(Ids(db, "score = 20.0"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    private static string Slug(string where) => where
        .Replace(" ", string.Empty).Replace(">", "gt").Replace("<", "lt")
        .Replace("=", "eq").Replace("!", "ne").Replace(".", "_");

    [Theory]
    [InlineData("score > 5.0", new long[] { 3, 4, 5 })]
    [InlineData("score >= 5.0", new long[] { 2, 3, 4, 5 })]
    [InlineData("score < 5.5", new long[] { 1, 2 })]
    [InlineData("score <= 5.0", new long[] { 1, 2 })]
    [InlineData("score != 5.0", new long[] { 1, 3, 4, 5 })]
    [InlineData("score <> 5.0", new long[] { 1, 3, 4, 5 })]
    [InlineData("score > 19.9", new long[] { 4, 5 })]
    public void NumericComparisons_WithDecimalLiterals(string where, long[] expected)
    {
        var db = Create("cmp-" + Slug(where));
        try
        {
            Assert.Equal(expected, Ids(db, where));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void IntegerEquality_StillMatches()
    {
        var db = Create("integer");
        try
        {
            Assert.Equal([1L, 3L, 5L], Ids(db, "age = 25"));
            Assert.Equal([2L], Ids(db, "age = 30"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void StringEquality_IsUnchanged()
    {
        // A string column keeps the exact ordinal comparison: a numeric-looking literal must not be
        // coerced into a number, and only the exact name matches.
        var db = Create("text");
        try
        {
            Assert.Equal([2L], Ids(db, "name = 'beta'"));
            Assert.Equal([1L, 3L, 4L, 5L], Ids(db, "name != 'beta'"));
            Assert.Empty(Ids(db, "name = '5.0'"));
            Assert.Empty(Ids(db, "name = '5'"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }

    [Fact]
    public void RealEquality_IsCultureIndependent()
    {
        // The literal is SQL syntax, so it parses with invariant culture even when the machine runs a
        // comma-decimal culture — the ordering operators used to parse with CultureInfo.CurrentCulture.
        var original = CultureInfo.CurrentCulture;
        IDatabase? db = null;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            db = Create("culture");
            Assert.Equal([3L], Ids(db, "score = 5.5"));
            Assert.Equal([2L], Ids(db, "score = 5.0"));
            Assert.Equal([1L, 2L], Ids(db, "score < 5.5"));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(false, true)]   // raw, fixed-width
    [InlineData(false, false)]  // raw, variable-length (the EvaluateWhere path)
    [InlineData(true, true)]    // at-rest, fixed-width
    [InlineData(true, false)]   // at-rest, variable-length (the EvaluateWhere path)
    public void NumericEquality_IsConsistentAcrossStorageLayouts(bool atRest, bool fixedWidth)
    {
        var db = Create($"matrix-{atRest}-{fixedWidth}", atRest, fixedWidth);
        try
        {
            Assert.Equal([2L], Ids(db, "score = 5.0"));
            Assert.Equal([4L], Ids(db, "score = 19.99"));
            Assert.Equal([1L, 2L], Ids(db, "score < 5.5"));
            Assert.Equal([1L, 3L, 5L], Ids(db, "age = 25"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
