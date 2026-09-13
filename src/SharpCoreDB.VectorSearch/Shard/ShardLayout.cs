// <copyright file="ShardLayout.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore shard.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Shard;

/// <summary>
/// A deterministic shard/partition plan: which shard a chunk belongs to, and which pool serves it.
/// </summary>
/// <remarks>
/// Routing a chunk to a shard must be reproducible across processes and runs, so the hash is
/// <b>FNV-1a 64</b> and not <see cref="string.GetHashCode()"/> — the latter is randomized per process
/// by design, which would silently place the same chunk on a different shard after every restart.
/// This is deliberately only the <i>plan</i>: SharpCoreDB has no coordinator process, so the layout
/// is consumed by a caller (the distributed package, a fan-out query, a partitioner).
/// </remarks>
public sealed class ShardLayout
{
    /// <summary>The routing policy version, recorded so a decision stays interpretable.</summary>
    public const uint PolicyVersion = 1;

    private readonly string[] _pools;

    /// <summary>Create a layout of <paramref name="shardCount"/> shards, pooling shard N as <c>shard-N</c>.</summary>
    public ShardLayout(int shardCount)
        : this(shardCount, null)
    {
    }

    /// <summary>Create a layout with explicit pool names (one per shard).</summary>
    /// <param name="shardCount">The number of shards.</param>
    /// <param name="poolNames">Optional pool name per shard; defaults to <c>shard-N</c>.</param>
    public ShardLayout(int shardCount, IReadOnlyList<string>? poolNames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shardCount);

        ShardCount = shardCount;
        if (poolNames is null)
        {
            _pools = new string[shardCount];
            for (int i = 0; i < shardCount; i++)
            {
                _pools[i] = $"shard-{i}";
            }
        }
        else
        {
            if (poolNames.Count != shardCount)
            {
                throw new ArgumentException(
                    $"expected {shardCount} pool names for {shardCount} shards, got {poolNames.Count}",
                    nameof(poolNames));
            }

            _pools = [.. poolNames];
        }
    }

    /// <summary>Gets the number of shards.</summary>
    public int ShardCount { get; }

    /// <summary>Gets the pool names, one per shard.</summary>
    public IReadOnlyList<string> Pools => _pools;

    /// <summary>The shard index a chunk belongs to.</summary>
    public int ShardOf(string chunkId)
    {
        ArgumentException.ThrowIfNullOrEmpty(chunkId);
        return (int)(Fnv1a64(chunkId) % (ulong)ShardCount);
    }

    /// <summary>The pool that serves a chunk.</summary>
    public string PoolOf(string chunkId) => _pools[ShardOf(chunkId)];

    /// <summary>Every shard index, in order.</summary>
    public IReadOnlyList<int> Shards()
    {
        var shards = new int[ShardCount];
        for (int i = 0; i < ShardCount; i++)
        {
            shards[i] = i;
        }

        return shards;
    }

    /// <summary>FNV-1a 64 over the UTF-8 bytes of the value: stable across processes and platforms.</summary>
    public static ulong Fnv1a64(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offsetBasis;
        foreach (char c in value)
        {
            // Fold UTF-16 into bytes so the hash does not depend on the platform's char width.
            hash = (hash ^ (byte)c) * prime;
            hash = (hash ^ (byte)(c >> 8)) * prime;
        }

        return hash;
    }
}
