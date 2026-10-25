// <copyright file="SqlVectorQueryTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch.Tests;

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;

/// <summary>
/// End-to-end coverage for the planner branch that answers
/// <c>ORDER BY vec_distance_*(column, vector) LIMIT k</c> from a vector index (Phase 5.4).
/// <para>
/// These tests exist because the branch was dead in two independent ways at once: the planner call
/// site was guarded by <c>SqlParser.VectorQueryOptimizer is not null</c>, and that member was an
/// <c>internal static</c> that nothing ever assigned (so <c>CREATE VECTOR INDEX</c> built nothing
/// either), and the method behind the guard had been replaced by a stub returning null. Unit tests
/// over <c>VectorQueryOptimizer</c> passed throughout, because the optimizer itself was fine — it
/// was never called. Only a query through <c>Database</c> can tell the difference, which is why
/// these go through <c>AddVectorSupport()</c> and SQL rather than constructing the optimizer.
/// </para>
/// </summary>
public sealed class SqlVectorQueryTests : IDisposable
{
    private const string QueryVectorLiteral = "'[1,0,0,0]'";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"SCDB_VectorSql_{Guid.NewGuid():N}");
    private readonly ServiceProvider _provider;
    private IDatabase? _db;

    /// <summary>Registers vector support exactly as a host application would.</summary>
    public SqlVectorQueryTests()
    {
        _provider = new ServiceCollection()
            .AddSharpCoreDB()
            .AddVectorSupport()
            .BuildServiceProvider();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        (_db as IDisposable)?.Dispose();
        _provider.Dispose();

        try
        {
            if (Directory.Exists(_path))
                Directory.Delete(_path, true);
        }
        catch (IOException)
        {
            // A leaked temp directory must not fail the suite.
        }
    }

    [Fact]
    public void OrderByDistanceAlias_WithIndex_ReturnsNearestRowsProjectedOnTheSelectList()
    {
        var db = SeededDatabase();
        db.ExecuteSQL("CREATE VECTOR INDEX idx_docs ON docs(embedding) USING FLAT");

        var rows = Query(
            db,
            $"SELECT id, body, vec_distance_cosine(embedding, {QueryVectorLiteral}) AS distance " +
            "FROM docs ORDER BY distance LIMIT 2");

        Assert.Equal(new[] { 1L, 3L }, rows.Select(r => Convert.ToInt64(r["id"], CultureInfo.InvariantCulture)).ToArray());

        // Cosine distance to [1,0,0,0]: id 1 is identical (0.0), id 3 has similarity 0.8 (0.2).
        Assert.Equal(0.0f, Convert.ToSingle(rows[0]["distance"], CultureInfo.InvariantCulture), 3);
        Assert.Equal(0.2f, Convert.ToSingle(rows[1]["distance"], CultureInfo.InvariantCulture), 3);

        // The projection is the SELECT list, not the whole row: the restored implementation copied
        // every column and appended "distance" whatever the query asked for.
        Assert.Equal(3, rows[0].Count);
        Assert.True(rows[0].ContainsKey("id") && rows[0].ContainsKey("body") && rows[0].ContainsKey("distance"));
    }

    [Fact]
    public void OrderByDistanceFunction_WithoutDistanceInTheSelectList_IsStillServedFromTheIndex()
    {
        var db = SeededDatabase();
        db.ExecuteSQL("CREATE VECTOR INDEX idx_docs ON docs(embedding) USING FLAT");

        var rows = Query(
            db,
            $"SELECT id FROM docs ORDER BY vec_distance_cosine(embedding, {QueryVectorLiteral}) LIMIT 1");

        Assert.Single(rows);
        Assert.Equal(1L, Convert.ToInt64(rows[0]["id"], CultureInfo.InvariantCulture));

        // ORDER BY named the function, not an alias of it; the distance was not selected.
        Assert.Single(rows[0]);
    }

    [Fact]
    public void OrderByDistanceAlias_WithBoundVectorParameter_IsServedFromTheIndex()
    {
        var db = SeededDatabase();
        db.ExecuteSQL("CREATE VECTOR INDEX idx_docs ON docs(embedding) USING FLAT");

        // A float[] parameter is bound to the bracketed vector literal by ParameterBinder, so the
        // planner sees the same text an inline literal would produce.
        var rows = db.ExecuteQuery(
            "SELECT id, vec_distance_cosine(embedding, @q) AS distance FROM docs ORDER BY distance LIMIT 1",
            new Dictionary<string, object?> { ["q"] = new float[] { 1, 0, 0, 0 } });

        Assert.Single(rows);
        Assert.Equal(1L, Convert.ToInt64(rows[0]["id"], CultureInfo.InvariantCulture));
        Assert.True(rows[0].ContainsKey("distance"));
    }

    [Fact]
    public void OrderByDistanceDescending_IsNotServedFromTheIndex()
    {
        var db = SeededDatabase();
        db.ExecuteSQL("CREATE VECTOR INDEX idx_docs ON docs(embedding) USING FLAT");

        var rows = Query(
            db,
            $"SELECT id, body, vec_distance_cosine(embedding, {QueryVectorLiteral}) AS distance " +
            "FROM docs ORDER BY distance DESC LIMIT 2");

        Assert.Equal(2, rows.Count);

        // A top-k index holds the k nearest rows; the k farthest cannot be derived from it, so the
        // query must keep its scan behaviour rather than receive a nearest-k answer labelled as DESC.
        // The scan path neither projects nor computes the distance, which is what this asserts.
        Assert.DoesNotContain("distance", rows[0].Keys);
        Assert.Contains("embedding", rows[0].Keys);
    }

    [Fact]
    public void OrderByDistanceWithWhereClause_IsNotServedFromTheIndex()
    {
        var db = SeededDatabase();
        db.ExecuteSQL("CREATE VECTOR INDEX idx_docs ON docs(embedding) USING FLAT");

        // Applying the predicate after the index chose its k would drop matching rows instead of
        // fetching further down the ranking, so a filtered top-k stays on the scan.
        var rows = Query(
            db,
            $"SELECT id, body, vec_distance_cosine(embedding, {QueryVectorLiteral}) AS distance " +
            "FROM docs WHERE id > 1 ORDER BY distance LIMIT 2");

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain("distance", rows[0].Keys);
        Assert.Contains("embedding", rows[0].Keys);
    }

    [Fact]
    public void DropVectorIndex_ReturnsTheQueryToTheScanPath()
    {
        var db = SeededDatabase();
        db.ExecuteSQL("CREATE VECTOR INDEX idx_docs ON docs(embedding) USING FLAT");

        var optimized = Query(
            db,
            $"SELECT id, vec_distance_cosine(embedding, {QueryVectorLiteral}) AS distance " +
            "FROM docs ORDER BY distance LIMIT 2");
        Assert.True(optimized[0].ContainsKey("distance"));

        db.ExecuteSQL("DROP VECTOR INDEX idx_docs ON docs");

        var scanned = Query(
            db,
            $"SELECT id, vec_distance_cosine(embedding, {QueryVectorLiteral}) AS distance " +
            "FROM docs ORDER BY distance LIMIT 2");
        Assert.DoesNotContain("distance", scanned[0].Keys);
    }

    [Fact]
    public void OrderByDistanceWithoutAnyIndex_FallsBackToTheScan()
    {
        var db = SeededDatabase();

        var rows = Query(
            db,
            $"SELECT id, vec_distance_cosine(embedding, {QueryVectorLiteral}) AS distance " +
            "FROM docs ORDER BY distance LIMIT 2");

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain("distance", rows[0].Keys);
    }

    /// <summary>
    /// Creates a database through the DI factory with four documents whose distances to
    /// <c>[1,0,0,0]</c> are distinct enough to identify, and where two rows tie on distance so the
    /// index's tie-break (id, ascending) is exercised too.
    /// </summary>
    /// <returns>The open database; the caller disposes it.</returns>
    private IDatabase SeededDatabase()
    {
        if (_db is not null)
        {
            return _db;
        }

        var factory = _provider.GetRequiredService<DatabaseFactory>();
        var db = factory.Create(_path, "pw", isReadOnly: false, config: new DatabaseConfig());

        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, body TEXT, embedding VECTOR(4))");
        db.ExecuteSQL("INSERT INTO docs (id, body, embedding) VALUES (1, 'nearest', [1,0,0,0])");
        db.ExecuteSQL("INSERT INTO docs (id, body, embedding) VALUES (2, 'orthogonal', [0,1,0,0])");
        db.ExecuteSQL("INSERT INTO docs (id, body, embedding) VALUES (3, 'close', [0.8,0.6,0,0])");
        db.ExecuteSQL("INSERT INTO docs (id, body, embedding) VALUES (4, 'far', [0,0,1,0])");

        _db = db;
        return db;
    }

    private static List<Dictionary<string, object>> Query(IDatabase db, string sql)
        => db.ExecuteQuery(sql, new Dictionary<string, object?>());
}

