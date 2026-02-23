// <copyright file="BufferLifecycle.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Collections.Concurrent;

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Tracks the complete lifecycle of buffer segments from allocation to deallocation.
/// Provides comprehensive monitoring and optimization capabilities.
/// C# 14: Primary constructors, collection expressions, pattern matching.
/// </summary>
public sealed class BufferLifecycle
{
    private readonly Dictionary<BufferSegment, BufferLifecycleRecord> _activeBuffers = [];
    private readonly ConcurrentQueue<BufferLifecycleEvent> _eventQueue = [];
    private readonly Lock _lifecycleLock = new();

    private long _totalAllocations;
    private long _totalDeallocations;
    private long _totalBytesAllocated;
    private long _totalBytesDeallocated;

    /// <summary>Gets the number of currently active buffers.</summary>
    public int ActiveBufferCount => _activeBuffers.Count;

    /// <summary>Gets the total number of allocations since startup.</summary>
    public long TotalAllocations => _totalAllocations;

    /// <summary>Gets the total number of deallocations since startup.</summary>
    public long TotalDeallocations => _totalDeallocations;

    /// <summary>Gets the total bytes allocated since startup.</summary>
    public long TotalBytesAllocated => _totalBytesAllocated;

    /// <summary>Gets the total bytes deallocated since startup.</summary>
    public long TotalBytesDeallocated => _totalBytesDeallocated;

    /// <summary>Gets the current memory usage of active buffers.</summary>
    public long CurrentMemoryUsage => _activeBuffers.Sum(kvp => kvp.Key.Data.Length);

    /// <summary>
    /// Records a buffer allocation event.
    /// </summary>
    /// <param name="buffer">The allocated buffer segment.</param>
    /// <param name="allocationSource">The source of the allocation request.</param>
    /// <param name="requestedSize">The requested buffer size.</param>
    /// <param name="actualSize">The actual allocated buffer size.</param>
    public void RecordAllocation(
        BufferSegment buffer,
        string allocationSource,
        int requestedSize,
        int actualSize)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(allocationSource);

        var record = new BufferLifecycleRecord
        {
            BufferId = GetBufferId(buffer),
            AllocationTime = DateTimeOffset.UtcNow,
            AllocationSource = allocationSource,
            RequestedSize = requestedSize,
            ActualSize = actualSize,
            Status = BufferStatus.Allocated,
            AllocationStackTrace = GetStackTrace()
        };

        lock (_lifecycleLock)
        {
            _activeBuffers[buffer] = record;
            Interlocked.Increment(ref _totalAllocations);
            Interlocked.Add(ref _totalBytesAllocated, actualSize);
        }

        var lifecycleEvent = new BufferLifecycleEvent
        {
            EventType = LifecycleEventType.Allocated,
            BufferId = record.BufferId,
            Timestamp = record.AllocationTime,
            Size = actualSize,
            Source = allocationSource
        };

        _eventQueue.Enqueue(lifecycleEvent);
    }

    /// <summary>
    /// Records a buffer rental event.
    /// </summary>
    /// <param name="buffer">The rented buffer segment.</param>
    /// <param name="rentalSource">The source of the rental request.</param>
    public void RecordRental(BufferSegment buffer, string rentalSource)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(rentalSource);

        lock (_lifecycleLock)
        {
            if (_activeBuffers.TryGetValue(buffer, out var record))
            {
                record.RentalCount++;
                record.LastRentalTime = DateTimeOffset.UtcNow;
                record.LastRentalSource = rentalSource;
                record.Status = BufferStatus.InUse;
            }
        }

        var lifecycleEvent = new BufferLifecycleEvent
        {
            EventType = LifecycleEventType.Rented,
            BufferId = GetBufferId(buffer),
            Timestamp = DateTimeOffset.UtcNow,
            Source = rentalSource
        };

        _eventQueue.Enqueue(lifecycleEvent);
    }

    /// <summary>
    /// Records a buffer return event.
    /// </summary>
    /// <param name="buffer">The returned buffer segment.</param>
    /// <param name="returnSource">The source of the return.</param>
    /// <param name="usageDuration">How long the buffer was in use.</param>
    public void RecordReturn(BufferSegment buffer, string returnSource, TimeSpan usageDuration)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnSource);

        lock (_lifecycleLock)
        {
            if (_activeBuffers.TryGetValue(buffer, out var record))
            {
                record.ReturnCount++;
                record.LastReturnTime = DateTimeOffset.UtcNow;
                record.LastReturnSource = returnSource;
                record.TotalUsageTime += usageDuration;
                record.Status = BufferStatus.Available;
            }
        }

        var lifecycleEvent = new BufferLifecycleEvent
        {
            EventType = LifecycleEventType.Returned,
            BufferId = GetBufferId(buffer),
            Timestamp = DateTimeOffset.UtcNow,
            Source = returnSource,
            Duration = usageDuration
        };

        _eventQueue.Enqueue(lifecycleEvent);
    }

    /// <summary>
    /// Records a buffer deallocation event.
    /// </summary>
    /// <param name="buffer">The deallocated buffer segment.</param>
    /// <param name="deallocationSource">The source of the deallocation.</param>
    /// <param name="lifetime">The total lifetime of the buffer.</param>
    public void RecordDeallocation(BufferSegment buffer, string deallocationSource, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrWhiteSpace(deallocationSource);

        BufferLifecycleRecord? record = null;
        lock (_lifecycleLock)
        {
            if (_activeBuffers.Remove(buffer, out record))
            {
                Interlocked.Increment(ref _totalDeallocations);
                Interlocked.Add(ref _totalBytesDeallocated, record.ActualSize);
            }
        }

        if (record is not null)
        {
            var lifecycleEvent = new BufferLifecycleEvent
            {
                EventType = LifecycleEventType.Deallocated,
                BufferId = record.BufferId,
                Timestamp = DateTimeOffset.UtcNow,
                Size = record.ActualSize,
                Source = deallocationSource,
                Duration = lifetime
            };

            _eventQueue.Enqueue(lifecycleEvent);
        }
    }

    /// <summary>
    /// Gets the lifecycle record for a buffer.
    /// </summary>
    /// <param name="buffer">The buffer segment.</param>
    /// <returns>The lifecycle record, or null if not found.</returns>
    public BufferLifecycleRecord? GetRecord(BufferSegment buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        lock (_lifecycleLock)
        {
            return _activeBuffers.TryGetValue(buffer, out var record) ? record : null;
        }
    }

    /// <summary>
    /// Gets all active buffer records.
    /// </summary>
    /// <returns>Collection of active buffer records.</returns>
    public IReadOnlyCollection<BufferLifecycleRecord> GetActiveRecords()
    {
        lock (_lifecycleLock)
        {
            return [.. _activeBuffers.Values];
        }
    }

    /// <summary>
    /// Gets pending lifecycle events.
    /// </summary>
    /// <param name="maxEvents">Maximum number of events to retrieve.</param>
    /// <returns>Collection of pending events.</returns>
    public IReadOnlyCollection<BufferLifecycleEvent> GetPendingEvents(int maxEvents = 100)
    {
        var events = new List<BufferLifecycleEvent>();

        while (events.Count < maxEvents && _eventQueue.TryDequeue(out var evt))
        {
            events.Add(evt);
        }

        return events;
    }

    /// <summary>
    /// Gets lifecycle statistics.
    /// </summary>
    /// <returns>Lifecycle statistics.</returns>
    public BufferLifecycleStats GetStats()
    {
        var activeRecords = GetActiveRecords();

        return new BufferLifecycleStats
        {
            ActiveBuffers = ActiveBufferCount,
            TotalAllocations = TotalAllocations,
            TotalDeallocations = TotalDeallocations,
            TotalBytesAllocated = TotalBytesAllocated,
            TotalBytesDeallocated = TotalBytesDeallocated,
            CurrentMemoryUsage = CurrentMemoryUsage,
            AverageBufferSize = activeRecords.Any() ? activeRecords.Average(r => r.ActualSize) : 0,
            AverageBufferLifetime = CalculateAverageLifetime(activeRecords),
            AverageUsageTime = activeRecords.Any() ? activeRecords.Average(r => r.TotalUsageTime.TotalMilliseconds) : 0,
            PendingEvents = _eventQueue.Count
        };
    }

    /// <summary>
    /// Finds buffers that have been rented for too long.
    /// </summary>
    /// <param name="maxRentalTime">Maximum allowed rental time.</param>
    /// <returns>Collection of long-rented buffers.</returns>
    public IReadOnlyCollection<BufferLifecycleRecord> FindLongRentedBuffers(TimeSpan maxRentalTime)
    {
        var now = DateTimeOffset.UtcNow;
        var longRented = new List<BufferLifecycleRecord>();

        lock (_lifecycleLock)
        {
            foreach (var record in _activeBuffers.Values)
            {
                if (record.Status == BufferStatus.InUse &&
                    record.LastRentalTime.HasValue &&
                    (now - record.LastRentalTime.Value) > maxRentalTime)
                {
                    longRented.Add(record);
                }
            }
        }

        return longRented;
    }

    /// <summary>
    /// Cleans up old event records.
    /// </summary>
    /// <param name="maxAge">Maximum age of events to keep.</param>
    public void CleanupOldEvents(TimeSpan maxAge)
    {
        // Note: In a real implementation, we'd maintain a timestamped queue
        // For now, this is a placeholder for event cleanup logic
    }

    /// <summary>
    /// Generates a unique identifier for a buffer.
    /// </summary>
    /// <param name="buffer">The buffer segment.</param>
    /// <returns>Unique buffer identifier.</returns>
    private static string GetBufferId(BufferSegment buffer)
    {
        // Use object hash code for unique identification
        return $"Buffer_{buffer.GetHashCode():X8}";
    }

    /// <summary>
    /// Gets a simplified stack trace for debugging.
    /// </summary>
    /// <returns>Stack trace string.</returns>
    private static string GetStackTrace()
    {
#if DEBUG
        return Environment.StackTrace;
#else
        return "Stack trace not available in release build";
#endif
    }

    /// <summary>
    /// Calculates the average lifetime of buffers.
    /// </summary>
    /// <param name="records">The buffer records.</param>
    /// <returns>Average lifetime.</returns>
    private static double CalculateAverageLifetime(IReadOnlyCollection<BufferLifecycleRecord> records)
    {
        if (!records.Any())
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var lifetimes = records
            .Where(r => r.Status != BufferStatus.Allocated) // Only count buffers that have been used
            .Select(r => (now - r.AllocationTime).TotalMilliseconds)
            .ToList();

        return lifetimes.Any() ? lifetimes.Average() : 0;
    }
}

/// <summary>
/// Represents a buffer lifecycle record.
/// </summary>
public class BufferLifecycleRecord
{
    /// <summary>Gets the unique buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the allocation timestamp.</summary>
    public DateTimeOffset AllocationTime { get; init; }

    /// <summary>Gets the allocation source.</summary>
    public string AllocationSource { get; init; } = string.Empty;

    /// <summary>Gets the requested buffer size.</summary>
    public int RequestedSize { get; init; }

    /// <summary>Gets the actual allocated buffer size.</summary>
    public int ActualSize { get; init; }

    /// <summary>Gets the current buffer status.</summary>
    public BufferStatus Status { get; set; }

    /// <summary>Gets the number of times this buffer has been rented.</summary>
    public int RentalCount { get; set; }

    /// <summary>Gets the number of times this buffer has been returned.</summary>
    public int ReturnCount { get; set; }

    /// <summary>Gets the last rental timestamp.</summary>
    public DateTimeOffset? LastRentalTime { get; set; }

    /// <summary>Gets the last rental source.</summary>
    public string? LastRentalSource { get; set; }

    /// <summary>Gets the last return timestamp.</summary>
    public DateTimeOffset? LastReturnTime { get; set; }

    /// <summary>Gets the last return source.</summary>
    public string? LastReturnSource { get; set; }

    /// <summary>Gets the total time this buffer has been in use.</summary>
    public TimeSpan TotalUsageTime { get; set; }

    /// <summary>Gets the allocation stack trace.</summary>
    public string? AllocationStackTrace { get; init; }

    /// <summary>Gets the current age of the buffer.</summary>
    public TimeSpan Age => DateTimeOffset.UtcNow - AllocationTime;

    /// <summary>Gets the average usage time per rental.</summary>
    public TimeSpan AverageUsageTime => RentalCount > 0
        ? TimeSpan.FromTicks(TotalUsageTime.Ticks / RentalCount)
        : TimeSpan.Zero;
}

/// <summary>
/// Represents a buffer lifecycle event.
/// </summary>
public class BufferLifecycleEvent
{
    /// <summary>Gets the event type.</summary>
    public LifecycleEventType EventType { get; init; }

    /// <summary>Gets the buffer identifier.</summary>
    public string BufferId { get; init; } = string.Empty;

    /// <summary>Gets the event timestamp.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the buffer size (for allocation/deallocation events).</summary>
    public int? Size { get; init; }

    /// <summary>Gets the event source.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the duration (for usage events).</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Buffer status enumeration.
/// </summary>
public enum BufferStatus
{
    /// <summary>Buffer has been allocated but not yet used.</summary>
    Allocated,

    /// <summary>Buffer is currently in use.</summary>
    InUse,

    /// <summary>Buffer is available for rental.</summary>
    Available,

    /// <summary>Buffer has been deallocated.</summary>
    Deallocated
}

/// <summary>
/// Lifecycle event types.
/// </summary>
public enum LifecycleEventType
{
    /// <summary>Buffer was allocated.</summary>
    Allocated,

    /// <summary>Buffer was rented.</summary>
    Rented,

    /// <summary>Buffer was returned.</summary>
    Returned,

    /// <summary>Buffer was deallocated.</summary>
    Deallocated
}

/// <summary>
/// Statistics for buffer lifecycle operations.
/// </summary>
public class BufferLifecycleStats
{
    /// <summary>Gets the number of active buffers.</summary>
    public int ActiveBuffers { get; init; }

    /// <summary>Gets the total number of allocations.</summary>
    public long TotalAllocations { get; init; }

    /// <summary>Gets the total number of deallocations.</summary>
    public long TotalDeallocations { get; init; }

    /// <summary>Gets the total bytes allocated.</summary>
    public long TotalBytesAllocated { get; init; }

    /// <summary>Gets the total bytes deallocated.</summary>
    public long TotalBytesDeallocated { get; init; }

    /// <summary>Gets the current memory usage.</summary>
    public long CurrentMemoryUsage { get; init; }

    /// <summary>Gets the average buffer size.</summary>
    public double AverageBufferSize { get; init; }

    /// <summary>Gets the average buffer lifetime in milliseconds.</summary>
    public double AverageBufferLifetime { get; init; }

    /// <summary>Gets the average usage time per rental in milliseconds.</summary>
    public double AverageUsageTime { get; init; }

    /// <summary>Gets the number of pending lifecycle events.</summary>
    public int PendingEvents { get; init; }

    /// <summary>Gets the allocation rate (allocations per second).</summary>
    public double AllocationRate => TotalAllocations > 0 ? TotalAllocations / Math.Max(AverageBufferLifetime / 1000, 1) : 0;
}
