// <copyright file="Database.UlidMigration.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using System.Text.Json;

/// <summary>
/// Database ULID migration support (1.9.5).
///
/// SharpCoreDB 1.9.5 made the Crockford Base32 ULID encoding standards-compliant (the first
/// character now carries only 3 significant bits). ULIDs stored by versions before 1.9.5 use the
/// legacy RFC-4648-style encoding and must be rewritten. Because the database metadata records the
/// ULID encoding generation the database was created with, legacy databases are detected
/// automatically — no schema or version guessing is needed.
///
/// Location: Database/Core/Database.UlidMigration.cs
/// Purpose: NeedsLegacyUlidMigration()/MigrateLegacyUlids() + metadata marker plumbing
/// </summary>
public partial class Database
{
    /// <summary>
    /// Gets whether this database was created before 1.9.5 and may contain ULIDs stored in the
    /// legacy (pre-spec) Base32 encoding that need to be converted.
    /// </summary>
    /// <returns>True when the database predates 1.9.5 and should be migrated; otherwise false.</returns>
    public bool NeedsLegacyUlidMigration()
    {
        return _ulidSpec == false;
    }

    /// <summary>
    /// Converts every ULID value stored in this database from the legacy pre-1.9.5 Base32 encoding
    /// to the ULID-spec-compliant encoding. The 128-bit value (timestamp + randomness) of every ULID
    /// is preserved exactly — only the Base32 text changes — so existing <c>_rowid</c> values and
    /// ULID columns migrate one-to-one. After a successful migration the database is permanently
    /// marked as spec-compliant and further calls are no-ops.
    ///
    /// Migration rewrites rows in every table that has at least one <c>DataType.Ulid</c> column.
    /// Rows are replaced via delete + insert, which is safe for ULID primary keys in every storage
    /// mode. Only columns declared as ULID are rewritten; applications that additionally mirror ULIDs
    /// in plain TEXT columns must convert those with <c>Ulid.FromLegacy</c> themselves.
    /// </summary>
    /// <returns>The number of rows whose ULID values were rewritten.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is read-only or a row
    /// cannot be located while migrating.</exception>
    public int MigrateLegacyUlids()
    {
        if (isReadOnly)
        {
            throw new InvalidOperationException("Cannot migrate ULIDs in a read-only database.");
        }

        if (!NeedsLegacyUlidMigration())
        {
            return 0;
        }

        int converted = 0;
        foreach (var table in tables.Values.OfType<Table>())
        {
            converted += MigrateTableUlids(table);
        }

        // Permanently mark the database as spec-compliant so the migration runs exactly once.
        _ulidSpec = true;
        SaveMetadata();
        return converted;
    }

    /// <summary>
    /// Rewrites every ULID value of a single table from the legacy encoding to the spec encoding.
    /// </summary>
    /// <param name="table">The table to migrate.</param>
    /// <returns>The number of rows rewritten.</returns>
    private static int MigrateTableUlids(Table table)
    {
        // Locate all ULID-typed columns of this table.
        List<int> ulidColumns = new(table.Columns.Count);
        for (int i = 0; i < table.Columns.Count; i++)
        {
            if (table.ColumnTypes[i] == DataType.Ulid)
            {
                ulidColumns.Add(i);
            }
        }

        if (ulidColumns.Count == 0)
        {
            return 0;
        }

        // Snapshot all rows (including the hidden _rowid column when present) and convert them.
        var rows = table.SelectIncludingRowId(where: null, orderBy: null, asc: true, noEncrypt: false);
        int converted = 0;

        foreach (var row in rows)
        {
            var updates = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (int columnIndex in ulidColumns)
            {
                string columnName = table.Columns[columnIndex];
                if (!row.TryGetValue(columnName, out var value) || value is null || value == DBNull.Value)
                {
                    continue;
                }

                // ULID columns deserialize to Ulid records; plain TEXT mirrors come back as strings.
                string text = value switch
                {
                    string s => s,
                    Ulid u => u.Value,
                    _ => string.Empty,
                };

                if (text.Length != 26)
                {
                    continue;
                }

                // Pre-1.9.5 databases stored every ULID in the legacy encoding, so the conversion
                // must succeed; a non-ULID string in a ULID column is left untouched.
                if (Ulid.TryFromLegacy(text, out var upgraded) && upgraded is not null && upgraded.Value != text)
                {
                    updates[columnName] = upgraded.Value;
                }
            }

            if (updates.Count == 0)
            {
                continue;
            }

            // Replace the row via delete + insert. This is the only path that is correct for every
            // storage mode when the primary key itself is a ULID (its text changes while the 128-bit
            // value stays identical), because delete/insert maintain the PK index consistently.
            if (table.PrimaryKeyIndex >= 0)
            {
                string pkColumn = table.Columns[table.PrimaryKeyIndex];
                if (row.TryGetValue(pkColumn, out var pkValue) && pkValue is not null && pkValue != DBNull.Value
                    && !table.DeleteByPrimaryKey(pkValue))
                {
                    throw new InvalidOperationException(
                        $"ULID migration failed: could not locate row by primary key '{pkColumn}' in table '{table.Name}'.");
                }
            }

            foreach (var kvp in updates)
            {
                row[kvp.Key] = kvp.Value;
            }

            table.Insert(row);
            converted++;
        }

        if (converted > 0)
        {
            // Columnar storage performs logical deletes: the pre-migration records (carrying the old
            // PK/ULID text) remain on disk until compaction and would otherwise resurface after a
            // reopen + PK-index rebuild. Compact now so only the spec-encoded records survive.
            table.CompactStorage();
        }

        return converted;
    }

    /// <summary>
    /// Reads the persisted ULID-spec marker from database metadata.
    /// </summary>
    /// <param name="meta">The deserialized metadata dictionary, or null when unavailable.</param>
    /// <param name="metaExists">True when metadata exists on disk (as opposed to a fresh database).</param>
    /// <returns>True when the database stores spec-compliant ULIDs.</returns>
    private static bool ReadUlidSpecMarker(Dictionary<string, object>? meta, bool metaExists)
    {
        if (meta is not null && meta.TryGetValue(PersistenceConstants.UlidSpecMarkerKey, out var marker))
        {
            return marker switch
            {
                bool b => b,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => false,
            };
        }

        // No marker → the database was created before the marker existed (pre-1.9.5), unless there is
        // no metadata at all, which means a fresh 1.9.5+ database (spec ULIDs from birth).
        return !metaExists;
    }

    /// <summary>
    /// Test hook: simulates a pre-1.9.5 database (or marks one as migrated) so the migration
    /// detection and rewrite behavior can be exercised without crafting legacy metadata on disk.
    /// </summary>
    /// <param name="value">The ULID-spec state to simulate.</param>
    internal void SetUlidSpecForTesting(bool value)
    {
        _ulidSpec = value;
    }
}
