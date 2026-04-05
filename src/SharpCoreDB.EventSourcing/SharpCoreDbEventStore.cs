// <copyright file="SharpCoreDbEventStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.EventSourcing;

using System.Globalization;
using SharpCoreDB.Interfaces;

/// <summary>
/// Persistent <see cref="IEventStore"/> implementation backed by SharpCoreDB tables.
/// </summary>
/// <remarks>
/// Uses append-only inserts with per-stream and global sequence assignment.
/// </remarks>
/// <param name="database">The SharpCoreDB database instance to use for persistence.</param>
/// <param name="tableName">Optional table name for event storage.</param>
public sealed class SharpCoreDbEventStore(IDatabase database, string tableName = "scdb_event_store_events") : IEventStore
{
    private readonly IDatabase _database = database ?? throw new ArgumentNullException(nameof(database));
    private readonly string _tableName = ValidateTableName(tableName);
    private readonly string _snapshotTableName = ValidateTableName($"{tableName}_snapshots");
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public Task<AppendResult> AppendEventAsync(
        EventStreamId streamId,
        EventAppendEntry entry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            EnsureSchema();

            var nextStreamSequence = GetNextStreamSequence(streamId);
            var nextGlobalSequence = GetNextGlobalSequence();
            var statement = BuildInsertStatement(streamId, nextStreamSequence, nextGlobalSequence, entry);

            _database.ExecuteBatchSQL([statement]);
            _database.Flush();
            _database.ForceSave();

            return Task.FromResult(AppendResult.Ok(streamId, nextStreamSequence, nextGlobalSequence));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AppendResult>> AppendEventsAsync(
        EventStreamId streamId,
        IEnumerable<EventAppendEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        var entryList = entries as IList<EventAppendEntry> ?? entries.ToList();
        if (entryList.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<AppendResult>>([]);
        }

        lock (_lock)
        {
            EnsureSchema();

            var nextStreamSequence = GetNextStreamSequence(streamId);
            var nextGlobalSequence = GetNextGlobalSequence();

            List<string> statements = new(entryList.Count);
            List<AppendResult> results = new(entryList.Count);

            foreach (var entry in entryList)
            {
                statements.Add(BuildInsertStatement(streamId, nextStreamSequence, nextGlobalSequence, entry));
                results.Add(AppendResult.Ok(streamId, nextStreamSequence, nextGlobalSequence));

                nextStreamSequence++;
                nextGlobalSequence++;
            }

            _database.ExecuteBatchSQL(statements);
            _database.Flush();
            _database.ForceSave();

            return Task.FromResult((IReadOnlyList<AppendResult>)results);
        }
    }

    /// <inheritdoc />
    public Task<ReadResult> ReadStreamAsync(
        EventStreamId streamId,
        EventReadRange range,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (range.ToSequence < range.FromSequence)
        {
            return Task.FromResult(ReadResult.Empty());
        }

        lock (_lock)
        {
            EnsureSchema();

            var query = $"SELECT stream_id, stream_sequence, global_sequence, event_type, payload_base64, metadata_base64, timestamp_utc FROM {_tableName} WHERE stream_id = ? AND stream_sequence >= ? AND stream_sequence <= ? ORDER BY stream_sequence";
            var parameters = new Dictionary<string, object?>
            {
                ["0"] = streamId.Value,
                ["1"] = range.FromSequence,
                ["2"] = range.ToSequence,
            };

            var rows = _database.ExecuteQuery(query, parameters);
            var envelopes = rows.Select(MapEnvelope).ToList();

            return Task.FromResult(ReadResult.Ok(envelopes, envelopes.Count));
        }
    }

    /// <inheritdoc />
    public Task<ReadResult> ReadAllAsync(
        long fromGlobalSequence = 1,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (limit <= 0)
        {
            return Task.FromResult(ReadResult.Empty());
        }

        lock (_lock)
        {
            EnsureSchema();

            var query = $"SELECT stream_id, stream_sequence, global_sequence, event_type, payload_base64, metadata_base64, timestamp_utc FROM {_tableName} WHERE global_sequence >= ? ORDER BY global_sequence";
            var rows = _database.ExecuteQuery(
                query,
                new Dictionary<string, object?>
                {
                    ["0"] = fromGlobalSequence,
                });

            var totalCount = rows.Count;
            var envelopes = rows.Take(limit).Select(MapEnvelope).ToList();
            return Task.FromResult(ReadResult.Ok(envelopes, totalCount));
        }
    }

    /// <inheritdoc />
    public Task<long> GetStreamLengthAsync(
        EventStreamId streamId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            EnsureSchema();

            var rows = _database.ExecuteQuery(
                $"SELECT stream_sequence FROM {_tableName} WHERE stream_id = ? ORDER BY stream_sequence DESC LIMIT 1",
                new Dictionary<string, object?> { ["0"] = streamId.Value });

            if (rows.Count == 0)
            {
                return Task.FromResult(0L);
            }

            return Task.FromResult(GetNullableLongValue(rows[0], "stream_sequence") ?? 0L);
        }
    }

    /// <inheritdoc />
    public Task SaveSnapshotAsync(
        EventSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            EnsureSchema();

            var snapshotDataBase64 = Convert.ToBase64String(snapshot.SnapshotData.ToArray());
            var createdAtText = snapshot.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var statements = new List<string>(2)
            {
                $"DELETE FROM {_snapshotTableName} WHERE stream_id = '{EscapeSqlLiteral(snapshot.StreamId.Value)}' AND version = {snapshot.Version}",
                // ✅ FIX: Explicitly specify column names to avoid misalignment with auto-generated _rowid column
                $"INSERT INTO {_snapshotTableName} (stream_id, version, snapshot_data_base64, created_at_utc) VALUES ('{EscapeSqlLiteral(snapshot.StreamId.Value)}', {snapshot.Version}, '{snapshotDataBase64}', '{createdAtText}')",
            };

            _database.ExecuteBatchSQL(statements);
            _database.Flush();
            _database.ForceSave();
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<EventSnapshot?> LoadSnapshotAsync(
        EventStreamId streamId,
        long maxVersion = long.MaxValue,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            EnsureSchema();

            var rows = _database.ExecuteQuery(
                $"SELECT stream_id, version, snapshot_data_base64, created_at_utc FROM {_snapshotTableName} WHERE stream_id = ? AND version <= ? ORDER BY version DESC LIMIT 1",
                new Dictionary<string, object?>
                {
                    ["0"] = streamId.Value,
                    ["1"] = maxVersion,
                });

            if (rows.Count == 0)
            {
                return Task.FromResult<EventSnapshot?>(null);
            }

            return Task.FromResult<EventSnapshot?>(MapSnapshot(rows[0]));
        }
    }

    private void EnsureSchema()
    {
        if (!_database.TryGetTable(_tableName, out _))
        {
            _database.ExecuteSQL($"CREATE TABLE {_tableName} (stream_id TEXT, stream_sequence LONG, global_sequence LONG, event_type TEXT, payload_base64 TEXT, metadata_base64 TEXT, timestamp_utc TEXT)");
        }

        if (!_database.TryGetTable(_snapshotTableName, out _))
        {
            _database.ExecuteSQL($"CREATE TABLE {_snapshotTableName} (stream_id TEXT, version LONG, snapshot_data_base64 TEXT, created_at_utc TEXT)");
        }

        _database.Flush();
        _database.ForceSave();
    }

    private long GetNextStreamSequence(EventStreamId streamId)
    {
        var rows = _database.ExecuteQuery(
            $"SELECT stream_sequence FROM {_tableName} WHERE stream_id = ? ORDER BY stream_sequence DESC LIMIT 1",
            new Dictionary<string, object?> { ["0"] = streamId.Value });

        if (rows.Count == 0)
        {
            return 1;
        }

        var currentMax = GetNullableLongValue(rows[0], "stream_sequence") ?? 0;
        return currentMax + 1;
    }

    private long GetNextGlobalSequence()
    {
        var rows = _database.ExecuteQuery($"SELECT global_sequence FROM {_tableName} ORDER BY global_sequence DESC LIMIT 1");
        if (rows.Count == 0)
        {
            return 1;
        }

        var currentMax = GetNullableLongValue(rows[0], "global_sequence") ?? 0;
        return currentMax + 1;
    }

    private string BuildInsertStatement(EventStreamId streamId, long streamSequence, long globalSequence, EventAppendEntry entry)
    {
        var payloadBase64 = Convert.ToBase64String(entry.Payload.ToArray());
        var metadataBase64 = Convert.ToBase64String(entry.Metadata.ToArray());
        var timestampText = entry.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        // ✅ FIX: Explicitly specify column names to avoid misalignment with auto-generated _rowid column
        return $"INSERT INTO {_tableName} (stream_id, stream_sequence, global_sequence, event_type, payload_base64, metadata_base64, timestamp_utc) VALUES ('{EscapeSqlLiteral(streamId.Value)}', {streamSequence}, {globalSequence}, '{EscapeSqlLiteral(entry.EventType)}', '{payloadBase64}', '{metadataBase64}', '{timestampText}')";
    }

    private static EventEnvelope MapEnvelope(Dictionary<string, object> row)
    {
        var streamId = GetStringValue(row, "stream_id");
        var streamSequence = GetLongValue(row, "stream_sequence");
        var globalSequence = GetLongValue(row, "global_sequence");
        var eventType = GetStringValue(row, "event_type");
        var payload = DecodeBase64(GetStringValue(row, "payload_base64"));
        var metadata = DecodeBase64(GetStringValue(row, "metadata_base64"));
        var timestampRaw = GetStringValue(row, "timestamp_utc");

        var timestamp = DateTimeOffset.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        return new EventEnvelope(
            new EventStreamId(streamId),
            streamSequence,
            globalSequence,
            eventType,
            payload,
            metadata,
            timestamp);
    }

    private static EventSnapshot MapSnapshot(Dictionary<string, object> row)
    {
        var streamId = GetStringValue(row, "stream_id");
        var version = GetLongValue(row, "version");
        var dataBase64 = GetStringValue(row, "snapshot_data_base64");
        var createdAtRaw = GetStringValue(row, "created_at_utc");

        var createdAt = DateTimeOffset.TryParse(createdAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        return new EventSnapshot(
            new EventStreamId(streamId),
            version,
            DecodeBase64(dataBase64),
            createdAt);
    }

    private static byte[] DecodeBase64(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return Convert.FromBase64String(value);
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new ArgumentException("Table name cannot be null or whitespace.", nameof(tableName));
        }

        foreach (var character in tableName)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                throw new ArgumentException("Table name can only contain letters, digits, and underscore.", nameof(tableName));
            }
        }

        return tableName;
    }

    private static string GetStringValue(Dictionary<string, object> row, string columnName)
    {
        if (!TryGetColumnValue(row, columnName, out var value) || value is null)
        {
            return string.Empty;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static long GetLongValue(Dictionary<string, object> row, string columnName)
    {
        if (!TryGetColumnValue(row, columnName, out var value) || value is null)
        {
            return 0;
        }

        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static long? GetNullableLongValue(Dictionary<string, object> row, string columnName)
    {
        if (!TryGetColumnValue(row, columnName, out var value) || value is null)
        {
            return null;
        }

        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static bool TryGetColumnValue(Dictionary<string, object> row, string columnName, out object? value)
    {
        if (row.TryGetValue(columnName, out var exactValue))
        {
            value = exactValue;
            return true;
        }

        foreach (var pair in row)
        {
            if (string.Equals(pair.Key, columnName, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
