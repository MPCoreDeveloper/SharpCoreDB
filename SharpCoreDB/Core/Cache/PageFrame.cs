// <copyright file="PageFrame.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Core.Cache;

using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// Represents a single page frame in the buffer pool cache.
/// Thread-safe with lightweight latching using Interlocked operations.
/// </summary>
public sealed class PageFrame : IDisposable
{
    private const int UnlatchedState = 0;
    private const int LatchedState = 1;
    
    private readonly int pageSize;
    private readonly IMemoryOwner<byte>? memoryOwner;
    private int latchState;
    private int pinCount;
    private int isDirty;
    private long lastAccessTick;
    private int clockBit;
    private int pageId;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageFrame"/> class.
    /// </summary>
    /// <param name="pageId">The page identifier.</param>
    /// <param name="pageSize">The size of the page in bytes.</param>
    /// <param name="memoryPool">The memory pool to rent from.</param>
    public PageFrame(int pageId, int pageSize, MemoryPool<byte> memoryPool)
    {
        this.pageId = pageId;
        this.pageSize = pageSize;
        this.memoryOwner = memoryPool.Rent(pageSize);
        this.latchState = UnlatchedState;
        this.pinCount = 0;
        this.isDirty = 0;
        this.lastAccessTick = Stopwatch.GetTimestamp();
        this.clockBit = 1; // Start with clock bit set (recently accessed)
        this.disposed = false;
    }

    /// <summary>
    /// Gets the page identifier.
    /// </summary>
    public int PageId => Volatile.Read(ref this.pageId);

    /// <summary>
    /// Gets the page buffer as a span.
    /// </summary>
    public Span<byte> Buffer
    {
        get
        {
            if (this.memoryOwner == null)
            {
                return Span<byte>.Empty;
            }
            return this.memoryOwner.Memory.Span.Slice(0, this.pageSize);
        }
    }

    /// <summary>
    /// Gets the page buffer as read-only memory.
    /// </summary>
    public ReadOnlyMemory<byte> ReadOnlyBuffer => this.memoryOwner?.Memory.Slice(0, this.pageSize) ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>
    /// Gets a value indicating whether the page is dirty (modified).
    /// </summary>
    public bool IsDirty => Volatile.Read(ref this.isDirty) == 1;

    /// <summary>
    /// Gets the current pin count (number of threads using this page).
    /// </summary>
    public int PinCount => Volatile.Read(ref this.pinCount);

    /// <summary>
    /// Gets the last access timestamp (Stopwatch ticks).
    /// </summary>
    public long LastAccessTick => Volatile.Read(ref this.lastAccessTick);

    /// <summary>
    /// Gets or sets the CLOCK algorithm bit (0 or 1).
    /// </summary>
    public int ClockBit
    {
        get => Volatile.Read(ref this.clockBit);
        set => Volatile.Write(ref this.clockBit, value);
    }

    /// <summary>
    /// Gets a value indicating whether the page is latched.
    /// </summary>
    public bool IsLatched => Volatile.Read(ref this.latchState) == LatchedState;

    /// <summary>
    /// Attempts to acquire a latch on this page frame using CAS (Compare-And-Swap).
    /// Lightweight and lock-free.
    /// </summary>
    /// <param name="spinCount">Number of spin attempts before giving up.</param>
    /// <returns>True if latch acquired, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryLatch(int spinCount = 100)
    {
        for (int i = 0; i < spinCount; i++)
        {
            if (Interlocked.CompareExchange(ref this.latchState, LatchedState, UnlatchedState) == UnlatchedState)
            {
                return true;
            }

            // Exponential backoff for better CPU efficiency
            if (i < 10)
            {
                Thread.SpinWait(1 << i); // 1, 2, 4, 8, 16, 32, 64, 128, 256, 512
            }
            else
            {
                Thread.Yield(); // Yield to other threads after initial spins
            }
        }

        return false;
    }

    /// <summary>
    /// Releases the latch on this page frame.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unlatch()
    {
        Volatile.Write(ref this.latchState, UnlatchedState);
    }

    /// <summary>
    /// Increments the pin count atomically.
    /// </summary>
    /// <returns>The new pin count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Pin()
    {
        UpdateLastAccessTime();
        this.clockBit = 1; // Mark as recently accessed
        return Interlocked.Increment(ref this.pinCount);
    }

    /// <summary>
    /// Decrements the pin count atomically.
    /// </summary>
    /// <returns>The new pin count.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unpin()
    {
        int newCount = Interlocked.Decrement(ref this.pinCount);
        if (newCount < 0)
        {
            // Should never happen in correct usage, but protect against underflow
            Interlocked.Increment(ref this.pinCount);
            throw new InvalidOperationException($"Page {this.PageId} pin count went negative");
        }

        return newCount;
    }

    /// <summary>
    /// Marks the page as dirty (modified).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MarkDirty()
    {
        Volatile.Write(ref this.isDirty, 1);
    }

    /// <summary>
    /// Clears the dirty flag (after flush to disk).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearDirty()
    {
        Volatile.Write(ref this.isDirty, 0);
    }

    /// <summary>
    /// Updates the last access timestamp.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateLastAccessTime()
    {
        Volatile.Write(ref this.lastAccessTick, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Checks if this page can be evicted (pin count must be 0 and not latched).
    /// </summary>
    /// <returns>True if page can be evicted.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CanEvict()
    {
        return Volatile.Read(ref this.pinCount) == 0 && !IsLatched;
    }

    /// <summary>
    /// Resets the page frame for reuse with a new page ID.
    /// Must be called while latched and with pin count of 0.
    /// </summary>
    /// <param name="newPageId">The new page ID.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset(int newPageId)
    {
        if (!IsLatched)
        {
            throw new InvalidOperationException("Page must be latched before reset");
        }

        if (PinCount != 0)
        {
            throw new InvalidOperationException($"Cannot reset page {this.PageId} with pin count {this.PinCount}");
        }

        // Clear the buffer
        this.Buffer.Clear();

        // Reset metadata
        Volatile.Write(ref this.isDirty, 0);
        Volatile.Write(ref this.lastAccessTick, Stopwatch.GetTimestamp());
        Volatile.Write(ref this.clockBit, 1);
        
        // Update page ID
        Volatile.Write(ref this.pageId, newPageId);
    }

    /// <summary>
    /// Gets diagnostic information about this page frame.
    /// </summary>
    /// <returns>A string with page frame statistics.</returns>
    public string GetDiagnostics()
    {
        return $"PageFrame[Id={this.PageId}, PinCount={this.PinCount}, " +
               $"Dirty={this.IsDirty}, Latched={this.IsLatched}, " +
               $"ClockBit={this.ClockBit}, LastAccess={(Stopwatch.GetTimestamp() - this.LastAccessTick) / (double)Stopwatch.Frequency:F3}s ago]";
    }

    /// <summary>
    /// Disposes the page frame and returns memory to pool.
    /// </summary>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.memoryOwner?.Dispose();
            this.disposed = true;
        }
    }
}
