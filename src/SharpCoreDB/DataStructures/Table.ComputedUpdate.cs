// <copyright file="Table.ComputedUpdate.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using System;
using System.Collections.Generic;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Table partial — an UPDATE whose SET values are computed per row (<c>SET n = n + 1</c>, <c>SET name = upper(name)</c>).
/// <para>
/// Up to 2.1.4 a SET value was always parsed as a literal of the column's type, so <c>SET n = n + 5</c> failed ("Invalid
/// value 'n + 5'") and <c>SET label = name</c> on a TEXT column stored the text <c>name</c>. As in SQLite, every
/// expression reads the row as it was before the statement, and every value is computed and every row validated before
/// the first row is written, so an expression or constraint that fails leaves the table unchanged.
/// </para>
/// </summary>
public partial class Table
{
    /// <inheritdoc />
    public int UpdateAffectedCount(
        string? where,
        Dictionary<string, object> updates,
        IReadOnlyList<KeyValuePair<string, Func<Dictionary<string, object>, object?>>> computed)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(computed);
        if (computed.Count == 0)
        {
            return UpdateAffectedCount(where, updates);
        }

        if (this.isReadOnly) throw new InvalidOperationException("Cannot update in readonly mode");

        this.rwLock.EnterWriteLock();
        try
        {
            var engine = GetOrCreateStorageEngine();
            EnsureAllRegisteredIndexesLoaded();

            var rows = ResolveUpdateRows(where);
            var rowUpdates = new List<Dictionary<string, object>>(rows.Count);
            foreach (var (_, row) in rows)
            {
                var values = new Dictionary<string, object>(updates);
                foreach (var (column, compute) in computed)
                {
                    values[column] = compute(row)!;
                }

                rowUpdates.Add(values);
            }

            if (rows.Count > 0 && UpdateTouchesKeyColumns(rowUpdates[0]))
            {
                var changes = new List<(IReadOnlyList<Dictionary<string, object>> Rows, Dictionary<string, object> Updates)>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    changes.Add(([rows[i].Row], rowUpdates[i]));
                }

                ValidateKeyChanges(changes);
            }

            bool hasCheckConstraints = HasCheckConstraints();
            for (int i = 0; i < rows.Count; i++)
            {
                ValidateRowAfterUpdate(rows[i].Row, rowUpdates[i], hasCheckConstraints);
            }

            for (int i = 0; i < rows.Count; i++)
            {
                UpdateSingleRow(rows[i].Row, engine, rowUpdates[i], rows[i].Position);
            }

            if (StorageMode == StorageMode.Columnar)
            {
                TryAutoCompact();
            }

            return rows.Count;
        }
        finally
        {
            this.rwLock.ExitWriteLock();
        }
    }
}
