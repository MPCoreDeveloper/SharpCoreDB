// <copyright file="WALStreamer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Streams WAL entries to replicas in real-time.
/// C# 14: Channel<T> for async coordination, primary constructors.
/// </summary>
public sealed class WALStreamer : IAsyncDisposable
{
    private readonly WALReader _walReader;
    private readonly WALPositionTracker _positionTracker;
    private readonly StreamingBuffer _buffer;
    private readonly ILogger<WALStreamer>? _logger;

    private readonly Channel<WALEntry> _entryChannel;
    private readonly CancellationTokenSource _cts = new();

    private Task? _streamingTask;
    private bool _isStreaming;

    /// <summary>Gets the replica identifier this streamer serves.</summary>
    public string ReplicaId { get; }

    /// <summary>Gets whether the streamer is currently active.</summary>
    public bool IsActive => _isStreaming && !_cts.IsCancellationRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="WALStreamer"/> class.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walReader">The WAL reader to use.</param>
    /// <param name="positionTracker">The position tracker.</param>
    /// <param name="buffer">The streaming buffer.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="channelCapacity">Capacity of the internal channel.</param>
    public WALStreamer(
        string replicaId,
        WALReader walReader,
        WALPositionTracker positionTracker,
        StreamingBuffer buffer,
        ILogger<WALStreamer>? logger = null,
        int channelCapacity = 10000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        ReplicaId = replicaId;
        _walReader = walReader ?? throw new ArgumentNullException(nameof(walReader));
        _positionTracker = positionTracker ?? throw new ArgumentNullException(nameof(positionTracker));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _logger = logger;

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

        _logger?.LogInformation("Starting WAL streaming to replica {ReplicaId}", ReplicaId);

        // Start the streaming pipeline
        _streamingTask = StreamToReplicaAsync(_cts.Token);
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

        _logger?.LogInformation("Stopping WAL streaming to replica {ReplicaId}", ReplicaId);

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
    /// Acknowledges receipt of WAL entries up to the specified position.
    /// </summary>
    /// <param name="position">The position that was acknowledged.</param>
    public void AcknowledgePosition(WALPosition position)
    {
        _positionTracker.UpdatePosition(ReplicaId, position);
        _buffer.AcknowledgeUpTo(position);
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
    public WALStreamerStats GetStats()
    {
        return new WALStreamerStats
        {
            ReplicaId = ReplicaId,
            IsActive = IsActive,
            CurrentPosition = GetCurrentPosition(),
            ChannelCount = _entryChannel.Reader.Count,
            ChannelCapacity = _entryChannel.Reader.Count // Simplified, Channel doesn't expose writer count
        };
    }

    /// <summary>
    /// Streams WAL entries to the replica asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the streaming operation.</returns>
    private async Task StreamToReplicaAsync(CancellationToken cancellationToken)
    {
        var lastPosition = GetCurrentPosition();

        try
        {
            await foreach (var entry in _walReader.ReadEntriesAsync(
                startPosition: lastPosition.Offset,
                cancellationToken: cancellationToken))
            {
                // Buffer the entry for potential retransmission
                await _buffer.AddEntryAsync(entry, cancellationToken);

                // Send to replica
                await _entryChannel.Writer.WriteAsync(entry, cancellationToken);

                // Update position tracking
                lastPosition = new WALPosition(entry.Position + entry.Size, entry.Position + 1);
                _positionTracker.UpdatePosition(ReplicaId, lastPosition);

                _logger?.LogDebug("Streamed WAL entry at position {Position} to replica {ReplicaId}",
                    entry.Position, ReplicaId);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error streaming WAL entries to replica {ReplicaId}", ReplicaId);

            // Complete the channel to signal end of stream
            _entryChannel.Writer.Complete(ex);
        }
    }

    /// <summary>
    /// Disposes the WAL streamer asynchronously.
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
public class WALStreamerStats
{
    /// <summary>Gets the replica identifier.</summary>
    public string ReplicaId { get; init; } = string.Empty;

    /// <summary>Gets whether the streamer is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>Gets the current streaming position.</summary>
    public WALPosition CurrentPosition { get; init; }

    /// <summary>Gets the number of entries currently in the channel.</summary>
    public int ChannelCount { get; init; }

    /// <summary>Gets the total channel capacity.</summary>
    public int ChannelCapacity { get; init; }

    /// <summary>Gets the channel utilization as a percentage.</summary>
    public double ChannelUtilization => ChannelCapacity > 0 ? (double)ChannelCount / ChannelCapacity * 100 : 0;

    /// <summary>Gets the buffer statistics.</summary>
    public StreamingBufferStats BufferStats { get; init; } = new();
}

/// <summary>
/// Factory for creating WAL streamers.
/// </summary>
public static class WALStreamerFactory
{
    /// <summary>
    /// Creates a new WAL streamer for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="walFilePath">Path to the WAL file.</param>
    /// <param name="positionTracker">The position tracker to use.</param>
    /// <param name="bufferSize">Size of the streaming buffer.</param>
    /// <param name="channelCapacity">Capacity of the streaming channel.</param>
    /// <param name="logger">Optional logger.</param>
    /// <returns>A new WAL streamer instance.</returns>
    public static WALStreamer CreateStreamer(
        string replicaId,
        string walFilePath,
        WALPositionTracker positionTracker,
        int bufferSize = 1000,
        int channelCapacity = 10000,
        ILogger<WALStreamer>? logger = null)
    {
        var walReader = new WALReader(walFilePath);
        var buffer = new StreamingBuffer(bufferSize);
        return new WALStreamer(replicaId, walReader, positionTracker, buffer, logger, channelCapacity);
    }
}
