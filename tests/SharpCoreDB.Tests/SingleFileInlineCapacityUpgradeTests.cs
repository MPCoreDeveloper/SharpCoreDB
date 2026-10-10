// <copyright file="SingleFileInlineCapacityUpgradeTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Plan §8c on the single-file (<c>.scdb</c>) path: an existing table whose <b>stored</b> inline capacity is below the
/// configured one upgrades itself on a writable open — the rows are rewritten through the new layout and the new
/// capacity is persisted in the table-directory entry, so the next reopen reads with it instead of migrating again.
/// <para>
/// The multi-file sibling of this test is
/// <see cref="FixedWidthInlineValueTests.ExistingTable_AutoUpgradesInlineCapacity_OnWritableOpen_AndTheRowsStayReachable"/>,
/// and both assert in the same order for the same reason: capacity, then row count, then the primary-key lookup, and
/// only then the values — because a misdecoded value and a missing row both surface as <see langword="null"/>.
/// </para>
/// </summary>
public sealed class SingleFileInlineCapacityUpgradeTests : IDisposable
{
    private const string Password = "pw"; // NOSONAR:S2068 - throwaway local test credential
    private const string LongValue = "a considerably longer tag value for the overflow block";

    private readonly DatabaseFactory _factory;
    private readonly string _path;

    public SingleFileInlineCapacityUpgradeTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _path = Path.Combine(Path.GetTempPath(), $"SCDB_SfInlineUpgrade_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    private static DatabaseOptions Options(int inlineBytes, bool readOnly = false)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        options.IsReadOnly = readOnly;
        options.DatabaseConfig = new DatabaseConfig
        {
            FixedWidthRecordLayout = true,
            FixedWidthInlineValueBytes = inlineBytes,
        };
        return options;
    }

    private static object? ValueOf(IDatabase db, int id, string column)
    {
        var rows = db.ExecuteQuery($"SELECT * FROM t WHERE id = {id}");
        return rows.Count == 0 ? null : rows[0][column];
    }

    [Fact]
    public async Task SingleFileTable_AutoUpgradesInlineCapacity_AndPersistsItInTheDirectoryEntry()
    {
        // Written at the historical capacity 0.
        await using (var created = _factory.CreateWithOptions(_path, Password, Options(0)))
        {
            created.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, note TEXT)");
            created.ExecuteSQL($"INSERT INTO t VALUES (1, 'short', '{LongValue}'), (2, 'User2', 'payload-2')");
            created.Flush();
        }

        // Writable open at 24: the upgrade happens here, and the new capacity must be visible immediately.
        await using (var upgraded = _factory.CreateWithOptions(_path, Password, Options(24)))
        {
            Assert.True(upgraded.TryGetTable("t", out var table));
            Assert.Equal(24, ((ITable)table).FixedWidthInlineValueBytes);
            Assert.Equal(2, upgraded.ExecuteQuery("SELECT * FROM t").Count);
            Assert.Single(upgraded.ExecuteQuery("SELECT * FROM t WHERE id = 1"));
            Assert.Equal("short", ValueOf(upgraded, 1, "name"));
            Assert.Equal(LongValue, ValueOf(upgraded, 1, "note"));
            Assert.Equal("payload-2", ValueOf(upgraded, 2, "note"));

            upgraded.ExecuteSQL("INSERT INTO t VALUES (3, 'after', 'migration')");
            Assert.Equal("after", ValueOf(upgraded, 3, "name"));
            upgraded.Flush();
        }

        // Reopen: the capacity came from the directory entry, so there is nothing left to migrate and no row is lost.
        await using (var again = _factory.CreateWithOptions(_path, Password, Options(24)))
        {
            Assert.True(again.TryGetTable("t", out var table));
            Assert.Equal(24, ((ITable)table).FixedWidthInlineValueBytes);
            Assert.Equal(3, again.ExecuteQuery("SELECT * FROM t").Count);
            Assert.Single(again.ExecuteQuery("SELECT * FROM t WHERE id = 3"));
            Assert.Equal(LongValue, ValueOf(again, 1, "note"));
        }

        // Read-only: everything readable, nothing rewritten.
        await using var readOnly = _factory.CreateWithOptions(_path, Password, Options(24, readOnly: true));
        Assert.True(readOnly.TryGetTable("t", out var readOnlyTable));
        Assert.Equal(24, ((ITable)readOnlyTable).FixedWidthInlineValueBytes);
        Assert.Equal(3, readOnly.ExecuteQuery("SELECT * FROM t").Count);
        Assert.Equal(LongValue, ValueOf(readOnly, 1, "note"));
    }

    [Fact]
    public async Task SingleFileTable_IsNeverDowngraded_ByALowerConfiguredCapacity()
    {
        await using (var created = _factory.CreateWithOptions(_path, Password, Options(24)))
        {
            created.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, note TEXT)");
            created.ExecuteSQL("INSERT INTO t VALUES (1, 'short', 'note1')");
            created.Flush();
        }

        await using var openedWithZero = _factory.CreateWithOptions(_path, Password, Options(0));
        Assert.True(openedWithZero.TryGetTable("t", out var table));
        Assert.Equal(24, ((ITable)table).FixedWidthInlineValueBytes);
        Assert.Equal("short", ValueOf(openedWithZero, 1, "name"));
        Assert.Equal("note1", ValueOf(openedWithZero, 1, "note"));
    }
}
