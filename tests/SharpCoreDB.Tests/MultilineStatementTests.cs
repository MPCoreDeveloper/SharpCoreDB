// <copyright file="MultilineStatementTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// SQL is whitespace-insensitive: SQLite accepts a line break or a tab wherever a space is legal, and tools that
/// generate SQL — linq2db's SQLite dialect, DbUp, Flyway, DbGate, most pretty-printers — lay statements out over
/// several lines with tab indentation.
/// <para>
/// SharpCoreDB's INSERT parser was not: it assumed exactly one space after <c>INSERT INTO</c> and read the table
/// name as "everything up to the next space". A pretty-printed INSERT was therefore rejected with a nonsensical
/// <c>Table notes]\n(\n\t[id],… does not exist</c> — the "table name" ran to the first space <em>inside the string
/// literal</em> — and <c>INSERT\nINTO …</c> threw <c>ArgumentOutOfRangeException</c> from a <c>-1</c> slice. These
/// tests pin the shapes that linq2db emits, on both the statement path and the batch path.
/// </para>
/// </summary>
public sealed class MultilineStatementTests : IDisposable
{
    private const string Schema = "CREATE TABLE notes (id INTEGER PRIMARY KEY, body TEXT)";

    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private int _dbCounter;

    public MultilineStatementTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_Multiline_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase Open()
    {
        var dir = Path.Combine(_dirPath, $"db{_dbCounter++}");
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        db.ExecuteSQL(Schema);
        return db;
    }

    private static List<Dictionary<string, object>> Rows(IDatabase db)
        => db.ExecuteQuery("SELECT id, body FROM notes ORDER BY id");

    [Fact]
    public void Insert_WithColumnListOnNextLine_StoresRow()
    {
        var db = Open();

        // The statement linq2db's SQLite dialect actually emits: a newline before the column list, tab indentation.
        db.ExecuteSQL("INSERT INTO notes\n(\n\t[id],\n\t[body]\n)\nVALUES\n(\n\t1,\n\t'it''s a probe'\n)");

        var rows = Rows(db);
        Assert.Single(rows);
        Assert.Equal(1L, Convert.ToInt64(rows[0]["id"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("it's a probe", rows[0]["body"]);
    }

    [Fact]
    public void Insert_WithBracketedNameAndValuesOnSeparateLines_StoresRow()
    {
        var db = Open();

        db.ExecuteSQL("INSERT INTO [notes]\r\n\t([id], [body])\r\nVALUES\r\n\t(2, 'line one'),\r\n\t(3, 'line two')");

        var rows = Rows(db);
        Assert.Equal(2, rows.Count);
        Assert.Equal("line one", rows[0]["body"]);
        Assert.Equal("line two", rows[1]["body"]);
    }

    [Fact]
    public void Insert_WithNewlineBetweenInsertAndInto_StoresRow()
    {
        var db = Open();

        // Previously ArgumentOutOfRangeException: the parser sliced the statement at the -1 it got from
        // IndexOf("INSERT INTO").
        db.ExecuteSQL("INSERT\nINTO notes\n(id, body)\nVALUES\n(4, 'split keywords')");

        var rows = Rows(db);
        Assert.Single(rows);
        Assert.Equal("split keywords", rows[0]["body"]);
    }

    [Fact]
    public void Insert_WithTabSeparatedKeywords_StoresRow()
    {
        var db = Open();

        db.ExecuteSQL("INSERT\tINTO\tnotes\t(id, body)\tVALUES\t(5, 'tabs')");

        var rows = Rows(db);
        Assert.Single(rows);
        Assert.Equal("tabs", rows[0]["body"]);
    }

    [Fact]
    public void ExecuteBatchSql_WithPrettyPrintedInserts_StoresAllRows()
    {
        var db = Open();

        // The batch path has its own INSERT parsers (Database.Batch.cs), which carried the same space scan.
        db.ExecuteBatchSQL(
        [
            "INSERT INTO notes\n(\n\t[id],\n\t[body]\n)\nVALUES\n(\n\t10,\n\t'batch one'\n)",
            "INSERT INTO [notes]\n([id], [body])\nVALUES\n(11, 'batch two')",
            "INSERT\nINTO notes\n(id, body)\nVALUES\n(12, 'batch three')",
        ]);

        var rows = Rows(db);
        Assert.Equal(3, rows.Count);
        Assert.Equal("batch one", rows[0]["body"]);
        Assert.Equal("batch two", rows[1]["body"]);
        Assert.Equal("batch three", rows[2]["body"]);
    }

    [Fact]
    public void UpdateAndDelete_WithPrettyPrintedStatements_AffectTheRow()
    {
        var db = Open();
        db.ExecuteSQL("INSERT INTO notes\n(\n\t[id],\n\t[body]\n)\nVALUES\n(20, 'before')");

        db.ExecuteSQL("UPDATE\n\t[notes]\nSET\n\t[body] = 'after'\nWHERE\n\t[id] = 20");
        Assert.Equal("after", Rows(db)[0]["body"]);

        db.ExecuteSQL("DELETE FROM\n\t[notes]\nWHERE\n\t[id] = 20");
        Assert.Empty(Rows(db));
    }
}
