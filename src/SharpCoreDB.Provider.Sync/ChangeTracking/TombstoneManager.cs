#nullable enable

using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Provider.Sync.ChangeTracking;

/// <summary>
/// Manages cleanup of deleted row tombstones.
/// Removes old tombstone records after retention period to prevent unbounded growth.
/// </summary>
public sealed class TombstoneManager : ITombstoneManager
{
    /// <inheritdoc />
    public async Task<int> CleanTombstonesAsync(IDatabase database, string tableName, int retentionDays, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentOutOfRangeException.ThrowIfNegative(retentionDays);

        var trackingTableName = TrackingTableBuilder.GetTrackingTableName(tableName);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).Ticks;

        var sql = $"DELETE FROM {trackingTableName} WHERE sync_row_is_tombstone = 1 AND timestamp < @cutoff";
        var parameters = new Dictionary<string, object?> { ["@cutoff"] = cutoff };

        await database.ExecuteSQLAsync(sql, parameters, cancellationToken).ConfigureAwait(false);

        var countSql = $"SELECT COUNT(*) AS count FROM {trackingTableName} WHERE sync_row_is_tombstone = 1";
        var rows = database.ExecuteQuery(countSql);
        if (rows.Count == 0 || !rows[0].TryGetValue("count", out var countValue))
            return 0;

        return Convert.ToInt32(countValue);
    }

    /// <inheritdoc />
    public Task<int> GetTombstoneCountAsync(IDatabase database, string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var trackingTableName = TrackingTableBuilder.GetTrackingTableName(tableName);
        var countSql = $"SELECT COUNT(*) AS count FROM {trackingTableName} WHERE sync_row_is_tombstone = 1";
        var rows = database.ExecuteQuery(countSql);
        if (rows.Count == 0 || !rows[0].TryGetValue("count", out var countValue))
            return Task.FromResult(0);

        return Task.FromResult(Convert.ToInt32(countValue));
    }
}
