// <copyright file="Table.TransactionUndo.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Storage.Engines;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Table partial — rolling a PageBased table back with its database transaction (known issue §13).
/// <para>
/// The PageBased engine writes into its page cache and page files directly, outside the storage transaction buffer that
/// columnar tables write through, and up to 2.1.4 nothing undid those writes: <c>CancelBatchUpdate()</c> — an ADO.NET
/// <c>DbTransaction.Rollback()</c> — left a PageBased UPDATE or INSERT in place, also after a reopen. The engine now keeps
/// each changed page's image for the length of the transaction and puts it back on rollback; the table then rebuilds its
/// primary-key index from the restored pages and lets its hash indexes rebuild on next use.
/// </para>
/// </summary>
public partial class Table
{
    /// <summary>Starts the transaction's undo scope (PageBased tables; a no-op for columnar ones).</summary>
    internal void BeginTransactionUndo()
    {
        if (StorageMode == StorageMode.PageBased && GetOrCreateStorageEngine() is PageBasedEngine engine)
        {
            engine.BeginUndoScope();
        }
    }

    /// <summary>Ends the undo scope keeping every change (the transaction committed).</summary>
    internal void CommitTransactionUndo()
    {
        if (_storageEngine is PageBasedEngine engine)
        {
            engine.CommitUndoScope();
        }
    }

    /// <summary>
    /// After a rollback: puts every page the transaction changed back (PageBased), then rebuilds the primary-key index
    /// from storage and lets the hash indexes rebuild on next use. A columnar table's rows are rolled back by the storage
    /// transaction buffer, but its in-memory indexes were not: an UPDATE of an indexed column, rolled back, left the hash
    /// index holding the rolled-back value, so <c>WHERE name = 'old'</c> found nothing. Both modes rebuild here, as every
    /// commit already rebuilds the primary-key index.
    /// </summary>
    internal void RollbackTransactionUndo()
    {
        if (_storageEngine is PageBasedEngine engine)
        {
            engine.RollbackUndoScope();
        }
        else if (_storageEngine is null)
        {
            return; // never touched: nothing in memory to rebuild
        }

        this.rwLock.EnterWriteLock();
        try
        {
            RebuildPrimaryKeyIndexFromDisk();
            foreach (var column in this.registeredIndexes.Keys)
            {
                this.staleIndexes.Add(column);
                this._indexReadyCache.TryRemove(column, out _);
            }
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }
}
