// <copyright file="WALReader.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Buffers;
using System.Runtime.CompilerServices;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Reads WAL entries from storage for streaming to replicas.
/// C# 14: Primary constructors, async streams, Span<T> for zero-copy reading.
/// </summary>
public sealed class WALReader : IAsyncDisposable
{
    private readonly string _walFilePath;
    private readonly FileStream _walStream;
    private readonly Lock _lock = new();

    private long _currentPosition;
    private bool _disposed;

    /// <summary>
    /// Gets the current reading position in the WAL file.
    /// </summary>
    public long CurrentPosition => _currentPosition;

    /// <summary>
    /// Gets the total length of the WAL file.
    /// </summary>
    public long WalFileLength => _walStream.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="WALReader"/> class.
    /// </summary>
    /// <param name="walFilePath">Path to the WAL file to read from.</param>
    public WALReader(string walFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walFilePath);

        _walFilePath = walFilePath;
        _walStream = new FileStream(
            walFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite, // Allow concurrent writes
            bufferSize: 64 * 1024, // 64KB buffer
            useAsync: true);
    }

    /// <summary>
    /// Reads WAL entries starting from the specified position.
    /// </summary>
    /// <param name="startPosition">Position to start reading from.</param>
    /// <param name="maxEntries">Maximum number of entries to read (0 = unlimited).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of WAL entries.</returns>
    public async IAsyncEnumerable<WALEntry> ReadEntriesAsync(
        long startPosition = 0,
        int maxEntries = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WALReader));
            }
        }

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024); // 64KB buffer
        try
        {
            _walStream.Position = startPosition;
            var entriesRead = 0;

            while (!cancellationToken.IsCancellationRequested &&
                   (maxEntries == 0 || entriesRead < maxEntries))
            {
                var entry = await TryReadNextEntryAsync(buffer, cancellationToken);
                if (entry is null)
                {
                    // No more entries available
                    break;
                }

                yield return entry;
                entriesRead++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads a single WAL entry at the current position.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The WAL entry, or null if no more entries.</returns>
    public async Task<WALEntry?> ReadNextEntryAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WALReader));
            }
        }

        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            return await TryReadNextEntryAsync(buffer, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Seeks to a specific position in the WAL file.
    /// </summary>
    /// <param name="position">Position to seek to.</param>
    public void Seek(long position)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WALReader));
            }

            if (position < 0 || position > _walStream.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            _walStream.Position = position;
            _currentPosition = position;
        }
    }

    /// <summary>
    /// Gets the last WAL position (end of file).
    /// </summary>
    /// <returns>The last WAL position.</returns>
    public long GetLastPosition()
    {
        lock (_lock)
        {
            return _walStream.Length;
        }
    }

    /// <summary>
    /// Waits for new WAL entries to become available.
    /// </summary>
    /// <param name="currentPosition">Current position to wait from.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if new entries are available, false on timeout.</returns>
    public async Task<bool> WaitForNewEntriesAsync(
        long currentPosition,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _walStream.Position = 0; // Refresh file length
                    if (_walStream.Length > currentPosition)
                    {
                        return true;
                    }
                }

                // Wait a bit before checking again
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout or cancellation
        }

        return false;
    }

    /// <summary>
    /// Attempts to read the next WAL entry from the stream.
    /// </summary>
    /// <param name="buffer">Buffer to use for reading.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The WAL entry, or null if no more data.</returns>
    private async Task<WALEntry?> TryReadNextEntryAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        // Read WAL record header (8 bytes: 4-byte length + 4-byte checksum)
        var headerBytes = buffer.AsMemory(0, 8);
        var bytesRead = await _walStream.ReadAsync(headerBytes, cancellationToken);

        if (bytesRead < 8)
        {
            // Not enough bytes for header
            return null;
        }

        var length = BitConverter.ToInt32(headerBytes.Span[0..4]);
        var checksum = BitConverter.ToUInt32(headerBytes.Span[4..8]);

        if (length <= 0 || length > buffer.Length - 8)
        {
            throw new InvalidDataException($"Invalid WAL record length: {length}");
        }

        // Read the data payload
        var dataBytes = buffer.AsMemory(8, length);
        bytesRead = await _walStream.ReadAsync(dataBytes, cancellationToken);

        if (bytesRead < length)
        {
            // Incomplete record
            return null;
        }

        // Validate checksum
        var computedChecksum = ComputeChecksum(dataBytes.Span);
        if (computedChecksum != checksum)
        {
            throw new InvalidDataException($"WAL record checksum mismatch: expected {checksum}, got {computedChecksum}");
        }

        var entryPosition = _currentPosition;
        _currentPosition = _walStream.Position;

        // Create WAL entry
        return new WALEntry
        {
            Position = entryPosition,
            Data = dataBytes.ToArray(), // TODO: Consider zero-copy with Memory<byte>
            Checksum = checksum,
            Timestamp = DateTimeOffset.UtcNow // TODO: Read from WAL record if available
        };
    }

    /// <summary>
    /// Computes CRC32 checksum for data validation.
    /// </summary>
    /// <param name="data">Data to compute checksum for.</param>
    /// <returns>The computed checksum.</returns>
    private static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        // Simple CRC32 implementation - in real code, use a proper CRC32 library
        const uint polynomial = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ ((crc & 1) * polynomial);
            }
        }

        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>
    /// Disposes the WAL reader asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await _walStream.DisposeAsync();
    }
}

/// <summary>
/// Represents a WAL entry for replication.
/// </summary>
public class WALEntry
{
    /// <summary>Gets or sets the position of this entry in the WAL file.</summary>
    public long Position { get; init; }

    /// <summary>Gets or sets the data payload of this entry.</summary>
    public byte[] Data { get; init; } = [];

    /// <summary>Gets or sets the checksum for data validation.</summary>
    public uint Checksum { get; init; }

    /// <summary>Gets or sets the timestamp when this entry was created.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the size of this entry in bytes.</summary>
    public int Size => 8 + Data.Length; // Header (8) + data
}
