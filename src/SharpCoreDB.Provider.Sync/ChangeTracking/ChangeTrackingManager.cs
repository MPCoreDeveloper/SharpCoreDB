#nullable enable

using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Manages change tracking setup and execution.
/// Creates/drops triggers that automatically capture INSERT/UPDATE/DELETE operations
/// in shadow tracking tables for sync change enumeration.
/// </summary>
public sealed class ChangeTrackingManager(TrackingTableBuilder trackingTableBuilder, SqliteDialect dialect) : IChangeTrackingManager
{
    private readonly TrackingTableBuilder _trackingTableBuilder = trackingTableBuilder
        ?? throw new ArgumentNullException(nameof(trackingTableBuilder));

    private readonly SqliteDialect _dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));

    /// <inheritdoc />
    public async Task ProvisionTrackingAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!database.TryGetTable(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' does not exist.");

        if (table.PrimaryKeyIndex < 0)
            throw new InvalidOperationException($"Table '{tableName}' must have a primary key for sync.");

        var pkColumn = table.Columns[table.PrimaryKeyIndex];
        var statements = new List<string>
        {
            _trackingTableBuilder.BuildCreateTrackingTableSql(table)
        };

        statements.AddRange(_trackingTableBuilder.BuildCreateTrackingIndexesSql(tableName));
        statements.AddRange(BuildCreateTriggerSql(tableName, pkColumn));

        await database.ExecuteBatchSQLAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeprovisionTrackingAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var trackingTableName = TrackingTableBuilder.GetTrackingTableName(tableName);

        // Drop triggers (silently ignored if not supported)
        foreach (var triggerSql in BuildDropTriggerSql(tableName))
        {
            await database.ExecuteSQLAsync(triggerSql, cancellationToken).ConfigureAwait(false);
        }

        // Drop tracking table
        var dropTableSql = $"DROP TABLE IF EXISTS {trackingTableName}";
        await database.ExecuteSQLAsync(dropTableSql, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> IsProvisionedAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var trackingTableName = TrackingTableBuilder.GetTrackingTableName(tableName);
        var exists = database.GetTables().Any(t => t.Name.Equals(trackingTableName, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    /// <inheritdoc />
    public async Task RecordChangeAsync(IDatabase database, string tableName, string primaryKeyValue, bool isDelete, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryKeyValue);

        if (!database.TryGetTable(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' does not exist.");

        var trackingTableName = TrackingTableBuilder.GetTrackingTableName(tableName);
        var pkColumn = table.Columns[table.PrimaryKeyIndex];
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tombstone = isDelete ? 1 : 0;
        var now = DateTime.UtcNow.ToString("o");

        // Check if tracking record already exists
        var existing = database.ExecuteQuery(
            $"SELECT {pkColumn} FROM {trackingTableName} WHERE {pkColumn} = {primaryKeyValue}");

        if (existing.Count > 0)
        {
            // Update existing tracking record
            database.ExecuteSQL(
                $"UPDATE {trackingTableName} SET timestamp = {timestamp}, sync_row_is_tombstone = {tombstone}, last_change_datetime = '{now}' WHERE {pkColumn} = {primaryKeyValue}");
        }
        else
        {
            // Insert new tracking record
            database.ExecuteSQL(
                $"INSERT INTO {trackingTableName} ({pkColumn}, timestamp, sync_row_is_tombstone, last_change_datetime) VALUES ({primaryKeyValue}, {timestamp}, {tombstone}, '{now}')");
        }

        // Flush to ensure data is persisted and visible to subsequent queries
        database.Flush();

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private IReadOnlyList<string> BuildCreateTriggerSql(string tableName, string pkColumn)
    {
        var trackingTableName = TrackingTableBuilder.GetTrackingTableName(tableName);
        var quotedTable = _dialect.QuoteIdentifier(tableName);
        var quotedTracking = _dialect.QuoteIdentifier(trackingTableName);
        var quotedPk = _dialect.QuoteIdentifier(pkColumn);
        var quotedScope = _dialect.QuoteIdentifier("update_scope_id");
        var quotedTimestamp = _dialect.QuoteIdentifier("timestamp");
        var quotedTombstone = _dialect.QuoteIdentifier("sync_row_is_tombstone");
        var quotedLastChange = _dialect.QuoteIdentifier("last_change_datetime");

        var insertTrigger = _dialect.QuoteIdentifier($"trg_{tableName}_insert_tracking");
        var updateTrigger = _dialect.QuoteIdentifier($"trg_{tableName}_update_tracking");
        var deleteTrigger = _dialect.QuoteIdentifier($"trg_{tableName}_delete_tracking");

        return
        [
            $"CREATE TRIGGER {insertTrigger} AFTER INSERT ON {quotedTable} BEGIN " +
            $"INSERT OR REPLACE INTO {quotedTracking} ({quotedPk}, {quotedScope}, {quotedTimestamp}, {quotedTombstone}, {quotedLastChange}) " +
            $"VALUES (NEW.{quotedPk}, NULL, SYNC_TIMESTAMP(), 0, CURRENT_TIMESTAMP); END",

            $"CREATE TRIGGER {updateTrigger} AFTER UPDATE ON {quotedTable} BEGIN " +
            $"UPDATE {quotedTracking} SET {quotedScope} = NULL, {quotedTimestamp} = SYNC_TIMESTAMP(), {quotedTombstone} = 0, {quotedLastChange} = CURRENT_TIMESTAMP " +
            $"WHERE {quotedPk} = OLD.{quotedPk}; END",

            $"CREATE TRIGGER {deleteTrigger} AFTER DELETE ON {quotedTable} BEGIN " +
            $"UPDATE {quotedTracking} SET {quotedScope} = NULL, {quotedTimestamp} = SYNC_TIMESTAMP(), {quotedTombstone} = 1, {quotedLastChange} = CURRENT_TIMESTAMP " +
            $"WHERE {quotedPk} = OLD.{quotedPk}; END"
        ];
    }

    private IReadOnlyList<string> BuildDropTriggerSql(string tableName)
    {
        var insertTrigger = _dialect.QuoteIdentifier($"trg_{tableName}_insert_tracking");
        var updateTrigger = _dialect.QuoteIdentifier($"trg_{tableName}_update_tracking");
        var deleteTrigger = _dialect.QuoteIdentifier($"trg_{tableName}_delete_tracking");

        return
        [
            $"DROP TRIGGER IF EXISTS {insertTrigger}",
            $"DROP TRIGGER IF EXISTS {updateTrigger}",
            $"DROP TRIGGER IF EXISTS {deleteTrigger}"
        ];
    }
}
