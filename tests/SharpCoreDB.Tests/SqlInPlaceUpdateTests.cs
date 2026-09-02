// <copyright file="SqlInPlaceUpdateTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// Issue #6: in-place UPDATE — a fixed-width (or unchanged-length) row update overwrites the
/// record in its existing storage slot instead of appending a new version, so the data file does
/// not grow and no stale version is left for compaction. Variable-width updates that change the
/// stored length fall back to the append path (correctness must be unchanged).
/// </summary>
public sealed class SqlInPlaceUpdateTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public SqlInPlaceUpdateTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_InPlaceUpd_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private long DataFileSize(string table) => new FileInfo(Path.Combine(_dirPath, $"{table}.dat")).Length;

    [Fact]
    public void SqlUpdate_FixedWidth_OverwritesInPlace_FileDoesNotGrow()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE fw (id INTEGER PRIMARY KEY, val INTEGER, flag BOOLEAN)");
            db.ExecuteSQL("INSERT INTO fw VALUES (1, 100, 1)");
            db.ExecuteSQL("INSERT INTO fw VALUES (2, 200, 0)");

            long sizeAfterInsert = DataFileSize("fw");
            Assert.True(sizeAfterInsert > 0);

            // 50 in-place updates: a fixed-width row serializes to the same length every time,
            // so every update overwrites the existing slot — the file must not grow.
            for (int i = 0; i < 50; i++)
            {
                db.ExecuteSQL($"UPDATE fw SET val = {100 + i} WHERE id = 1");
            }

            Assert.Equal(sizeAfterInsert, DataFileSize("fw"));

            // Results are correct and the table still has exactly 2 rows.
            var rows = db.ExecuteQuery("SELECT * FROM fw WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal(149, rows[0]["val"]);
            Assert.Equal(2, db.ExecuteQuery("SELECT * FROM fw").Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlUpdate_Parameterized_FixedWidth_OverwritesInPlace()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE pw (id INTEGER PRIMARY KEY, val INTEGER)");
            db.ExecuteSQL("INSERT INTO pw VALUES (1, 10)");
            long sizeAfterInsert = DataFileSize("pw");

            for (int i = 0; i < 20; i++)
            {
                db.ExecuteSQL("UPDATE pw SET val = @p WHERE id = @id",
                    new Dictionary<string, object?> { ["@p"] = 10 + i, ["@id"] = 1 });
            }

            Assert.Equal(sizeAfterInsert, DataFileSize("pw"));
            Assert.Equal(29, db.ExecuteQuery("SELECT * FROM pw WHERE id = 1")[0]["val"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void SqlUpdate_VariableWidth_GrowsWhenStoredLengthChanges_StillCorrect()
    {
        var db = _factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE vw (id INTEGER PRIMARY KEY, name TEXT, val INTEGER)");
            db.ExecuteSQL("INSERT INTO vw VALUES (1, 'short', 1)");
            long sizeAfterInsert = DataFileSize("vw");

            // Growing the string changes the stored record length → append fallback (file grows).
            db.ExecuteSQL("UPDATE vw SET name = 'a much longer name that no longer fits' WHERE id = 1");
            Assert.True(DataFileSize("vw") > sizeAfterInsert);

            var rows = db.ExecuteQuery("SELECT * FROM vw WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal("a much longer name that no longer fits", rows[0]["name"]);

            // Updating a fixed-width column with an UNCHANGED string length → in-place again.
            long sizeAfterGrow = DataFileSize("vw");
            db.ExecuteSQL("UPDATE vw SET val = 42 WHERE id = 1");
            Assert.Equal(sizeAfterGrow, DataFileSize("vw"));
            Assert.Equal(42, db.ExecuteQuery("SELECT * FROM vw WHERE id = 1")[0]["val"]);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_NoPrimaryKey_HashIndexLookup_PatchesInPlace()
    {
        // Regression: ExecuteBatchSQL groups UPDATEs into UpdateMultiple, which previously
        // resolved rows without their storage positions. Without a PK the columnar write path
        // could not find the record slot → it appended a new version per update (stale rows +
        // compaction storm). The position now comes from the hash-index lookup, so fixed-size
        // fields (score REAL) are patched in place and the row count stays stable.
        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            var rows = new List<Dictionary<string, object>>(200);
            for (int i = 0; i < 200; i++)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["name"] = $"User{i}",
                    ["email"] = $"user{i}@test.com",
                    ["age"] = 20 + i % 60,
                    ["score"] = i * 0.1,
                    ["data"] = $"payload-{i}",
                });
            }
            db.InsertBatch("docs", rows);
            long sizeAfterInsert = DataFileSize("docs");

            var stmts = new List<string>(200);
            for (int i = 0; i < 200; i++)
            {
                stmts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = {0:F1} WHERE name = 'User{1}'", i * 99.9, i));
            }
            db.ExecuteBatchSQL(stmts);

            // All 200 rows still present, no duplicates from stale appends.
            Assert.Equal(200, db.ExecuteQuery("SELECT * FROM docs").Count);

            // REAL score is a fixed-size field → patched in place, file does not grow.
            Assert.Equal(sizeAfterInsert, DataFileSize("docs"));

            // Values are actually updated and visible through the hash-index lookup.
            var updated = db.ExecuteQuery("SELECT * FROM docs WHERE name = @n",
                new Dictionary<string, object?> { ["@n"] = "User42" });
            Assert.Single(updated);
            Assert.Equal(42 * 99.9, updated[0]["score"]);

            var all = db.ExecuteQuery("SELECT * FROM docs");
            Assert.Equal(200, all.Count);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_NoPrimaryKey_PageBased_AppliesUpdates()
    {
        // Same scenario on the PageBased engine: without the position pass-through, updates on a
        // PK-less table were silently dropped (no PK → no engine.Update). They must now be applied.
        var config = new DatabaseConfig
        {
            NoEncryptMode = true,
            StorageEngineType = StorageEngineType.PageBased,
            EnableHashIndexes = true,
            UseMemoryMapping = true,
            WalDurabilityMode = DurabilityMode.Async,
        };

        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw", isReadOnly: false, config: config);
        try
        {
            db.ExecuteSQL(@"CREATE TABLE docs (
            name TEXT NOT NULL,
            score REAL
        )");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            var rows = new List<Dictionary<string, object>>(50);
            for (int i = 0; i < 50; i++)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["name"] = $"User{i}",
                    ["score"] = i * 1.5,
                });
            }
            db.InsertBatch("docs", rows);

            var stmts = new List<string>(50);
            for (int i = 0; i < 50; i++)
            {
                stmts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = {0:F1} WHERE name = 'User{1}'", i * 77.7, i));
            }
            db.ExecuteBatchSQL(stmts);

            Assert.Equal(50, db.ExecuteQuery("SELECT * FROM docs").Count);
            var updated = db.ExecuteQuery("SELECT * FROM docs WHERE name = @n",
                new Dictionary<string, object?> { ["@n"] = "User7" });
            Assert.Single(updated);
            Assert.Equal(7 * 77.7, (double)updated[0]["score"], precision: 6);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_NoPrimaryKey_Columnar_FileStable()
    {
        // Regression: ExecuteBatchSQL on a PK-less table resolves matching rows through the
        // hash index but previously discarded the storage position. The columnar write path
        // then could not patch in place → every update appended a new version (file growth,
        // stale rows, compaction storm). With the position passed through, fixed-size fields
        // are patched in place and the file size stays constant.
        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL(@"CREATE TABLE docs (
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                score REAL,
                data TEXT
            )");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            var rows = new List<Dictionary<string, object>>(200);
            for (int i = 0; i < 200; i++)
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["name"] = $"User{i}",
                    ["email"] = $"user{i}@test.com",
                    ["age"] = 20 + i % 60,
                    ["score"] = i * 0.1,
                    ["data"] = $"payload-{i}",
                });
            }
            db.InsertBatch("docs", rows);
            long sizeAfterInsert = DataFileSize("docs");
            Assert.True(sizeAfterInsert > 0);

            var stmts = new List<string>(200);
            for (int i = 0; i < 200; i++)
            {
                stmts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "UPDATE docs SET score = {0:F1} WHERE name = 'User{1}'", i * 99.9, i));
            }
            db.ExecuteBatchSQL(stmts);

            // No stale versions appended: same row count and unchanged file size.
            Assert.Equal(200, db.ExecuteQuery("SELECT * FROM docs").Count);
            Assert.Equal(sizeAfterInsert, DataFileSize("docs"));

            // Values are actually updated and visible through the hash-index lookup.
            var updated = db.ExecuteQuery("SELECT * FROM docs WHERE name = @n",
                new Dictionary<string, object?> { ["@n"] = "User42" });
            Assert.Single(updated);
            Assert.Equal(42 * 99.9, (double)updated[0]["score"], precision: 6);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_Rollback_RestoresOriginalValues()
    {
        // B7 regression: in-place overwrites inside a transaction are write-behind. Rollback
        // must drop the buffered overwrites so the on-disk records stay byte-for-byte original.
        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT NOT NULL, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");

            var rows = new List<Dictionary<string, object>>(50);
            for (int i = 0; i < 50; i++)
            {
                rows.Add(new Dictionary<string, object> { ["name"] = $"User{i}", ["score"] = i * 1.5 });
            }
            db.InsertBatch("t", rows);

            db.BeginStorageTransactionOnly();
            try
            {
                db.ExecuteBatchSQL(new[] { "UPDATE t SET score = 999.9 WHERE name = 'User42'" });

                // Inside the transaction the new value is visible (buffered overwrite).
                var inside = db.ExecuteQuery("SELECT * FROM t WHERE name = @n",
                    new Dictionary<string, object?> { ["@n"] = "User42" });
                Assert.Single(inside);
                Assert.Equal(999.9, (double)inside[0]["score"], precision: 6);
            }
            finally
            {
                db.RollbackStorageTransaction();
            }

            // After rollback the pre-transaction value is restored and no rows were lost.
            Assert.Equal(50, db.ExecuteQuery("SELECT * FROM t").Count);
            var after = db.ExecuteQuery("SELECT * FROM t WHERE name = @n",
                new Dictionary<string, object?> { ["@n"] = "User42" });
            Assert.Single(after);
            Assert.Equal(42 * 1.5, (double)after[0]["score"], precision: 6);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_FastPatch_NotNullViolationThrows()
    {
        // B7 fast patch: NOT NULL must still be validated on the changed values even though the
        // full row is never deserialized.
        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT NOT NULL, score REAL NOT NULL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('a', 1.0)");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                db.ExecuteBatchSQL(new[] { "UPDATE t SET score = NULL WHERE name = 'a'" }));
            Assert.Contains("cannot be NULL", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The row is unchanged.
            var rows = db.ExecuteQuery("SELECT * FROM t");
            Assert.Single(rows);
            Assert.Equal(1.0, (double)rows[0]["score"], precision: 6);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_WithCheckConstraint_FallsBackAndApplies()
    {
        // B7 fast patch is disabled when a CHECK constraint exists (the constraint may read
        // non-updated columns) — the full-row fallback must still apply the update correctly.
        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL(@"CREATE TABLE t (name TEXT NOT NULL, score REAL CHECK (score >= 0))");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('a', 1.0)");

            db.ExecuteBatchSQL(new[] { "UPDATE t SET score = 42.5 WHERE name = 'a'" });
            var rows = db.ExecuteQuery("SELECT * FROM t");
            Assert.Single(rows);
            Assert.Equal(42.5, (double)rows[0]["score"], precision: 6);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void BatchSqlUpdate_WhereColumnTouched_FallsBackAndApplies()
    {
        // B7 fast patch only applies when no indexed column changes. Updating the WHERE column
        // itself must still work through the full-row path (including hash-index maintenance).
        var db = (SharpCoreDB.Database)_factory.Create(_dirPath, "pw");
        try
        {
            db.ExecuteSQL("CREATE TABLE t (name TEXT NOT NULL, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_t_name ON t(name)");
            db.ExecuteSQL("INSERT INTO t VALUES ('a', 1.0)");

            db.ExecuteBatchSQL(new[] { "UPDATE t SET name = 'b' WHERE name = 'a'" });

            Assert.Equal(0, db.ExecuteQuery("SELECT * FROM t WHERE name = @n",
                new Dictionary<string, object?> { ["@n"] = "a" }).Count);
            var rows = db.ExecuteQuery("SELECT * FROM t WHERE name = @n",
                new Dictionary<string, object?> { ["@n"] = "b" });
            Assert.Single(rows);
            Assert.Equal(1.0, (double)rows[0]["score"], precision: 6);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
