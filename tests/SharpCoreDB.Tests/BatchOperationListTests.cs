// <copyright file="BatchOperationListTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using Xunit;

/// <summary>
/// Pins the pooled per-table operation bucket the batch dispatcher fills while classifying
/// (session 79, plan §5.5 (ii) residual <c>parse</c> stage). It replaced a <c>List&lt;T&gt;</c> whose
/// growth allocated ~2× the final capacity — 131 B per statement, 23 % of the stage — so the two
/// properties a pool-backed list can get wrong are the ones tested here: every element has to survive a
/// grow in order, and a released bucket must come back empty (the array returns to a shared pool).
/// </summary>
public sealed class BatchOperationListTests
{
    [Fact]
    public void Add_PastTheFirstRental_KeepsEveryElementInOrder()
    {
        var list = new BatchOperationList<int>();

        for (int i = 0; i < 1_000; i++)
        {
            list.Add(i);
        }

        Assert.Equal(1_000, list.Count);
        for (int i = 0; i < 1_000; i++)
        {
            Assert.Equal(i, list[i]);
        }

        int seen = 0;
        foreach (int value in list)
        {
            Assert.Equal(seen++, value);
        }

        Assert.Equal(1_000, seen);
        list.Release();
    }

    [Fact]
    public void Add_PastTheInitialCapacityHint_GrowsAndKeepsEveryElement()
    {
        // The classifier passes the batch's statement count as a hint; a table that nevertheless receives
        // more operations than that (a shape its caller could not know) must still grow correctly.
        var list = new BatchOperationList<int>(initialCapacity: 4);

        for (int i = 0; i < 100; i++)
        {
            list.Add(i);
        }

        Assert.Equal(100, list.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i, list[i]);
        }

        list.Release();
    }

    [Fact]
    public void Release_ThenReuse_StartsWithAnEmptyBucket()
    {
        var list = new BatchOperationList<string>();
        list.Add("a");
        list.Add("b");

        list.Release();

        Assert.Equal(0, list.Count);
        list.Add("c");
        Assert.Equal(1, list.Count);
        Assert.Equal("c", list[0]);

        // Release is idempotent — the executor calls it in a finally, and a batch whose table lookup
        // failed still reaches it.
        list.Release();
        list.Release();
        Assert.Equal(0, list.Count);
    }
}
