// <copyright file="MvccManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.MVCC;

using System.Collections.Concurrent;
using SharpCoreDB.DataStructures;

/// <summary>
/// MVCC (Multi-Version Concurrency Control) manager with generic type-safe rows.
/// Provides snapshot isolation for concurrent transactions without locking readers.
/// Target: 1000 parallel SELECTs in &lt; 10ms on 16 threads.
/// </summary>
/// <typeparam name="TKey">The type of the row key.</typeparam>
/// <typeparam name="TData">The type of the row data (must be a reference type).</typeparam>
public sealed class MvccManager<TKey, TData> : IDisposable
    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    where TData : class
{
    private readonly ConcurrentDictionary<TKey, VersionChain<TData>> _versionChains = new();
    private readonly GenericHashIndex<TKey> _primaryIndex;
    private readonly ConcurrentDictionary<long, MvccTransaction> _activeTransactions = new();
    private long _currentVersion;
    private long _nextTransactionId;
    private readonly object _versionLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MvccManager{TKey, TData}"/> class.
    /// </summary>
    /// <param name="indexName">The name of the primary index.</param>
    public MvccManager(string indexName = "primary")
    {
        _primaryIndex = new GenericHashIndex<TKey>(indexName);
        _currentVersion = 0;
        _nextTransactionId = 1;
    }

    #region Transaction Management

    /// <summary>
    /// Begins a new transaction with snapshot isolation.
    /// Read-only transactions have zero overhead on writes.
    /// </summary>
    /// <param name="isReadOnly">Whether this is a read-only transaction.</param>
    /// <returns>A new transaction context.</returns>
    public MvccTransaction BeginTransaction(bool isReadOnly = false)
    {
        var transactionId = Interlocked.Increment(ref _nextTransactionId);
        var snapshotVersion = Interlocked.Read(ref _currentVersion);

        var transaction = new MvccTransaction(
            transactionId,
            snapshotVersion,
            isReadOnly,
            onDispose: tx => _activeTransactions.TryRemove(tx.TransactionId, out _));

        _activeTransactions[transactionId] = transaction;
        return transaction;
    }

    /// <summary>
    /// Commits a transaction and assigns it a commit version.
    /// </summary>
    public void CommitTransaction(MvccTransaction transaction)
    {
        if (transaction.IsReadOnly)
        {
            transaction.Commit(transaction.SnapshotVersion);
            return;
        }

        lock (_versionLock)
        {
            var commitVersion = ++_currentVersion;
            transaction.Commit(commitVersion);
        }
    }

    #endregion

    #region Read Operations (Lock-Free!)

    /// <summary>
    /// Gets a row by key for the given transaction.
    /// Lock-free read operation - concurrent readers don't block each other!
    /// Target: &lt; 1µs for hot data.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public TData? Get(TKey key, MvccTransaction transaction)
    {
        if (!_versionChains.TryGetValue(key, out var chain))
            return null;

        var version = chain.GetVisibleVersion(transaction.SnapshotVersion);
        return version?.Data;
    }

    /// <summary>
    /// Gets multiple rows by keys (batch operation).
    /// Optimized for parallel execution.
    /// </summary>
    public IEnumerable<TData> GetMany(IEnumerable<TKey> keys, MvccTransaction transaction)
    {
        foreach (var key in keys)
        {
            var data = Get(key, transaction);
            if (data != null)
                yield return data;
        }
    }

    /// <summary>
    /// Scans all visible rows for the transaction.
    /// Uses MVCC visibility rules - no locking needed!
    /// </summary>
    public IEnumerable<TData> Scan(MvccTransaction transaction)
    {
        foreach (var chain in _versionChains.Values)
        {
            var version = chain.GetVisibleVersion(transaction.SnapshotVersion);
            if (version != null)
                yield return version.Data;
        }
    }

    #endregion

    #region Write Operations

    /// <summary>
    /// Inserts a new row with MVCC versioning.
    /// Creates a new version chain for the key.
    /// </summary>
    public void Insert(TKey key, TData data, MvccTransaction transaction)
    {
        if (transaction.IsReadOnly)
            throw new InvalidOperationException("Cannot insert in read-only transaction");

        lock (_versionLock)
        {
            var versionNumber = _currentVersion + 1;
            var version = new VersionedRow<TData>(data, versionNumber);
            var chain = _versionChains.GetOrAdd(key, _ => new VersionChain<TData>());
            
            chain.AddVersion(version);
            
            // Update index (using our fast generic index!)
            _primaryIndex.Add(key, versionNumber);
        }
    }

    /// <summary>
    /// Updates a row with MVCC versioning.
    /// Creates a new version instead of modifying in-place.
    /// Old versions remain visible to older transactions (snapshot isolation).
    /// </summary>
    public bool Update(TKey key, TData newData, MvccTransaction transaction)
    {
        if (transaction.IsReadOnly)
            throw new InvalidOperationException("Cannot update in read-only transaction");

        if (!_versionChains.TryGetValue(key, out var chain))
            return false;

        // Get the current visible version
        var currentVersion = chain.GetVisibleVersion(transaction.SnapshotVersion);
        if (currentVersion == null)
            return false;

        // Get the commit version for this transaction (assigned when it commits)
        // For now, use current version + 1 (will be updated on commit)
        lock (_versionLock)
        {
            var newVersionNumber = _currentVersion + 1;
            var newVersion = new VersionedRow<TData>(newData, newVersionNumber);
            chain.AddVersion(newVersion);
        }

        return true;
    }

    /// <summary>
    /// Deletes a row by marking it as deleted in MVCC.
    /// Old transactions can still see the row (snapshot isolation).
    /// </summary>
    public bool Delete(TKey key, MvccTransaction transaction)
    {
        if (transaction.IsReadOnly)
            throw new InvalidOperationException("Cannot delete in read-only transaction");

        if (!_versionChains.TryGetValue(key, out var chain))
            return false;

        var version = chain.GetVisibleVersion(transaction.SnapshotVersion);
        if (version == null)
            return false;

        // Mark as deleted
        chain.AddVersion(version.MarkDeleted(transaction.SnapshotVersion));
        return true;
    }

    #endregion

    #region Vacuum & Garbage Collection

    /// <summary>
    /// Removes old versions that are no longer visible to any active transaction.
    /// Should be called periodically to prevent unbounded growth.
    /// </summary>
    public int Vacuum()
    {
        // Find oldest active transaction
        var oldestActive = _activeTransactions.Values
            .Select(tx => tx.SnapshotVersion)
            .DefaultIfEmpty(Interlocked.Read(ref _currentVersion))
            .Min();

        int totalRemoved = 0;

        foreach (var chain in _versionChains.Values)
        {
            totalRemoved += chain.RemoveOldVersions(oldestActive);
        }

        return totalRemoved;
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets MVCC statistics for monitoring.
    /// </summary>
    public MvccStatistics GetStatistics()
    {
        var versionCounts = _versionChains.Values
            .Select(chain => chain.Count)
            .ToList();

        return new MvccStatistics
        {
            TotalKeys = _versionChains.Count,
            TotalVersions = versionCounts.Sum(),
            AverageVersionsPerKey = versionCounts.Any() 
                ? versionCounts.Average() 
                : 0,
            MaxVersionsPerKey = versionCounts.Any() 
                ? versionCounts.Max() 
                : 0,
            ActiveTransactions = _activeTransactions.Count,
            CurrentVersion = Interlocked.Read(ref _currentVersion),
            IndexStatistics = _primaryIndex.GetStatistics()
        };
    }

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        _versionChains.Clear();
        _activeTransactions.Clear();
    }
}

/// <summary>
/// MVCC statistics for monitoring and optimization.
/// </summary>
public sealed record MvccStatistics
{
    /// <summary>Gets the total number of unique keys.</summary>
    public required int TotalKeys { get; init; }

    /// <summary>Gets the total number of versions across all keys.</summary>
    public required int TotalVersions { get; init; }

    /// <summary>Gets the average number of versions per key.</summary>
    public required double AverageVersionsPerKey { get; init; }

    /// <summary>Gets the maximum number of versions for any single key.</summary>
    public required int MaxVersionsPerKey { get; init; }

    /// <summary>Gets the number of active transactions.</summary>
    public required int ActiveTransactions { get; init; }

    /// <summary>Gets the current global version number.</summary>
    public required long CurrentVersion { get; init; }

    /// <summary>Gets the primary index statistics.</summary>
    public required Interfaces.IndexStatistics IndexStatistics { get; init; }
}
