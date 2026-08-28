#nullable enable

using System.Data;
using Dotmim.Sync;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Provider.Sync.Metadata;

/// <summary>
/// Reads existing table schemas from a SharpCoreDB database for sync setup auto-discovery.
/// Extracts metadata: tables, columns, primary keys, and relations.
/// Uses <see cref="IDatabase.GetTables"/> and <see cref="IDatabase.GetColumns"/> for discovery.
/// </summary>
public sealed class SharpCoreDBSchemaReader
{
    private SharpCoreDBSchemaReader() { } // Static utility class - prevent instantiation.

    /// <summary>
    /// Gets all user tables in the database.
    /// Excludes internal tracking tables (suffix <c>_tracking</c>) and scope tables.
    /// </summary>
    /// <param name="connection">An open <see cref="SharpCoreDBConnection"/>.</param>
    /// <returns>List of table names.</returns>
    public static IReadOnlyList<string> GetTables(SharpCoreDBConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var db = connection.DbInstance
            ?? throw new InvalidOperationException("Connection must be open before reading schema.");

        return db.GetTables()
            .Where(t => !t.Name.EndsWith("_tracking", StringComparison.OrdinalIgnoreCase)
                     && !t.Name.StartsWith("scope_", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .ToList();
    }

    /// <summary>
    /// Gets column metadata for a specific table.
    /// Returns Dotmim.Sync <see cref="SyncColumn"/> instances ready for sync setup.
    /// </summary>
    /// <param name="connection">An open <see cref="SharpCoreDBConnection"/>.</param>
    /// <param name="tableName">The table name to inspect.</param>
    /// <returns>List of <see cref="SyncColumn"/> describing each column.</returns>
    public static IReadOnlyList<SyncColumn> GetColumns(SharpCoreDBConnection connection, string tableName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var db = connection.DbInstance
            ?? throw new InvalidOperationException("Connection must be open before reading schema.");

        var columns = db.GetColumns(tableName);
        var result = new List<SyncColumn>(columns.Count);

        foreach (var col in columns)
        {
            var syncCol = new SyncColumn(col.Name)
            {
                OriginalTypeName = MapToSqlTypeName(col.DataType),
                DataType = MapToSqlTypeName(col.DataType),
                AllowDBNull = col.IsNullable,
                Ordinal = col.Ordinal,
                MaxLength = GetDefaultMaxLength(col.DataType),
                DbType = (int)MapToDbType(col.DataType),
            };

            result.Add(syncCol);
        }

        return result;
    }

    /// <summary>
    /// Gets the primary key column names for a table.
    /// SharpCoreDB tables typically use the first column or an explicit PK.
    /// Falls back to the first column if no explicit PK metadata is available.
    /// </summary>
    /// <param name="connection">An open <see cref="SharpCoreDBConnection"/>.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>List of primary key column names.</returns>
    public static IReadOnlyList<string> GetPrimaryKeys(SharpCoreDBConnection connection, string tableName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var db = connection.DbInstance
            ?? throw new InvalidOperationException("Connection must be open before reading schema.");

        // SharpCoreDB uses PrimaryKeyIndex to identify the PK column.
        if (db.TryGetTable(tableName, out var table))
        {
            var pkIndex = table.PrimaryKeyIndex;
            if (pkIndex >= 0 && pkIndex < table.Columns.Count)
                return [table.Columns[pkIndex]];
        }

        // Fallback: first column
        var columns = db.GetColumns(tableName);
        if (columns.Count > 0)
            return [columns[0].Name];

        return [];
    }

    /// <summary>
    /// Gets foreign key relations for a table.
    /// SharpCoreDB does not currently expose FK metadata, so this returns an empty list.
    /// </summary>
    /// <param name="connection">An open <see cref="SharpCoreDBConnection"/>.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>Empty list (FK metadata not yet available in SharpCoreDB).</returns>
    public static IReadOnlyList<SyncRelation> GetRelations(SharpCoreDBConnection connection, string tableName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        // SharpCoreDB does not expose FK metadata yet.
        return [];
    }

    /// <summary>
    /// Builds a complete <see cref="SyncTable"/> from the database schema for a given table.
    /// Populates columns and primary keys for Dotmim.Sync setup.
    /// </summary>
    /// <param name="connection">An open <see cref="SharpCoreDBConnection"/>.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>A fully populated <see cref="SyncTable"/>.</returns>
    public static SyncTable BuildSyncTable(SharpCoreDBConnection connection, string tableName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var syncTable = new SyncTable(tableName);

        // Add columns
        var columns = GetColumns(connection, tableName);
        foreach (var col in columns)
        {
            syncTable.Columns.Add(col);
        }

        // Set primary keys
        var pks = GetPrimaryKeys(connection, tableName);
        foreach (var pk in pks)
        {
            syncTable.PrimaryKeys.Add(pk);
        }

        return syncTable;
    }

    private static string MapToSqlTypeName(string dataType)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "INT" or "INT32" or "INTEGER" => "INTEGER",
            "LONG" or "INT64" or "BIGINT" => "BIGINT",
            "STRING" or "TEXT" => "TEXT",
            "DOUBLE" or "FLOAT" or "REAL" => "REAL",
            "BLOB" or "BYTE[]" => "BLOB",
            "BOOL" or "BOOLEAN" => "BOOLEAN",
            "DECIMAL" => "DECIMAL",
            "GUID" => "GUID",
            "DATETIME" => "DATETIME",
            var other => other
        };
    }

    private static DbType MapToDbType(string dataType)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "INT" or "INT32" or "INTEGER" => DbType.Int32,
            "LONG" or "INT64" or "BIGINT" => DbType.Int64,
            "STRING" or "TEXT" => DbType.String,
            "DOUBLE" or "FLOAT" or "REAL" => DbType.Double,
            "BLOB" or "BYTE[]" => DbType.Binary,
            "BOOL" or "BOOLEAN" => DbType.Boolean,
            "DECIMAL" => DbType.Decimal,
            "GUID" => DbType.Guid,
            "DATETIME" => DbType.DateTime,
            _ => DbType.String
        };
    }

    private static int GetDefaultMaxLength(string dataType)
    {
        return dataType.Trim().ToUpperInvariant() switch
        {
            "GUID" => 36,
            "DATETIME" => 33,
            _ => 0  // unlimited
        };
    }
}
