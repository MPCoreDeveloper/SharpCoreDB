// <copyright file="PrimaryKeyUpdateTests.cs" company="MPCoreDeveloper">
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
/// An UPDATE that changes a PRIMARY KEY may not move a row onto a key another row holds, and a rejected UPDATE changes
/// nothing — on columnar and page-based storage, in the session and after a reopen.
/// <para>
/// Up to 2.1.4 the new key was never checked (known issue §8): <c>UPDATE t SET id = 1 WHERE id = 3</c> with a row 1
/// present was accepted, the primary-key index was re-pointed at the moved row, and the original row 1 became
/// unreachable — silent data loss. Single-file storage enforces no primary-key uniqueness at all yet (§11).
/// </para>
/// </summary>
public sealed class PrimaryKeyUpdateTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_PkUpdate_{Guid.NewGuid():N}");

    public PrimaryKeyUpdateTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

    public static TheoryData<string> Modes => new() { "columnar", "page-based" };

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
    [MemberData(nameof(Modes))]
    public void Update_PrimaryKeyOntoAnExistingKey_IsRejectedAndLosesNoRow(string mode)
    {
        var (db, _) = Open(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET id = 1 WHERE id = 3"));
        Assert.Equal([(1, "one"), (3, "three")], Rows(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_PrimaryKeyOntoAnExistingKey_AfterReopen_LosesNoRow(string mode)
    {
        var (db, path) = Open(mode);
        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET id = 1 WHERE id = 3"));
        (db as IDisposable)?.Dispose();

        var reopened = _factory.Create(path, "pw");
        Assert.Equal([(1, "one"), (3, "three")], Rows(reopened));
        (reopened as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_PrimaryKeyToAFreeKey_MovesTheRow(string mode)
    {
        var (db, _) = Open(mode);

        db.ExecuteSQL("UPDATE t SET id = 7 WHERE id = 3");

        Assert.Equal([(1, "one"), (7, "three")], Rows(db));
        Assert.Equal("three", db.ExecuteQuery("SELECT name FROM t WHERE id = 7").Single()["name"]);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_PrimaryKeyToItsOwnValue_IsAccepted(string mode)
    {
        var (db, _) = Open(mode);

        db.ExecuteSQL("UPDATE t SET id = 3, name = 'still three' WHERE id = 3");

        Assert.Equal([(1, "one"), (3, "still three")], Rows(db));
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Update_PrimaryKeyOfSeveralRowsToOneKey_IsRejectedAndChangesNothing(string mode)
    {
        var (db, _) = Open(mode);

        Assert.ThrowsAny<Exception>(() => db.ExecuteSQL("UPDATE t SET id = 9 WHERE id > 0"));
        Assert.Equal([(1, "one"), (3, "three")], Rows(db));
    }

    private (IDatabase Db, string Path) Open(string mode)
    {
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var db = _factory.Create(path, "pw");
        var create = "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)";
        db.ExecuteSQL(mode == "page-based" ? create + " STORAGE = PAGE_BASED" : create);
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (1, 'one')");
        db.ExecuteSQL("INSERT INTO t (id, name) VALUES (3, 'three')");
        return (db, path);
    }

    private static (int Id, string? Name)[] Rows(IDatabase db) =>
        [.. db.ExecuteQuery("SELECT id, name FROM t")
            .Select(r => (Convert.ToInt32(r["id"], CultureInfo.InvariantCulture), r["name"] as string))
            .OrderBy(r => r.Item1)];
}
