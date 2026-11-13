// <copyright file="QuotedIdentifierDdlTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.IO;
using Xunit;

/// <summary>
/// Pins that a <c>CREATE TABLE</c> whose name is quoted registers under the <em>bare</em> name, so the statements
/// that follow it resolve.
/// <para>
/// linq2db's SQLite dialect, EF Core and most generators quote every identifier, so the statement that creates a
/// table is <c>CREATE TABLE [notes] (…)</c>. The parser used to take that token verbatim: the catalog was keyed by
/// <c>[notes]</c> while the INSERT path resolved <c>notes</c> through
/// <c>SqlIdentifierScanner.IdentifierText</c>, so the next <c>INSERT INTO [notes] …</c> threw
/// <em>Table notes does not exist</em>.
/// </para>
/// <para>
/// Reported as part of issue #497 — a LINQ to DB model over a directory-mode database
/// (<c>Data Source=probe.db</c>, i.e. without the <c>.scdb</c> extension that selects single-file storage) created
/// its table through <c>CreateTableAsync</c> and then could not insert a single row.
/// </para>
/// </summary>
public sealed class QuotedIdentifierDdlTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root;

    public QuotedIdentifierDdlTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _root = Path.Combine(Path.GetTempPath(), $"SCDB_QuotedDdl_{Guid.NewGuid():N}");
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
        catch
        {
            // the test's own temp directory
        }
    }

    private IDatabase Create(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
    }

    [Theory]
    [InlineData("[notes]")]
    [InlineData("\"notes\"")]
    [InlineData("`notes`")]
    [InlineData("notes")]
    public async Task CreateTable_QuotedTableName_IsResolvableByUnquotedStatements(string createdName)
    {
        await using var db = Create($"quoted_{createdName.Replace("[", "b").Replace("\"", "d").Replace("`", "t")}");

        db.ExecuteSQL($"CREATE TABLE {createdName} (id INTEGER, body TEXT)");

        // The catalog holds the bare name — this is what every later statement resolves.
        Assert.True(db.TryGetTable("notes", out _), $"a table created as {createdName} must be registered as notes");

        db.ExecuteSQL("INSERT INTO notes (id, body) VALUES (1, 'unquoted')");
        db.ExecuteSQL("INSERT INTO [notes] ([id], [body]) VALUES (2, 'quoted')");

        var rows = db.ExecuteQuery("SELECT id, body FROM notes ORDER BY id");
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task CreateTableIfNotExists_QuotedTableName_RegistersUnderBareNameAndStaysIdempotent()
    {
        await using var db = Create("if_not_exists");

        db.ExecuteSQL("CREATE TABLE IF NOT EXISTS [notes] (id INTEGER, body TEXT)");
        // The IF NOT EXISTS branch reads the name from a different token (parts[5]); a second run must be a no-op.
        db.ExecuteSQL("CREATE TABLE IF NOT EXISTS [notes] (id INTEGER, body TEXT)");

        Assert.True(db.TryGetTable("notes", out _));
        db.ExecuteSQL("INSERT INTO notes (id, body) VALUES (1, 'x')");
        Assert.Single(db.ExecuteQuery("SELECT id FROM notes"));
    }

    [Fact]
    public async Task CreateTable_ColumnListAttachedToName_RegistersUnderBareName()
    {
        await using var db = Create("attached_paren");

        // No space before the column list, which SQLite accepts: the token is "notes(id".
        db.ExecuteSQL("CREATE TABLE notes(id INTEGER, body TEXT)");

        Assert.True(db.TryGetTable("notes", out _));
        db.ExecuteSQL("INSERT INTO notes (id, body) VALUES (1, 'x')");
        Assert.Single(db.ExecuteQuery("SELECT id FROM notes"));
    }

    [Theory]
    [InlineData("[id]")]
    [InlineData("\"id\"")]
    [InlineData("`id`")]
    [InlineData("id")]
    public async Task CreateTable_QuotedColumnNames_AreResolvableByUnquotedStatements(string declaredId)
    {
        await using var db = Create($"quoted_col_{declaredId.Replace("[", "b").Replace("\"", "d").Replace("`", "t")}");

        db.ExecuteSQL($"CREATE TABLE notes ({declaredId} INTEGER, body TEXT)");

        // The columns are catalogued by their bare names. Before the fix the catalog held "[id]", so the INSERT
        // below found no such column and threw ArgumentOutOfRangeException from table.ColumnTypes[-1].
        db.ExecuteSQL("INSERT INTO notes (id, body) VALUES (1, 'x')");
        db.ExecuteSQL("INSERT INTO notes ([id], [body]) VALUES (2, 'y')");

        var rows = db.ExecuteQuery("SELECT body FROM notes WHERE id = 2");
        Assert.Single(rows);
    }

    [Fact]
    public async Task AlterTable_AddColumn_QuotedName_RegistersUnderBareName()
    {
        await using var db = Create("alter_add_column");

        db.ExecuteSQL("CREATE TABLE notes (id INTEGER)");
        db.ExecuteSQL("ALTER TABLE notes ADD COLUMN [extra] TEXT");

        // A column registered as "[extra]" is invisible to a statement that names it as SQL does.
        db.ExecuteSQL("INSERT INTO notes (id, extra) VALUES (1, 'kept')");
        Assert.Single(db.ExecuteQuery("SELECT extra FROM notes WHERE id = 1"));
    }
}
