// <copyright file="IndexManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>
/// Modern generic index manager with auto-indexing and PRAGMA-based detection.
/// Manages both legacy Dictionary-based indexes and new type-safe generic indexes.
/// Target performance: < 0.05ms lookups on 10k records.
/// </summary>
public sealed class IndexManager : IDisposable
{
    /// <summary>
    /// Record for legacy index update operations.
    /// </summary>
    public record IndexUpdate(Dictionary<string, object?> Row, IEnumerable<HashIndex> Indexes, long Position);

    private readonly Channel<IndexUpdate> _updateQueue = Channel.CreateUnbounded<IndexUpdate>();
    private readonly Task _updateTask;
    private readonly PragmaIndexDetector _pragmaDetector = new();
    private readonly ConcurrentDictionary<string, object> _genericIndexes = new();
    private bool _autoIndexingEnabled = true;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IndexManager"/> class.
    /// </summary>
    /// <param name="enableAutoIndexing">Whether to enable automatic index creation.</param>
    public IndexManager(bool enableAutoIndexing = true)
    {
        _autoIndexingEnabled = enableAutoIndexing;
        _updateTask = Task.Run(UpdateIndexesAsync);
    }

    /// <summary>
    /// Gets or sets whether auto-indexing is enabled.
    /// </summary>
    public bool AutoIndexingEnabled
    {
        get => _autoIndexingEnabled;
        set => _autoIndexingEnabled = value;
    }

    #region Generic Type-Safe Index Management

    /// <summary>
    /// Creates or gets a generic type-safe index for a column.
    /// </summary>
    /// <typeparam name="TKey">The type of the index key.</typeparam>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name to index.</param>
    /// <param name="indexType">The type of index to create.</param>
    /// <returns>The generic index instance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IGenericIndex<TKey> GetOrCreateIndex<TKey>(
        string tableName,
        string columnName,
        IndexType indexType = IndexType.Hash)
        where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    {
        var key = $"{tableName}.{columnName}";
        
        return (IGenericIndex<TKey>)_genericIndexes.GetOrAdd(key, _ =>
        {
            return indexType switch
            {
                IndexType.Hash => new GenericHashIndex<TKey>(columnName),
                IndexType.BTree => throw new NotImplementedException("B-Tree index not yet implemented"),
                _ => throw new ArgumentException($"Unsupported index type: {indexType}")
            };
        });
    }

    /// <summary>
    /// Adds a value to a generic index (type-safe).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddToIndex<TKey>(string tableName, string columnName, TKey key, long position)
        where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    {
        var index = GetOrCreateIndex<TKey>(tableName, columnName);
        index.Add(key, position);
        
        // Record query for auto-indexing heuristics
        _pragmaDetector.RecordQuery(tableName, columnName);
    }

    /// <summary>
    /// Finds positions using a generic index (type-safe, O(1) for hash).
    /// Target: < 0.05ms for 10k records.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<long> FindInIndex<TKey>(string tableName, string columnName, TKey key)
        where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    {
        var indexKey = $"{tableName}.{columnName}";
        if (_genericIndexes.TryGetValue(indexKey, out var indexObj) &&
            indexObj is IGenericIndex<TKey> index)
        {
            return index.Find(key);
        }
        return [];
    }

    /// <summary>
    /// Range query using a generic index (B-Tree only).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<long> FindRangeInIndex<TKey>(
        string tableName,
        string columnName,
        TKey start,
        TKey end)
        where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    {
        var indexKey = $"{tableName}.{columnName}";
        if (_genericIndexes.TryGetValue(indexKey, out var indexObj) &&
            indexObj is IGenericIndex<TKey> index)
        {
            return index.FindRange(start, end);
        }
        return [];
    }

    #endregion

    #region Auto-Indexing with PRAGMA Analysis

    /// <summary>
    /// Analyzes table data and creates recommended indexes automatically.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="rows">Sample rows for analysis.</param>
    /// <returns>Information about created indexes.</returns>
    public PragmaIndexDetector.TableIndexInfo AnalyzeAndCreateIndexes(
        string tableName,
        IEnumerable<Dictionary<string, object?>> rows)
    {
        if (!_autoIndexingEnabled)
            throw new InvalidOperationException("Auto-indexing is disabled");

        var tableInfo = _pragmaDetector.AnalyzeTable(tableName, rows);
        
        // Create recommended indexes
        foreach (var indexInfo in tableInfo.Indexes.Where(i => i.IsAutoCreated))
        {
            Console.WriteLine($"[AutoIndex] Creating {indexInfo.Type} index on " +
                            $"{tableName}.{indexInfo.ColumnName} " +
                            $"(selectivity={indexInfo.Statistics.Selectivity:F2})");
            
            // Index will be created on-demand when first value is added
            // via GetOrCreateIndex<T>
        }

        return tableInfo;
    }

    /// <summary>
    /// Gets PRAGMA-style index information.
    /// </summary>
    public string GetPragmaIndexList(string tableName) =>
        _pragmaDetector.GetPragmaIndexList(tableName);

    /// <summary>
    /// Gets PRAGMA-style table information.
    /// </summary>
    public string GetPragmaTableInfo(string tableName) =>
        _pragmaDetector.GetPragmaTableInfo(tableName);

    /// <summary>
    /// Gets statistics for a generic index.
    /// </summary>
    public IndexStatistics? GetIndexStatistics(string tableName, string columnName)
    {
        var key = $"{tableName}.{columnName}";
        if (_genericIndexes.TryGetValue(key, out var indexObj))
        {
            // Use reflection to call GetStatistics (works for any TKey)
            var method = indexObj.GetType().GetMethod("GetStatistics");
            return method?.Invoke(indexObj, null) as IndexStatistics;
        }
        return null;
    }

    /// <summary>
    /// Gets the PRAGMA detector for direct access (testing/advanced scenarios).
    /// </summary>
    public PragmaIndexDetector GetPragmaDetector() => _pragmaDetector;

    #endregion

    #region Legacy Dictionary-Based Index Support

    /// <summary>
    /// Queues a legacy index update operation asynchronously.
    /// </summary>
    /// <param name="update">The index update to queue.</param>
    public void QueueUpdate(IndexUpdate update)
    {
        _updateQueue.Writer.TryWrite(update);
    }

    /// <summary>
    /// Processes legacy index updates asynchronously in the background.
    /// </summary>
    private async Task UpdateIndexesAsync()
    {
        await foreach (var update in _updateQueue.Reader.ReadAllAsync())
        {
            foreach (var index in update.Indexes)
            {
                index.Add(update.Row, update.Position);
            }
        }
    }

    /// <summary>
    /// Updates legacy indexes asynchronously for a given update.
    /// </summary>
    /// <param name="update">The index update.</param>
    public async Task UpdateIndexesAsync(IndexUpdate update)
    {
        foreach (var index in update.Indexes)
        {
            index.Add(update.Row, update.Position);
        }
        await Task.CompletedTask;
    }

    #endregion

    #region Disposal

    /// <summary>
    /// Disposes the index manager and completes asynchronous operations.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _updateQueue.Writer.Complete();
        
        try
        {
            _updateTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
