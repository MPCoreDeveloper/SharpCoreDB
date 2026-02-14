// <copyright file="Table.BTreeIndexing.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Services;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Collections.Generic;

/// <summary>
/// B-tree index management for Table - NEW partial class file to avoid edit conflicts.
/// Provides range query optimization via B-tree indexes.
/// </summary>
public partial class Table
{
#if DEBUG
    // ✅ DEBUG: B-tree debug output only in DEBUG builds
    private const bool BTREE_DEBUG = true;
#endif

    // B-tree index manager - initialized lazily on first use
    private BTreeIndexManager? _btreeManager;

    /// <summary>
    /// Gets or creates the B-tree index manager.
    /// Lazy initialization ensures it's only created when needed.
    /// </summary>
    private BTreeIndexManager GetOrCreateBTreeManager()
    {
        if (_btreeManager == null)
        {
            _btreeManager = new BTreeIndexManager(this.Columns, this.ColumnTypes);
            
#if DEBUG
            Console.WriteLine($"[BTREE] Created BTreeIndexManager for table '{Name}' with {Columns.Count} columns");
#endif
        }
        return _btreeManager;
    }

    /// <summary>
    /// Creates a B-tree index on the specified column for range queries.
    /// Supports: WHERE column &gt; value, WHERE column BETWEEN x AND y, ORDER BY column.
    /// Expected performance: O(log n + k) range scans vs O(n) full table scan.
    /// </summary>
    /// <param name="columnName">The column name to index.</param>
    public void CreateBTreeIndex(string columnName)
    {
#if DEBUG
        Console.WriteLine($"[BTREE] CreateBTreeIndex called for column '{columnName}' on table '{Name}'");
#endif

        var manager = GetOrCreateBTreeManager();
        manager.CreateIndex(columnName);
        BuildBTreeIndexesFromExistingRows();

#if DEBUG
        Console.WriteLine($"[BTREE] ✅ B-tree index created on '{columnName}' (count: {manager.Count})");
#endif
    }

    /// <summary>
    /// Creates a named B-tree index on the specified column.
    /// Supports SQL syntax: CREATE [UNIQUE] INDEX idx_name ON table(column) USING BTREE.
    /// </summary>
    /// <param name="indexName">The index name (e.g., "idx_age_btree").</param>
    /// <param name="columnName">The column name to index (e.g., "age").</param>
    /// <param name="isUnique">Whether to enforce uniqueness (default: false).</param>
    public void CreateBTreeIndex(string indexName, string columnName, bool isUnique = false)
    {
#if DEBUG
        Console.WriteLine($"[BTREE] CreateBTreeIndex called: indexName='{indexName}', columnName='{columnName}', isUnique={isUnique}");
#endif

        CreateBTreeIndex(columnName);
        
        // Map index name to column name for DROP INDEX support
        this.rwLock.EnterWriteLock();
        try
        {
            this.indexNameToColumn[indexName] = columnName;
            
#if DEBUG
            Console.WriteLine($"[BTREE] ✅ Named index '{indexName}' mapped to column '{columnName}' (unique={isUnique})");
#endif
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Checks if a B-tree index exists for the specified column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if B-tree index exists.</returns>
    public bool HasBTreeIndex(string columnName)
    {
        bool hasIndex = _btreeManager?.HasIndex(columnName) ?? false;
        
#if DEBUG
        Console.WriteLine($"[BTREE] HasBTreeIndex('{columnName}') = {hasIndex}");
#endif
        
        return hasIndex;
    }

    /// <summary>
    /// Removes a B-tree index for the specified column.
    /// Called from RemoveHashIndex when an index is dropped.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if index was removed.</returns>
    internal bool RemoveBTreeIndexInternal(string columnName)
    {
        bool removed = _btreeManager?.RemoveIndex(columnName) ?? false;
        
#if DEBUG
        Console.WriteLine($"[BTREE] RemoveBTreeIndexInternal('{columnName}') = {removed}");
#endif
        
        return removed;
    }

    /// <summary>
    /// Clears all B-tree indexes.
    /// Called from ClearAllIndexes during table drop/recreation.
    /// </summary>
    internal void ClearBTreeIndexes()
    {
#if DEBUG
        Console.WriteLine($"[BTREE] ClearBTreeIndexes called on table '{Name}'");
#endif

        _btreeManager?.Clear();
    }

    /// <summary>
    /// Gets the B-tree index for a column (for range scan operations).
    /// </summary>
    internal object? GetBTreeIndex(string columnName)
    {
        var index = _btreeManager?.GetIndex(columnName);
        
#if DEBUG
        Console.WriteLine($"[BTREE] GetBTreeIndex('{columnName}') = {(index != null ? "found" : "null")}");
#endif
        
        return index;
    }

    /// <summary>
    /// Tries to use B-tree index for range query optimization.
    /// Returns null if B-tree cannot be used, otherwise returns results.
    /// Expected performance: O(log n + k) instead of O(n) for range queries.
    /// </summary>
    /// <param name="where">WHERE clause to evaluate.</param>
    /// <param name="orderBy">Optional ORDER BY column.</param>
    /// <param name="asc">Sort direction.</param>
    /// <returns>Query results if B-tree was used, null otherwise.</returns>
    internal List<Dictionary<string, object>>? TryBTreeRangeScan(
        string where,
        string? orderBy,
        bool asc)
    {
#if DEBUG
        Console.WriteLine($"[BTREE] ════════════════════════════════════════");
        Console.WriteLine($"[BTREE] TryBTreeRangeScan START");
        Console.WriteLine($"[BTREE]   Table: '{Name}'");
        Console.WriteLine($"[BTREE]   WHERE: '{where}'");
        Console.WriteLine($"[BTREE]   ORDER BY: '{orderBy ?? "none"}'");
        Console.WriteLine($"[BTREE]   Manager exists: {_btreeManager != null}");
        Console.WriteLine($"[BTREE]   Index count: {_btreeManager?.Count ?? 0}");
#endif

        if (string.IsNullOrEmpty(where))
        {
#if DEBUG
            Console.WriteLine($"[BTREE] ❌ WHERE clause is empty - cannot use B-tree");
            Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
            return null;
        }

        // Try to parse as range query
        if (!TryParseRangeWhereClause(where, out var col, out var start, out var end))
        {
#if DEBUG
            Console.WriteLine($"[BTREE] ❌ Failed to parse WHERE clause as range query");
            Console.WriteLine($"[BTREE]   Supported formats:");
            Console.WriteLine($"[BTREE]     - 'column >= value'");
            Console.WriteLine($"[BTREE]     - 'column > value'");
            Console.WriteLine($"[BTREE]     - 'column BETWEEN x AND y'");
            Console.WriteLine($"[BTREE]     - 'column <= value'");
            Console.WriteLine($"[BTREE]     - 'column < value'");
            Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
            return null;
        }

#if DEBUG
        Console.WriteLine($"[BTREE] ✅ Parsed range query:");
        Console.WriteLine($"[BTREE]   Column: '{col}'");
        Console.WriteLine($"[BTREE]   Start: '{start}'");
        Console.WriteLine($"[BTREE]   End: '{end}'");
#endif

        // Check if B-tree index exists
        if (!HasBTreeIndex(col))
        {
#if DEBUG
            Console.WriteLine($"[BTREE] ❌ No B-tree index exists for column '{col}'");
            Console.WriteLine($"[BTREE]   Available indexes: {(_btreeManager != null ? string.Join(", ", GetAvailableIndexes()) : "none")}");
            Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
            return null;
        }

        var colIdx = this.Columns.IndexOf(col);
        if (colIdx < 0)
        {
#if DEBUG
            Console.WriteLine($"[BTREE] ❌ Column '{col}' not found in table schema");
            Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
            return null;
        }

        try
        {
#if DEBUG
            Console.WriteLine($"[BTREE] 🔍 Executing B-tree range scan...");
#endif

            var results = new List<Dictionary<string, object>>();
            var engine = GetOrCreateStorageEngine();
            var colType = this.ColumnTypes[colIdx];

            // Get B-tree index via manager
            var btreeIndex = GetBTreeIndex(col);
            if (btreeIndex == null)
            {
#if DEBUG
                Console.WriteLine($"[BTREE] ❌ GetBTreeIndex returned null for '{col}'");
                Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
                return null;
            }

            // Parse range values to correct type
            var startKey = ParseValueForBTreeLookup(start, colType);
            var endKey = ParseValueForBTreeLookup(end, colType);

            if (startKey == null || endKey == null)
            {
#if DEBUG
                Console.WriteLine($"[BTREE] ❌ Failed to parse range values:");
                Console.WriteLine($"[BTREE]   startKey: {startKey ?? "null"}");
                Console.WriteLine($"[BTREE]   endKey: {endKey ?? "null"}");
                Console.WriteLine($"[BTREE]   columnType: {colType}");
                Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
                return null;
            }

#if DEBUG
            Console.WriteLine($"[BTREE] ✅ Parsed range values:");
            Console.WriteLine($"[BTREE]   startKey: {startKey} (type: {startKey.GetType().Name})");
            Console.WriteLine($"[BTREE]   endKey: {endKey} (type: {endKey.GetType().Name})");
#endif

            // Call FindRange via reflection (since type is dynamic)
            var findRangeMethod = btreeIndex.GetType().GetMethod("FindRange");
            if (findRangeMethod == null)
            {
#if DEBUG
                Console.WriteLine($"[BTREE] ❌ FindRange method not found on index type: {btreeIndex.GetType().Name}");
                Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
                return null;
            }

#if DEBUG
            Console.WriteLine($"[BTREE] 🚀 Calling FindRange on B-tree index...");
#endif

            var positions = (IEnumerable<long>)findRangeMethod.Invoke(
                btreeIndex,
                new[] { startKey, endKey })!;

            int positionCount = 0;
            foreach (var pos in positions)
            {
                positionCount++;
                var data = engine.Read(this.Name, pos);
                if (data != null)
                {
                    var row = DeserializeRow(data);
                    if (row != null && IsCurrentVersion(row, pos))
                    {
                        if (row.TryGetValue(col, out var rowValue) && rowValue != null)
                        {
                            var normalizedValue = ConvertValueForBTreeKey(rowValue, colType) ?? rowValue;
                            var valueStr = normalizedValue.ToString() ?? string.Empty;
                            var startStr = startKey.ToString() ?? string.Empty;
                            var endStr = endKey.ToString() ?? string.Empty;

                            if (normalizedValue is IComparable comparableValue
                                && startKey is IComparable comparableStart
                                && endKey is IComparable comparableEnd
                                && normalizedValue.GetType() == startKey.GetType()
                                && normalizedValue.GetType() == endKey.GetType())
                            {
                                if (comparableValue.CompareTo(comparableStart) < 0 ||
                                    comparableValue.CompareTo(comparableEnd) > 0)
                                {
                                    continue;
                                }
                            }
                            else if (string.CompareOrdinal(valueStr, startStr) < 0 ||
                                     string.CompareOrdinal(valueStr, endStr) > 0)
                            {
                                continue;
                            }
                        }

                        if (EvaluateWhere(row, where))
                        {
                            results.Add(row);
                        }
                    }
                }
            }

#if DEBUG
            Console.WriteLine($"[BTREE] ✅ B-tree scan completed:");
            Console.WriteLine($"[BTREE]   Positions found: {positionCount}");
            Console.WriteLine($"[BTREE]   Results after filtering: {results.Count}");
#endif

            // Remove duplicates by primary key (inline to avoid dependency)
            if (this.PrimaryKeyIndex >= 0 && results.Count > 0)
            {
                var pkCol = this.Columns[this.PrimaryKeyIndex];
                var seen = new HashSet<string>();
                var deduplicated = new List<Dictionary<string, object>>();

                foreach (var row in results)
                {
                    if (row.TryGetValue(pkCol, out var pkVal) && pkVal != null)
                    {
                        var pkStr = pkVal.ToString() ?? string.Empty;
                        if (seen.Add(pkStr))
                        {
                            deduplicated.Add(row);
                        }
                    }
                    else
                    {
                        deduplicated.Add(row);
                    }
                }

                results = deduplicated;
                
#if DEBUG
                Console.WriteLine($"[BTREE]   After deduplication: {results.Count}");
#endif
            }

            var orderedResults = ApplyOrdering(results, orderBy, asc);

#if DEBUG
            Console.WriteLine($"[BTREE] ✅ SUCCESS - Returning {orderedResults.Count} results");
            Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif

            return orderedResults;
        }
        catch (Exception)
        {
#if DEBUG
            Console.WriteLine($"[BTREE] ❌ EXCEPTION during B-tree scan:");
            Console.WriteLine($"[BTREE] Fallback to full scan");
            Console.WriteLine($"[BTREE] ════════════════════════════════════════");
#endif
            // If B-tree fails, return null to fall back to full scan
            return null;
        }
    }

#if DEBUG
    /// <summary>
    /// Helper to get available B-tree index names for debugging.
    /// </summary>
    private IEnumerable<string> GetAvailableIndexes()
    {
        if (_btreeManager == null)
        {
            return Enumerable.Empty<string>();
        }

        return Enumerable.Empty<string>();
    }
#endif

    /// <summary>
    /// Checks if a row is the current version (not stale) by comparing position with primary key index.
    /// </summary>
    private bool IsCurrentVersion(Dictionary<string, object> row, long position)
    {
        if (this.PrimaryKeyIndex < 0)
            return true;

        var pkCol = this.Columns[this.PrimaryKeyIndex];
        if (!row.TryGetValue(pkCol, out var pkVal) || pkVal == null)
            return true;

        var pkStr = pkVal.ToString() ?? string.Empty;
        var search = this.Index.Search(pkStr);
        return search.Found && search.Value == position;
    }

    /// <summary>
    /// 🔥 NEW: Indexes a single row in all B-tree indexes.
    /// Called automatically after Insert to keep B-tree in sync with data.
    /// </summary>
    /// <param name="row">The row to index.</param>
    /// <param name="position">The storage position of the row.</param>
    private void IndexRowInBTree(Dictionary<string, object> row, long position)
    {
        if (_btreeManager == null)
            return;

        // Index in all registered B-tree indexes
        for (int i = 0; i < this.Columns.Count; i++)
        {
            var col = this.Columns[i];
            
            if (!HasBTreeIndex(col))
                continue;

            // Get column value
            if (!row.TryGetValue(col, out var value) || value == null)
                continue;

            try
            {
                // Get B-tree index
                var index = GetBTreeIndex(col);
                if (index == null)
                    continue;

                // Insert via reflection (dynamic type)
                var insertMethod = index.GetType().GetMethod("Add");
                if (insertMethod != null)
                {
                    // Convert value to correct type
                    var convertedValue = ConvertValueForBTreeKey(value, this.ColumnTypes[i]);
                    if (convertedValue != null)
                    {
                        insertMethod.Invoke(index, new[] { convertedValue, position });
                    }
                }
            }
            catch (Exception)
            {
#if DEBUG
                Console.WriteLine($"[BTREE] Warning: Failed to index column '{col}'");
#endif
                // Continue indexing other columns
            }
        }
    }

    /// <summary>
    /// 🔥 NEW: Bulk indexes multiple rows in B-tree indexes.
    /// Optimized for InsertBatch - processes all rows at once.
    /// </summary>
    /// <param name="rows">The rows to index.</param>
    /// <param name="positions">The storage positions (must match rows.Count).</param>
    private void BulkIndexRowsInBTree(List<Dictionary<string, object>> rows, long[] positions)
    {
        if (_btreeManager == null || rows.Count == 0)
            return;

        if (rows.Count != positions.Length)
        {
#if DEBUG
            Console.WriteLine($"[BTREE] Warning: Row count mismatch in BulkIndexRowsInBTree");
#endif
            return;
        }

        // Index each row
        for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
        {
            IndexRowInBTree(rows[rowIdx], positions[rowIdx]);
        }

#if DEBUG
        Console.WriteLine($"[BTREE] Bulk indexed {rows.Count} rows across {_btreeManager.Count} B-tree indexes");
#endif
    }

    private void BuildBTreeIndexesFromExistingRows()
    {
        if (_btreeManager == null)
            return;

        var engine = GetOrCreateStorageEngine();

        foreach (var (position, data) in engine.GetAllRecords(Name))
        {
            Dictionary<string, object>? row = StorageMode == StorageMode.Columnar
                ? DeserializeRow(data)
                : DeserializeRowFromSpan(data);

            if (row != null)
            {
                IndexRowInBTree(row, position);
            }
        }
    }

    /// <summary>
    /// Converts a value to the appropriate type for B-tree key.
    /// Handles type coercion for integers, strings, etc.
    /// </summary>
    private static object? ConvertValueForBTreeKey(object value, DataType type)
    {
        try
        {
            return type switch
            {
                DataType.Integer => Convert.ToInt32(value),
                DataType.Long => Convert.ToInt64(value),
                DataType.Real => Convert.ToDouble(value),
                DataType.Decimal => Convert.ToDecimal(value),
                DataType.String => value.ToString(),
                DataType.DateTime => value is DateTime dt ? dt : DateTime.Parse(value.ToString()!, System.Globalization.CultureInfo.InvariantCulture),
                _ => value
            };
        }
        catch
        {
            return null;
        }
    }
}
