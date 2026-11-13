// <copyright file="FunctionalLinq2DbContextGuardTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Functional.Linq2DB.Tests;

using System.Linq.Expressions;
using SharpCoreDB.Functional.Linq2DB;
using Xunit;

/// <summary>
/// The argument and cancellation contract of <see cref="FunctionalLinq2DbContext"/>.
/// Added with the mutation baseline: 24 statement mutations survived — every method's
/// <c>ArgumentNullException.ThrowIfNull</c> and <c>ThrowIfCancellationRequested</c> guard could be deleted
/// without a red test, so a null predicate or a cancelled token would have surfaced as a query against the
/// database instead of an argument error. (The 15 surviving Boolean mutations are
/// <c>ConfigureAwait(false)</c> → <c>true</c>, which is behaviourally identical.)
/// </summary>
public sealed class FunctionalLinq2DbContextGuardTests : IAsyncDisposable
{
    private readonly SharpCoreDBDataConnection _connection;
    private readonly FunctionalLinq2DbContext _sut;
    private readonly string _path;

    public FunctionalLinq2DbContextGuardTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"scdb_l2db_guard_{Guid.NewGuid():N}");
        _connection = new SharpCoreDBDataConnection($"Data Source={_path}");
        _sut = new FunctionalLinq2DbContext(_connection);
    }

    private static CancellationToken Cancelled => new(canceled: true);

    [Fact]
    public async Task GetByIdAsync_WithNullKeyValues_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.GetByIdAsync<Row>(null!));
    }

    [Fact]
    public async Task GetByIdAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.GetByIdAsync<Row>([1], Cancelled));
    }

    [Fact]
    public async Task FindOneAsync_WithNullPredicate_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.FindOneAsync<Row>(null!));
    }

    [Fact]
    public async Task FindOneAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.FindOneAsync<Row>(r => r.Id == 1, Cancelled));
    }

    [Fact]
    public async Task QueryAsync_WithNullQueryBuilder_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.QueryAsync<Row>((Func<IQueryable<Row>, IQueryable<Row>>)null!));
    }

    [Fact]
    public async Task QueryAsync_WithQueryBuilder_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.QueryAsync<Row>(q => q, Cancelled));
    }

    [Fact]
    public async Task QueryAsync_WithNullPredicate_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.QueryAsync<Row>((Expression<Func<Row, bool>>)null!));
    }

    [Fact]
    public async Task QueryAsync_WithPredicate_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.QueryAsync<Row>(r => r.Id > 0, Cancelled));
    }

    [Fact]
    public async Task GetAllAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.GetAllAsync<Row>(Cancelled));
    }

    [Fact]
    public async Task InsertAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.InsertAsync<Row>(null!));
    }

    [Fact]
    public async Task InsertAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.InsertAsync(new Row { Id = 1 }, Cancelled));
    }

    [Fact]
    public async Task InsertBatchAsync_WithNullEntities_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.InsertBatchAsync<Row>(null!));
    }

    [Fact]
    public async Task InsertBatchAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.InsertBatchAsync([new Row { Id = 1 }], Cancelled));
    }

    [Fact]
    public async Task UpdateAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.UpdateAsync<Row>(null!));
    }

    [Fact]
    public async Task UpdateAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.UpdateAsync(new Row { Id = 1 }, Cancelled));
    }

    [Fact]
    public async Task DeleteAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.DeleteAsync<Row>(null!));
    }

    [Fact]
    public async Task DeleteAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.DeleteAsync(new Row { Id = 1 }, Cancelled));
    }

    [Fact]
    public async Task DeleteWhereAsync_WithNullPredicate_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.DeleteWhereAsync<Row>(null!));
    }

    [Fact]
    public async Task DeleteWhereAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.DeleteWhereAsync<Row>(r => r.Id == 1, Cancelled));
    }

    [Fact]
    public async Task CountAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.CountAsync<Row>(null, Cancelled));
    }

    [Fact]
    public async Task ExistsAsync_WithNullPredicate_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.ExistsAsync<Row>(null!));
    }

    [Fact]
    public async Task ExistsAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.ExistsAsync<Row>(r => r.Id == 1, Cancelled));
    }

    [Fact]
    public async Task TransactionAsync_WithNullAction_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.TransactionAsync<int>(null!));
    }

    [Fact]
    public async Task TransactionAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.TransactionAsync(() => Task.FromResult(1), Cancelled));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();

        try
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, true);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
    }
}
