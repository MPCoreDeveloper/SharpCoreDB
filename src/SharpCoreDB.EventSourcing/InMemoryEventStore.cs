// <copyright file="InMemoryEventStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing;

using System.Collections.Concurrent;

/// <summary>
/// In-memory event store implementation for testing and lightweight scenarios.
/// Thread-safe append-only semantics with per-stream sequence tracking and global ordered reads.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly Lock _lock = new();
    private readonly ConcurrentDictionary<string, StreamData> _streams = [];
    private readonly ConcurrentDictionary<string, List<EventSnapshot>> _snapshots = [];
    private long _globalSequence = 0;

    private sealed record StreamData(
        List<EventEnvelope> Events,
        long HighestSequence);

    /// <inheritdoc />
    public Task<AppendResult> AppendEventAsync(
        EventStreamId streamId,
        EventAppendEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AppendResult>(cancellationToken);
        }

        lock (_lock)
        {
            var (events, nextSequence) = GetOrCreateStream(streamId);

            var globalSeq = Interlocked.Increment(ref _globalSequence);
            var envelope = new EventEnvelope(
                streamId,
                nextSequence,
                globalSeq,
                entry.EventType,
                entry.Payload,
                entry.Metadata,
                entry.TimestampUtc);

            events.Add(envelope);
            
            // Update stream data with new highest sequence
            _streams[streamId.Value] = new StreamData(events, nextSequence);

            return Task.FromResult(AppendResult.Ok(streamId, nextSequence, globalSeq));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AppendResult>> AppendEventsAsync(
        EventStreamId streamId,
        IEnumerable<EventAppendEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<IReadOnlyList<AppendResult>>(cancellationToken);
        }

        var results = new List<AppendResult>();

        lock (_lock)
        {
            var (events, nextSequence) = GetOrCreateStream(streamId);

            foreach (var entry in entries)
            {
                var globalSeq = Interlocked.Increment(ref _globalSequence);
                var envelope = new EventEnvelope(
                    streamId,
                    nextSequence,
                    globalSeq,
                    entry.EventType,
                    entry.Payload,
                    entry.Metadata,
                    entry.TimestampUtc);

                events.Add(envelope);
                results.Add(AppendResult.Ok(streamId, nextSequence, globalSeq));

                nextSequence++;
            }

            _streams[streamId.Value] = new StreamData(events, nextSequence - 1);
        }

        return Task.FromResult((IReadOnlyList<AppendResult>)results);
    }

    /// <inheritdoc />
    public Task<ConditionalAppendResult> TryAppendEventsAsync(
        EventStreamId streamId,
        long expectedVersion,
        IEnumerable<EventAppendEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ConditionalAppendResult>(cancellationToken);
        }

        var entryList = entries as IList<EventAppendEntry> ?? entries.ToList();

        lock (_lock)
        {
            var currentVersion = _streams.TryGetValue(streamId.Value, out var existing)
                ? existing.HighestSequence
                : 0L;

            if (expectedVersion != ExpectedVersion.Any && expectedVersion != currentVersion)
            {
                return Task.FromResult(ConditionalAppendResult.Conflict(expectedVersion, currentVersion));
            }

            if (entryList.Count == 0)
            {
                return Task.FromResult(ConditionalAppendResult.Ok(expectedVersion, currentVersion, []));
            }

            var (events, nextSequence) = GetOrCreateStream(streamId);
            var results = new List<AppendResult>(entryList.Count);

            foreach (var entry in entryList)
            {
                var globalSeq = Interlocked.Increment(ref _globalSequence);
                var envelope = new EventEnvelope(
                    streamId,
                    nextSequence,
                    globalSeq,
                    entry.EventType,
                    entry.Payload,
                    entry.Metadata,
                    entry.TimestampUtc);

                events.Add(envelope);
                results.Add(AppendResult.Ok(streamId, nextSequence, globalSeq));

                nextSequence++;
            }

            _streams[streamId.Value] = new StreamData(events, nextSequence - 1);

            return Task.FromResult(ConditionalAppendResult.Ok(expectedVersion, nextSequence - 1, results));
        }
    }

    /// <inheritdoc />
    public Task<ConditionalAppendResult> TryAppendEventAsync(
        EventStreamId streamId,
        long expectedVersion,
        EventAppendEntry entry,
        CancellationToken cancellationToken = default) =>
        TryAppendEventsAsync(streamId, expectedVersion, [entry], cancellationToken);

    /// <inheritdoc />
    public Task<ReadResult> ReadStreamAsync(
        EventStreamId streamId,
        EventReadRange range,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ReadResult>(cancellationToken);
        }

        lock (_lock)
        {
            if (!_streams.TryGetValue(streamId.Value, out var streamData))
            {
                return Task.FromResult(ReadResult.Empty());
            }

            var filtered = streamData.Events
                .Where(e => e.Sequence >= range.FromSequence && e.Sequence <= range.ToSequence)
                .ToList();

            return Task.FromResult(ReadResult.Ok(filtered, filtered.Count));
        }
    }

    /// <inheritdoc />
    public Task<ReadResult> ReadAllAsync(
        long fromGlobalSequence = 1,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ReadResult>(cancellationToken);
        }

        lock (_lock)
        {
            var allEvents = _streams.Values
                .SelectMany(s => s.Events)
                .Where(e => e.GlobalSequence >= fromGlobalSequence)
                .OrderBy(e => e.GlobalSequence)
                .Take(limit)
                .ToList();

            var totalCount = _streams.Values
                .SelectMany(s => s.Events)
                .LongCount(e => e.GlobalSequence >= fromGlobalSequence);

            return Task.FromResult(ReadResult.Ok(allEvents, totalCount));
        }
    }

    /// <inheritdoc />
    public Task<long> GetStreamLengthAsync(
        EventStreamId streamId,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<long>(cancellationToken);
        }

        lock (_lock)
        {
            if (_streams.TryGetValue(streamId.Value, out var streamData))
            {
                return Task.FromResult(streamData.HighestSequence);
            }

            return Task.FromResult(0L);
        }
    }

    /// <inheritdoc />
    public Task SaveSnapshotAsync(
        EventSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        lock (_lock)
        {
            var streamSnapshots = _snapshots.GetOrAdd(snapshot.StreamId.Value, _ => []);
            var existingIndex = streamSnapshots.FindIndex(s => s.Version == snapshot.Version);
            if (existingIndex >= 0)
            {
                streamSnapshots[existingIndex] = snapshot;
            }
            else
            {
                streamSnapshots.Add(snapshot);
            }

            streamSnapshots.Sort(static (left, right) => left.Version.CompareTo(right.Version));
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<EventSnapshot?> LoadSnapshotAsync(
        EventStreamId streamId,
        long maxVersion = long.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<EventSnapshot?>(cancellationToken);
        }

        lock (_lock)
        {
            if (!_snapshots.TryGetValue(streamId.Value, out var streamSnapshots) || streamSnapshots.Count == 0)
            {
                return Task.FromResult<EventSnapshot?>(null);
            }

            var snapshot = streamSnapshots
                .Where(s => s.Version <= maxVersion)
                .OrderByDescending(s => s.Version)
                .Cast<EventSnapshot?>()
                .FirstOrDefault();

            return Task.FromResult(snapshot);
        }
    }

    private (List<EventEnvelope>, long nextSequence) GetOrCreateStream(EventStreamId streamId)
    {
        if (_streams.TryGetValue(streamId.Value, out var existing))
        {
            return (existing.Events, existing.HighestSequence + 1);
        }

        var newEvents = new List<EventEnvelope>();
        _streams[streamId.Value] = new StreamData(newEvents, 0);
        return (newEvents, 1);
    }
}
