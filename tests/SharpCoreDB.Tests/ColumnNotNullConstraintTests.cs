// <copyright file="ColumnNotNullConstraintTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// A declared NOT NULL constraint is enforced on single-file storage as it is on directory storage — on INSERT, on
/// a batch INSERT, and on UPDATE — in the creating session and after a reopen.
/// <para>
/// Up to 2.1.4 the single-file table never checked NOT NULL at all: its INSERT paths only applied defaults, so a NULL
/// went into a NOT NULL column in any session. Its constructor also reset the NOT NULL and AUTOINCREMENT flags it had
/// just copied (<c>InitializeColumnMetadata()</c> ran after the copy), so the creating session's table did not even
/// know about the constraint. The flags written to the file were correct.
/// </para>
/// </summary>
public sealed class ColumnNotNullConstraintTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"SCDB_SingleFileConstraints_{Guid.NewGuid():N}.scdb");

    public ColumnNotNullConstraintTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            var directory = Path.ChangeExtension(_path, null);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file.
        }
    }

    [Fact]
    public void Insert_NullIntoNotNullColumn_InTheCreatingSession_IsRejected()
    {
        var db = Open();
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, NULL)"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Fact]
    public void Insert_NullIntoNotNullColumn_AfterReopen_IsRejected()
    {
        var db = Open();
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        (db as IDisposable)?.Dispose();

        var reopened = Open();
        Assert.ThrowsAny<Exception>(() => reopened.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, NULL)"));
        (reopened as IDisposable)?.Dispose();
    }

    [Fact]
    public void Insert_ValueIntoNotNullColumn_IsAccepted()
    {
        var db = Open();
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");

        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'kept')");

        Assert.Single(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("single-file")]
    public void Update_SetNotNullColumnToNull_IsRejectedAndChangesNothing(string mode)
    {
        var db = mode == "single-file" ? Open() : _factory.Create(Path.ChangeExtension(_path, null), "pw");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'kept')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET name = NULL WHERE id = 1"));
        Assert.Equal("kept", Assert.Single(db.ExecuteQuery("SELECT name FROM t"))["name"]);
    }

    [Fact]
    public void Update_SetNotNullColumnToNull_MatchingNoRow_IsAccepted()
    {
        var db = Open();
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");

        db.ExecuteSQL("UPDATE t SET name = NULL WHERE id = 99");

        Assert.Empty(db.ExecuteQuery("SELECT id FROM t"));
    }

    private IDatabase Open() => _factory.CreateWithOptions(_path, "pw", DatabaseOptions.CreateSingleFileDefault());
}
