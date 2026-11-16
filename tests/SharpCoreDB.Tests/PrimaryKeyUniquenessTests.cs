// <copyright file="PrimaryKeyUniquenessTests.cs" company="MPCoreDeveloper">
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
/// A multi-row INSERT that fails is one atomic unit — nothing of it is kept — on both storage modes, as in SQLite, and
/// directory storage enforces PRIMARY KEY uniqueness.
/// <para>
/// Up to 2.1.4 a single-file multi-row INSERT ran row by row, so a row failing NOT NULL left the rows before it in
/// place; directory storage validates the whole statement first, and single-file storage now takes the same batch
/// path. Single-file PRIMARY KEY uniqueness is not enforced (the skipped cases, known issue §11).
/// </para>
/// </summary>
public sealed class PrimaryKeyUniquenessTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_PkUnique_{Guid.NewGuid():N}");

    public PrimaryKeyUniquenessTests()
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
    [InlineData("directory")]
    [InlineData("single-file", Skip = "Single-file storage does not enforce PRIMARY KEY uniqueness, and cannot do so safely yet: its format persists the PrimaryKey flag on one column only, so a composite key (EF Core declares one inline PRIMARY KEY per key column) is indistinguishable from a single-column key. See docs/sharpcoredb-known-issues.md, §11.")]
    public void Insert_DuplicatePrimaryKey_IsRejected(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'dup')"));
        Assert.Single(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("single-file", Skip = "Single-file storage does not enforce PRIMARY KEY uniqueness, and cannot do so safely yet: its format persists the PrimaryKey flag on one column only, so a composite key (EF Core declares one inline PRIMARY KEY per key column) is indistinguishable from a single-column key. See docs/sharpcoredb-known-issues.md, §11.")]
    public void MultiRowInsert_RepeatingAnExistingKey_KeepsNoRowOfTheStatement(string mode)
    {
        var (db, _) = Open(mode);
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'x')");

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, name) VALUES (2, 'y'), (1, 'dup')"));
        Assert.Single(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("single-file", Skip = "Single-file storage does not enforce PRIMARY KEY uniqueness, and cannot do so safely yet: its format persists the PrimaryKey flag on one column only, so a composite key (EF Core declares one inline PRIMARY KEY per key column) is indistinguishable from a single-column key. See docs/sharpcoredb-known-issues.md, §11.")]
    public void MultiRowInsert_RepeatingAKeyWithinTheStatement_KeepsNoRow(string mode)
    {
        var (db, _) = Open(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, name) VALUES (5, 'a'), (5, 'b')"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("single-file")]
    public void MultiRowInsert_WithANotNullViolation_KeepsNoRow(string mode)
    {
        var (db, _) = Open(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'a'), (2, NULL), (3, 'c')"));
        Assert.Empty(db.ExecuteQuery("SELECT id FROM t"));
    }

    [Fact(Skip = "Single-file storage does not enforce PRIMARY KEY uniqueness, and cannot do so safely yet: its format persists the PrimaryKey flag on one column only, so a composite key (EF Core declares one inline PRIMARY KEY per key column) is indistinguishable from a single-column key. See docs/sharpcoredb-known-issues.md, §11.")]
    public void Insert_DuplicatePrimaryKey_AfterReopen_IsRejectedOnSingleFile()
    {
        var (db, path) = Open("single-file");
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'x')");
        (db as IDisposable)?.Dispose();

        var reopened = _factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault());
        Assert.ThrowsAny<Exception>(() => reopened.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'dup')"));
        Assert.Single(reopened.ExecuteQuery("SELECT id FROM t"));
        (reopened as IDisposable)?.Dispose();
    }

    private (IDatabase Db, string Path) Open(string mode)
    {
        var path = Path.Combine(_root, mode == "single-file" ? $"db_{Guid.NewGuid():N}.scdb" : $"db_{Guid.NewGuid():N}");
        var db = mode == "single-file"
            ? _factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault())
            : _factory.Create(path, "pw");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        return (db, path);
    }
}
