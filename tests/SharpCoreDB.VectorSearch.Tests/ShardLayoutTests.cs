// <copyright file="ShardLayoutTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore shard.rs.
// </copyright>

using SharpCoreDB.VectorSearch.Shard;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>Deterministic shard routing.</summary>
public class ShardLayoutTests
{
    [Fact]
    public void A_chunk_always_routes_to_the_same_shard()
    {
        var layout = new ShardLayout(8);

        int first = layout.ShardOf("chunk-42");
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(first, layout.ShardOf("chunk-42"));
        }

        Assert.InRange(first, 0, 7);
    }

    [Fact]
    public void Routing_is_stable_across_instances()
    {
        var a = new ShardLayout(16);
        var b = new ShardLayout(16);

        Assert.Equal(a.ShardOf("tenant/document#7"), b.ShardOf("tenant/document#7"));
    }

    [Fact]
    public void Fnv1a64_is_the_recorded_hash_and_does_not_move()
    {
        // A fixed vector: if this changes, every stored routing decision changes with it.
        Assert.Equal(14695981039346656037UL, ShardLayout.Fnv1a64(string.Empty));
        Assert.Equal(ShardLayout.Fnv1a64("abc"), ShardLayout.Fnv1a64("abc"));
        Assert.NotEqual(ShardLayout.Fnv1a64("abc"), ShardLayout.Fnv1a64("abd"));
    }

    [Fact]
    public void Pools_are_addressed_by_shard_and_every_shard_is_reachable()
    {
        var layout = new ShardLayout(8, ["a", "b", "c", "d", "e", "f", "g", "h"]);
        var used = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < 5_000; i++)
        {
            used.Add(layout.PoolOf($"doc-{i}"));
        }

        Assert.Equal(8, used.Count);
    }

    [Fact]
    public void A_layout_validates_its_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShardLayout(0));
        Assert.Throws<ArgumentException>(() => new ShardLayout(4, ["only-one"]));
        Assert.Throws<ArgumentException>(() => new ShardLayout(2).ShardOf(string.Empty));
    }

    [Fact]
    public void Every_shard_index_is_enumerable_in_order()
    {
        var layout = new ShardLayout(3);

        Assert.Equal(new[] { 0, 1, 2 }, layout.Shards());
        Assert.Equal(new[] { "shard-0", "shard-1", "shard-2" }, layout.Pools);
    }
}
