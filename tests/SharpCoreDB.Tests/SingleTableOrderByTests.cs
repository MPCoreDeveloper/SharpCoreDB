// <copyright file="SingleTableOrderByTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// ORDER BY on the single-table SELECT path follows SQLite: every key in the list, each with its own direction,
/// expressions and SELECT-list aliases as keys, NULLs first when ascending, BINARY collation unless the column or the
/// key says otherwise — and the sort happens before OFFSET/LIMIT.
/// <para>
/// Up to 2.1.4 the path read only the single token after ORDER BY. A second key was ignored — <c>ORDER BY grp, name</c>
/// came back unsorted, which is what linq2db emits for <c>OrderBy(…).ThenBy(…)</c> — an expression or alias key left
/// the rows unsorted, a qualified key (<c>[x].[name]</c>) matched no row value, and a single text column was ordered
/// with the machine's culture (case-insensitively under en-US) instead of SQLite's BINARY collation.
/// </para>
/// </summary>
public sealed class SingleTableOrderByTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_OrderBy_{Guid.NewGuid():N}");

    public SingleTableOrderByTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp directory.
        }
    }

    [Theory]
    [InlineData("directory", "SELECT id FROM t ORDER BY name", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY name DESC", new[] { 3, 1, 2, 4, 5 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name DESC", new[] { 3, 1, 2, 4, 5 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY name NULLS LAST", new[] { 4, 2, 1, 3, 5 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name NULLS LAST", new[] { 4, 2, 1, 3, 5 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY grp, name", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY grp, name", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY grp DESC, name ASC", new[] { 1, 3, 4, 2, 5 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY grp DESC, name ASC", new[] { 1, 3, 4, 2, 5 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY length(name) DESC", new[] { 4, 3, 1, 2, 5 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY length(name) DESC", new[] { 4, 3, 1, 2, 5 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY strftime('%Y', at)", new[] { 5, 4, 2, 3, 1 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY strftime('%Y', at)", new[] { 5, 4, 2, 3, 1 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY upper(name)", new[] { 5, 2, 1, 3, 4 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY upper(name)", new[] { 5, 2, 1, 3, 4 })]
    [InlineData("directory", "SELECT id, upper(name) AS u FROM t ORDER BY u DESC", new[] { 4, 3, 1, 2, 5 })]
    [InlineData("single-file", "SELECT id, upper(name) AS u FROM t ORDER BY u DESC", new[] { 4, 3, 1, 2, 5 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY name COLLATE NOCASE", new[] { 5, 2, 1, 3, 4 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name COLLATE NOCASE", new[] { 5, 2, 1, 3, 4 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY nc", new[] { 5, 2, 1, 3, 4 })]
    [InlineData("directory", "SELECT [x].[id] FROM t x ORDER BY [x].[name]", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("single-file", "SELECT [x].[id] FROM t x ORDER BY [x].[name]", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("directory", "SELECT id, name FROM t ORDER BY 2", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("single-file", "SELECT id, name FROM t ORDER BY 2", new[] { 5, 4, 2, 1, 3 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY grp, length(name) DESC LIMIT 3", new[] { 5, 4, 2 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY grp, length(name) DESC LIMIT 3", new[] { 5, 4, 2 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY name LIMIT 2 OFFSET 1", new[] { 4, 2 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name LIMIT 2 OFFSET 1", new[] { 4, 2 })]
    [InlineData("directory", "SELECT id FROM t WHERE grp = 2 ORDER BY length(name) DESC", new[] { 3, 1 })]
    [InlineData("single-file", "SELECT id FROM t WHERE grp = 2 ORDER BY length(name) DESC", new[] { 3, 1 })]
    // SQLite's LIMIT forms: `LIMIT m, n` (offset m) and a negative count meaning no limit — linq2db writes
    // LIMIT -1 OFFSET m for Skip without Take. Up to 2.1.4 both, and LIMIT n OFFSET m above, threw "Invalid LIMIT
    // clause", and LIMIT 0 returned every row.
    [InlineData("directory", "SELECT id FROM t ORDER BY name LIMIT 1, 2", new[] { 4, 2 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name LIMIT 1, 2", new[] { 4, 2 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY name LIMIT -1 OFFSET 3", new[] { 1, 3 })]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name LIMIT -1 OFFSET 3", new[] { 1, 3 })]
    [InlineData("directory", "SELECT id FROM t ORDER BY name LIMIT 0", new int[0])]
    [InlineData("single-file", "SELECT id FROM t ORDER BY name LIMIT 0", new int[0])]
    public void OrderBy_OnTheSingleTablePath_SortsAsSqliteDoes(string mode, string sql, int[] expectedIds)
    {
        var db = Open(mode);

        var ids = db.ExecuteQuery(sql).Select(r => Convert.ToInt32(r["id"], CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(expectedIds, ids);
    }

    [Fact(Skip = "Single-file storage does not record a column's declared COLLATE (it is neither parsed nor persisted, " +
                 "and its WHERE evaluator has no collation support): an owner decision on extending the column-entry " +
                 "format is pending. See docs/sharpcoredb-known-issues.md, 'Single-file storage ignores COLLATE'.")]
    public void OrderBy_ColumnDeclaredNoCase_OnSingleFile_SortsCaseInsensitively()
    {
        var db = Open("single-file");

        var ids = db.ExecuteQuery("SELECT id FROM t ORDER BY nc").Select(r => Convert.ToInt32(r["id"], CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal([5, 2, 1, 3, 4], ids);
    }

    private IDatabase Open(string mode)
    {
        var db = mode == "single-file"
            ? _factory.CreateWithOptions(Path.Combine(_root, $"db_{Guid.NewGuid():N}.scdb"), "pw", DatabaseOptions.CreateSingleFileDefault())
            : _factory.Create(Path.Combine(_root, $"db_{Guid.NewGuid():N}"), "pw");

        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, grp INTEGER, name TEXT, nc TEXT COLLATE NOCASE, at TEXT)");
        db.ExecuteSQL("INSERT INTO t (id, grp, name, nc, at) VALUES (1, 2, 'bb', 'bb', '2026-03-01 00:00:00.000')");
        db.ExecuteSQL("INSERT INTO t (id, grp, name, nc, at) VALUES (2, 1, 'a', 'a', '2024-01-01 00:00:00.000')");
        db.ExecuteSQL("INSERT INTO t (id, grp, name, nc, at) VALUES (3, 2, 'ccc', 'ccc', '2025-06-01 00:00:00.000')");
        db.ExecuteSQL("INSERT INTO t (id, grp, name, nc, at) VALUES (4, 1, 'Dddd', 'Dddd', '2023-01-01 00:00:00.000')");
        db.ExecuteSQL("INSERT INTO t (id, grp, name, nc, at) VALUES (5, NULL, NULL, NULL, NULL)");
        return db;
    }
}
