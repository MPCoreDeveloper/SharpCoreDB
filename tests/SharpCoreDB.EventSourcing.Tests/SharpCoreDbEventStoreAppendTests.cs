// <copyright file="SharpCoreDbEventStoreAppendTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing.Tests;

using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Append/read semantics of <see cref="SharpCoreDbEventStore"/>: stream-sequence assignment, the
/// expected-version (optimistic concurrency) contract, range reads and the stream length.
/// Added with the mutation baseline — 48 mutants survived, 20 of them statement mutations on the sequence
/// bookkeeping and the version checks. A store that assigned the same sequence twice, or that reported a
/// conflict and wrote anyway, would have stayed green: the existing tests exercise the happy path only.
/// </summary>
public sealed class SharpCoreDbEventStoreAppendTests : IAsyncDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];

    public SharpCoreDbEventStoreAppendTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public async Task AppendEventAsync_OnANewStream_StartsBothSequencesAtOne()
    {
        var (_, store) = Create();

        var result = await store.AppendEventAsync(Stream("orders"), Entry());

        Assert.True(result.Success);
        Assert.Equal(1, result.AppendedSequence);
        Assert.Equal(1, result.GlobalSequence);
    }

    [Fact]
    public async Task AppendEventAsync_Twice_IncrementsBothSequences()
    {
        var (_, store) = Create();
        await store.AppendEventAsync(Stream("orders"), Entry());

        var second = await store.AppendEventAsync(Stream("orders"), Entry());

        Assert.Equal(2, second.AppendedSequence);
        Assert.Equal(2, second.GlobalSequence);
    }

    [Fact]
    public async Task AppendEventAsync_TwoStreams_KeepIndependentStreamSequences()
    {
        // Arrange — the stream sequence is per stream, the global sequence is not
        var (_, store) = Create();
        var orders = Stream("orders");
        var invoices = Stream("invoices");

        await store.AppendEventAsync(orders, Entry());
        await store.AppendEventAsync(invoices, Entry());
        var thirdOrder = await store.AppendEventAsync(orders, Entry());

        Assert.Equal(2, thirdOrder.AppendedSequence);
        Assert.Equal(3, thirdOrder.GlobalSequence);
        Assert.Equal(1, await store.GetStreamLengthAsync(invoices));
    }

    [Fact]
    public async Task AppendEventsAsync_AssignsConsecutiveSequencesPerEntry()
    {
        // Arrange
        var (_, store) = Create();
        var stream = Stream("orders");

        // Act
        var results = await store.AppendEventsAsync(stream, [Entry("A"), Entry("B"), Entry("C")]);

        // Assert — 1,2,3 in order, global 1,2,3
        Assert.Equal([1, 2, 3], results.Select(r => r.AppendedSequence).ToArray());
        Assert.Equal([1, 2, 3], results.Select(r => r.GlobalSequence).ToArray());
        Assert.Equal(3, await store.GetStreamLengthAsync(stream));
    }

    [Fact]
    public async Task AppendEventsAsync_WithNoEntries_ReturnsEmptyAndWritesNothing()
    {
        var (_, store) = Create();
        var stream = Stream("orders");

        var results = await store.AppendEventsAsync(stream, []);

        Assert.Empty(results);
        Assert.Equal(0, await store.GetStreamLengthAsync(stream));
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithTheCurrentVersion_AppendsAndReportsTheNewLength()
    {
        // Arrange
        var (_, store) = Create();
        var stream = Stream("orders");
        await store.AppendEventAsync(stream, Entry());

        // Act
        var result = await store.TryAppendEventsAsync(stream, expectedVersion: 1, [Entry("B")]);

        // Assert — ActualVersion is the length AFTER the append
        Assert.True(result.Success);
        Assert.Equal(1, result.ExpectedVersion);
        Assert.Equal(2, result.ActualVersion);
        Assert.Equal(2, Assert.Single(result.Results).AppendedSequence);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithAStaleVersion_ConflictsAndWritesNothing()
    {
        // Arrange — the caller thinks the stream is empty
        var (_, store) = Create();
        var stream = Stream("orders");
        await store.AppendEventAsync(stream, Entry());

        // Act
        var result = await store.TryAppendEventsAsync(stream, expectedVersion: 0, [Entry("B")]);

        // Assert — conflict reports the version that disagreed, and the stream did not grow
        Assert.False(result.Success);
        Assert.Equal(0, result.ExpectedVersion);
        Assert.Equal(1, result.ActualVersion);
        Assert.Empty(result.Results);
        Assert.Equal(1, await store.GetStreamLengthAsync(stream));
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithExpectedVersionAny_AlwaysAppends()
    {
        // Arrange
        var (_, store) = Create();
        var stream = Stream("orders");
        await store.AppendEventAsync(stream, Entry());
        await store.AppendEventAsync(stream, Entry());

        // Act
        var result = await store.TryAppendEventsAsync(stream, ExpectedVersion.Any, [Entry("C")]);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.ActualVersion);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithAStaleVersionAndNoEntries_StillConflicts()
    {
        // Arrange — the version check runs before the empty-batch short circuit
        var (_, store) = Create();
        var stream = Stream("orders");
        await store.AppendEventAsync(stream, Entry());

        // Act
        var result = await store.TryAppendEventsAsync(stream, expectedVersion: 5, []);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(1, result.ActualVersion);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithTheCurrentVersionAndNoEntries_ReturnsOkWithoutWriting()
    {
        // Arrange
        var (_, store) = Create();
        var stream = Stream("orders");
        await store.AppendEventAsync(stream, Entry());

        // Act
        var result = await store.TryAppendEventsAsync(stream, expectedVersion: 1, []);

        // Assert — success, nothing appended, length unchanged
        Assert.True(result.Success);
        Assert.Equal(1, result.ActualVersion);
        Assert.Empty(result.Results);
        Assert.Equal(1, await store.GetStreamLengthAsync(stream));
    }

    [Fact]
    public async Task ReadStreamAsync_ReturnsTheRequestedRangeInSequenceOrderWithThePayload()
    {
        // Arrange
        var (_, store) = Create();
        var stream = Stream("orders");
        await store.AppendEventsAsync(stream, [Entry("A", "first"), Entry("B", "second"), Entry("C", "third")]);

        // Act — inclusive range of the last two
        var read = await store.ReadStreamAsync(stream, new EventReadRange(2, 3));

        // Assert
        Assert.Equal(2, read.TotalCount);
        Assert.Equal([2, 3], read.Events.Select(e => e.Sequence).ToArray());
        Assert.Equal(["B", "C"], read.Events.Select(e => e.EventType).ToArray());
        Assert.Equal("second", Encoding.UTF8.GetString(read.Events[0].Payload.Span));
    }

    [Fact]
    public async Task ReadStreamAsync_WithAnInvertedRange_ReturnsEmpty()
    {
        var (_, store) = Create();
        await store.AppendEventAsync(Stream("orders"), Entry());

        var read = await store.ReadStreamAsync(Stream("orders"), new EventReadRange(3, 1));

        Assert.Empty(read.Events);
        Assert.Equal(0, read.TotalCount);
    }

    [Fact]
    public async Task ReadAllAsync_RespectsTheLimitAndReportsTheTotalCount()
    {
        // Arrange — three events across two streams
        var (_, store) = Create();
        await store.AppendEventAsync(Stream("orders"), Entry());
        await store.AppendEventAsync(Stream("invoices"), Entry());
        await store.AppendEventAsync(Stream("orders"), Entry());

        // Act
        var read = await store.ReadAllAsync(fromGlobalSequence: 1, limit: 2);

        // Assert — two returned, three matched
        Assert.Equal(2, read.Events.Count);
        Assert.Equal(3, read.TotalCount);
        Assert.Equal([1, 2], read.Events.Select(e => e.GlobalSequence).ToArray());
    }

    [Fact]
    public async Task ReadAllAsync_WithNonPositiveLimit_ReturnsEmpty()
    {
        var (_, store) = Create();
        await store.AppendEventAsync(Stream("orders"), Entry());

        var read = await store.ReadAllAsync(fromGlobalSequence: 1, limit: 0);

        Assert.Empty(read.Events);
    }

    [Fact]
    public async Task GetStreamLengthAsync_OnAnUnknownStream_ReturnsZero()
    {
        var (_, store) = Create();

        Assert.Equal(0, await store.GetStreamLengthAsync(Stream("missing")));
    }

    [Fact]
    public async Task AppendEventsAsync_WithNullEntries_ThrowsArgumentNullException()
    {
        var (_, store) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AppendEventsAsync(Stream("orders"), null!));
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithNullEntries_ThrowsArgumentNullException()
    {
        var (_, store) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.TryAppendEventsAsync(Stream("orders"), ExpectedVersion.Any, null!));
    }

    [Fact]
    public async Task AppendEventAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var (_, store) = Create();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.AppendEventAsync(Stream("orders"), Entry(), new CancellationToken(canceled: true)));
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

    private (IDatabase Db, SharpCoreDbEventStore Store) Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"SCDB_EventStore_{Guid.NewGuid():N}");
        _paths.Add(path);
        var db = _factory.Create(path, "eventstore-test-password");
        _dbs.Add(db);
        return (db, new SharpCoreDbEventStore(db));
    }

    private static EventStreamId Stream(string value) => new(value);

    private static EventAppendEntry Entry(string type = "Created", string payload = "{}") =>
        new(type, Encoding.UTF8.GetBytes(payload), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow);
}

