// <copyright file="SelectSemanticsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// The SELECT-list and WHERE semantics an ADO.NET consumer depends on, pinned in <b>both</b> storage modes (the
/// directory engine and the single-file one), because the two implement WHERE separately and drifted apart.
/// <para>
/// What was wrong: a SELECT list was ignored (the whole row came back, in table order, aliases dropped), so
/// linq2db — which maps columns by ordinal — read <c>name</c> as an integer; and the WHERE forms a SQL generator
/// emits for a <c>bool</c> property (<c>WHERE [t].[is_active]</c>, <c>WHERE NOT [t].[is_active]</c>) matched
/// either every row or none. <c>LIKE … ESCAPE</c>, which linq2db uses for <c>string.StartsWith</c>, matched
/// nothing at all.
/// </para>
/// </summary>
public sealed class SelectSemanticsTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private readonly string _scdbPath;
    private int _counter;

    public SelectSemanticsTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_SelectSemantics_{Guid.NewGuid():N}");
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_SelectSemantics_{Guid.NewGuid():N}.scdb");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    /// <summary>Creates a seeded database in the requested storage mode.</summary>
    private IDatabase Open(bool singleFile)
    {
        IDatabase db;
        if (singleFile)
        {
            db = _factory.CreateWithOptions(_scdbPath, "pw", DatabaseOptions.CreateSingleFileDefault());
        }
        else
        {
            var dir = Path.Combine(_dirPath, $"db{_counter++}");
            Directory.CreateDirectory(dir);
            db = _factory.Create(dir, "pw");
        }

        db.ExecuteSQL("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, price REAL, active INTEGER)");
        db.ExecuteSQL("INSERT INTO items VALUES (1, 'alpha', 10.5, 1)");
        db.ExecuteSQL("INSERT INTO items VALUES (2, 'beta', 20.0, 0)");
        db.ExecuteSQL("INSERT INTO items VALUES (3, 'gamma', 15.25, 1)");
        return db;
    }

    private static string[] Keys(IReadOnlyList<Dictionary<string, object>> rows) => [.. rows[0].Keys];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_WithColumnList_ReturnsOnlyTheListedColumnsInListedOrder(bool singleFile)
    {
        var db = Open(singleFile);

        var rows = db.ExecuteQuery("SELECT price, name FROM items WHERE id = 1");

        Assert.Single(rows);
        Assert.Equal(["price", "name"], Keys(rows));
        Assert.Equal("alpha", rows[0]["name"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_WithAlias_NamesTheColumnByTheAlias(bool singleFile)
    {
        var db = Open(singleFile);

        var rows = db.ExecuteQuery("SELECT name AS label, price AS cost FROM items WHERE id = 2");

        Assert.Single(rows);
        Assert.Equal(["label", "cost"], Keys(rows));
        Assert.Equal("beta", rows[0]["label"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_WithQualifiedAndBracketedColumns_ResolvesThem(bool singleFile)
    {
        var db = Open(singleFile);

        var rows = db.ExecuteQuery("SELECT [i].[name] FROM [items] [i] WHERE [i].[id] = 3");

        Assert.Single(rows);
        Assert.Equal(["name"], Keys(rows));
        Assert.Equal("gamma", rows[0]["name"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_WhereBareBooleanColumn_FiltersRows(bool singleFile)
    {
        var db = Open(singleFile);

        var rows = db.ExecuteQuery("SELECT name FROM items WHERE [items].[active]");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["alpha", "gamma"], [.. rows.Select(row => (string)row["name"]).OrderBy(name => name)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_WhereNegatedBareBooleanColumn_FiltersRows(bool singleFile)
    {
        var db = Open(singleFile);

        var rows = db.ExecuteQuery("SELECT name FROM items WHERE NOT [items].[active]");

        Assert.Single(rows);
        Assert.Equal("beta", rows[0]["name"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_WhereLikeWithEscape_TreatsTheEscapedWildcardAsLiteral(bool singleFile)
    {
        var db = Open(singleFile);
        db.ExecuteSQL("INSERT INTO items VALUES (4, 'a_b', 1.0, 1)");
        db.ExecuteSQL("INSERT INTO items VALUES (5, 'axb', 1.0, 1)");

        // linq2db renders string.StartsWith("a_") as LIKE 'a~_%' ESCAPE '~': the underscore is a literal.
        var rows = db.ExecuteQuery("SELECT name FROM items WHERE name LIKE 'a~_%' ESCAPE '~'");

        Assert.Single(rows);
        Assert.Equal("a_b", rows[0]["name"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_ExistsSubquery_ReturnsOneOrZero(bool singleFile)
    {
        var db = Open(singleFile);

        var found = db.ExecuteQuery("SELECT EXISTS (SELECT * FROM items WHERE name = 'beta')");
        var missing = db.ExecuteQuery("SELECT EXISTS (SELECT * FROM items WHERE name = 'omega')");

        Assert.Single(found);
        Assert.Single(missing);
        Assert.Equal(1L, Convert.ToInt64(found[0].Values.First(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0L, Convert.ToInt64(missing[0].Values.First(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Select_BetweenWithAnd_KeepsTheRange(bool singleFile)
    {
        var db = Open(singleFile);

        // The AND inside BETWEEN must not be read as a conjunction of two conditions.
        var rows = db.ExecuteQuery("SELECT name FROM items WHERE price BETWEEN 10 AND 16");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["alpha", "gamma"], [.. rows.Select(row => (string)row["name"]).OrderBy(name => name)]);
    }
}
