// <copyright file="ParallelQueryExecutor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Query;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Parallel query executor for multi-threaded query processing.
/// C# 14: Async/await, Parallel LINQ, partitioning.
/// 
/// ✅ SCDB Phase 7.4: Advanced Query Optimization - Parallel Execution
/// 
/// Purpose:
/// - Multi-threaded table scans
/// - Partitioned aggregations
/// - Parallel joins
/// - Work-stealing scheduler
/// </summary>
public sealed class ParallelQueryExecutor : IDisposable
{
    private readonly int _degreeOfParallelism;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParallelQueryExecutor"/> class.
    /// </summary>
    /// <param name="degreeOfParallelism">Number of parallel threads (0 = auto).</param>
    public ParallelQueryExecutor(int degreeOfParallelism = 0)
    {
        _degreeOfParallelism = degreeOfParallelism > 0
            ? degreeOfParallelism
            : Environment.ProcessorCount;
    }

    /// <summary>Gets the degree of parallelism.</summary>
    public int DegreeOfParallelism => _degreeOfParallelism;

    /// <summary>
    /// Executes a parallel scan with filtering.
    /// </summary>
    public async Task<List<T>> ParallelScanAsync<T>(
        IEnumerable<T> data,
        Func<T, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(predicate);

        var dataList = data.ToList();
        var partitionSize = Math.Max(1, dataList.Count / _degreeOfParallelism);
        var partitions = Partition(dataList, partitionSize);

        var results = new ConcurrentBag<T>();

        await Parallel.ForEachAsync(
            partitions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _degreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (partition, ct) =>
            {
                await Task.Run(() =>
                {
                    foreach (var item in partition)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (predicate(item))
                        {
                            results.Add(item);
                        }
                    }
                }, ct);
            });

        return [.. results];
    }

    /// <summary>
    /// Executes a parallel aggregation.
    /// </summary>
    public async Task<TResult> ParallelAggregateAsync<T, TResult>(
        IEnumerable<T> data,
        TResult seed,
        Func<TResult, T, TResult> accumulator,
        Func<TResult, TResult, TResult> combiner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(accumulator);
        ArgumentNullException.ThrowIfNull(combiner);

        var dataList = data.ToList();
        var partitionSize = Math.Max(1, dataList.Count / _degreeOfParallelism);
        var partitions = Partition(dataList, partitionSize);

        var partialResults = new ConcurrentBag<TResult>();

        await Parallel.ForEachAsync(
            partitions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _degreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (partition, ct) =>
            {
                await Task.Run(() =>
                {
                    var partialResult = seed;

                    foreach (var item in partition)
                    {
                        ct.ThrowIfCancellationRequested();
                        partialResult = accumulator(partialResult, item);
                    }

                    partialResults.Add(partialResult);
                }, ct);
            });

        // Combine partial results
        return partialResults.Aggregate(seed, combiner);
    }

    /// <summary>
    /// Executes a parallel group-by aggregation.
    /// </summary>
    public async Task<Dictionary<TKey, TValue>> ParallelGroupByAsync<T, TKey, TValue>(
        IEnumerable<T> data,
        Func<T, TKey> keySelector,
        Func<IEnumerable<T>, TValue> aggregator,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(aggregator);

        var dataList = data.ToList();
        var partitionSize = Math.Max(1, dataList.Count / _degreeOfParallelism);
        var partitions = Partition(dataList, partitionSize);

        var partialResults = new ConcurrentBag<Dictionary<TKey, List<T>>>();

        await Parallel.ForEachAsync(
            partitions,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _degreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (partition, ct) =>
            {
                await Task.Run(() =>
                {
                    var groups = new Dictionary<TKey, List<T>>();

                    foreach (var item in partition)
                    {
                        ct.ThrowIfCancellationRequested();

                        var key = keySelector(item);
                        if (!groups.TryGetValue(key, out var list))
                        {
                            list = [];
                            groups[key] = list;
                        }

                        list.Add(item);
                    }

                    partialResults.Add(groups);
                }, ct);
            });

        // Merge partial group results
        var merged = new Dictionary<TKey, List<T>>();
        foreach (var partialResult in partialResults)
        {
            foreach (var (key, items) in partialResult)
            {
                if (!merged.TryGetValue(key, out var list))
                {
                    list = [];
                    merged[key] = list;
                }

                list.AddRange(items);
            }
        }

        // Apply aggregation to each group
        return merged.ToDictionary(
            kvp => kvp.Key,
            kvp => aggregator(kvp.Value));
    }

    /// <summary>
    /// Executes a parallel sort.
    /// </summary>
    public async Task<List<T>> ParallelSortAsync<T>(
        IEnumerable<T> data,
        IComparer<T>? comparer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var dataList = data.ToList();

        if (dataList.Count <= 1000)
        {
            // Small dataset: use regular sort
            dataList.Sort(comparer);
            return dataList;
        }

        // Parallel merge sort
        return await Task.Run(() =>
        {
            var sorted = dataList.AsParallel()
                .WithDegreeOfParallelism(_degreeOfParallelism)
                .WithCancellation(cancellationToken)
                .OrderBy(x => x, comparer)
                .ToList();

            return sorted;
        }, cancellationToken);
    }

    /// <summary>
    /// Gets execution statistics.
    /// </summary>
    public ParallelExecutionStats GetStats()
    {
        return new ParallelExecutionStats
        {
            DegreeOfParallelism = _degreeOfParallelism,
            ProcessorCount = Environment.ProcessorCount,
            RecommendedParallelism = Environment.ProcessorCount
        };
    }

    // Private helpers

    private static IEnumerable<List<T>> Partition<T>(List<T> data, int partitionSize)
    {
        for (int i = 0; i < data.Count; i += partitionSize)
        {
            var size = Math.Min(partitionSize, data.Count - i);
            var partition = new List<T>(size);

            for (int j = 0; j < size; j++)
            {
                partition.Add(data[i + j]);
            }

            yield return partition;
        }
    }

    /// <summary>
    /// Disposes the executor.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// Parallel execution statistics.
/// </summary>
public sealed record ParallelExecutionStats
{
    /// <summary>Configured degree of parallelism.</summary>
    public required int DegreeOfParallelism { get; init; }

    /// <summary>System processor count.</summary>
    public required int ProcessorCount { get; init; }

    /// <summary>Recommended parallelism.</summary>
    public required int RecommendedParallelism { get; init; }
}
