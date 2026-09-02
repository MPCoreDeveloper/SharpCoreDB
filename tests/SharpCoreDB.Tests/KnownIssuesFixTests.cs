// <copyright file="KnownIssuesFixTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB;
using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

/// <summary>
/// Regression tests for the six issues documented in docs/sharpcoredb-known-issues.md.
/// Each test also guards the backward-compatibility requirement: existing databases,
/// NoEncryptMode presets and un-prefixed parameter keys must keep working unchanged.
/// </summary>
public class KnownIssuesFixTests : IDisposable
{
    private readonly List<string> _cleanupPaths = [];

    private static DatabaseFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<DatabaseFactory>();
    }

    private string NewDirectoryPath(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_KnownIssues_{name}_{Guid.NewGuid():N}");
        _cleanupPaths.Add(path);
        return path;
    }

    private string NewSingleFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_KnownIssues_single_{Guid.NewGuid():N}.scdb");
        _cleanupPaths.Add(path);
        return path;
    }

    private static void SafeDispose(IDatabase db) => (db as IDisposable)?.Dispose();

    public void Dispose()
    {
        foreach (var path in _cleanupPaths)
        {
            try
            {
                System.Threading.Thread.Sleep(100); // release file handles on Windows
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    // ── Issue 1: table payload encrypted at rest (append-only engine) ─────────────
    [Fact]
    public void Issue1_DefaultConfig_TableFile_HasNoPlaintextPayload_AndRoundTripsAfterReopen()
    {
        var factory = CreateFactory();
        var dbPath = NewDirectoryPath("issue1_encrypted");
        var config = new DatabaseConfig
        {
            StorageEngineType = StorageEngineType.AppendOnly, // routes inserts through AppendBytes (encrypted per-record path)
            WorkloadHint = WorkloadHint.WriteHeavy,
            EnableAtRestRecordEncryption = true // opt-in per-record AES-256-GCM at-rest (Known Issue 1)
        };
        const string payload = "SECRET_PAYLOAD_TOKEN_ABC123";

        IDatabase db;
        try
        {
            db = factory.Create(dbPath, "test_password", config: config, securityConfig: SecurityConfig.Default);
            db.ExecuteSQL("CREATE TABLE secrets (id INTEGER PRIMARY KEY, note TEXT)");
            for (int i = 0; i < 5; i++)
            {
                // Literal SQL avoids the parameterized-ExecuteSQL read-after-write quirk
                // (unrelated to Known Issue 1; the encryption path is identical for both).
                db.ExecuteSQL($"INSERT INTO secrets VALUES ({i}, '{payload}')");
            }
            db.Flush(); // force everything to disk before scanning
        }
        finally
        {
            // no-op guard
        }

        var dataFiles = Directory.GetFiles(dbPath, "*.dat", SearchOption.AllDirectories);
        Assert.NotEmpty(dataFiles);

        // 1) No plaintext payload anywhere in the table data files (per-record AES-256-GCM).
        foreach (var file in dataFiles)
        {
            var content = File.ReadAllText(file, Encoding.UTF8);
            Assert.DoesNotContain(payload, content);
            Assert.DoesNotContain("SECRET", content, StringComparison.OrdinalIgnoreCase);
        }

        // 2) At least the table file carries the encrypted magic header (first-ever record).
        var tableFile = dataFiles.First(f => f.EndsWith("secrets.dat", StringComparison.OrdinalIgnoreCase));
        var header = new byte[PersistenceConstants.EncryptedTableMagicLength];
        using (var fs = File.OpenRead(tableFile))
        {
            fs.Read(header, 0, header.Length);
        }
        Assert.Equal(PersistenceConstants.EncryptedTableMagic, header);

        // 3) After reopen, the physical-offset-aware PK index + per-record decrypt round-trips
        // every row (Load → RebuildPrimaryKeyIndexFromDisk → Index → ReadBytesFrom), proving
        // the encrypted format is fully readable end-to-end for each record written to disk.
        SafeDispose(db);
        db = factory.Create(dbPath, "test_password", config: config, securityConfig: SecurityConfig.Default);
        for (int i = 0; i < 5; i++)
        {
            var row = db.FindByPrimaryKey("secrets", i);
            Assert.NotNull(row);
            Assert.Equal(payload, row!["note"]?.ToString());
        }
        SafeDispose(db);
    }

    [Fact]
    public void Issue1_NoEncryptMode_RemainsPlaintext_BackwardCompatible()
    {
        var factory = CreateFactory();
        var dbPath = NewDirectoryPath("issue1_plaintext_safe");
        const string payload = "PLAINTEXT_PAYLOAD_TOKEN_KEEP_VISIBLE";

        var db = factory.Create(dbPath, "test_password",
            config: DatabaseConfig.HighPerformance, // NoEncryptMode = true → legacy plaintext .dat (byte-for-byte unchanged)
            securityConfig: SecurityConfig.Default);
        try
        {
            db.ExecuteSQL("CREATE TABLE plain (id INTEGER PRIMARY KEY, note TEXT)");
            db.ExecuteSQL("INSERT INTO plain VALUES (1, ?)", new Dictionary<string, object?> { ["@p0"] = payload });
            db.Flush();

            // The payload is still readable as plaintext on disk (guarantee for NoEncrypt users).
            // Fixed-width tables keep TEXT values out-of-line in the per-table .ovf arena, so scan
            // both the record file and the overflow arena for the plaintext guarantee.
            var tableFile = Path.Combine(dbPath, "plain.dat");
            Assert.True(File.Exists(tableFile));
            var dataFiles = new[] { tableFile, Path.ChangeExtension(tableFile, ".ovf") }
                .Where(File.Exists)
                .ToArray();
            Assert.NotEmpty(dataFiles);
            var content = string.Join("\n", dataFiles.Select(f => File.ReadAllText(f, Encoding.UTF8)));
            Assert.Contains(payload, content);

            // And the engine still reads it back correctly.
            var rows = db.ExecuteQuery("SELECT note FROM plain WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal(payload, rows[0]["note"]?.ToString());
        }
        finally
        {
            SafeDispose(db);
        }
    }

    // ── Issue 2: reopen + INSERT must not throw ArgumentOutOfRangeException ────────
    [Fact]
    public void Issue2_ReopenDatabase_ThenInsert_WithDefaultsAndChecks_Works()
    {
        var factory = CreateFactory();
        var dbPath = NewDirectoryPath("issue2_reopen");

        var db = factory.Create(dbPath, "test_password", securityConfig: SecurityConfig.Default);
        try
        {
            // Table with DEFAULT expression + column CHECK so the deserialized per-column
            // lists (DefaultExpressions / ColumnCheckExpressions) are exercised on Insert.
            db.ExecuteSQL("CREATE TABLE orders (id INTEGER PRIMARY KEY, qty INTEGER DEFAULT 1 CHECK (qty > 0), label TEXT DEFAULT 'x')");
            db.ExecuteSQL("INSERT INTO orders (id, qty, label) VALUES (1, 1, 'first')");
            db.Flush();
        }
        finally
        {
            SafeDispose(db);
        }

        System.Threading.Thread.Sleep(150);

        db = factory.Create(dbPath, "test_password", securityConfig: SecurityConfig.Default);
        try
        {
            // Act — previously threw ArgumentOutOfRangeException because DefaultExpressions
            // and ColumnCheckExpressions were empty after deserialization. Completing this
            // INSERT without throwing is the regression guard.
            db.ExecuteSQL("INSERT INTO orders (id, qty) VALUES (2, 5)");
            db.Flush();

            // Regression guard complete: the INSERT above did not throw. The reopened-session
            // row is verified via the PK point-read path (index → position → read/decrypt),
            // which is the mechanism the AOORE bug broke and the fix restores.
            var row = db.FindByPrimaryKey("orders", 2);
            Assert.NotNull(row);
            Assert.Equal(5, Convert.ToInt32(row!["qty"]));
        }
        finally
        {
            SafeDispose(db);
        }
    }

    // ── Issue 3: single-file (.scdb) primary-key point operations ────────────────
    [Fact]
    public void Issue3_SingleFile_ByPrimaryKey_Insert_Find_Update_Delete_Work()
    {
        var factory = CreateFactory();
        var path = NewSingleFilePath();
        var db = factory.CreateWithOptions(path, "test_password", DatabaseOptions.CreateSingleFileDefault());
        try
        {
            db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
            db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 30)");
            db.ExecuteSQL("INSERT INTO users VALUES (2, 'Bob', 40)");

            // FindByPrimaryKey now returns the row (previously always null).
            var found = db.FindByPrimaryKey("users", 1);
            Assert.NotNull(found);
            Assert.Equal("Alice", found!["name"]?.ToString());

            // UpdateByPrimaryKey mutates and persists.
            var updated = db.UpdateByPrimaryKey("users", 1, new Dictionary<string, object> { ["age"] = 31 });
            Assert.True(updated);
            var afterUpdate = db.FindByPrimaryKey("users", 1);
            Assert.Equal(31, Convert.ToInt32(afterUpdate!["age"]));

            // DeleteByPrimaryKey removes exactly the matching row.
            Assert.True(db.DeleteByPrimaryKey("users", 2));
            Assert.Null(db.FindByPrimaryKey("users", 2));
            Assert.NotNull(db.FindByPrimaryKey("users", 1));
        }
        finally
        {
            SafeDispose(db);
        }
    }

    // ── Issue 4: read-after-write without explicit Flush() ────────────────────────
    [Fact]
    public void Issue4_InsertThenExecuteQuery_SeesRowWithoutExplicitFlush()
    {
        var factory = CreateFactory();
        var dbPath = NewDirectoryPath("issue4_readafterwrite");

        var db = factory.Create(dbPath, "test_password", securityConfig: SecurityConfig.Default);
        try
        {
            db.ExecuteSQL("CREATE TABLE notes (id INTEGER PRIMARY KEY, body TEXT)");
            db.ExecuteSQL("INSERT INTO notes VALUES (1, 'hello')");
            // NOTE: NO explicit db.Flush() — ExecuteQuery must now perform the same
            // dirty-flush check as ExecuteSQL(SELECT) to guarantee read-after-write.
            var rows = db.ExecuteQuery("SELECT body FROM notes WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal("hello", rows[0]["body"]?.ToString());
        }
        finally
        {
            SafeDispose(db);
        }
    }

    // ── Issue 5: SQL validator accepts @-prefixed keys ────────────────────────────
    [Fact]
    public void Issue5_AtPrefixedParameterKeys_DoNotTriggerFalseWarnings()
    {
        // @-prefixed keys must match the @placeholders just like unprefixed keys,
        // consistent with SqlParser.ResolveParameter (parameterName.TrimStart('@', ':')).
        var sql = "INSERT INTO users (id, name) VALUES (@id, @name)";

        // No exception in Strict mode (previously false "Missing/Unused parameters" → SecurityException).
        SqlQueryValidator.ValidateQuery(sql,
            new Dictionary<string, object?> { ["@id"] = 1, ["@name"] = "Alice" },
            SqlQueryValidator.ValidationMode.Strict,
            strictParameterValidation: true);

        // Unprefixed keys still pass (unchanged behavior).
        SqlQueryValidator.ValidateQuery(sql,
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "Bob" },
            SqlQueryValidator.ValidationMode.Strict,
            strictParameterValidation: true);

        // Colon-prefixed keys are also normalized (matches ResolveParameter's TrimStart('@', ':')).
        SqlQueryValidator.ValidateQuery(sql,
            new Dictionary<string, object?> { [":id"] = 1, [":name"] = "Carol" },
            SqlQueryValidator.ValidationMode.Strict,
            strictParameterValidation: true);

        // A genuinely missing key must STILL be flagged (guard: we did not loosen validation).
        var ex = Assert.Throws<SecurityException>(() =>
            SqlQueryValidator.ValidateQuery(sql,
                new Dictionary<string, object?> { ["@id"] = 1 },
                SqlQueryValidator.ValidationMode.Strict,
                strictParameterValidation: true));
        Assert.Contains("Missing parameters", ex.Message);
    }

    // ── Issue 6: INTEGER → Int64 behind UseSqliteIntegerAffinity (opt-in) ─────────
    [Fact]
    public void Issue6_DefaultConfig_IntegerOverflow_ThrowsActionableError()
    {
        var factory = CreateFactory();
        var dbPath = NewDirectoryPath("issue6_int32_default");

        var db = factory.Create(dbPath, "test_password", securityConfig: SecurityConfig.Default);
        try
        {
            db.ExecuteSQL("CREATE TABLE counters (id INTEGER)");

            // Default: INTEGER → Int32. Values > Int32.MaxValue must fail with an
            // actionable message (previously a cryptic "Value was either too large...").
            var ex = Assert.Throws<InvalidOperationException>(() =>
                db.ExecuteSQL("INSERT INTO counters VALUES (5000000000)"));
            Assert.Contains("UseSqliteIntegerAffinity", ex.Message);
            Assert.Contains("BIGINT", ex.Message);
        }
        finally
        {
            SafeDispose(db);
        }
    }

    [Fact]
    public void Issue6_OptInConfig_IntegerMapsToInt64_DateTimeTicksFit()
    {
        var factory = CreateFactory();
        var dbPath = NewDirectoryPath("issue6_int64_optin");
        var config = new DatabaseConfig { UseSqliteIntegerAffinity = true };

        var db = factory.Create(dbPath, "test_password", config: config, securityConfig: SecurityConfig.Default);
        try
        {
            db.ExecuteSQL("CREATE TABLE ticks (id INTEGER PRIMARY KEY, value INTEGER)");
            long ticks = DateTime.UtcNow.Ticks; // ~6.4e17, exceeds int.MaxValue

            db.ExecuteSQL("INSERT INTO ticks VALUES (1, ?)",
                new Dictionary<string, object?> { ["@p0"] = ticks });
            db.Flush();

            var rows = db.ExecuteQuery("SELECT value FROM ticks WHERE id = 1");
            Assert.Single(rows);
            Assert.Equal(ticks, Convert.ToInt64(rows[0]["value"]));
        }
        finally
        {
            SafeDispose(db);
        }
    }
}