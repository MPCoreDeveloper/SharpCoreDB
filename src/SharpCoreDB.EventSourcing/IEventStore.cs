// <copyright file="IEventStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing;

/// <summary>
/// Low-level event store contract for append-only stream operations.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Appends a single event to a stream.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="entry">The event append entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The append result with assigned sequence and global sequence.</returns>
    Task<AppendResult> AppendEventAsync(
        EventStreamId streamId,
        EventAppendEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple events to a stream in atomic batch.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="entries">The event append entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of append results, one per entry in order.</returns>
    Task<IReadOnlyList<AppendResult>> AppendEventsAsync(
        EventStreamId streamId,
        IEnumerable<EventAppendEntry> entries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads events from a stream by sequence range.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="range">The sequence range (inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read result with events and total count.</returns>
    Task<ReadResult> ReadStreamAsync(
        EventStreamId streamId,
        EventReadRange range,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all events from all streams in global order.
    /// </summary>
    /// <param name="fromGlobalSequence">The starting global sequence (inclusive). Use 1 to read from the beginning.</param>
    /// <param name="limit">Maximum events to return per call.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read result with events and total count.</returns>
    Task<ReadResult> ReadAllAsync(
        long fromGlobalSequence = 1,
        int limit = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current stream length (highest assigned sequence).
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current stream length, or 0 if the stream does not exist.</returns>
    Task<long> GetStreamLengthAsync(
        EventStreamId streamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or replaces a snapshot for a stream version.
    /// </summary>
    /// <param name="snapshot">The snapshot to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveSnapshotAsync(
        EventSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the latest snapshot for a stream up to the specified version.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="maxVersion">The maximum stream version to consider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest matching snapshot, or <see langword="null"/> when no snapshot exists.</returns>
    Task<EventSnapshot?> LoadSnapshotAsync(
        EventStreamId streamId,
        long maxVersion = long.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple events to a stream only when the stream's current length
    /// equals <paramref name="expectedVersion"/>, as one atomic compare-and-set.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="expectedVersion">
    /// The expected current stream length. Use <see cref="ExpectedVersion.NoStream"/>
    /// for a new stream, or <see cref="ExpectedVersion.Any"/> to skip the check.
    /// </param>
    /// <param name="entries">The event append entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConditionalAppendResult"/>. On a version mismatch nothing is
    /// written and <see cref="ConditionalAppendResult.Success"/> is <see langword="false"/>.
    /// </returns>
    Task<ConditionalAppendResult> TryAppendEventsAsync(
        EventStreamId streamId,
        long expectedVersion,
        IEnumerable<EventAppendEntry> entries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a single event to a stream only when the stream's current length
    /// equals <paramref name="expectedVersion"/>, as one atomic compare-and-set.
    /// </summary>
    /// <param name="streamId">The stream identifier.</param>
    /// <param name="expectedVersion">
    /// The expected current stream length. Use <see cref="ExpectedVersion.NoStream"/>
    /// for a new stream, or <see cref="ExpectedVersion.Any"/> to skip the check.
    /// </param>
    /// <param name="entry">The event append entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ConditionalAppendResult"/>. On a version mismatch nothing is
    /// written and <see cref="ConditionalAppendResult.Success"/> is <see langword="false"/>.
    /// </returns>
    Task<ConditionalAppendResult> TryAppendEventAsync(
        EventStreamId streamId,
        long expectedVersion,
        EventAppendEntry entry,
        CancellationToken cancellationToken = default) =>
        TryAppendEventsAsync(streamId, expectedVersion, [entry], cancellationToken);
}
