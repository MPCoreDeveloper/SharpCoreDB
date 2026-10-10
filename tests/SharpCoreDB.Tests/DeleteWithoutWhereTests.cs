// <copyright file="DeleteWithoutWhereTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// <c>DELETE FROM t</c> with no WHERE deletes every row in SQLite, and the table layer already reads a
/// null/empty WHERE that way (the full-scan filter in <c>Table.CRUD</c> is
/// <c>string.IsNullOrEmpty(where) || EvaluateSimpleWhere(...)</c>). The SQL parser required a WHERE, so the
/// statement threw "Invalid DELETE syntax" — a missing capability rather than a broken one. Found in
/// session 73 while profiling the second batch dispatcher; fixed in <c>SqlParser.DML.cs</c> (the live
/// <c>DeleteRegex</c>) and in the unreachable <c>DatabaseExtensions.ExecuteDeleteInternal</c> copy. The
/// last two facts guard the other direction: the WHERE must still be parsed and honoured.
/// </summary>
public sealed class DeleteWithoutWhereTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dir;

    public DeleteWithoutWhereTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dir = Path.Combine(Path.GetTempPath(), $"SCDB_DeleteNoWhere_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private IDatabase NewDatabase()
    {
        IDatabase db = _factory.Create(
            _dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT)");
        return db;
    }

    private static void Seed(IDatabase db)
    {
        db.ExecuteSQL("INSERT INTO docs VALUES (1, 'a')");
        db.ExecuteSQL("INSERT INTO docs VALUES (2, 'b')");
        db.ExecuteSQL("INSERT INTO docs VALUES (3, 'c')");
    }

    [Fact]
    public async Task DeleteFrom_WithoutWhere_RemovesEveryRow_AndReportsTheCount()
    {
        await using IDatabase db = NewDatabase();
        Seed(db);

        db.ExecuteSQL("DELETE FROM docs");

        Assert.Equal(3, db.GetLastChanges());
        Assert.Empty(db.ExecuteQuery("SELECT * FROM docs"));
    }

    [Fact]
    public async Task DeleteFrom_WithoutWhere_OnAnEmptyTable_ReportsNoChanges()
    {
        await using IDatabase db = NewDatabase();

        db.ExecuteSQL("DELETE FROM docs");

        Assert.Equal(0, db.GetLastChanges());
    }

    [Fact]
    public async Task DeleteFrom_WithoutWhere_ThroughTheBatchDispatcher_RemovesEveryRow()
    {
        await using IDatabase db = NewDatabase();
        Seed(db);

        db.ExecuteBatchSQL(["DELETE FROM docs"]);

        Assert.Empty(db.ExecuteQuery("SELECT * FROM docs"));
    }

    [Fact]
    public async Task DeleteFrom_WithoutWhere_OnAMissingTable_StillThrows()
    {
        await using IDatabase db = NewDatabase();

        var ex = Assert.Throws<InvalidOperationException>(() => db.ExecuteSQL("DELETE FROM nope"));
        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteFrom_WithWhere_StillDeletesOnlyTheMatchingRows()
    {
        await using IDatabase db = NewDatabase();
        Seed(db);

        db.ExecuteSQL("DELETE FROM docs WHERE id = 2");

        Assert.Equal(1, db.GetLastChanges());
        List<Dictionary<string, object>> remaining = db.ExecuteQuery("SELECT * FROM docs");
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, r => Convert.ToInt32(r["id"]) == 2);
    }

    [Fact]
    public async Task DeleteFrom_WithoutWhere_WithTrailingSemicolon_RemovesEveryRow()
    {
        await using IDatabase db = NewDatabase();
        Seed(db);

        db.ExecuteSQL("DELETE FROM docs;");

        Assert.Empty(db.ExecuteQuery("SELECT * FROM docs"));
    }
}
