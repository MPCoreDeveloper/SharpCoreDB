// <copyright file="Database.FixedWidthMigration.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.DataStructures;

/// <summary>
/// B5: 1.x → 2.0 record-format migration at the database level. Exposes the on-demand conversion
/// of a legacy (variable-length records) table to the fixed-width record layout and persists the
/// new record-format flag in metadata.
/// </summary>
public partial class Database
{
    /// <inheritdoc />
    public int MigrateTableToFixedWidth(string tableName)
    {
        if (isReadOnly)
        {
            throw new InvalidOperationException("Cannot migrate a table in a read-only database.");
        }

        if (!tables.TryGetValue(tableName, out var table))
        {
            throw new InvalidOperationException($"Unknown table: {tableName}");
        }

        if (table is not Table concrete)
        {
            throw new NotSupportedException(
                $"Table '{tableName}' does not support the fixed-width record layout (single-file tables use their own storage format).");
        }

        int migrated = concrete.MigrateToFixedWidth();
        SaveMetadata(); // persist the new record-format flag so reopen keeps the layout
        return migrated;
    }
}
