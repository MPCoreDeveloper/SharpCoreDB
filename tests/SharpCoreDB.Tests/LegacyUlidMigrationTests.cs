// <copyright file="LegacyUlidMigrationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Base32Encoding;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for the 1.9.5 ULID-spec upgrade path: automatic legacy-database detection
/// (<see cref="Database.NeedsLegacyUlidMigration"/>) and the one-shot rewrite of every ULID value
/// (<see cref="Database.MigrateLegacyUlids"/>).
/// </summary>
public sealed class LegacyUlidMigrationTests
{
    private static (IDatabase Db, string Path) CreateDatabase(string suffix)
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scdb_ulid_migr_{Guid.NewGuid():N}{suffix}");
        return (factory.Create(dbPath, "test123"), dbPath);
    }

    private static void CleanupDatabase(IDatabase db, string dbPath)
    {
        try
        {
            if (db is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch
        {
            // Ignore disposal errors
        }

        try
        {
            if (Directory.Exists(dbPath))
            {
                Directory.Delete(dbPath, true);
            }
            else if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>Builds the 16-byte ULID value for the given timestamp and fixed randomness.</summary>
    private static byte[] BuildUlidBytes(long timestamp, byte fill)
    {
        var bytes = new byte[16];
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(timestamp & 0xFF);
            timestamp >>= 8;
        }

        for (int i = 6; i < 16; i++)
        {
            bytes[i] = fill;
        }

        return bytes;
    }

    /// <summary>Normalizes a stored cell value to a string (ULID columns deserialize to Ulid records).</summary>
    private static string? AsString(object? value)
    {
        return value is null or DBNull ? null : value.ToString();
    }

    [Fact]
    public void NewDatabase_IsSpecCompliant_NoMigrationNeeded()
    {
        var (db, dbPath) = CreateDatabase("");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id ULID PRIMARY KEY, note TEXT)");

            // Fresh 1.9.5+ databases store spec-compliant ULIDs from birth.
            Assert.False(db.NeedsLegacyUlidMigration());
            Assert.Equal(0, db.MigrateLegacyUlids());
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void NewSingleFileDatabase_IsSpecCompliant_NoMigrationNeeded()
    {
        var (db, dbPath) = CreateDatabase(".scdb");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id TEXT PRIMARY KEY, note TEXT)");

            // New SCDB files carry the ULID-spec feature flag in the file header.
            Assert.False(db.NeedsLegacyUlidMigration());
            Assert.Equal(0, db.MigrateLegacyUlids());
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void LegacyDatabase_Detected_And_AllUlidValuesMigrated()
    {
        var (db, dbPath) = CreateDatabase("");
        try
        {
            // Explicit ULID primary key + plain ULID column, and a table relying on the hidden
            // _rowid ULID primary key.
            db.ExecuteSQL("CREATE TABLE items (id ULID PRIMARY KEY, note TEXT, parent ULID)");
            db.ExecuteSQL("CREATE TABLE tags (label TEXT)");

            var legacyId1 = Base32.LegacyEncode(BuildUlidBytes(1_000_000_000L, 0x11));
            var legacyParent1 = Base32.LegacyEncode(BuildUlidBytes(1_000_000_001L, 0x22));
            var legacyId2 = Base32.LegacyEncode(BuildUlidBytes(1_000_000_002L, 0x33));
            var legacyRowId = Base32.LegacyEncode(BuildUlidBytes(1_000_000_003L, 0x44));

            db.ExecuteSQL($"INSERT INTO items (id, note, parent) VALUES ('{legacyId1}', 'first', '{legacyParent1}')");
            db.ExecuteSQL($"INSERT INTO items (id, note, parent) VALUES ('{legacyId2}', 'second', NULL)");
            db.ExecuteSQL($"INSERT INTO tags (_rowid, label) VALUES ('{legacyRowId}', 'tag-a')");

            // Simulate a pre-1.9.5 database (metadata without the ULID-spec marker).
            ((Database)db).SetUlidSpecForTesting(false);

            Assert.True(db.NeedsLegacyUlidMigration());
            int converted = db.MigrateLegacyUlids();
            Assert.Equal(3, converted);
            Assert.False(db.NeedsLegacyUlidMigration());

            // Every ULID value was rewritten to the spec encoding, preserving the 128-bit value.
            var items = db.ExecuteQuery("SELECT id, note, parent FROM items ORDER BY note");
            Assert.Equal(2, items.Count);
            Assert.Equal(Base32.Encode(BuildUlidBytes(1_000_000_000L, 0x11)), AsString(items[0]["id"]));
            Assert.Equal("first", items[0]["note"]);
            Assert.Equal(Base32.Encode(BuildUlidBytes(1_000_000_001L, 0x22)), AsString(items[0]["parent"]));
            Assert.Equal(Base32.Encode(BuildUlidBytes(1_000_000_002L, 0x33)), AsString(items[1]["id"]));
            Assert.Equal("second", items[1]["note"]);
            Assert.Null(AsString(items[1]["parent"]));

            var tags = db.ExecuteQuery("SELECT _rowid, label FROM tags");
            Assert.Single(tags);
            Assert.Equal(Base32.Encode(BuildUlidBytes(1_000_000_003L, 0x44)), AsString(tags[0]["_rowid"]));
            Assert.Equal("tag-a", tags[0]["label"]);
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void MigrateLegacyUlids_IsIdempotent()
    {
        var (db, dbPath) = CreateDatabase("");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (id ULID PRIMARY KEY, note TEXT)");
            var legacy = Base32.LegacyEncode(BuildUlidBytes(1_000_000_000L, 0x55));
            db.ExecuteSQL($"INSERT INTO t (id, note) VALUES ('{legacy}', 'x')");

            ((Database)db).SetUlidSpecForTesting(false);
            Assert.Equal(1, db.MigrateLegacyUlids());

            // After the marker is set the migration is a no-op.
            Assert.Equal(0, db.MigrateLegacyUlids());
        }
        finally
        {
            CleanupDatabase(db, dbPath);
        }
    }

    [Fact]
    public void UlidSpecMarker_PersistsAcrossReopen()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scdb_ulid_migr_{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();

        try
        {
            var db = factory.Create(dbPath, "test123");
            db.ExecuteSQL("CREATE TABLE t (id ULID PRIMARY KEY, note TEXT)");
            var legacy = Base32.LegacyEncode(BuildUlidBytes(1_000_000_000L, 0x66));
            db.ExecuteSQL($"INSERT INTO t (id, note) VALUES ('{legacy}', 'x')");
            ((Database)db).SetUlidSpecForTesting(false);
            Assert.Equal(1, db.MigrateLegacyUlids());
            ((IDisposable)db).Dispose();

            // Reopening the database reads the persisted marker: it is spec-compliant now and the
            // migrated value is stable.
            var reopened = factory.Create(dbPath, "test123");
            try
            {
                Assert.False(reopened.NeedsLegacyUlidMigration());
                var rows = reopened.ExecuteQuery("SELECT id, note FROM t");
                Assert.Single(rows);
                Assert.Equal(Base32.Encode(BuildUlidBytes(1_000_000_000L, 0x66)), AsString(rows[0]["id"]));
                Assert.Equal("x", rows[0]["note"]);
            }
            finally
            {
                ((IDisposable)reopened).Dispose();
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(dbPath))
                {
                    Directory.Delete(dbPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
