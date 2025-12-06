// <copyright file="WalManager.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;

/// <summary>
/// Manages pooled FileStream instances for WAL operations to avoid creating new streams per connection.
/// </summary>
public class WalManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ObjectPool<PooledFileStream>> _pools = new();
    private readonly int _bufferSize;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="WalManager"/> class.
    /// </summary>
    /// <param name="bufferSize">The buffer size for file streams (default 4096).</param>
    public WalManager(int bufferSize = 4096)
    {
        _bufferSize = bufferSize;
    }

    /// <summary>
    /// Gets a pooled FileStream for the specified WAL path.
    /// </summary>
    /// <param name="walPath">The WAL file path.</param>
    /// <returns>A pooled FileStream instance.</returns>
    public FileStream GetStream(string walPath)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WalManager));
        }

        var pool = _pools.GetOrAdd(walPath, _ => new DefaultObjectPool<PooledFileStream>(
            new PooledFileStreamPolicy(walPath, _bufferSize),
            maximumRetained: 10));

        var pooled = pool.Get();
        return pooled.Stream;
    }

    /// <summary>
    /// Returns a FileStream to the pool.
    /// </summary>
    /// <param name="stream">The FileStream to return.</param>
    public void ReturnStream(FileStream stream)
    {
        if (_disposed || stream == null)
        {
            return;
        }

        // Find the pooled wrapper
        // Since we can't associate directly, we assume the stream is wrapped
        // In practice, WAL should call ReturnStream with the path
        throw new NotImplementedException("ReturnStream requires path; use ReturnStream(string walPath, FileStream stream)");
    }

    /// <summary>
    /// Returns a FileStream to the pool for the specified path.
    /// </summary>
    /// <param name="walPath">The WAL file path.</param>
    /// <param name="stream">The FileStream to return.</param>
    public void ReturnStream(string walPath, FileStream stream)
    {
        if (_disposed || stream == null)
        {
            return;
        }

        if (_pools.TryGetValue(walPath, out var pool))
        {
            var pooled = new PooledFileStream(stream, walPath);
            pool.Return(pooled);
        }
        else
        {
            // Dispose if pool not found
            stream.Dispose();
        }
    }

    /// <summary>
    /// Disposes the WalManager and clears all pools.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pool in _pools.Values)
        {
            // Pools will dispose objects when collected
        }
        _pools.Clear();
    }

    /// <summary>
    /// Wrapper for pooled FileStream with path.
    /// </summary>
    private class PooledFileStream
    {
        public FileStream Stream { get; }
        public string WalPath { get; }

        public PooledFileStream(FileStream stream, string walPath)
        {
            Stream = stream;
            WalPath = walPath;
        }
    }

    /// <summary>
    /// Pooled object policy for PooledFileStream.
    /// </summary>
    private class PooledFileStreamPolicy : IPooledObjectPolicy<PooledFileStream>
    {
        private readonly string _walPath;
        private readonly int _bufferSize;

        public PooledFileStreamPolicy(string walPath, int bufferSize)
        {
            _walPath = walPath;
            _bufferSize = bufferSize;
        }

        public PooledFileStream Create()
        {
            var stream = new FileStream(_walPath, FileMode.Append, FileAccess.Write, FileShare.Read, _bufferSize, FileOptions.Asynchronous);
            return new PooledFileStream(stream, _walPath);
        }

        public bool Return(PooledFileStream obj)
        {
            // Check if stream is still valid
            if (obj.Stream.CanWrite && !obj.Stream.SafeFileHandle.IsClosed)
            {
                // Reset position if needed
                obj.Stream.Flush();
                return true;
            }
            obj.Stream.Dispose();
            return false;
        }
    }
}
