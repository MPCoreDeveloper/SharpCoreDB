// <copyright file="ShardKey.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Sharding;

/// <summary>
/// Abstract base class for shard key strategies.
/// Defines how data is distributed across shards.
/// C# 14: Abstract class with covariant return types.
/// </summary>
public abstract class ShardKey
{
    /// <summary>Gets the name of the column used for sharding.</summary>
    public string ColumnName { get; }

    /// <summary>Gets the data type of the shard key column.</summary>
    public Type ColumnType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardKey"/> class.
    /// </summary>
    /// <param name="columnName">The name of the sharding column.</param>
    /// <param name="columnType">The data type of the sharding column.</param>
    protected ShardKey(string columnName, Type columnType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        ArgumentNullException.ThrowIfNull(columnType);

        ColumnName = columnName;
        ColumnType = columnType;
    }

    /// <summary>
    /// Calculates the shard index for a given key value.
    /// </summary>
    /// <param name="value">The shard key value.</param>
    /// <param name="shardCount">The total number of shards.</param>
    /// <returns>The shard index (0 to shardCount-1).</returns>
    public abstract int GetShardIndex(object? value, int shardCount);

    /// <summary>
    /// Validates that a value is compatible with this shard key.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns>True if the value is valid for this shard key.</returns>
    public virtual bool IsValidValue(object? value)
    {
        if (value is null)
        {
            return true; // Allow null values
        }

        return ColumnType.IsAssignableFrom(value.GetType());
    }

    /// <summary>
    /// Gets the shard key strategy type.
    /// </summary>
    public abstract ShardKeyStrategy Strategy { get; }
}

/// <summary>
/// Hash-based sharding strategy.
/// Distributes data evenly using hash functions.
/// </summary>
public sealed class HashShardKey : ShardKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HashShardKey"/> class.
    /// </summary>
    /// <param name="columnName">The name of the sharding column.</param>
    /// <param name="columnType">The data type of the sharding column.</param>
    public HashShardKey(string columnName, Type? columnType = null)
        : base(columnName, columnType ?? typeof(string))
    {
    }

    /// <inheritdoc />
    public override ShardKeyStrategy Strategy => ShardKeyStrategy.Hash;

    /// <inheritdoc />
    public override int GetShardIndex(object? value, int shardCount)
    {
        if (shardCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shardCount), "Shard count must be positive.");
        }

        if (value is null)
        {
            return 0; // Null values go to shard 0
        }

        // Use string representation for consistent hashing
        var stringValue = value.ToString() ?? string.Empty;
        var hash = stringValue.GetHashCode();

        // Ensure positive hash value
        var positiveHash = hash & 0x7FFFFFFF;

        return positiveHash % shardCount;
    }
}

/// <summary>
/// Range-based sharding strategy.
/// Partitions data by value ranges.
/// </summary>
public sealed class RangeShardKey : ShardKey
{
    private readonly List<RangeBoundary> _ranges = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeShardKey"/> class.
    /// </summary>
    /// <param name="columnName">The name of the sharding column.</param>
    /// <param name="columnType">The data type of the sharding column.</param>
    public RangeShardKey(string columnName, Type columnType)
        : base(columnName, columnType)
    {
    }

    /// <inheritdoc />
    public override ShardKeyStrategy Strategy => ShardKeyStrategy.Range;

    /// <summary>
    /// Adds a range boundary for shard assignment.
    /// </summary>
    /// <param name="maxValue">The maximum value for this range (inclusive).</param>
    /// <param name="shardIndex">The shard index for values in this range.</param>
    public void AddRange(IComparable maxValue, int shardIndex)
    {
        ArgumentNullException.ThrowIfNull(maxValue);

        _ranges.Add(new RangeBoundary(maxValue, shardIndex));
        _ranges.Sort((a, b) => a.MaxValue.CompareTo(b.MaxValue));
    }

    /// <inheritdoc />
    public override int GetShardIndex(object? value, int shardCount)
    {
        if (value is null)
        {
            return 0; // Null values go to shard 0
        }

        if (value is not IComparable comparable)
        {
            throw new InvalidOperationException($"Value of type {value.GetType()} is not comparable.");
        }

        foreach (var range in _ranges)
        {
            if (comparable.CompareTo(range.MaxValue) <= 0)
            {
                return range.ShardIndex;
            }
        }

        // Values greater than all ranges go to the last shard
        return _ranges.Count > 0 ? _ranges[^1].ShardIndex : 0;
    }

    /// <inheritdoc />
    public override bool IsValidValue(object? value)
    {
        if (!base.IsValidValue(value))
        {
            return false;
        }

        return value is null || value is IComparable;
    }

    private readonly struct RangeBoundary(IComparable maxValue, int shardIndex)
    {
        public IComparable MaxValue { get; } = maxValue;
        public int ShardIndex { get; } = shardIndex;
    }
}

/// <summary>
/// List-based sharding strategy.
/// Explicitly assigns values to specific shards.
/// </summary>
public sealed class ListShardKey : ShardKey
{
    private readonly Dictionary<object, int> _valueMappings = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ListShardKey"/> class.
    /// </summary>
    /// <param name="columnName">The name of the sharding column.</param>
    /// <param name="columnType">The data type of the sharding column.</param>
    public ListShardKey(string columnName, Type columnType)
        : base(columnName, columnType)
    {
    }

    /// <inheritdoc />
    public override ShardKeyStrategy Strategy => ShardKeyStrategy.List;

    /// <summary>
    /// Maps a specific value to a shard.
    /// </summary>
    /// <param name="value">The value to map.</param>
    /// <param name="shardIndex">The shard index for this value.</param>
    public void MapValue(object value, int shardIndex)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!IsValidValue(value))
        {
            throw new ArgumentException($"Value type {value.GetType()} is not compatible with column type {ColumnType}.");
        }

        _valueMappings[value] = shardIndex;
    }

    /// <summary>
    /// Maps multiple values to the same shard.
    /// </summary>
    /// <param name="values">The values to map.</param>
    /// <param name="shardIndex">The shard index for these values.</param>
    public void MapValues(IEnumerable<object> values, int shardIndex)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var value in values)
        {
            MapValue(value, shardIndex);
        }
    }

    /// <inheritdoc />
    public override int GetShardIndex(object? value, int shardCount)
    {
        if (value is null)
        {
            return 0; // Null values go to shard 0
        }

        if (_valueMappings.TryGetValue(value, out var shardIndex))
        {
            return shardIndex;
        }

        // Unmapped values go to shard 0 by default
        return 0;
    }

    /// <summary>
    /// Gets all mapped values for a shard.
    /// </summary>
    /// <param name="shardIndex">The shard index.</param>
    /// <returns>Collection of values mapped to the shard.</returns>
    public IReadOnlyCollection<object> GetValuesForShard(int shardIndex)
    {
        return [.. _valueMappings.Where(kvp => kvp.Value == shardIndex).Select(kvp => kvp.Key)];
    }
}

/// <summary>
/// Composite sharding strategy.
/// Uses multiple columns for sharding decisions.
/// </summary>
public sealed class CompositeShardKey : ShardKey
{
    private readonly ShardKey[] _keys;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeShardKey"/> class.
    /// </summary>
    /// <param name="keys">The shard keys to combine.</param>
    public CompositeShardKey(params ShardKey[] keys)
        : base(string.Join(",", keys.Select(k => k.ColumnName)), typeof(string))
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Length == 0)
        {
            throw new ArgumentException("At least one shard key must be provided.");
        }

        _keys = [.. keys];
    }

    /// <inheritdoc />
    public override ShardKeyStrategy Strategy => ShardKeyStrategy.Composite;

    /// <inheritdoc />
    public override int GetShardIndex(object? value, int shardCount)
    {
        // For composite keys, we expect a tuple or array of values
        if (value is not object[] values || values.Length != _keys.Length)
        {
            throw new InvalidOperationException("Composite shard key requires an array of values matching the number of keys.");
        }

        // Combine hash codes from all keys
        var combinedHash = 0;
        for (var i = 0; i < _keys.Length; i++)
        {
            var keyHash = _keys[i].GetShardIndex(values[i], shardCount);
            combinedHash = HashCode.Combine(combinedHash, keyHash);
        }

        return Math.Abs(combinedHash) % shardCount;
    }

    /// <inheritdoc />
    public override bool IsValidValue(object? value)
    {
        if (value is not object[] values || values.Length != _keys.Length)
        {
            return false;
        }

        for (var i = 0; i < _keys.Length; i++)
        {
            if (!_keys[i].IsValidValue(values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Gets the individual shard keys.</summary>
    public IReadOnlyList<ShardKey> Keys => _keys;
}

/// <summary>
/// Defines the type of sharding strategy.
/// </summary>
public enum ShardKeyStrategy
{
    /// <summary>Hash-based distribution.</summary>
    Hash,

    /// <summary>Range-based partitioning.</summary>
    Range,

    /// <summary>Explicit value-to-shard mapping.</summary>
    List,

    /// <summary>Composite key using multiple columns.</summary>
    Composite
}
