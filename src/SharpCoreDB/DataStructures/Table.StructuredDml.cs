// <copyright file="Table.StructuredDml.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// SQL-free batch DELETE — the DELETE sibling of the INSERT fast path
/// (<c>InsertBatch(object[][], columnOrder)</c>) and of <c>UpdateMultipleStructured</c>.
/// The caller supplies the key column and its <b>already-typed</b> value, so unlike the SQL batch
/// dispatcher's structured path (<see cref="DeleteMultipleKeys"/>) nothing is scanned out of a
/// statement, no literal is unquoted and no text is converted back to the column's type.
/// <para>
/// Semantics mirror <see cref="DeleteMultipleKeys"/> exactly: PK fast path → registered-hash-index
/// fast path (including the W1 key-only shortcut) → generic fallback, then the same shared
/// <c>DeleteRecordsCore</c> that performs the physical delete, the index cleanup and the
/// tombstone/compaction bookkeeping. The key column is matched case-sensitively against the
/// registered indexes, which is what the SQL path's canonical scanner does as well.
/// </para>
/// </summary>
public partial class Table
{
    /// <summary>
    /// Deletes every row matched by the supplied typed keys in one batch under a single write lock.
    /// </summary>
    /// <param name="keys">
    /// One (key column, typed key value) pair per delete target, in statement order. The column must
    /// be the primary key or carry a registered index for the batch to stay text-free; any other
    /// column still deletes correctly through the generic fallback, whose predicate text is built
    /// once for that key rather than once per statement.
    /// </param>
    /// <returns>The number of rows deleted.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal int DeleteBatchStructured(IReadOnlyList<(string KeyColumn, object? KeyValue)> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (this.isReadOnly) throw new InvalidOperationException(ReadOnlyDeleteError);
        if (keys.Count == 0) return 0;

        // Reject unusable keys before the write lock: a missing column or a null key cannot locate a
        // row, and silently skipping it would turn a caller's bug into a silent no-op delete.
        foreach (var (keyColumn, keyValue) in keys)
        {
            if (string.IsNullOrWhiteSpace(keyColumn))
            {
                throw new ArgumentException("A key column is required for every batch DELETE target.", nameof(keys));
            }

            if (keyValue is null)
            {
                throw new ArgumentException("A non-null key value is required for every batch DELETE target.", nameof(keys));
            }
        }

        this.rwLock.EnterWriteLock();
        try
        {
            var engine = GetOrCreateStorageEngine();
            EnsureAllRegisteredIndexesLoaded();

            // B9: the same single-pass contiguous DELETE the SQL path uses for a `pk = literal` batch. Measured
            // (session 46): without this attempt the structured entry point lost arm B's fastest DELETE outright
            // — 0,38× the SQL batch path on the default posture, because the SQL path delegates to this resolver
            // and the typed loop below does a PK search plus a row decode per key. Its gate is all-or-nothing and
            // consumes `col = literal` text, so the keys are formatted once for this ONE gated call, and only
            // when every key targets the PK column — which is its own precondition anyway.
            if (this.PrimaryKeyIndex >= 0 && AllKeysTargetPrimaryKey(keys))
            {
                var wheres = new List<string>(keys.Count);
                foreach (var (column, value) in keys)
                {
                    wheres.Add(BuildStructuredWhereText(column, value));
                }

                if (TryBulkDeleteContiguousFixedWidth(wheres))
                {
                    return keys.Count;
                }
            }

            // B1: decode only the columns the delete core touches (PK + loaded hash-index columns).
            int[] deleteKeyColumns = BuildDeleteKeyColumns();

            // A1: resolve every target from one in-memory snapshot when the file is small enough.
            byte[]? wholeFile = TryLoadWholeFileForRowAccess();

            var recordsToDelete = new List<(long storagePosition, Dictionary<string, object> row)>();

            foreach (var (keyColumn, keyValue) in keys)
            {
                // Issue #7 PK fast path, structured: the key is already typed, so the index is searched
                // with its text form and nothing is parsed back out of a literal.
                if (StorageMode != StorageMode.PageBased &&
                    this.PrimaryKeyIndex >= 0 &&
                    string.Equals(keyColumn, this.Columns[this.PrimaryKeyIndex], StringComparison.OrdinalIgnoreCase))
                {
                    var pkSearch = this.Index.Search(keyValue.ToString() ?? string.Empty);
                    if (pkSearch.Found)
                    {
                        Dictionary<string, object>? pkRow = null;
                        if (wholeFile != null)
                        {
                            pkRow = DeserializeDeleteKeyRowFromFile(wholeFile, pkSearch.Value, deleteKeyColumns);
                        }
                        else
                        {
                            var pkData = engine.Read(Name, pkSearch.Value);
                            if (pkData != null)
                            {
                                pkRow = DeserializeDeleteKeyRow(pkData, deleteKeyColumns) ?? DeserializeRowFromSpan(pkData);
                            }
                        }

                        if (pkRow != null)
                        {
                            recordsToDelete.Add((pkSearch.Value, pkRow));
                        }

                        continue;
                    }
                }

                // Registered-hash-index fast path — the fair shape's route (no PK, predicate on an
                // indexed column).
                if (this.registeredIndexes.ContainsKey(keyColumn))
                {
                    EnsureIndexLoaded(keyColumn);
                    if (this.hashIndexes.TryGetValue(keyColumn, out var hashIndex))
                    {
                        var keyColIdx = this.Columns.IndexOf(keyColumn);
                        if (keyColIdx >= 0)
                        {
                            var key = CoerceStructuredKeyForHashLookup(keyValue, this.ColumnTypes[keyColIdx]);

                            // W1 (unchanged from the text path): when this is the ONLY index the delete
                            // core must maintain (no PK tree, a single loaded hash index whose key is the
                            // condition value itself, no secondary B-tree manager), the position can be
                            // removed with the already-known key — no per-row engine.Read or row decode.
                            bool keyOnlyRow =
                                this.PrimaryKeyIndex < 0 &&
                                this.hashIndexes.Count == 1 &&
                                _btreeManager is null &&
                                this.registeredIndexes.Count == 1;

                            if (keyOnlyRow)
                            {
                                foreach (var pos in hashIndex.LookupPositionsUnsafe(key))
                                {
                                    recordsToDelete.Add((pos, new Dictionary<string, object>(1)
                                    {
                                        [keyColumn] = key
                                    }));
                                }

                                continue;
                            }

                            foreach (var pos in hashIndex.LookupPositionsUnsafe(key))
                            {
                                Dictionary<string, object>? row = null;
                                if (wholeFile != null)
                                {
                                    row = DeserializeDeleteKeyRowFromFile(wholeFile, pos, deleteKeyColumns);
                                }
                                else
                                {
                                    var data = engine.Read(Name, pos);
                                    if (data != null)
                                    {
                                        row = DeserializeDeleteKeyRow(data, deleteKeyColumns) ?? DeserializeRowFromSpan(data);
                                    }
                                }

                                if (row != null) recordsToDelete.Add((pos, row));
                            }

                            continue;
                        }
                    }
                }

                // Generic fallback for a key column with no consumer for a typed value (unindexed,
                // non-PK). The predicate text is built for this key only — the same shape
                // DeleteMultipleKeys uses — and the established resolution runs unchanged.
                var where = BuildStructuredWhereText(keyColumn, keyValue);
                if (this.PrimaryKeyIndex >= 0)
                {
                    var pkColumn = this.Columns[this.PrimaryKeyIndex];
                    foreach (var row in SelectInternal(where, orderBy: null, asc: true, noEncrypt: false))
                    {
                        if (row.TryGetValue(pkColumn, out var pkValue) && pkValue != null)
                        {
                            var searchResult = this.Index.Search(pkValue.ToString() ?? string.Empty);
                            if (searchResult.Found)
                            {
                                recordsToDelete.Add((searchResult.Value, row));
                            }
                        }
                    }
                }
                else
                {
                    foreach (var (storageRef, data) in engine.GetAllRecords(Name))
                    {
                        var row = DeserializeRowFromSpan(data);
                        if (row != null && EvaluateSimpleWhere(row, where))
                        {
                            recordsToDelete.Add((storageRef, row));
                        }
                    }
                }
            }

            if (recordsToDelete.Count == 0) return 0;

            DeleteRecordsCore(recordsToDelete);
            return recordsToDelete.Count;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// True when every key targets the table's primary-key column — the precondition the contiguous
    /// fixed-width DELETE resolver has anyway, checked up front so a mixed batch takes the typed loop
    /// instead of formatting text it would then throw away.
    /// </summary>
    private bool AllKeysTargetPrimaryKey(IReadOnlyList<(string KeyColumn, object? KeyValue)> keys)
    {
        var pkColumn = this.Columns[this.PrimaryKeyIndex];
        foreach (var (column, _) in keys)
        {
            if (!string.Equals(column, pkColumn, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Produces the hash-index key for a batch operation's key value, so a typed key resolves the
    /// same index entry the SQL path resolves.
    /// <para>
    /// A text key goes through <c>ParseValueForHashLookup</c> exactly as the SQL path does (the index
    /// was built from the row's typed value, so the text has to reach that form). An already-typed
    /// value is coerced to the column's type instead — <c>int</c>/<c>long</c>/<c>double</c> keys stay
    /// equal to the stored keys when the caller's CLR type merely differs in width.
    /// </para>
    /// </summary>
    private static object CoerceStructuredKeyForHashLookup(object? value, DataType type)
    {
        if (value is string text)
        {
            return ParseValueForHashLookup(text, type);
        }

        return value is null
            ? string.Empty
            : ConvertValueForBTreeKey(value, type) ?? value;
    }

    /// <summary>
    /// Builds the <c>column = literal</c> predicate text for a structured key. Only the fallback
    /// branches need it: every indexed and PK route consumes the typed value directly, and the SQL
    /// callers already hold their own predicate.
    /// </summary>
    internal static string BuildStructuredWhereText(string keyColumn, object? keyValue) =>
        keyColumn + " = " + FormatValue(keyValue);
}

