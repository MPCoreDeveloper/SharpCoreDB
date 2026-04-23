using SharpCoreDB.Client;
using SharpCoreDB.Interfaces;
using SharpCoreDB.WebViewer.Models;

namespace SharpCoreDB.WebViewer.Services;

/// <summary>
/// Loads schema metadata for the active SharpCoreDB web viewer session.
/// </summary>
public sealed class MetadataService(IViewerConnectionService connectionService, DatabaseFactory databaseFactory) : IMetadataService
{
    private readonly IViewerConnectionService _connectionService = connectionService;
    private readonly DatabaseFactory _databaseFactory = databaseFactory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetTableNamesAsync(CancellationToken cancellationToken = default)
    {
        var session = _connectionService.GetCurrentSession()
            ?? throw new InvalidOperationException("No active database connection is available.");

        return session.ConnectionMode switch
        {
            ViewerConnectionMode.Server => await GetServerTableNamesAsync(session, cancellationToken).ConfigureAwait(false),
            _ => await GetLocalTableNamesAsync(session, cancellationToken).ConfigureAwait(false)
        };
    }

    /// <inheritdoc />
    public async Task<TableMetadata?> GetTableMetadataAsync(string tableName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var session = _connectionService.GetCurrentSession()
            ?? throw new InvalidOperationException("No active database connection is available.");

        return session.ConnectionMode switch
        {
            ViewerConnectionMode.Server => await GetServerTableMetadataAsync(session, tableName, cancellationToken).ConfigureAwait(false),
            _ => await GetLocalTableMetadataAsync(session, tableName, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<IReadOnlyList<string>> GetLocalTableNamesAsync(ViewerSessionState session, CancellationToken cancellationToken)
    {
        await using var database = await OpenLocalDatabaseAsync(session, cancellationToken).ConfigureAwait(false);
        if (database is not IMetadataProvider metadataProvider)
        {
            throw new InvalidOperationException("The active database does not expose schema metadata.");
        }

        return [.. metadataProvider.GetTables()
            .Select(static table => table.Name)
            .OrderBy(static tableName => tableName, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<TableMetadata?> GetLocalTableMetadataAsync(ViewerSessionState session, string tableName, CancellationToken cancellationToken)
    {
        await using var database = await OpenLocalDatabaseAsync(session, cancellationToken).ConfigureAwait(false);
        if (database is not IMetadataProvider metadataProvider)
        {
            throw new InvalidOperationException("The active database does not expose schema metadata.");
        }

        if (!database.TryGetTable(tableName, out _))
        {
            return null;
        }

        var columns = metadataProvider.GetColumns(tableName)
            .Select(static column => new TableColumnMetadata
            {
                Name = column.Name,
                DataType = column.DataType,
                IsNullable = column.IsNullable,
                IsHidden = column.IsHidden
            })
            .ToArray();

        var escapedTableName = tableName.Replace("'", "''", StringComparison.Ordinal);
        var indexes = TryGetLocalObjectNames(database, $"SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='{escapedTableName}' ORDER BY name");
        var triggers = TryGetLocalObjectNames(database, $"SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name='{escapedTableName}' ORDER BY name");

        return new TableMetadata
        {
            Name = tableName,
            Columns = columns,
            Indexes = indexes,
            Triggers = triggers
        };
    }

    private async Task<IReadOnlyList<string>> GetServerTableNamesAsync(ViewerSessionState session, CancellationToken cancellationToken)
    {
        var sql = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var result = await ExecuteServerQueryAsync(session, sql, cancellationToken).ConfigureAwait(false);
        return [.. result.Select(static row => row.Count == 0 ? string.Empty : row[0] ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<TableMetadata?> GetServerTableMetadataAsync(ViewerSessionState session, string tableName, CancellationToken cancellationToken)
    {
        var escapedTableName = tableName.Replace("'", "''", StringComparison.Ordinal);

        var columnRows = await ExecuteServerQueryAsync(
            session,
            $"PRAGMA table_info('{escapedTableName}')",
            cancellationToken).ConfigureAwait(false);

        if (columnRows.Count == 0)
        {
            return null;
        }

        var columns = columnRows
            .Select(static row => new TableColumnMetadata
            {
                Name = row.ElementAtOrDefault(1) ?? string.Empty,
                DataType = row.ElementAtOrDefault(2) ?? "TEXT",
                IsNullable = row.ElementAtOrDefault(3) != "1",
                IsHidden = false
            })
            .ToArray();

        var indexRows = await ExecuteServerQueryAsync(
            session,
            $"SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='{escapedTableName}' ORDER BY name",
            cancellationToken).ConfigureAwait(false);

        var triggerRows = await ExecuteServerQueryAsync(
            session,
            $"SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name='{escapedTableName}' ORDER BY name",
            cancellationToken).ConfigureAwait(false);

        return new TableMetadata
        {
            Name = tableName,
            Columns = columns,
            Indexes = [.. indexRows.Select(static row => row.Count == 0 ? string.Empty : row[0] ?? string.Empty).Where(static name => !string.IsNullOrWhiteSpace(name))],
            Triggers = [.. triggerRows.Select(static row => row.Count == 0 ? string.Empty : row[0] ?? string.Empty).Where(static name => !string.IsNullOrWhiteSpace(name))]
        };
    }

    private async Task<IReadOnlyList<IReadOnlyList<string?>>> ExecuteServerQueryAsync(
        ViewerSessionState session,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = new SharpCoreDB.Client.SharpCoreDBConnection(_connectionService.BuildServerConnectionString(session));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var rows = new List<IReadOnlyList<string?>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new string?[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                values[index] = value is DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }

            rows.Add(values);
        }

        return rows;
    }

    private async ValueTask<IDatabase> OpenLocalDatabaseAsync(ViewerSessionState session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (session.ConnectionMode != ViewerConnectionMode.Local)
            {
                throw new InvalidOperationException("The active session is not configured for local connection mode.");
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(session.LocalDatabasePath);

            var options = session.LocalStorageMode switch
            {
                DatabaseStorageMode.SingleFile => DatabaseOptions.CreateSingleFileDefault(),
                _ => DatabaseOptions.CreateDirectoryDefault()
            };

            options.IsReadOnly = session.LocalReadOnly;
            return _databaseFactory.CreateWithOptions(session.LocalDatabasePath, session.Password, options);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> TryGetLocalObjectNames(IDatabase database, string sql)
    {
        try
        {
            return [.. database.ExecuteQuery(sql, [])
                .SelectMany(static row => row.Values.Take(1))
                .Select(static value => value?.ToString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)];
        }
        catch
        {
            return [];
        }
    }
}
