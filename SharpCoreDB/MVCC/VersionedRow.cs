// <copyright file="VersionedRow.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.MVCC;

/// <summary>
/// Generic versioned row for MVCC (Multi-Version Concurrency Control).
/// Provides type-safe row versioning with minimal overhead.
/// Target: &lt; 10ns version check, &lt; 100ns version creation.
/// </summary>
/// <typeparam name="TData">The type of the row data (must be a reference type).</typeparam>
/// <param name="Data">The row data.</param>
/// <param name="Version">The transaction version that created this row.</param>
/// <param name="DeletedVersion">The transaction version that deleted this row (null if not deleted).</param>
public sealed record VersionedRow<TData>(
    TData Data,
    long Version,
    long? DeletedVersion = null) where TData : class
{
    /// <summary>
    /// Gets whether this row version is visible to the given transaction.
    /// MVCC visibility rules:
    /// - Row was created before or at the transaction's snapshot
    /// - Row was not deleted, or was deleted after the transaction's snapshot
    /// </summary>
    /// <param name="transactionVersion">The transaction's snapshot version.</param>
    /// <returns>True if visible, false otherwise.</returns>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public bool IsVisibleTo(long transactionVersion)
    {
        // Created after transaction started? Not visible
        if (Version > transactionVersion)
            return false;

        // Deleted before or at transaction snapshot? Not visible
        if (DeletedVersion.HasValue && DeletedVersion.Value <= transactionVersion)
            return false;

        return true;
    }

    /// <summary>
    /// Creates a new version by marking this row as deleted.
    /// </summary>
    /// <param name="deletionVersion">The version that deleted this row.</param>
    /// <returns>A new versioned row with deletion marker.</returns>
    public VersionedRow<TData> MarkDeleted(long deletionVersion) =>
        this with { DeletedVersion = deletionVersion };

    /// <summary>
    /// Creates a new version with updated data.
    /// In MVCC, updates create new versions rather than modifying in place.
    /// </summary>
    /// <param name="newData">The updated data.</param>
    /// <param name="newVersion">The version creating the update.</param>
    /// <returns>A new versioned row with updated data.</returns>
    public VersionedRow<TData> Update(TData newData, long newVersion) =>
        new(newData, newVersion, DeletedVersion: null);
}

/// <summary>
/// Version chain for a single row key in MVCC.
/// Maintains multiple versions of the same logical row for concurrent access.
/// Uses linked list for O(1) append, O(n) traversal (but n is typically small).
/// </summary>
/// <typeparam name="TData">The type of the row data (must be a reference type).</typeparam>
public sealed class VersionChain<TData> where TData : class
{
    private readonly List<VersionedRow<TData>> _versions = [];
    private readonly object _lock = new();

    /// <summary>
    /// Gets the number of versions in this chain.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _versions.Count;
        }
    }

    /// <summary>
    /// Adds a new version to the chain.
    /// Thread-safe operation for concurrent inserts.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void AddVersion(VersionedRow<TData> version)
    {
        lock (_lock)
        {
            _versions.Add(version);
        }
    }

    /// <summary>
    /// Gets the visible version for a given transaction.
    /// Returns the latest version that is visible to the transaction.
    /// Performance: O(n) where n = number of versions (typically 1-3).
    /// </summary>
    /// <param name="transactionVersion">The transaction's snapshot version.</param>
    /// <returns>The visible version, or null if no visible version exists.</returns>
    public VersionedRow<TData>? GetVisibleVersion(long transactionVersion)
    {
        lock (_lock)
        {
            // Iterate backwards (latest versions first) for better performance
            for (int i = _versions.Count - 1; i >= 0; i--)
            {
                var version = _versions[i];
                if (version.IsVisibleTo(transactionVersion))
                    return version;
            }
            return null;
        }
    }

    /// <summary>
    /// Gets all versions (for diagnostics/testing).
    /// </summary>
    public IReadOnlyList<VersionedRow<TData>> GetAllVersions()
    {
        lock (_lock)
        {
            return _versions.ToList();
        }
    }

    /// <summary>
    /// Removes old versions that are no longer visible to any active transaction.
    /// This is called during vacuum/garbage collection.
    /// </summary>
    /// <param name="oldestActiveVersion">The oldest active transaction version.</param>
    /// <returns>Number of versions removed.</returns>
    public int RemoveOldVersions(long oldestActiveVersion)
    {
        lock (_lock)
        {
            var removed = _versions.RemoveAll(v =>
                v.DeletedVersion.HasValue &&
                v.DeletedVersion.Value < oldestActiveVersion);
            
            return removed;
        }
    }
}

/// <summary>
/// MVCC transaction context.
/// Provides snapshot isolation for concurrent transactions.
/// </summary>
public sealed class MvccTransaction : IDisposable
{
    /// <summary>
    /// Gets the unique transaction ID.
    /// </summary>
    public long TransactionId { get; }

    /// <summary>
    /// Gets the snapshot version (used for MVCC visibility).
    /// </summary>
    public long SnapshotVersion { get; }

    /// <summary>
    /// Gets the commit version (assigned when transaction commits).
    /// </summary>
    public long? CommitVersion { get; private set; }

    /// <summary>
    /// Gets whether this transaction is read-only.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// Gets the transaction state.
    /// </summary>
    public TransactionState State { get; private set; }

    private readonly Action<MvccTransaction>? _onDispose;

    /// <summary>
    /// Initializes a new instance of the <see cref="MvccTransaction"/> class.
    /// </summary>
    public MvccTransaction(
        long transactionId,
        long snapshotVersion,
        bool isReadOnly = false,
        Action<MvccTransaction>? onDispose = null)
    {
        TransactionId = transactionId;
        SnapshotVersion = snapshotVersion;
        IsReadOnly = isReadOnly;
        State = TransactionState.Active;
        _onDispose = onDispose;
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    public void Commit(long commitVersion)
    {
        if (State != TransactionState.Active)
            throw new InvalidOperationException($"Cannot commit transaction in state {State}");

        CommitVersion = commitVersion;
        State = TransactionState.Committed;
    }

    /// <summary>
    /// Aborts the transaction.
    /// </summary>
    public void Abort()
    {
        if (State != TransactionState.Active)
            throw new InvalidOperationException($"Cannot abort transaction in state {State}");

        State = TransactionState.Aborted;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (State == TransactionState.Active)
            Abort();

        _onDispose?.Invoke(this);
    }
}

/// <summary>
/// Transaction state enumeration.
/// </summary>
public enum TransactionState
{
    /// <summary>Transaction is active and can perform operations.</summary>
    Active,

    /// <summary>Transaction has committed successfully.</summary>
    Committed,

    /// <summary>Transaction has been aborted/rolled back.</summary>
    Aborted
}
