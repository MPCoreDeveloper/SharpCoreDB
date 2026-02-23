// <copyright file="WALPosition.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Represents a position in the WAL file for replication tracking.
/// Immutable struct for thread-safe position management.
/// C# 14: Struct with primary constructor, implements comparison operators.
/// </summary>
public readonly struct WALPosition : IComparable<WALPosition>, IEquatable<WALPosition>
{
    /// <summary>Gets the file offset in bytes.</summary>
    public long Offset { get; }

    /// <summary>Gets the logical sequence number (LSN) for ordering.</summary>
    public long Lsn { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WALPosition"/> struct.
    /// </summary>
    /// <param name="offset">File offset in bytes.</param>
    /// <param name="lsn">Logical sequence number.</param>
    public WALPosition(long offset, long lsn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(lsn);

        Offset = offset;
        Lsn = lsn;
    }

    /// <summary>
    /// Creates a WAL position from a file offset only.
    /// LSN will be set to the same value as offset.
    /// </summary>
    /// <param name="offset">File offset in bytes.</param>
    /// <returns>A new WAL position.</returns>
    public static WALPosition FromOffset(long offset)
    {
        return new WALPosition(offset, offset);
    }

    /// <summary>
    /// Gets the minimum possible WAL position (beginning of file).
    /// </summary>
    public static WALPosition MinValue => new(0, 0);

    /// <summary>
    /// Gets the maximum possible WAL position.
    /// </summary>
    public static WALPosition MaxValue => new(long.MaxValue, long.MaxValue);

    /// <summary>
    /// Advances this position by the specified number of bytes.
    /// </summary>
    /// <param name="bytes">Number of bytes to advance.</param>
    /// <returns>The new WAL position.</returns>
    public WALPosition Advance(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return new WALPosition(Offset + bytes, Lsn + 1);
    }

    /// <summary>
    /// Checks if this position is before another position.
    /// </summary>
    /// <param name="other">The other position to compare with.</param>
    /// <returns>True if this position is before the other.</returns>
    public bool IsBefore(WALPosition other)
    {
        return Lsn < other.Lsn;
    }

    /// <summary>
    /// Checks if this position is after another position.
    /// </summary>
    /// <param name="other">The other position to compare with.</param>
    /// <returns>True if this position is after the other.</returns>
    public bool IsAfter(WALPosition other)
    {
        return Lsn > other.Lsn;
    }

    /// <summary>
    /// Calculates the distance between two positions.
    /// </summary>
    /// <param name="other">The other position.</param>
    /// <returns>The distance in logical sequence numbers.</returns>
    public long DistanceTo(WALPosition other)
    {
        return Math.Abs(Lsn - other.Lsn);
    }

    /// <summary>
    /// Gets the string representation of this position.
    /// </summary>
    /// <returns>String representation.</returns>
    public override string ToString()
    {
        return $"WALPosition(Offset={Offset}, LSN={Lsn})";
    }

    /// <summary>
    /// Compares this position to another position.
    /// </summary>
    /// <param name="other">The other position to compare with.</param>
    /// <returns>Comparison result.</returns>
    public int CompareTo(WALPosition other)
    {
        var lsnComparison = Lsn.CompareTo(other.Lsn);
        return lsnComparison != 0 ? lsnComparison : Offset.CompareTo(other.Offset);
    }

    /// <summary>
    /// Checks equality with another position.
    /// </summary>
    /// <param name="other">The other position to compare with.</param>
    /// <returns>True if positions are equal.</returns>
    public bool Equals(WALPosition other)
    {
        return Offset == other.Offset && Lsn == other.Lsn;
    }

    /// <summary>
    /// Checks equality with an object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if the object is an equal WAL position.</returns>
    public override bool Equals(object? obj)
    {
        return obj is WALPosition other && Equals(other);
    }

    /// <summary>
    /// Gets the hash code for this position.
    /// </summary>
    /// <returns>Hash code.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Offset, Lsn);
    }

    // Operator overloads for convenient comparisons

    /// <summary>Equality operator.</summary>
    public static bool operator ==(WALPosition left, WALPosition right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(WALPosition left, WALPosition right) => !left.Equals(right);

    /// <summary>Less than operator.</summary>
    public static bool operator <(WALPosition left, WALPosition right) => left.CompareTo(right) < 0;

    /// <summary>Less than or equal operator.</summary>
    public static bool operator <=(WALPosition left, WALPosition right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than operator.</summary>
    public static bool operator >(WALPosition left, WALPosition right) => left.CompareTo(right) > 0;

    /// <summary>Greater than or equal operator.</summary>
    public static bool operator >=(WALPosition left, WALPosition right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// Tracks WAL positions for multiple replicas.
/// Thread-safe management of replication positions.
/// </summary>
public sealed class WALPositionTracker
{
    private readonly Dictionary<string, WALPosition> _replicaPositions = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets the current position for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <returns>The current position, or MinValue if not tracked.</returns>
    public WALPosition GetPosition(string replicaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        lock (_lock)
        {
            return _replicaPositions.TryGetValue(replicaId, out var position) ? position : WALPosition.MinValue;
        }
    }

    /// <summary>
    /// Updates the position for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="position">The new position.</param>
    public void UpdatePosition(string replicaId, WALPosition position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        lock (_lock)
        {
            _replicaPositions[replicaId] = position;
        }
    }

    /// <summary>
    /// Advances the position for a replica by the specified bytes.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <param name="bytes">Number of bytes to advance.</param>
    /// <returns>The new position.</returns>
    public WALPosition AdvancePosition(string replicaId, long bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        lock (_lock)
        {
            var currentPosition = GetPosition(replicaId);
            var newPosition = currentPosition.Advance(bytes);
            _replicaPositions[replicaId] = newPosition;
            return newPosition;
        }
    }

    /// <summary>
    /// Removes position tracking for a replica.
    /// </summary>
    /// <param name="replicaId">The replica identifier.</param>
    /// <returns>True if the replica was being tracked.</returns>
    public bool RemoveReplica(string replicaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replicaId);

        lock (_lock)
        {
            return _replicaPositions.Remove(replicaId);
        }
    }

    /// <summary>
    /// Gets all tracked replica positions.
    /// </summary>
    /// <returns>Dictionary of replica IDs to positions.</returns>
    public IReadOnlyDictionary<string, WALPosition> GetAllPositions()
    {
        lock (_lock)
        {
            return new Dictionary<string, WALPosition>(_replicaPositions);
        }
    }

    /// <summary>
    /// Gets the minimum position across all replicas.
    /// Useful for determining the oldest position that can be safely truncated.
    /// </summary>
    /// <returns>The minimum position, or MaxValue if no replicas are tracked.</returns>
    public WALPosition GetMinimumPosition()
    {
        lock (_lock)
        {
            if (_replicaPositions.Count == 0)
            {
                return WALPosition.MaxValue;
            }

            var minPosition = WALPosition.MaxValue;
            foreach (var position in _replicaPositions.Values)
            {
                if (position < minPosition)
                {
                    minPosition = position;
                }
            }

            return minPosition;
        }
    }

    /// <summary>
    /// Gets the maximum position across all replicas.
    /// </summary>
    /// <returns>The maximum position, or MinValue if no replicas are tracked.</returns>
    public WALPosition GetMaximumPosition()
    {
        lock (_lock)
        {
            if (_replicaPositions.Count == 0)
            {
                return WALPosition.MinValue;
            }

            var maxPosition = WALPosition.MinValue;
            foreach (var position in _replicaPositions.Values)
            {
                if (position > maxPosition)
                {
                    maxPosition = position;
                }
            }

            return maxPosition;
        }
    }

    /// <summary>
    /// Gets statistics about position tracking.
    /// </summary>
    /// <returns>Position tracking statistics.</returns>
    public WALPositionStats GetStats()
    {
        lock (_lock)
        {
            return new WALPositionStats
            {
                TrackedReplicas = _replicaPositions.Count,
                MinimumPosition = GetMinimumPosition(),
                MaximumPosition = GetMaximumPosition(),
                AveragePosition = _replicaPositions.Count > 0
                    ? new WALPosition(
                        (long)_replicaPositions.Values.Average(p => p.Offset),
                        (long)_replicaPositions.Values.Average(p => p.Lsn))
                    : WALPosition.MinValue
            };
        }
    }
}

/// <summary>
/// Statistics for WAL position tracking.
/// </summary>
public class WALPositionStats
{
    /// <summary>Gets the number of tracked replicas.</summary>
    public int TrackedReplicas { get; init; }

    /// <summary>Gets the minimum position across all replicas.</summary>
    public WALPosition MinimumPosition { get; init; }

    /// <summary>Gets the maximum position across all replicas.</summary>
    public WALPosition MaximumPosition { get; init; }

    /// <summary>Gets the average position across all replicas.</summary>
    public WALPosition AveragePosition { get; init; }

    /// <summary>Gets the position range (max - min).</summary>
    public long PositionRange => MaximumPosition.Lsn - MinimumPosition.Lsn;
}
