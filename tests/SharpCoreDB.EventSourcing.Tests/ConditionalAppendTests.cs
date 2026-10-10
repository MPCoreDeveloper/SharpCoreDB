// <copyright file="ConditionalAppendTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Tests for optimistic-concurrency conditional appends (<c>expectedVersion</c>) on <see cref="IEventStore"/>.
/// </summary>
public class ConditionalAppendTests
{
    [Fact]
    public async Task TryAppendEventsAsync_WithNoStreamOnEmptyStream_WritesAndReportsNewVersion()
    {
        var store = new InMemoryEventStore();
        var streamId = new EventStreamId("ledger-1");

        var result = await store.TryAppendEventsAsync(streamId, ExpectedVersion.NoStream, [Entry("ClaimRecorded"), Entry("ClaimRecorded")]);

        Assert.True(result.Success);
        Assert.Equal(ExpectedVersion.NoStream, result.ExpectedVersion);
        Assert.Equal(2, result.ActualVersion);
        Assert.Equal(2, result.Results.Count);
        Assert.Equal(1, result.Results[0].AppendedSequence);
        Assert.Equal(2, result.Results[1].AppendedSequence);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithStaleExpectedVersion_ReturnsConflictAndWritesNothing()
    {
        var store = new InMemoryEventStore();
        var streamId = new EventStreamId("ledger-2");
        await store.AppendEventAsync(streamId, Entry("ClaimRecorded"));

        var result = await store.TryAppendEventsAsync(streamId, ExpectedVersion.NoStream, [Entry("ClaimCorrected")]);

        Assert.False(result.Success);
        Assert.Equal(ExpectedVersion.NoStream, result.ExpectedVersion);
        Assert.Equal(1, result.ActualVersion);
        Assert.Empty(result.Results);

        var read = await store.ReadStreamAsync(streamId, new EventReadRange(1, long.MaxValue));
        Assert.Equal(1, read.Events.Count);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithMatchingExpectedVersion_AppendsAtNextSequence()
    {
        var store = new InMemoryEventStore();
        var streamId = new EventStreamId("ledger-3");
        await store.AppendEventAsync(streamId, Entry("ClaimRecorded"));

        var result = await store.TryAppendEventsAsync(streamId, expectedVersion: 1, [Entry("ClaimSuperseded")]);

        Assert.True(result.Success);
        Assert.Equal(1, result.ExpectedVersion);
        Assert.Equal(2, result.ActualVersion);
        Assert.Single(result.Results);
        Assert.Equal(2, result.Results[0].AppendedSequence);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithAnyVersion_SkipsTheCheck()
    {
        var store = new InMemoryEventStore();
        var streamId = new EventStreamId("ledger-4");
        await store.AppendEventAsync(streamId, Entry("ClaimRecorded"));

        var result = await store.TryAppendEventsAsync(streamId, ExpectedVersion.Any, [Entry("ClaimRecorded")]);

        Assert.True(result.Success);
        Assert.Equal(2, result.ActualVersion);
    }

    [Fact]
    public async Task TryAppendEventAsync_WithMatchingVersion_ReturnsSingleResult()
    {
        var store = new InMemoryEventStore();
        var streamId = new EventStreamId("ledger-5");

        var result = await store.TryAppendEventAsync(streamId, ExpectedVersion.NoStream, Entry("ClaimRecorded"));

        Assert.True(result.Success);
        Assert.Single(result.Results);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WhenTwoWritersUseTheSameVersion_OnlyOneIsAccepted()
    {
        var store = new InMemoryEventStore();
        var streamId = new EventStreamId("ledger-race");

        var first = store.TryAppendEventsAsync(streamId, ExpectedVersion.NoStream, [Entry("ClaimRecorded")]);
        var second = store.TryAppendEventsAsync(streamId, ExpectedVersion.NoStream, [Entry("ClaimRecorded")]);

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(r => r.Success));
        Assert.Equal(1, results.Count(r => !r.Success));

        var read = await store.ReadStreamAsync(streamId, new EventReadRange(1, long.MaxValue));
        Assert.Equal(1, read.Events.Count);
    }

    [Fact]
    public async Task TryAppendEventsAsync_WithPersistentStore_EnforcesExpectedVersionAcrossStoreInstances()
    {
        var databasePath = GetTempDatabasePath();
        var streamId = new EventStreamId("ledger-persistent");

        var initialStore = CreateStore(databasePath);
        var first = await initialStore.TryAppendEventsAsync(streamId, ExpectedVersion.NoStream, [Entry("ClaimRecorded")]);
        Assert.True(first.Success);
        Assert.Equal(1, first.ActualVersion);

        // Replaying the same expected version against the persisted stream must conflict.
        var secondStore = CreateStore(databasePath);
        var conflict = await secondStore.TryAppendEventsAsync(streamId, ExpectedVersion.NoStream, [Entry("ClaimCorrected")]);
        Assert.False(conflict.Success);
        Assert.Equal(1, conflict.ActualVersion);

        var next = await secondStore.TryAppendEventsAsync(streamId, expectedVersion: 1, [Entry("ClaimCorrected")]);
        Assert.True(next.Success);
        Assert.Equal(2, next.ActualVersion);
    }

    private static EventAppendEntry Entry(string eventType) =>
        new(eventType, Array.Empty<byte>(), Array.Empty<byte>(), DateTimeOffset.UtcNow);

    private static SharpCoreDbEventStore CreateStore(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var database = factory.Create(databasePath, "event-store-test-password");

        return new SharpCoreDbEventStore(database);
    }

    private static string GetTempDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"SharpCoreDB_ConditionalAppend_{Guid.NewGuid():N}");
}
