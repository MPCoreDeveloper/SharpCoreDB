// <copyright file="BatchStatementOrderTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// A batch applies its statements in the order the caller gave them, exactly as SQLite does.
/// <para>
/// <see cref="Database.ExecuteBatchSQL"/> classifies a batch into INSERT / UPDATE / DELETE buckets and runs
/// the buckets in a fixed order (inserts, then updates, then deletes). That is a performance win for a
/// homogeneous batch, and a correctness bug for a mixed one: a <c>[DELETE …, INSERT …]</c> batch inserted
/// the new row first and then deleted it, leaving the table empty.
/// </para>
/// <para>
/// Found 2026-11-13 while closing mutation-testing survivors: <c>SharpCoreDbProjectionCheckpointStore</c>
/// saves a checkpoint with exactly that statement pair, so overwriting a checkpoint deleted it instead of
/// replacing it — a projection would have re-processed the same events for ever. These tests pin the
/// ordering at the layer that owns the defect, on both the sync and the async dispatcher.
/// </para>
/// </summary>
public sealed class BatchStatementOrderTests : IAsyncDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];

    public BatchStatementOrderTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public void ExecuteBatchSQL_DeleteThenInsert_AppliesInCallerOrder()
    {
        // Arrange
        var db = CreateSeededDatabase();

        // Act — the shape the projection checkpoint store uses for an overwrite
        db.ExecuteBatchSQL(
        [
            "DELETE FROM checkpoints WHERE name = 'orders'",
            "INSERT INTO checkpoints VALUES ('orders', 20)",
        ]);

        // Assert — the inserted row survives, and the deleted one is gone
        Assert.Equal(20L, ReadSequence(db, "orders"));
        Assert.Single(db.ExecuteQuery("SELECT name, seq FROM checkpoints"));
    }

    [Fact]
    public void ExecuteBatchSQL_InsertThenDelete_RemovesTheInsertedRow()
    {
        // Arrange
        var db = CreateSeededDatabase();

        // Act
        db.ExecuteBatchSQL(
        [
            "INSERT INTO checkpoints VALUES ('invoices', 30)",
            "DELETE FROM checkpoints WHERE name = 'invoices'",
        ]);

        // Assert — the reverse order must also be honoured
        Assert.Null(ReadSequence(db, "invoices"));
        Assert.Equal(10L, ReadSequence(db, "orders"));
    }

    [Fact]
    public void ExecuteBatchSQL_AllInserts_InsertsEveryRow()
    {
        // Arrange — a homogeneous batch keeps the grouped fast path
        var db = CreateSeededDatabase();

        // Act
        db.ExecuteBatchSQL(
        [
            "INSERT INTO checkpoints VALUES ('a', 1)",
            "INSERT INTO checkpoints VALUES ('b', 2)",
            "INSERT INTO checkpoints VALUES ('c', 3)",
        ]);

        // Assert
        Assert.Equal(1L, ReadSequence(db, "a"));
        Assert.Equal(2L, ReadSequence(db, "b"));
        Assert.Equal(3L, ReadSequence(db, "c"));
        Assert.Equal(4, db.ExecuteQuery("SELECT name, seq FROM checkpoints").Count);
    }

    [Fact]
    public void ExecuteBatchSQL_UpdateThenDelete_AppliesInCallerOrder()
    {
        // Arrange
        var db = CreateSeededDatabase();

        // Act
        db.ExecuteBatchSQL(
        [
            "UPDATE checkpoints SET seq = 99 WHERE name = 'orders'",
            "DELETE FROM checkpoints WHERE name = 'orders'",
        ]);

        // Assert — the update happened first, then the row was removed
        Assert.Null(ReadSequence(db, "orders"));
        Assert.Empty(db.ExecuteQuery("SELECT name, seq FROM checkpoints"));
    }

    [Fact]
    public async Task ExecuteBatchSQLAsync_DeleteThenInsert_AppliesInCallerOrder()
    {
        // Arrange
        var db = CreateSeededDatabase();

        // Act — the async dispatcher shares the classifier, so it must share the ordering contract
        await db.ExecuteBatchSQLAsync(
        [
            "DELETE FROM checkpoints WHERE name = 'orders'",
            "INSERT INTO checkpoints VALUES ('orders', 21)",
        ]);

        // Assert
        Assert.Equal(21L, ReadSequence(db, "orders"));
        Assert.Single(db.ExecuteQuery("SELECT name, seq FROM checkpoints"));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _dbs)
        {
            await db.DisposeAsync();
        }

        foreach (var path in _paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private IDatabase CreateSeededDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SCDB_BatchOrder_{Guid.NewGuid():N}");
        _paths.Add(path);
        var db = _factory.Create(path, "batch-order-test-password");
        _dbs.Add(db);

        db.ExecuteSQL("CREATE TABLE checkpoints (name TEXT, seq LONG)");
        db.ExecuteSQL("INSERT INTO checkpoints VALUES ('orders', 10)");
        db.Flush();

        return db;
    }

    private static long? ReadSequence(IDatabase db, string name)
    {
        var rows = db.ExecuteQuery($"SELECT name, seq FROM checkpoints WHERE name = '{name}'");
        return rows.Count == 0 ? null : Convert.ToInt64(rows[0]["seq"], System.Globalization.CultureInfo.InvariantCulture);
    }
}
