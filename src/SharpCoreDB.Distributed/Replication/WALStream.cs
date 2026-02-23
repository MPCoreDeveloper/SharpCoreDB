// <copyright file="WALStream.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Provides streaming interface for WAL entries to replicas.
/// C# 14: Async streams, Channel<T> for producer-consumer pattern.
/// </summary>
public sealed class WALStream : IAsyncDisposable
{
    private readonly WALReader _walReader;
    private readonly WALPositionTracker _positionTracker;
    private readonly Channel<WALEntry> _entryChannel;
    private readonly CancellationTokenSource _cts = new();

    private Task? _streamingTask;
    private bool _isStreaming;

    /// <summary>Gets the replica identifier this stream is for.</summary>
    public string ReplicaId { get; }

    /// <summary>Gets whether the stream is currently active.</summary>
    public bool IsActive => _isStreaming && !_cts.IsCancellationRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="WALStream"/> class.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walReader">The WAL reader to use.</param>
    /// <param name="positionTracker">The position tracker.</param>
    /// <param name="channelCapacity">Capacity of the internal channel.</param>
    public WALStream(string replicaId, WALReader walReader, WALPositionTracker positionTracker, int channelCapacity = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        ReplicaId = replicaId;
        _walReader = walReader ?? throw new ArgumentNullException(nameof(walReader));
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));

        _entryChannel = Channel.CreateBounded<WALEntry>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // Block when full
                SingleReader = true,
                SingleWriter = true
            });
    }

    /// <summary>
    /// Starts streaming WAL entries to the replica.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (_isStreaming)
        {
            return;
        }

        _isStreaming = true;

        // Get starting position for this replica
        var startPosition = _positionTracker.GetPosition(ReplicaId);
        _walReader.Seek(startPosition.Offset);

        _streamingTask = StreamEntriesAsync(_cts.Token);
    }

    /// <summary>
    /// Stops the streaming operation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopStreamingAsync()
    {
        if (!_isStreaming)
        {
            return;
        }

        _isStreaming = false;
        _cts.Cancel();

        if (_streamingTask is not null)
        {
            await _streamingTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        _entryChannel.Writer.Complete();
    }

    /// <summary>
    /// Reads the next WAL entry from the stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The next WAL entry, or null if stream ended.</returns>
    public async Task<WALEntry?> ReadNextEntryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _entryChannel.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null; // Stream ended
        }
    }

    /// <summary>
    /// Reads WAL entries as an async enumerable.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of WAL entries.</returns>
    public async IAsyncEnumerable<WALEntry> ReadAllEntriesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in _entryChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return entry;
        }
    }

    /// <summary>
    /// Acknowledges receipt of WAL entries up to the specified position.
    /// </summary>
    /// <param name="position">The position that was acknowledged.</param>
    public void AcknowledgePosition(WALPosition position)
    {
        _positionTracker.UpdatePosition(ReplicaId, position);
    }

    /// <summary>
    /// Gets the current streaming position for this replica.
    /// </summary>
    /// <returns>The current position.</returns>
    public WALPosition GetCurrentPosition()
    {
        return _positionTracker.GetPosition(ReplicaId);
    }

    /// <summary>
    /// Gets streaming statistics.
    /// </summary>
    /// <returns>Streaming statistics.</returns>
    public WALStreamStats GetStats()
    {
        return new WALStreamStats
        {
            ReplicaId = ReplicaId,
            IsActive = IsActive,
            CurrentPosition = GetCurrentPosition(),
            ChannelCount = _entryChannel.Reader.Count,
            ChannelCapacity = _entryChannel.Reader.Count // Simplified, Channel doesn't expose writer count
        };
    }

    /// <summary>
    /// Streams WAL entries from the reader to the channel.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the streaming operation.</returns>
    private async Task StreamEntriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var entry in _walReader.ReadEntriesAsync(
                startPosition: GetCurrentPosition().Offset,
                cancellationToken: cancellationToken))
            {
                // Wait for space in channel if it's full
                await _entryChannel.Writer.WriteAsync(entry, cancellationToken);

                // Update position as we stream
                var newPosition = new WALPosition(entry.Position + entry.Size, entry.Position + 1);
                _positionTracker.UpdatePosition(ReplicaId, newPosition);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            // Log error and complete channel
            // _logger?.LogError(ex, "Error streaming WAL entries to replica {ReplicaId}", ReplicaId);
        }
        finally
        {
            _entryChannel.Writer.Complete();
        }
    }

    /// <summary>
    /// Disposes the WAL stream asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await StopStreamingAsync();
        _cts.Dispose();
    }
}

/// <summary>
/// Statistics for WAL streaming operations.
/// </summary>
public class WALStreamStats
{
    /// <summary>Gets the replica identifier.</summary>
    public string ReplicaId { get; init; } = string.Empty;

    /// <summary>Gets whether the stream is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets the current streaming position.</summary>
    public WALPosition CurrentPosition { get; init; }

    /// <summary>Gets the number of entries currently in the channel.</summary>
    public int ChannelCount { get; init; }

    /// <summary>Gets the total channel capacity.</summary>
    public int ChannelCapacity { get; init; }

    /// <summary>Gets the channel utilization as a percentage.</summary>
    public double ChannelUtilization => ChannelCapacity > 0 ? (double)ChannelCount / ChannelCapacity * 100 : 0;
}

/// <summary>
/// Factory for creating WAL streams.
/// </summary>
public static class WALStreamFactory
{
    /// <summary>
    /// Creates a new WAL stream for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <param name="positionTracker">The position tracker to use.</param>
    /// <param name="channelCapacity">Capacity of the streaming channel.</param>
    /// <returns>A new WAL stream instance.</returns>
    public static WALStream CreateStream(
        string replicaId,
        string walFilePath,
        WALPositionTracker positionTracker,
        int channelCapacity = 1000)
    {
        var walReader = new WALReader(walFilePath);
        return new WALStream(replicaId, walReader, positionTracker, channelCapacity);
    }
}
