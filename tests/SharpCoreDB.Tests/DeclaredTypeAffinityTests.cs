// <copyright file="DeclaredTypeAffinityTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Regression tests for SQLite column-affinity resolution of declared column types
/// (see <c>src/SharpCoreDB/Services/SqlTypeAffinity.cs</c>) and the views that report it.
/// The DDL type map used to recognise canonical names only and route everything else to
/// <see cref="DataType.String"/>, so <c>DOUBLE</c>/<c>FLOAT</c> (SQLite: REAL affinity) and
/// <c>INT</c>/<c>SMALLINT</c> (SQLite: INTEGER affinity) were TEXT columns, and a
/// <c>DECIMAL(10,2)</c> was TEXT because the size argument defeated the exact-name match.
/// </summary>
public sealed class DeclaredTypeAffinityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseFactory _factory;
    private readonly Database _db;

    public DeclaredTypeAffinityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"declared_type_affinity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dbPath);

        var provider = new ServiceCollection().AddSharpCoreDB().BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();

        _db = new Database(
            provider,
            _dbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(100);

        if (Directory.Exists(_dbPath))
        {
            try { Directory.Delete(_dbPath, recursive: true); } catch { }
        }
    }

    /// <summary>Reads the <c>information_schema.columns.data_type</c> the engine reports for a column.</summary>
    private string DataTypeOf(string tableName, string columnName)
    {
        var rows = _db.ExecuteQuery(
            $"SELECT data_type FROM information_schema.columns " +
            $"WHERE table_name = '{tableName}' AND column_name = '{columnName}'");

        Assert.Single(rows);
        return Convert.ToString(rows[0]["data_type"], CultureInfo.InvariantCulture) ?? string.Empty;
    }

    // ──────────────────────────────────────────────
    //  The affinity shortfall
    // ──────────────────────────────────────────────

    [Fact]
    public void CreateTable_WithDoubleColumn_IsRealAndStoresFractionalValues()
    {
        _db.ExecuteSQL("CREATE TABLE metrics (id INTEGER, score DOUBLE)");

        Assert.Equal("double precision", DataTypeOf("metrics", "score"));

        _db.ExecuteSQL("INSERT INTO metrics (id, score) VALUES (1, 2.5)");

        var rows = _db.ExecuteQuery("SELECT score FROM metrics WHERE id = 1");
        Assert.Equal(2.5d, Convert.ToDouble(rows[0]["score"], CultureInfo.InvariantCulture));

        // A REAL column must also answer a numeric predicate on its own values.
        var filtered = _db.ExecuteQuery("SELECT id FROM metrics WHERE score > 2");
        Assert.Single(filtered);
    }

    [Fact]
    public void CreateTable_WithIntColumn_IsIntegerAndMatchesNumericPredicate()
    {
        _db.ExecuteSQL("CREATE TABLE counters (id INT PRIMARY KEY, name TEXT)");

        Assert.Equal("integer", DataTypeOf("counters", "id"));

        _db.ExecuteSQL("INSERT INTO counters (id, name) VALUES (5, 'five')");

        var rows = _db.ExecuteQuery("SELECT name FROM counters WHERE id = 5");
        Assert.Single(rows);
        Assert.Equal("five", rows[0]["name"]);
    }

    [Fact]
    public void CreateTable_WithFloatAndSmallint_UsesTheirSqliteBuckets()
    {
        _db.ExecuteSQL("CREATE TABLE readings (ratio FLOAT, level SMALLINT)");

        Assert.Equal("double precision", DataTypeOf("readings", "ratio"));
        Assert.Equal("integer", DataTypeOf("readings", "level"));
    }

    [Fact]
    public void CreateTable_WithParameterizedTypes_ResolvesTheNameNotTheSize()
    {
        _db.ExecuteSQL("CREATE TABLE catalog (name VARCHAR(255), code CHAR(3), amount DECIMAL(10,2))");

        Assert.Equal("text", DataTypeOf("catalog", "name"));
        Assert.Equal("text", DataTypeOf("catalog", "code"));
        Assert.Equal("numeric", DataTypeOf("catalog", "amount"));

        _db.ExecuteSQL("INSERT INTO catalog (name, code, amount) VALUES ('widget', 'WID', 12.34)");
        var rows = _db.ExecuteQuery("SELECT amount FROM catalog WHERE name = 'widget'");
        Assert.Equal(12.34m, Convert.ToDecimal(rows[0]["amount"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CreateTable_WithMultiWordAndUnknownTypes_ClassifiesEveryOne()
    {
        _db.ExecuteSQL(
            "CREATE TABLE exotic (a DOUBLE PRECISION, b UNSIGNED BIG INT, c MYSTERY_TYPE, d NUMERIC(18,4))");

        Assert.Equal("double precision", DataTypeOf("exotic", "a"));
        Assert.Equal("integer", DataTypeOf("exotic", "b"));

        // Deliberate deviation from SQLite's NUMERIC catch-all: an unrecognised name keeps the
        // engine's historical TEXT mapping, because a strongly typed column cannot hold numbers
        // and non-numeric text in one type the way SQLite's NUMERIC affinity can.
        Assert.Equal("text", DataTypeOf("exotic", "c"));

        Assert.Equal("numeric", DataTypeOf("exotic", "d"));
    }

    [Fact]
    public void CreateTable_WithTypeLessColumn_HasBlobAffinity()
    {
        // SQLite rule 3: no declared type means BLOB affinity. This used to throw
        // IndexOutOfRangeException because the parser indexed the type token unconditionally.
        _db.ExecuteSQL("CREATE TABLE raw_events (payload)");

        Assert.Equal("bytea", DataTypeOf("raw_events", "payload"));
    }

    [Fact]
    public void CreateTable_WithCanonicalEngineTypes_KeepsTheirMapping()
    {
        _db.ExecuteSQL(
            "CREATE TABLE canonical (a INTEGER, b BIGINT, c TEXT, d REAL, e BLOB, f BOOLEAN, " +
            "g DATETIME, h DECIMAL, i GUID, j ULID, k VECTOR(8))");

        Assert.Equal("integer", DataTypeOf("canonical", "a"));
        Assert.Equal("bigint", DataTypeOf("canonical", "b"));
        Assert.Equal("text", DataTypeOf("canonical", "c"));
        Assert.Equal("double precision", DataTypeOf("canonical", "d"));
        Assert.Equal("bytea", DataTypeOf("canonical", "e"));
        Assert.Equal("boolean", DataTypeOf("canonical", "f"));
        Assert.Equal("timestamp without time zone", DataTypeOf("canonical", "g"));
        Assert.Equal("numeric", DataTypeOf("canonical", "h"));
        Assert.Equal("uuid", DataTypeOf("canonical", "i"));
        Assert.Equal("text", DataTypeOf("canonical", "j"));
        Assert.Equal("vector", DataTypeOf("canonical", "k"));
    }

    [Fact]
    public void CreateTable_WithAliasNames_MapsToTheSameTypesAsTheirCanonicalForm()
    {
        _db.ExecuteSQL("CREATE TABLE aliases (a INT8, b BOOL, c UUID, d TIMESTAMP, e DATE)");

        Assert.Equal("bigint", DataTypeOf("aliases", "a"));
        Assert.Equal("boolean", DataTypeOf("aliases", "b"));
        Assert.Equal("uuid", DataTypeOf("aliases", "c"));
        Assert.Equal("timestamp without time zone", DataTypeOf("aliases", "d"));
        Assert.Equal("timestamp without time zone", DataTypeOf("aliases", "e"));
    }

    [Fact]
    public void CreateTable_WithAutoModifier_KeepsTheDeclaredTypeAndStillAutoGenerates()
    {
        // Regression: "ULID AUTO" / "GUID AUTO" must map to ULID/GUID, not to a TEXT column named
        // "ULID AUTO" — the CREATE TABLE path reads the declared type, and AUTO is a column modifier.
        _db.ExecuteSQL("CREATE TABLE auto_ids (id INTEGER PRIMARY KEY AUTO, ulid ULID AUTO, guid GUID AUTO)");

        Assert.Equal("integer", DataTypeOf("auto_ids", "id"));
        Assert.Equal("text", DataTypeOf("auto_ids", "ulid"));   // DataType.Ulid reports as text
        Assert.Equal("uuid", DataTypeOf("auto_ids", "guid"));

        _db.ExecuteSQL("INSERT INTO auto_ids (id) VALUES (1)");

        var rows = _db.ExecuteQuery("SELECT ulid, guid FROM auto_ids WHERE id = 1");
        Assert.Single(rows);
        Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(rows[0]["ulid"], CultureInfo.InvariantCulture)));
        Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(rows[0]["guid"], CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void AlterTable_AddColumn_WithDouble_UsesTheSameAffinityAsCreateTable()
    {
        _db.ExecuteSQL("CREATE TABLE events (id INTEGER)");
        _db.ExecuteSQL("ALTER TABLE events ADD COLUMN score DOUBLE");

        Assert.Equal("double precision", DataTypeOf("events", "score"));

        _db.ExecuteSQL("INSERT INTO events (id, score) VALUES (1, 4.5)");
        var rows = _db.ExecuteQuery("SELECT score FROM events WHERE id = 1");
        Assert.Equal(4.5d, Convert.ToDouble(rows[0]["score"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AlterTable_AddColumn_OnSingleFileDatabase_UsesTheSameAffinityAsCreateTable()
    {
        var singleFilePath = Path.Combine(Path.GetTempPath(), $"declared_type_affinity_{Guid.NewGuid():N}.scdb");

        try
        {
            var singleFileDb = _factory.CreateWithOptions(
                singleFilePath, "test_password", DatabaseOptions.CreateSingleFileDefault());

            singleFileDb.ExecuteSQL("CREATE TABLE events (id INTEGER)");
            singleFileDb.ExecuteSQL("ALTER TABLE events ADD COLUMN ratio FLOAT");

            var rows = singleFileDb.ExecuteQuery(
                "SELECT data_type FROM information_schema.columns " +
                "WHERE table_name = 'events' AND column_name = 'ratio'");

            Assert.Single(rows);
            Assert.Equal("double precision", Convert.ToString(rows[0]["data_type"], CultureInfo.InvariantCulture));

            (singleFileDb as IDisposable)?.Dispose();
        }
        finally
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(100);
            try { if (File.Exists(singleFilePath)) File.Delete(singleFilePath); } catch { }
        }
    }

    // ──────────────────────────────────────────────
    //  The resolver itself
    // ──────────────────────────────────────────────

    [Theory]
    [InlineData("INTEGER", false, DataType.Integer)]
    [InlineData("INTEGER", true, DataType.Long)]
    [InlineData("INT", false, DataType.Integer)]
    [InlineData("INT", true, DataType.Long)]
    [InlineData("SMALLINT", false, DataType.Integer)]
    [InlineData("TINYINT", false, DataType.Integer)]
    [InlineData("BIGINT", false, DataType.Long)]
    [InlineData("INT8", false, DataType.Long)]
    [InlineData("UNSIGNED BIG INT", false, DataType.Integer)]
    [InlineData("POINT", false, DataType.Integer)]
    [InlineData("DOUBLE", false, DataType.Real)]
    [InlineData("FLOAT", false, DataType.Real)]
    [InlineData("DOUBLE PRECISION", false, DataType.Real)]
    [InlineData("TEXT", false, DataType.String)]
    [InlineData("VARCHAR(255)", false, DataType.String)]
    [InlineData("CHARACTER VARYING(10)", false, DataType.String)]
    [InlineData("CLOB", false, DataType.String)]
    [InlineData("BLOB", false, DataType.Blob)]
    [InlineData("MYSTERY_TYPE", false, DataType.String)]
    [InlineData("BOOL", false, DataType.Boolean)]
    [InlineData("UUID", false, DataType.Guid)]
    [InlineData("ULID", false, DataType.Ulid)]
    [InlineData("NUMERIC", false, DataType.Decimal)]
    [InlineData("DECIMAL(10,2)", false, DataType.Decimal)]
    [InlineData("NUMBER", false, DataType.Decimal)]
    [InlineData("DATE", false, DataType.DateTime)]
    [InlineData("TIMESTAMP", false, DataType.DateTime)]
    [InlineData("ROWREF", false, DataType.RowRef)]
    [InlineData("VECTOR(1536)", false, DataType.Vector)]
    [InlineData("", false, DataType.Blob)]
    public void Resolve_DeclaredType_FollowsSqliteAffinity(
        string declaredType, bool useSqliteIntegerAffinity, DataType expected)
    {
        Assert.Equal(expected, SqlTypeAffinity.Resolve(declaredType, useSqliteIntegerAffinity));
    }

    [Theory]
    [InlineData("id INTEGER PRIMARY KEY", "INTEGER")]
    [InlineData("score DOUBLE PRECISION NOT NULL", "DOUBLE PRECISION")]
    [InlineData("name VARCHAR(255) UNIQUE", "VARCHAR")]
    [InlineData("amount DECIMAL(10, 2) DEFAULT 0", "DECIMAL")]
    [InlineData("created TIMESTAMP DEFAULT current_timestamp", "TIMESTAMP")]
    [InlineData("payload", "")]
    [InlineData("payload CHECK (payload IS NOT NULL)", "")]
    [InlineData("ulid ULID AUTO", "ULID")]
    [InlineData("guid GUID AUTO", "GUID")]
    [InlineData("id INTEGER PRIMARY KEY AUTO", "INTEGER")]
    [InlineData("counter BIGINT AUTOINCREMENT", "BIGINT")]
    public void ExtractDeclaredType_ColumnDefinition_ReturnsTheTypeWithoutConstraints(
        string columnDefinition, string expected)
    {
        var parts = columnDefinition.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(expected, SqlTypeAffinity.ExtractDeclaredType(parts));
    }
}
