// <copyright file="VectorColumnStorageTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Round-trip tests for <c>VECTOR(N)</c> columns (see <c>docs/Vectors/TECHNICAL_SPEC.md</c> §7,
/// "Vectors are stored as typed BLOBs ... the <c>DataType.Vector</c> enum value tells the
/// serializer to use the optimized vector binary format").
/// The vector cases were missing from every row codec, so a VECTOR value fell through the
/// serializer's <c>default</c> branch and was persisted as <c>value.ToString()</c> — a
/// <c>float[]</c> became the literal text <c>"System.Single[]"</c> and the data was silently
/// lost, in both the batched and the row-at-a-time write path.
/// </summary>
public sealed class VectorColumnStorageTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private readonly string _filePath;

    public VectorColumnStorageTests()
    {
        var provider = new ServiceCollection().AddSharpCoreDB().BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_VectorDir_{Guid.NewGuid():N}");
        _filePath = Path.Combine(Path.GetTempPath(), $"SCDB_VectorFile_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
        try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
    }

    /// <summary>Storage variants that must all keep vector values intact.</summary>
    public static TheoryData<string, DatabaseConfig> Variants => new()
    {
        { "directory-fixedwidth-default", new DatabaseConfig() },
        { "directory-legacy-variable", new DatabaseConfig { AutoFixedWidthRecords = false } },
        { "singlefile-default", new DatabaseConfig() },
    };

    [Fact]
    public void Insert_VectorLiteral_ThenSelect_ReturnsFloatArray()
    {
        WithDb(_dirPath, new DatabaseConfig(), db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(4))");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, [0.5, 0.25, 0.125, 0.0625])");

            AssertVectorEquals(SingleRow(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"],
                0.5f, 0.25f, 0.125f, 0.0625f);
        });
    }

    [Fact]
    public void Insert_VectorLiteral_StoresVectorBinaryBlob()
    {
        // Spec §7: the column payload is the vector binary format — magic 'VEC\0', version 1,
        // flags, then the 4-byte dimension count. Written unencrypted so the bytes are visible.
        string path = Path.Combine(Path.GetTempPath(), $"SCDB_VectorBlob_{Guid.NewGuid():N}");
        var config = new DatabaseConfig { NoEncryptMode = true };
        try
        {
            WithDb(path, config, db =>
            {
                db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(4))");
                db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, [0.5, 0.25, 0.125, 0.0625])");
            });

            var payloads = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(File.ReadAllBytes)
                .ToList();

            Assert.True(payloads.Any(bytes => ContainsVectorHeader(bytes, dimensions: 4)),
                "no file holds the 'VEC\\0' version-1 vector header with a 4-dimension count");
        }
        finally
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }

    [Fact]
    public void Insert_JsonStringParameter_ThenSelect_ReturnsFloatArray()
    {
        WithDb(_dirPath, new DatabaseConfig(), db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(3))");
            db.ExecuteSQL(
                "INSERT INTO docs (id, embedding) VALUES (@id, @embedding)",
                new Dictionary<string, object?> { ["@id"] = 1, ["@embedding"] = "[1.5, 2.5, 3.5]" });

            AssertVectorEquals(SingleRow(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"],
                1.5f, 2.5f, 3.5f);
        });
    }

    [Fact]
    public void Insert_FloatArrayParameter_ThenSelect_ReturnsFloatArray()
    {
        // The EF Core provider and the object API both hand the engine a float[]; the binder must
        // render it as a vector literal instead of 'System.Single[]'.
        WithDb(_dirPath, new DatabaseConfig(), db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(3))");
            db.ExecuteSQL(
                "INSERT INTO docs (id, embedding) VALUES (@id, @embedding)",
                new Dictionary<string, object?> { ["@id"] = 1, ["@embedding"] = new float[] { 0.5f, -0.25f, 0f } });

            AssertVectorEquals(SingleRow(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"],
                0.5f, -0.25f, 0f);
        });
    }

    [Fact]
    public void Update_VectorValue_ThenSelect_ReturnsUpdatedFloatArray()
    {
        WithDb(_dirPath, new DatabaseConfig(), db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, [1.0, 0.0])");
            db.ExecuteSQL("UPDATE docs SET embedding = [0.0, 1.0] WHERE id = 1");

            AssertVectorEquals(SingleRow(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"],
                0f, 1f);
        });
    }

    [Fact]
    public void Insert_NullVector_ThenSelect_ReturnsNull()
    {
        WithDb(_dirPath, new DatabaseConfig(), db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, NULL)");

            var row = SingleRow(db, "SELECT embedding FROM docs WHERE id = 1");
            Assert.True(row["embedding"] is null or DBNull, $"expected NULL, got '{row["embedding"]}'");
        });
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void RoundTrip_Reopen_KeepsVectorValues(string variant, DatabaseConfig config)
    {
        string path = variant.StartsWith("singlefile", StringComparison.Ordinal) ? _filePath : _dirPath;
        if (Directory.Exists(path)) Directory.Delete(path, true);
        if (File.Exists(path)) File.Delete(path);

        WithDb(path, config, db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(4))");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, [0.5, 0.25, 0.125, 0.0625])");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (2, [-1.5, 2.0, 0.0, 0.75])");
        });

        WithDb(path, config, db =>
        {
            var rows = db.ExecuteQuery("SELECT id, embedding FROM docs ORDER BY id", new Dictionary<string, object?>());
            Assert.Equal(2, rows.Count);
            AssertVectorEquals(rows[0]["embedding"], 0.5f, 0.25f, 0.125f, 0.0625f);
            AssertVectorEquals(rows[1]["embedding"], -1.5f, 2.0f, 0.0f, 0.75f);
        });
    }

    /// <summary>Asserts the column returned <c>expected</c> as a <see cref="float"/> array.</summary>
    private static void AssertVectorEquals(object? value, params float[] expected)
    {
        if (value is not float[] actual)
        {
            Assert.Fail(
                $"VECTOR column returned {value?.GetType().Name ?? "null"} ({value ?? "null"}) " +
                $"instead of float[{expected.Length}]");
            return;
        }

        Assert.Equal(expected, actual);
    }

    private static bool ContainsVectorHeader(byte[] data, int dimensions)
    {
        for (int i = 0; i + 12 <= data.Length; i++)
        {
            if (data[i] != (byte)'V' || data[i + 1] != (byte)'E' || data[i + 2] != (byte)'C' || data[i + 3] != 0)
                continue;

            if (data[i + 4] == 1 && data[i + 5] == 0 && BitConverter.ToInt32(data, i + 8) == dimensions)
                return true;
        }

        return false;
    }

    private static Dictionary<string, object> SingleRow(IDatabase db, string sql)
    {
        var rows = db.ExecuteQuery(sql, new Dictionary<string, object?>());
        Assert.Single(rows);
        return rows[0];
    }

    private void WithDb(string path, DatabaseConfig config, Action<IDatabase> action)
    {
        var db = _factory.Create(path, "pw", isReadOnly: false, config: config);
        try
        {
            action(db);
            db.Flush();
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }
}
