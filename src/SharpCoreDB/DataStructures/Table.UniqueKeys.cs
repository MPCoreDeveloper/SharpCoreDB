// <copyright file="Table.UniqueKeys.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Table partial — uniqueness of the PRIMARY KEY and of single-column UNIQUE constraints on every write path.
/// <para>
/// Up to 2.1.4 two guarantees were missing (known issues §8 and §9). An UPDATE that changed the primary key never
/// checked the new key: <c>UPDATE t SET id = 1 WHERE id = 3</c> with a row 1 present was accepted, the primary-key
/// index was re-pointed at the moved row and the original row 1 became unreachable. And a UNIQUE column constraint was
/// parsed and stored but never enforced — the INSERT-time check was a stub, a UNIQUE <em>index</em> was checked only on
/// a single-row INSERT into a columnar table, and multi-row INSERTs and UPDATEs were never checked.
/// </para>
/// <para>
/// A UNIQUE column now gets an automatic unique hash index, as SQLite gives it a <c>sqlite_autoindex_…</c>, so the
/// check is a hash lookup. Every check starts with a test that allocates nothing and returns at once for a table or a
/// statement without keys to protect, which keeps the measured write paths unchanged.
/// </para>
/// </summary>
public partial class Table
{
    /// <summary>
    /// Registers an automatic unique hash index for every single-column UNIQUE constraint that has none — at CREATE TABLE
    /// and on every open, so tables created before this existed are covered too. Registration does not build anything:
    /// the index is built from the stored rows on first use, and rows stored before (duplicates included) never make
    /// registration fail; only a new duplicate is refused.
    /// </summary>
    internal void EnsureUniqueConstraintIndexes()
    {
        for (int i = 0; i < this.UniqueConstraints.Count; i++)
        {
            var constraint = this.UniqueConstraints[i];
            if (constraint.Count != 1)
            {
                continue;
            }

            var column = this.Columns.Find(c => c.Equals(constraint[0], StringComparison.OrdinalIgnoreCase));
            if (column is null || (this.PrimaryKeyIndex >= 0 && column == this.Columns[this.PrimaryKeyIndex]))
            {
                continue;
            }

            var indexName = $"sqlite_autoindex_{this.Name}_{i + 1}";
            if (this.indexNameToColumn.ContainsKey(indexName))
            {
                continue;
            }

            CreateHashIndex(indexName, column, isUnique: true);
        }
    }

    /// <summary>Returns <c>true</c> when any unique index (other than the primary key's) is registered. Allocates nothing.</summary>
    private bool HasUniqueIndex()
    {
        foreach (var (column, metadata) in
                 this.registeredIndexes) // NOSONAR:S3267 - write hot path: LINQ would allocate
        {
            if (metadata.IsUnique && !IsPrimaryKeyColumn(column))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="updates"/> sets the primary key or a uniquely indexed column. Allocates
    /// nothing, so every UPDATE path can ask it first.
    /// </summary>
    private bool UpdateTouchesKeyColumns(Dictionary<string, object> updates)
    {
        if (this.PrimaryKeyIndex >= 0 && TryGetUpdateValue(updates, this.Columns[this.PrimaryKeyIndex], out _))
        {
            return true;
        }

        foreach (var (column, metadata) in
                 this.registeredIndexes) // NOSONAR:S3267 - write hot path: LINQ would allocate
        {
            if (metadata.IsUnique && !IsPrimaryKeyColumn(column) && TryGetUpdateValue(updates, column, out _))
            {
                return true;
            }
        }

        return false;
    }

    // The three entry points below take the no-unique-index exit before anything is captured: a lambda that captures a
    // parameter makes the compiler allocate its closure at method entry, so the capturing calls live in the *Core
    // helpers and an INSERT into a table without unique indexes allocates nothing here.

    /// <summary>Validates one inserted row against the unique indexes. Returns at once without any.</summary>
    private void ValidateUniqueValues(Dictionary<string, object> row)
    {
        if (HasUniqueIndex())
        {
            ValidateUniqueValuesCore(row);
        }
    }

    /// <summary>Validates inserted rows (one statement) against the unique indexes and against each other.</summary>
    private void ValidateUniqueValues(IReadOnlyList<Dictionary<string, object>> rows)
    {
        if (HasUniqueIndex())
        {
            ValidateUniqueValuesCore(rows);
        }
    }

    /// <summary>Validates inserted rows in table column order (the batch parser's shape).</summary>
    private void ValidateUniqueValues(object[][] rows)
    {
        if (HasUniqueIndex())
        {
            ValidateUniqueValuesCore(rows);
        }
    }

    private void ValidateUniqueValuesCore(Dictionary<string, object> row) =>
        ValidateUniqueValues(1, (_, column) => row.TryGetValue(column, out var value) ? value : null);

    private void ValidateUniqueValuesCore(IReadOnlyList<Dictionary<string, object>> rows) =>
        ValidateUniqueValues(rows.Count, (r, column) => rows[r].TryGetValue(column, out var value) ? value : null);

    private void ValidateUniqueValuesCore(object[][] rows) =>
        ValidateUniqueValues(rows.Length, (r, column) =>
        {
            int index = this.Columns.IndexOf(column);
            return index >= 0 && index < rows[r].Length ? rows[r][index] : null;
        });

    private void ValidateUniqueValues(int rowCount, Func<int, string, object?> valueAt)
    {
        foreach (var column in UniqueIndexedColumns())
        {
            EnsureIndexLoaded(column);
            this.hashIndexes.TryGetValue(column, out var hashIndex);
            var seen = new HashSet<string>(UniqueKeyComparer(column));
            for (int r = 0; r < rowCount; r++)
            {
                var value = valueAt(r, column);
                if (value is null or DBNull)
                {
                    continue; // NULLs never conflict, as in SQLite
                }

                if (!seen.Add(UniqueKey(value)) || (hashIndex is not null && hashIndex.ContainsKey(value)))
                {
                    throw DuplicateUniqueValue(column, value);
                }
            }
        }
    }

    /// <summary>
    /// Validates a statement's (or a batch's) key changes before any row is written: a primary key or a uniquely indexed
    /// column may only take a value no other row holds — counting the values earlier changes in the same batch claim and
    /// release — and a SET of such a column may match at most one row. An UPDATE that keeps a row's own value passes.
    /// </summary>
    /// <param name="changes">Each change's matched rows and SET values, in execution order.</param>
    private void ValidateKeyChanges(
        IReadOnlyList<(IReadOnlyList<Dictionary<string, object>> Rows, Dictionary<string, object> Updates)> changes)
    {
        if (this.PrimaryKeyIndex >= 0)
        {
            var pkColumn = this.Columns[this.PrimaryKeyIndex];
            ValidateKeyColumnChanges(changes, pkColumn, StringComparer.Ordinal, (key, _) => IsPrimaryKeyTaken(key),
                isPrimaryKey: true);
        }

        foreach (var column in UniqueIndexedColumns())
        {
            EnsureIndexLoaded(column);
            this.hashIndexes.TryGetValue(column, out var hashIndex);
            ValidateKeyColumnChanges(
                changes,
                column,
                UniqueKeyComparer(column),
                (_, value) => hashIndex is not null && hashIndex.ContainsKey(value),
                isPrimaryKey: false);
        }
    }

    private void ValidateKeyColumnChanges(
        IReadOnlyList<(IReadOnlyList<Dictionary<string, object>> Rows, Dictionary<string, object> Updates)> changes,
        string column,
        StringComparer comparer,
        Func<string, object, bool> isTaken,
        bool isPrimaryKey)
    {
        var claimed = new HashSet<string>(comparer);
        var released = new HashSet<string>(comparer);
        foreach (var (rows, updates) in changes)
        {
            if (rows.Count == 0 || !TryGetUpdateValue(updates, column, out var newValue) || newValue is null or DBNull)
            {
                continue;
            }

            var newKey = UniqueKey(newValue);
            if (rows.Count > 1)
            {
                throw isPrimaryKey
                    ? new InvalidOperationException(
                        $"Primary key violation: the UPDATE would give {rows.Count} rows the key {newKey}")
                    : DuplicateUniqueValue(column, newValue);
            }

            var oldValue = rows[0].TryGetValue(column, out var current) ? current : null;
            var oldKey = oldValue is null or DBNull ? null : UniqueKey(oldValue);
            if (oldKey is not null && comparer.Equals(oldKey, newKey))
            {
                continue; // the row keeps its own value
            }

            if (!claimed.Add(newKey) || (isTaken(newKey, newValue) && !released.Contains(newKey)))
            {
                throw isPrimaryKey
                    ? new InvalidOperationException($"Primary key violation: {newKey}")
                    : DuplicateUniqueValue(column, newValue);
            }

            if (oldKey is not null)
            {
                released.Add(oldKey);
            }
        }
    }

    /// <summary>
    /// Validates the key changes of a batched UPDATE (<c>UpdateMultipleCore</c>, the path <c>ExecuteBatchSQL</c> and the
    /// structured batch API take) before any operation is written. The scan for an operation that touches a key column
    /// allocates nothing; only those operations have their rows resolved.
    /// </summary>
    private void ValidateBatchKeyChanges(
        IReadOnlyList<(string Where, string? KeyColumn, object? KeyValue, string? KeyLiteral, Dictionary<string, object>
            Updates)> operations)
    {
        for (int i = 0; i < operations.Count; i++)
        {
            if (UpdateTouchesKeyColumns(operations[i].Updates))
            {
                ValidateBatchKeyChangesCore(operations);
                return;
            }
        }
    }

    private void ValidateBatchKeyChangesCore(
        IReadOnlyList<(string Where, string? KeyColumn, object? KeyValue, string? KeyLiteral, Dictionary<string, object>
            Updates)> operations)
    {
        var changes = new List<(IReadOnlyList<Dictionary<string, object>> Rows, Dictionary<string, object> Updates)>();
        foreach (var (where, keyColumn, keyValue, keyLiteral, updates) in operations)
        {
            if (!UpdateTouchesKeyColumns(updates))
            {
                continue;
            }

            // The same WHERE text UpdateMultipleCore falls back to for a structured operation.
            var whereText = where.Length > 0 || keyColumn is null
                ? where
                : keyLiteral is not null
                    ? keyColumn + " = " + keyLiteral
                    : BuildStructuredWhereText(keyColumn, keyValue);

            changes.Add((ResolveUpdateRows(whereText).ConvertAll(r => r.Row), updates));
        }

        ValidateKeyChanges(changes);
    }

    /// <summary>The uniquely indexed columns other than the primary key (which has its own index and check).</summary>
    private List<string> UniqueIndexedColumns()
    {
        var columns = new List<string>();
        foreach (var (column, metadata) in this.registeredIndexes)
        {
            if (metadata.IsUnique && !IsPrimaryKeyColumn(column))
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    private bool IsPrimaryKeyColumn(string column) =>
        this.PrimaryKeyIndex >= 0 &&
        column.Equals(this.Columns[this.PrimaryKeyIndex], StringComparison.OrdinalIgnoreCase);

    /// <summary>Compares unique keys with the column's collation: case-insensitively for NOCASE columns.</summary>
    private StringComparer UniqueKeyComparer(string column)
    {
        int index = this.Columns.IndexOf(column);
        var collation = index >= 0 && index < this.ColumnCollations.Count
            ? this.ColumnCollations[index]
            : CollationType.Binary;
        return collation is CollationType.NoCase or CollationType.UnicodeCaseInsensitive
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static string UniqueKey(object value) =>
        value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool TryGetUpdateValue(Dictionary<string, object> updates, string column, out object? value)
    {
        if (updates.TryGetValue(column, out var exact))
        {
            value = exact;
            return true;
        }

        foreach (var (key, candidate) in updates) // NOSONAR:S3267 - SET lists are tiny; LINQ would allocate per call
        {
            if (key.Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static InvalidOperationException DuplicateUniqueValue(string column, object value) =>
        new($"Duplicate key value '{value}' violates unique constraint on column '{column}'");
}
