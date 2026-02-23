#nullable enable

using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Builds DDL for shadow tracking tables.
/// Creates the metadata table structure that records which rows changed and when.
/// </summary>
public sealed class TrackingTableBuilder(SqliteDialect dialect)
{
    private readonly SqliteDialect _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));

    /// <summary>
    /// Builds the CREATE TABLE statement for a tracking table.
    /// </summary>
    /// <param name="table">Source table metadata.</param>
    /// <returns>SQLite-compatible CREATE TABLE statement.</returns>
    public string BuildCreateTrackingTableSql(ITable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.PrimaryKeyIndex < 0)
            throw new InvalidOperationException($"Table '{table.Name}' must have a primary key for sync.");

        var pkName = table.Columns[table.PrimaryKeyIndex];
        var pkType = table.ColumnTypes[table.PrimaryKeyIndex];
        var pkTypeSql = MapDataType(pkType);

        var trackingTableName = GetTrackingTableName(table.Name);

        return $"CREATE TABLE {trackingTableName} (" +
               $"{pkName} {pkTypeSql} PRIMARY KEY NOT NULL, " +
               $"update_scope_id TEXT, " +
               $"timestamp BIGINT NOT NULL, " +
               $"sync_row_is_tombstone INTEGER NOT NULL DEFAULT 0, " +
               $"last_change_datetime TEXT NOT NULL)";
    }

    /// <summary>
    /// Builds CREATE INDEX statements for tracking table columns.
    /// </summary>
    /// <param name="tableName">Source table name.</param>
    /// <returns>SQL statements for tracking indexes.</returns>
    public IReadOnlyList<string> BuildCreateTrackingIndexesSql(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var trackingTableName = GetTrackingTableName(tableName);
        var quotedTracking = _dialect.QuoteIdentifier(trackingTableName);
        var quotedTimestamp = _dialect.QuoteIdentifier("timestamp");
        var indexName = _dialect.QuoteIdentifier($"idx_{trackingTableName}_timestamp");

        return
        [
            $"CREATE INDEX IF NOT EXISTS {indexName} ON {quotedTracking}({quotedTimestamp})"
        ];
    }

    /// <summary>
    /// Builds a DROP TABLE statement for the tracking table.
    /// </summary>
    /// <param name="tableName">Source table name.</param>
    /// <returns>SQLite-compatible DROP TABLE statement.</returns>
    public string BuildDropTrackingTableSql(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var trackingTableName = GetTrackingTableName(tableName);
        var quotedTracking = _dialect.QuoteIdentifier(trackingTableName);
        return $"DROP TABLE IF EXISTS {quotedTracking}";
    }

    /// <summary>
    /// Gets the tracking table name for a given base table name.
    /// </summary>
    public static string GetTrackingTableName(string tableName) => $"{tableName}_tracking";

    private static string MapDataType(DataType type) => type switch
    {
        DataType.Integer => "INTEGER",
        DataType.Long => "BIGINT",
        DataType.String => "TEXT",
        DataType.Real => "REAL",
        DataType.Blob => "BLOB",
        DataType.Boolean => "BOOLEAN",
        DataType.DateTime => "DATETIME",
        DataType.Decimal => "DECIMAL",
        DataType.Ulid => "ULID",
        DataType.Guid => "GUID",
        DataType.RowRef => "ROWREF",
        DataType.Vector => "VECTOR",
        _ => "TEXT"
    };
}
