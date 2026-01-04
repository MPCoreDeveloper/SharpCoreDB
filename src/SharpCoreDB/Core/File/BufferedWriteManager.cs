// <copyright file="BufferedWriteManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.File;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// Manages buffered writes to files with batching and atomic flush operations.
/// Eliminates the O(n²) behavior of full-file rewrites by batching multiple
/// writes to the same file into a single disk operation.
/// </summary>
public class BufferedWriteManager : IDisposable
{
    // Per-file buffered writes
    private readonly Dictionary<string, FileWriteBuffer> fileBuffers = [];
    private readonly Lock bufferLock = new();
    
    private bool disposed = false;

    /// <summary>
    /// Represents buffered writes for a single file.
    /// </summary>
    private sealed class FileWriteBuffer
    {
        public required string FilePath { get; set; }
        public List<(long Position, byte[] Data)> Writes { get; set; } = [];
        public int TotalBytes { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of BufferedWriteManager.
    /// </summary>
    public BufferedWriteManager()
    {
    }

    /// <summary>
    /// Adds a write operation to the buffer for the specified file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="position">Position in file to write.</param>
    /// <param name="data">The data to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void BufferWrite(string filePath, long position, byte[] data)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(BufferedWriteManager));
        
        lock (this.bufferLock)
        {
            if (!this.fileBuffers.TryGetValue(filePath, out var buffer))
            {
                buffer = new FileWriteBuffer { FilePath = filePath, Writes = [], TotalBytes = 0 };
                this.fileBuffers[filePath] = buffer;
            }
            
            buffer.Writes.Add((position, data));
            buffer.TotalBytes += data.Length;
        }
    }

    /// <summary>
    /// Flushes all buffered writes for a specific file to disk.
    /// Writes are sorted by position and written sequentially for efficiency.
    /// </summary>
    /// <param name="filePath">The file path to flush.</param>
    /// <returns>Number of writes flushed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int FlushFile(string filePath)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(BufferedWriteManager));
        
        lock (this.bufferLock)
        {
            if (!this.fileBuffers.TryGetValue(filePath, out var buffer))
            {
                return 0;  // Nothing to flush
            }
            
            if (buffer.Writes.Count == 0)
            {
                return 0;
            }
            
            int writeCount = FlushFileInternal(buffer);
            buffer.Writes.Clear();
            buffer.TotalBytes = 0;
            
            return writeCount;
        }
    }

    /// <summary>
    /// Flushes all buffered writes across all files to disk.
    /// </summary>
    /// <returns>Total number of writes flushed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int FlushAll()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(BufferedWriteManager));
        
        lock (this.bufferLock)
        {
            int totalWrites = 0;
            
            foreach (var buffer in this.fileBuffers.Values)
            {
                if (buffer.Writes.Count > 0)
                {
                    totalWrites += FlushFileInternal(buffer);
                    buffer.Writes.Clear();
                    buffer.TotalBytes = 0;
                }
            }
            
            return totalWrites;
        }
    }

    /// <summary>
    /// Internal flush method that assumes lock is held.
    /// </summary>
    private static int FlushFileInternal(FileWriteBuffer buffer)
    {
        if (buffer.Writes.Count == 0)
            return 0;
        
        var sortedWrites = buffer.Writes.OrderBy(w => w.Position).ToList();
        int writeCount = sortedWrites.Count;
        
        using (var fs = new FileStream(
            buffer.FilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            65536,
            FileOptions.WriteThrough))
        {
            foreach (var (position, data) in sortedWrites)
            {
                if (fs.Position != position)
                {
                    fs.Seek(position, SeekOrigin.Begin);
                }
                
                fs.Write(data.AsSpan());
            }
            
            fs.Flush(flushToDisk: true);
        }
        
        return writeCount;
    }

    /// <summary>
    /// Gets the number of pending writes for a specific file.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <returns>Number of pending writes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetPendingWriteCount(string filePath)
    {
        lock (this.bufferLock)
        {
            return this.fileBuffers.TryGetValue(filePath, out var buffer) ? buffer.Writes.Count : 0;
        }
    }

    /// <summary>
    /// Gets the total number of pending writes across all files.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTotalPendingWriteCount()
    {
        lock (this.bufferLock)
        {
            return this.fileBuffers.Values.Sum(b => b.Writes.Count);
        }
    }

    /// <summary>
    /// Gets the total bytes pending across all files.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetTotalPendingBytes()
    {
        lock (this.bufferLock)
        {
            return this.fileBuffers.Values.Sum(b => b.TotalBytes);
        }
    }

    /// <summary>
    /// Clears all buffered writes without flushing them.
    /// </summary>
    public void Clear()
    {
        lock (this.bufferLock)
        {
            this.fileBuffers.Clear();
        }
    }

    /// <summary>
    /// Disposes the manager and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose implementation.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
            return;
        
        if (disposing)
        {
            lock (this.bufferLock)
            {
                this.fileBuffers.Clear();
            }
        }
        
        disposed = true;
    }
}
