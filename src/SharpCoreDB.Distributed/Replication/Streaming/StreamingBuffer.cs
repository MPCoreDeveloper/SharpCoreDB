// <copyright file="StreamingBuffer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Buffers WAL entries for streaming to replicas.
/// Provides efficient buffering with acknowledgment tracking.
/// C# 14: Collection expressions, primary constructors.
/// </summary>
public sealed class StreamingBuffer
{
    private readonly Queue<WALEntry> _buffer;
    private readonly Lock _lock = new();
    private readonly int _maxSize;

    private WALPosition _lastAcknowledgedPosition = WALPosition.MinValue;

    /// <summary>Gets the current number of buffered entries.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _buffer.Count;
            }
        }
    }

    /// <summary>Gets the maximum buffer size.</summary>
    public int MaxSize => _maxSize;

    /// <summary>Gets whether the buffer is full.</summary>
    public bool IsFull => Count >= _maxSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingBuffer"/> class.
    /// </summary>
    /// <param name="maxSize">Maximum number of entries to buffer.</param>
    public StreamingBuffer(int maxSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);

        _maxSize = maxSize;
        _buffer = new Queue<WALEntry>(maxSize);
    }

    /// <summary>
    /// Adds a WAL entry to the buffer.
    /// </summary>
    /// <param name="entry">The WAL entry to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when buffer is full.</exception>
    public async Task AddEntryAsync(WALEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30)); // Timeout for buffer operations

        await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_buffer.Count >= _maxSize)
                {
                    throw new InvalidOperationException("Buffer is full");
                }

                _buffer.Enqueue(entry);
            }
        }, linkedCts.Token);
    }

    /// <summary>
    /// Tries to add a WAL entry to the buffer without blocking.
    /// </summary>
    /// <param name="entry">The WAL entry to add.</param>
    /// <returns>True if the entry was added, false if buffer is full.</returns>
    public bool TryAddEntry(WALEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            if (_buffer.Count >= _maxSize)
            {
                return false;
            }

            _buffer.Enqueue(entry);
            return true;
        }
    }

    /// <summary>
    /// Peeks at the next entry in the buffer without removing it.
    /// </summary>
    /// <returns>The next entry, or null if buffer is empty.</returns>
    public WALEntry? PeekNextEntry()
    {
        lock (_lock)
        {
            return _buffer.Count > 0 ? _buffer.Peek() : null;
        }
    }

    /// <summary>
    /// Removes and returns the next entry from the buffer.
    /// </summary>
    /// <returns>The next entry, or null if buffer is empty.</returns>
    public WALEntry? DequeueEntry()
    {
        lock (_lock)
        {
            return _buffer.Count > 0 ? _buffer.Dequeue() : null;
        }
    }

    /// <summary>
    /// Acknowledges all entries up to the specified position.
    /// </summary>
    /// <param name="position">The position to acknowledge up to.</param>
    public void AcknowledgeUpTo(WALPosition position)
    {
        lock (_lock)
        {
            _lastAcknowledgedPosition = position;

            // Remove acknowledged entries from buffer
            while (_buffer.Count > 0)
            {
                var entry = _buffer.Peek();
                var entryPosition = new WALPosition(entry.Position + entry.Size, entry.Position + 1);

                if (entryPosition <= position)
                {
                    _buffer.Dequeue();
                }
                else
                {
                    break; // Remaining entries are not acknowledged yet
                }
            }
        }
    }

    /// <summary>
    /// Gets all entries that need to be retransmitted after the last acknowledged position.
    /// </summary>
    /// <returns>Collection of entries to retransmit.</returns>
    public IReadOnlyCollection<WALEntry> GetEntriesForRetransmission()
    {
        lock (_lock)
        {
            var entries = new List<WALEntry>();

            foreach (var entry in _buffer)
            {
                var entryPosition = new WALPosition(entry.Position + entry.Size, entry.Position + 1);
                if (entryPosition > _lastAcknowledgedPosition)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }
    }

    /// <summary>
    /// Clears all entries from the buffer.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
            _lastAcknowledgedPosition = WALPosition.MinValue;
        }
    }

    /// <summary>
    /// Gets statistics about the buffer.
    /// </summary>
    /// <returns>Buffer statistics.</returns>
    public StreamingBufferStats GetStats()
    {
        lock (_lock)
        {
            var totalSize = _buffer.Sum(e => e.Size);

            return new StreamingBufferStats
            {
                EntryCount = _buffer.Count,
                MaxSize = _maxSize,
                TotalSizeBytes = totalSize,
                UtilizationPercent = (double)_buffer.Count / _maxSize * 100,
                LastAcknowledgedPosition = _lastAcknowledgedPosition
            };
        }
    }
}

/// <summary>
/// Statistics for streaming buffer operations.
/// </summary>
public class StreamingBufferStats
{
    /// <summary>Gets the current number of entries in the buffer.</summary>
    public int EntryCount { get; init; }

    /// <summary>Gets the maximum buffer size.</summary>
    public int MaxSize { get; init; }

    /// <summary>Gets the total size of all buffered entries in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Gets the buffer utilization as a percentage.</summary>
    public double UtilizationPercent { get; init; }

    /// <summary>Gets the last acknowledged position.</summary>
    public WALPosition LastAcknowledgedPosition { get; init; }

    /// <summary>Gets the average entry size in bytes.</summary>
    public double AverageEntrySize => EntryCount > 0 ? (double)TotalSizeBytes / EntryCount : 0;
}
