// <copyright file="VectorColumnRegressionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Optimizations;
using Xunit;

/// <summary>
/// Regression tests for the VECTOR(N) defect family found while fixing
/// <see cref="VectorColumnStorageTests"/>: the vector case was missing from eight per-type switches and from
/// the value tokenizers, so a vector was stored as the literal text <c>"System.Single[]"</c> (or came back as
/// a <c>JsonElement</c> from the single-file store). Each test here pins one of the copies that was fixed —
/// the batched writer, the streaming encoder/decoder pair, the multi-row VALUES tokenizer, the parameter
/// binder, the UPDATE SET splitter — plus the loud failures that replaced the silent ones.
/// </summary>
public sealed class VectorColumnRegressionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public VectorColumnRegressionTests()
    {
        var provider = new ServiceCollection().AddSharpCoreDB().BuildServiceProvider();
        _factory = provider.GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_VectorReg_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    [Fact]
    public void InsertBatch_TableApi_WithFloatArrays_RoundTrips()
    {
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(3))");
            db.TryGetTable("docs", out var table);

            table.InsertBatch(
            [
                new Dictionary<string, object> { ["id"] = 1, ["embedding"] = new float[] { 0.5f, 0.25f, 0.125f } },
                new Dictionary<string, object> { ["id"] = 2, ["embedding"] = new float[] { -1f, 2f, 0.75f } },
            ]);

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], 0.5f, 0.25f, 0.125f);
            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 2")["embedding"], -1f, 2f, 0.75f);
        });
    }

    [Fact]
    public void InsertBatchFromBuffer_StreamingEncoder_DecodesVectorBack()
    {
        // StreamingRowEncoder and BinaryRowDecoder are one write/read pair; the vector case was missing from
        // both, so this round trip decoded the blob into the wrong shape (or threw).
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(3))");
            db.TryGetTable("docs", out var table);

            using var encoder = new StreamingRowEncoder(table.Columns, table.ColumnTypes);
            Assert.True(encoder.EncodeRow(
                new Dictionary<string, object> { ["id"] = 7, ["embedding"] = new float[] { 0.1f, 0.2f, 0.3f } }));

            table.InsertBatchFromBuffer(encoder.GetBatchData(), encoder.GetRowCount());

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 7")["embedding"], 0.1f, 0.2f, 0.3f);
        });
    }

    [Fact]
    public void MultiRowInsert_TwoVectorLiterals_ThenSelect_ReturnsBoth()
    {
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, [0.5, 0.25]), (2, [-0.5, -0.25])");

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], 0.5f, 0.25f);
            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 2")["embedding"], -0.5f, -0.25f);
        });
    }

    [Fact]
    public void Insert_QuotedJsonVectorLiteral_RoundTrips()
    {
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, '[0.5, 0.25]')");

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], 0.5f, 0.25f);
        });
    }

    [Fact]
    public void Insert_Base64VectorLiteral_RoundTrips()
    {
        float[] expected = [0.5f, -0.25f];
        string base64 = Convert.ToBase64String(MemoryMarshal.AsBytes(expected.AsSpan()));

        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.ExecuteSQL($"INSERT INTO docs (id, embedding) VALUES (1, '{base64}')");

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], expected);
        });
    }

    [Fact]
    public void Update_TwoAssignmentsIncludingVector_RoundTrips()
    {
        // The SET splitter used to cut on every comma, so `SET tag = 'x', embedding = [0.1, 0.2]` became two
        // assignments with half a vector each.
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, tag TEXT, embedding VECTOR(2))");
            db.ExecuteSQL("INSERT INTO docs (id, tag, embedding) VALUES (1, 'a', [1.0, 0.0])");
            db.ExecuteSQL("UPDATE docs SET tag = 'b', embedding = [0.1, 0.2] WHERE id = 1");

            var row = Row(db, "SELECT tag, embedding FROM docs WHERE id = 1");
            Assert.Equal("b", row["tag"]);
            AssertVectorEquals(row["embedding"], 0.1f, 0.2f);
        });
    }

    [Fact]
    public void Insert_ReadOnlyMemoryParameter_RoundTrips()
    {
        // ReadOnlyMemory<float> is what Microsoft.Extensions.AI embeddings and EF Core hand over.
        ReadOnlyMemory<float> memory = new float[] { 0.5f, 0.25f, 0.125f };

        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(3))");
            db.ExecuteSQL(
                "INSERT INTO docs (id, embedding) VALUES (@id, @embedding)",
                new Dictionary<string, object?> { ["@id"] = 1, ["@embedding"] = memory });

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], 0.5f, 0.25f, 0.125f);
        });
    }

    [Fact]
    public void Insert_DoubleArrayParameter_RoundTrips()
    {
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.ExecuteSQL(
                "INSERT INTO docs (id, embedding) VALUES (@id, @embedding)",
                new Dictionary<string, object?> { ["@id"] = 1, ["@embedding"] = new double[] { 0.5, -0.25 } });

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], 0.5f, -0.25f);
        });
    }

    [Fact]
    public void Insert_InvalidVectorLiteral_Throws()
    {
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");

            var error = Assert.Throws<InvalidOperationException>(
                () => db.ExecuteSQL("INSERT INTO docs (id, embedding) VALUES (1, 'not-a-vector')"));
            Assert.Contains("VECTOR", error.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Insert_NonVectorValue_ThrowsInsteadOfStoringToString()
    {
        // The defect this whole family came from: an unconvertible value fell through to value.ToString() and
        // was stored as text. It must be a type error instead, for a parameter and for a column-shaped value.
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");

            var error = Assert.Throws<InvalidOperationException>(
                () => db.ExecuteSQL(
                    "INSERT INTO docs (id, embedding) VALUES (@id, @embedding)",
                    new Dictionary<string, object?> { ["@id"] = 1, ["@embedding"] = Guid.NewGuid() }));
            Assert.Contains("VECTOR", error.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ObjectApi_JsonStringValue_ThenSelect_ReturnsFloatArray()
    {
        // The object API accepts the same textual forms the SQL literal does, and the fixed-width codec must
        // write the VEC\0 blob for a VECTOR column even when the caller handed over a string (the codec's
        // string fast path used to store raw UTF-8 in the slot, which the reader then decoded as float bytes).
        WithDb(db =>
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, embedding VECTOR(2))");
            db.TryGetTable("docs", out var table);

            table.Insert(new Dictionary<string, object> { ["id"] = 1, ["embedding"] = "[0.5, 0.25]" });

            AssertVectorEquals(Row(db, "SELECT embedding FROM docs WHERE id = 1")["embedding"], 0.5f, 0.25f);
        });
    }

    private static Dictionary<string, object> Row(IDatabase db, string sql)
    {
        var rows = db.ExecuteQuery(sql, new Dictionary<string, object?>());
        Assert.Single(rows);
        return rows[0];
    }

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

    private void WithDb(Action<IDatabase> action)
    {
        var db = _factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig());
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
