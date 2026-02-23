// <copyright file="VectorClock.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Vector clock for tracking causality in distributed systems.
/// Used in multi-master replication to detect concurrent updates and ordering.
/// C# 14: Primary constructors, collection expressions.
/// </summary>
public sealed class VectorClock : IEquatable<VectorClock>, IComparable<VectorClock>
{
    private readonly Dictionary<string, long> _clocks = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="VectorClock"/> class.
    /// </summary>
    public VectorClock()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VectorClock"/> class with initial values.
    /// </summary>
    /// <param name="initialClocks">The initial clock values.</param>
    public VectorClock(IReadOnlyDictionary<string, long> initialClocks)
    {
        foreach (var (nodeId, value) in initialClocks)
        {
            _clocks[nodeId] = value;
        }
    }

    /// <summary>
    /// Gets the clock value for a specific node.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>The clock value for the node.</returns>
    public long GetClock(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        return _clocks.TryGetValue(nodeId, out var value) ? value : 0;
    }

    /// <summary>
    /// Increments the clock for a specific node.
    /// </summary>
    /// <param name="nodeId">The node identifier.</param>
    /// <returns>A new vector clock with the incremented value.</returns>
    public VectorClock Increment(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var newClock = new VectorClock(_clocks);
        newClock._clocks[nodeId] = GetClock(nodeId) + 1;
        return newClock;
    }

    /// <summary>
    /// Merges this vector clock with another.
    /// </summary>
    /// <param name="other">The other vector clock to merge with.</param>
    /// <returns>A new vector clock containing the maximum values from both clocks.</returns>
    public VectorClock Merge(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var merged = new VectorClock(_clocks);

        foreach (var (nodeId, value) in other._clocks)
        {
            var currentValue = merged.GetClock(nodeId);
            merged._clocks[nodeId] = Math.Max(currentValue, value);
        }

        return merged;
    }

    /// <summary>
    /// Checks if this vector clock happened before another.
    /// </summary>
    /// <param name="other">The other vector clock.</param>
    /// <returns>True if this clock happened before the other.</returns>
    public bool HappenedBefore(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        // This happened before other if for all nodes, this[node] <= other[node]
        // and there exists at least one node where this[node] < other[node]
        bool hasLessThan = false;

        foreach (var nodeId in _clocks.Keys.Union(other._clocks.Keys))
        {
            var thisValue = GetClock(nodeId);
            var otherValue = other.GetClock(nodeId);

            if (thisValue > otherValue)
            {
                return false; // This has a higher value, so it didn't happen before
            }

            if (thisValue < otherValue)
            {
                hasLessThan = true;
            }
        }

        return hasLessThan;
    }

    /// <summary>
    /// Checks if this vector clock is concurrent with another.
    /// </summary>
    /// <param name="other">The other vector clock.</param>
    /// <returns>True if the clocks are concurrent (neither happened before the other).</returns>
    public bool IsConcurrent(VectorClock other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return !HappenedBefore(other) && !other.HappenedBefore(this);
    }

    /// <summary>
    /// Gets all node identifiers in this vector clock.
    /// </summary>
    public IReadOnlyCollection<string> NodeIds => [.. _clocks.Keys];

    /// <summary>
    /// Returns a string representation of the vector clock.
    /// </summary>
    /// <returns>A string representation.</returns>
    public override string ToString()
    {
        var parts = _clocks.Select(kvp => $"{kvp.Key}:{kvp.Value}");
        return $"{{{string.Join(", ", parts)}}}";
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>True if the specified object is equal to the current object.</returns>
    public override bool Equals(object? obj)
    {
        return obj is VectorClock other && Equals(other);
    }

    /// <summary>
    /// Determines whether the specified vector clock is equal to the current vector clock.
    /// </summary>
    /// <param name="other">The vector clock to compare with the current vector clock.</param>
    /// <returns>True if the specified vector clock is equal to the current vector clock.</returns>
    public bool Equals(VectorClock? other)
    {
        if (other is null)
        {
            return false;
        }

        if (_clocks.Count != other._clocks.Count)
        {
            return false;
        }

        foreach (var (nodeId, value) in _clocks)
        {
            if (other.GetClock(nodeId) != value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns a hash code for the vector clock.
    /// </summary>
    /// <returns>A hash code for the vector clock.</returns>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (nodeId, value) in _clocks.OrderBy(kvp => kvp.Key))
        {
            hash = HashCode.Combine(hash, nodeId, value);
        }
        return hash;
    }

    /// <summary>
    /// Compares this vector clock with another.
    /// </summary>
    /// <param name="other">The other vector clock to compare.</param>
    /// <returns>A value indicating the relative order of the vector clocks.</returns>
    public int CompareTo(VectorClock? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (HappenedBefore(other))
        {
            return -1;
        }

        if (other.HappenedBefore(this))
        {
            return 1;
        }

        // Concurrent or equal
        return 0;
    }

    /// <summary>
    /// Determines whether two vector clocks are equal.
    /// </summary>
    /// <param name="left">The first vector clock.</param>
    /// <param name="right">The second vector clock.</param>
    /// <returns>True if the vector clocks are equal.</returns>
    public static bool operator ==(VectorClock? left, VectorClock? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two vector clocks are not equal.
    /// </summary>
    /// <param name="left">The first vector clock.</param>
    /// <param name="right">The second vector clock.</param>
    /// <returns>True if the vector clocks are not equal.</returns>
    public static bool operator !=(VectorClock? left, VectorClock? right)
    {
        return !(left == right);
    }
}
