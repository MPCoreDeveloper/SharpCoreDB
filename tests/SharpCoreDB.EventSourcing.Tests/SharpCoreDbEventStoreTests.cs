// <copyright file="SharpCoreDbEventStoreTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Integration tests for <see cref="SharpCoreDbEventStore"/> persistence behavior.
/// </summary>
public class SharpCoreDbEventStoreTests
{
    [Fact]
    public async Task AppendEventAsync_WithPersistentStore_WritesAndReadsFromDatabase()
    {
        var databasePath = GetTempDatabasePath();
        var store = CreateStore(databasePath);
        var streamId = new EventStreamId("order-100");
        var entry = new EventAppendEntry("OrderCreated", "payload"u8.ToArray(), "meta"u8.ToArray(), DateTimeOffset.UtcNow);

        var result = await store.AppendEventAsync(streamId, entry);
        var read = await store.ReadStreamAsync(streamId, new EventReadRange(1, long.MaxValue));

        Assert.True(result.Success);
        Assert.Equal(1, read.Events.Count);
        Assert.Equal("OrderCreated", read.Events[0].EventType);
    }

    [Fact]
    public async Task AppendEventAsync_AfterReopen_PreservesEventsAcrossStoreInstances()
    {
        var databasePath = GetTempDatabasePath();
        var streamId = new EventStreamId("order-200");
        var entry = new EventAppendEntry("OrderPaid", "payload"u8.ToArray(), Array.Empty<byte>(), DateTimeOffset.UtcNow);

        // Create first store and database
        var services1 = new ServiceCollection();
        services1.AddSharpCoreDB();
        var serviceProvider1 = services1.BuildServiceProvider();
        var factory1 = serviceProvider1.GetRequiredService<DatabaseFactory>();
        var database1 = factory1.Create(databasePath, "event-store-test-password");
        var firstStore = new SharpCoreDbEventStore(database1);

        await firstStore.AppendEventAsync(streamId, entry);

        // ✅ FIX: Dispose the first database instance to ensure all data is flushed to disk
        await database1.DisposeAsync();

        var secondStore = CreateStore(databasePath);
        var read = await secondStore.ReadStreamAsync(streamId, new EventReadRange(1, long.MaxValue));

        Assert.Equal(1, read.Events.Count);
        Assert.Equal("OrderPaid", read.Events[0].EventType);
    }

    [Fact]
    public async Task ReadAllAsync_WithMultipleStreams_ReturnsGlobalOrderedEvents()
    {
        var databasePath = GetTempDatabasePath();
        var store = CreateStore(databasePath);

        await store.AppendEventAsync(new EventStreamId("stream-a"), new EventAppendEntry("A1", Array.Empty<byte>(), Array.Empty<byte>(), DateTimeOffset.UtcNow));
        await store.AppendEventAsync(new EventStreamId("stream-b"), new EventAppendEntry("B1", Array.Empty<byte>(), Array.Empty<byte>(), DateTimeOffset.UtcNow));

        var read = await store.ReadAllAsync(1, 100);

        Assert.Equal(2, read.Events.Count);
        Assert.Equal(1, read.Events[0].GlobalSequence);
        Assert.Equal(2, read.Events[1].GlobalSequence);
    }

    [Fact]
    public async Task SaveSnapshotAsync_ThenLoadSnapshotAsync_ReturnsLatestSnapshot()
    {
        var databasePath = GetTempDatabasePath();
        var streamId = new EventStreamId("snapshot-order-1");
        var store = CreateStore(databasePath);

        await store.SaveSnapshotAsync(new EventSnapshot(streamId, 10, "snapshot-v10"u8.ToArray(), DateTimeOffset.UtcNow.AddMinutes(-1)));
        await store.SaveSnapshotAsync(new EventSnapshot(streamId, 20, "snapshot-v20"u8.ToArray(), DateTimeOffset.UtcNow));

        var snapshot = await store.LoadSnapshotAsync(streamId);

        Assert.NotNull(snapshot);
        Assert.Equal(20, snapshot.Value.Version);
    }

    [Fact]
    public async Task LoadSnapshotAsync_AfterReopen_PreservesSnapshotData()
    {
        var databasePath = GetTempDatabasePath();
        var streamId = new EventStreamId("snapshot-order-2");
        var initialStore = CreateStore(databasePath);

        await initialStore.SaveSnapshotAsync(new EventSnapshot(streamId, 7, "snapshot-v7"u8.ToArray(), DateTimeOffset.UtcNow));

        var reopenedStore = CreateStore(databasePath);
        var snapshot = await reopenedStore.LoadSnapshotAsync(streamId);

        Assert.NotNull(snapshot);
        Assert.Equal(7, snapshot.Value.Version);
    }

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
        Path.Combine(Path.GetTempPath(), $"SharpCoreDB_EventStore_{Guid.NewGuid():N}");
}
