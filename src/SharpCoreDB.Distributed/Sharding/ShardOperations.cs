// <copyright file="ShardOperations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Distributed.Sharding;

/// <summary>
/// Provides basic operations for managing database shards.
/// C# 15: Async methods, collection expressions, modern error handling.
/// </summary>
public sealed class ShardOperations
{
    private const string ShardMetadataTableName = "__shard_metadata";

    private readonly ShardManager _shardManager;
    private readonly ShardRouter _shardRouter;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShardOperations"/> class.
    /// </summary>
    /// <param name="shardManager">The shard manager.</param>
    /// <param name="shardRouter">The shard router.</param>
    public ShardOperations(ShardManager shardManager, ShardRouter shardRouter)
    {
        _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
        _shardRouter = shardRouter ?? throw new ArgumentNullException(nameof(shardRouter));
    }

    /// <summary>
    /// Creates a new shard with the specified configuration.
    /// </summary>
    /// <param name="shardId">Unique shard identifier.</param>
    /// <param name="connectionString">Database connection string.</param>
    /// <param name="isMaster">Whether this is a master shard.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CreateShardAsync(string shardId, string connectionString, bool isMaster = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _shardManager.RegisterShard(shardId, connectionString, isMaster);
        _shardManager.UpdateShardStatus(shardId, ShardStatus.Synchronizing);

        try
        {
            await TestShardConnectionAsync(shardId, cancellationToken).ConfigureAwait(false);
            await InitializeShardAsync(shardId, cancellationToken).ConfigureAwait(false);
            _shardManager.UpdateShardStatus(shardId, ShardStatus.Online);
        }
        catch
        {
            _shardManager.UnregisterShard(shardId);
            throw;
        }
    }

    /// <summary>
    /// Removes a shard from the cluster.
    /// </summary>
    /// <param name="shardId">The shard identifier to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RemoveShardAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);
        cancellationToken.ThrowIfCancellationRequested();

        var shard = GetRequiredShard(shardId);
        var replicas = _shardManager.GetReplicaShards(shardId)
            .OrderByDescending(static replica => replica.Status == ShardStatus.Online)
            .ThenByDescending(static replica => replica.Priority)
            .ThenByDescending(static replica => replica.Capacity)
            .ToList();

        _shardManager.UpdateShardStatus(shardId, ShardStatus.Offline, "Removing shard");

        if (shard.IsMaster && replicas.Count > 0)
        {
            var promotedReplica = replicas[0];
            if (!_shardManager.PromoteShardToMaster(promotedReplica.ShardId))
            {
                throw new InvalidOperationException($"Failed to promote replica '{promotedReplica.ShardId}' while removing master shard '{shardId}'.");
            }
        }
        else if (!shard.IsMaster && shard.MasterShardId is not null)
        {
            _shardManager.ClearReplicaAssignment(shardId);
        }

        _shardManager.UnregisterShard(shardId);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Tests connectivity to a shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task TestShardConnectionAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = GetRequiredShard(shardId);

        try
        {
            await ExecuteWithShardDatabaseAsync(
                shard.ConnectionString,
                static _ => true,
                cancellationToken).ConfigureAwait(false);

            _shardManager.UpdateShardStatus(shardId, ShardStatus.Online);
            _shardManager.UpdateHeartbeat(shardId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _shardManager.UpdateShardStatus(shardId, ShardStatus.Offline, ex.Message);
            throw new ShardConnectionException($"Failed to connect to shard '{shardId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Initializes a shard with necessary schema and data.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeShardAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = GetRequiredShard(shardId);

        await ExecuteWithShardDatabaseAsync(
            shard.ConnectionString,
            db =>
            {
                db.ExecuteSQL($"CREATE TABLE IF NOT EXISTS {EscapeIdentifier(ShardMetadataTableName)} (ShardId TEXT PRIMARY KEY, Role TEXT, MasterShardId TEXT, InitializedAt TEXT, LastUpdatedAt TEXT)");
                db.ExecuteSQL($"DELETE FROM {EscapeIdentifier(ShardMetadataTableName)} WHERE ShardId = {FormatSqlLiteral(shard.ShardId)}");
                db.ExecuteSQL($"INSERT INTO {EscapeIdentifier(ShardMetadataTableName)} (ShardId, Role, MasterShardId, InitializedAt, LastUpdatedAt) VALUES ({FormatSqlLiteral(shard.ShardId)}, {FormatSqlLiteral(shard.IsMaster ? "Master" : "Replica")}, {FormatSqlLiteral(shard.MasterShardId)}, {FormatSqlLiteral(shard.CreatedAt)}, {FormatSqlLiteral(DateTimeOffset.UtcNow)})");
                db.Flush();
                db.ForceSave();
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        _shardManager.UpdateHeartbeat(shardId);
    }

    /// <summary>
    /// Gets the health status of all shards.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with health status.</returns>
    public async Task<ShardHealthStatus> GetShardHealthAsync(CancellationToken cancellationToken = default)
    {
        var allShards = _shardManager.GetAllShards();
        var healthChecks = new List<Task<ShardHealthCheck>>(allShards.Count);

        foreach (var shard in allShards)
        {
            healthChecks.Add(CheckShardHealthAsync(shard.ShardId, cancellationToken));
        }

        var results = await Task.WhenAll(healthChecks).ConfigureAwait(false);

        return new ShardHealthStatus
        {
            TotalShards = allShards.Count,
            HealthyShards = results.Count(r => r.IsHealthy),
            UnhealthyShards = results.Count(r => !r.IsHealthy),
            ShardHealthChecks = [.. results]
        };
    }

    /// <summary>
    /// Checks the health of a specific shard.
    /// </summary>
    /// <param name="shardId">The shard identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation with health check result.</returns>
    public async Task<ShardHealthCheck> CheckShardHealthAsync(string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardId);

        var shard = _shardManager.GetShard(shardId);
        if (shard is null)
        {
            return new ShardHealthCheck
            {
                ShardId = shardId,
                IsHealthy = false,
                Status = "Not Found",
                LastChecked = DateTimeOffset.UtcNow
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var initializationState = await ExecuteWithShardDatabaseAsync(
                shard.ConnectionString,
                db => InspectShardInitialization(db, shardId),
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            _shardManager.UpdateShardStatus(shardId, initializationState.IsInitialized ? ShardStatus.Online : ShardStatus.Synchronizing);
            _shardManager.UpdateHeartbeat(shardId);

            return new ShardHealthCheck
            {
                ShardId = shardId,
                IsHealthy = initializationState.IsInitialized,
                Status = initializationState.Status,
                LastChecked = DateTimeOffset.UtcNow,
                ResponseTime = stopwatch.Elapsed,
                ErrorMessage = initializationState.IsInitialized ? null : initializationState.Status
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ShardHealthCheck
            {
                ShardId = shardId,
                IsHealthy = false,
                Status = $"Unhealthy: {ex.Message}",
                LastChecked = DateTimeOffset.UtcNow,
                ResponseTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Balances data across shards by redistributing based on current load.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RebalanceShardsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var allShards = _shardManager.GetAllShards();
        if (allShards.Count <= 1)
        {
            return;
        }

        var onlineMasters = _shardManager.GetMasterShards()
            .Where(static shard => shard.Status == ShardStatus.Online)
            .OrderByDescending(static shard => shard.Capacity)
            .ThenByDescending(static shard => shard.Priority)
            .ToList();

        if (onlineMasters.Count == 0)
        {
            var fallbackMaster = allShards
                .Where(static shard => shard.Status == ShardStatus.Online)
                .OrderByDescending(static shard => shard.Capacity)
                .ThenByDescending(static shard => shard.Priority)
                .FirstOrDefault();

            if (fallbackMaster is null || !_shardManager.PromoteShardToMaster(fallbackMaster.ShardId))
            {
                throw new InvalidOperationException("Unable to rebalance shards because no online master shard is available.");
            }

            onlineMasters.Add(GetRequiredShard(fallbackMaster.ShardId));
        }

        var replicaCounts = onlineMasters.ToDictionary(
            static master => master.ShardId,
            master => _shardManager.GetReplicaShards(master.ShardId).Count);

        var orphanReplicas = allShards
            .Where(shard =>
                !shard.IsMaster &&
                shard.Status == ShardStatus.Online &&
                (string.IsNullOrWhiteSpace(shard.MasterShardId) ||
                 _shardManager.GetShard(shard.MasterShardId) is not { IsMaster: true, Status: ShardStatus.Online }))
            .OrderByDescending(static shard => shard.Capacity)
            .ThenByDescending(static shard => shard.Priority)
            .ToList();

        foreach (var orphanReplica in orphanReplicas)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetMaster = onlineMasters
                .OrderBy(master => replicaCounts[master.ShardId])
                .ThenByDescending(static master => master.Capacity)
                .ThenByDescending(static master => master.Priority)
                .First();

            if (_shardManager.AssignReplicaToMaster(orphanReplica.ShardId, targetMaster.ShardId))
            {
                replicaCounts[targetMaster.ShardId]++;
                _shardManager.UpdateShardStatus(orphanReplica.ShardId, ShardStatus.Synchronizing);
                await InitializeShardAsync(orphanReplica.ShardId, cancellationToken).ConfigureAwait(false);
                _shardManager.UpdateShardStatus(orphanReplica.ShardId, ShardStatus.Online);
            }
        }
    }

    /// <summary>
    /// Migrates data from one shard to another.
    /// </summary>
    /// <param name="sourceShardId">The source shard identifier.</param>
    /// <param name="targetShardId">The target shard identifier.</param>
    /// <param name="tableName">The table name to migrate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task MigrateTableAsync(string sourceShardId, string targetShardId, string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceShardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetShardId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (string.Equals(sourceShardId, targetShardId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target shards must be different.", nameof(targetShardId));
        }

        var sourceShard = GetRequiredShard(sourceShardId);
        var targetShard = GetRequiredShard(targetShardId);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sourceProvider = CreateServiceProvider();
            using var targetProvider = CreateServiceProvider();

            var sourceOptions = ParseShardConnectionString(sourceShard.ConnectionString);
            var targetOptions = ParseShardConnectionString(targetShard.ConnectionString);
            var sourceFactory = sourceProvider.GetRequiredService<DatabaseFactory>();
            var targetFactory = targetProvider.GetRequiredService<DatabaseFactory>();
            var sourceDb = sourceFactory.Create(sourceOptions.DatabasePath, sourceOptions.Password, sourceOptions.IsReadOnly);
            var targetDb = targetFactory.Create(targetOptions.DatabasePath, targetOptions.Password, targetOptions.IsReadOnly);

            try
            {
                var metadataProvider = sourceDb as IMetadataProvider;
                var columns = metadataProvider?.GetColumns(tableName)
                    .OrderBy(static column => column.Ordinal)
                    .ToArray() ?? [];

                if (columns.Length == 0)
                {
                    throw new InvalidOperationException($"Table '{tableName}' was not found on shard '{sourceShardId}'.");
                }

                var rows = sourceDb.ExecuteQuery($"SELECT * FROM {EscapeIdentifier(tableName)}");
                targetDb.ExecuteSQL(BuildCreateTableStatement(tableName, columns));

                foreach (var row in rows)
                {
                    targetDb.ExecuteSQL(BuildInsertStatement(tableName, columns, row));
                }

                targetDb.Flush();
                targetDb.ForceSave();
            }
            finally
            {
                (sourceDb as IDisposable)?.Dispose();
                (targetDb as IDisposable)?.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);

        _shardManager.UpdateHeartbeat(sourceShardId);
        _shardManager.UpdateHeartbeat(targetShardId);
    }

    /// <summary>
    /// Gets statistics about shard operations.
    /// </summary>
    /// <returns>Shard operation statistics.</returns>
    public ShardOperationStats GetOperationStats()
    {
        return new ShardOperationStats
        {
            TotalShards = _shardManager.ShardCount,
            OnlineShards = _shardManager.OnlineShardCount,
            RoutingStats = _shardRouter.GetRoutingStats()
        };
    }

    private ShardMetadata GetRequiredShard(string shardId)
    {
        return _shardManager.GetShard(shardId)
            ?? throw new InvalidOperationException($"Shard '{shardId}' not found.");
    }

    private static ShardInitializationState InspectShardInitialization(IDatabase db, string shardId)
    {
        if (db is not IMetadataProvider metadataProvider)
        {
            return new ShardInitializationState(false, "Metadata unavailable");
        }

        var metadataTableExists = metadataProvider.GetTables()
            .Any(table => string.Equals(table.Name, ShardMetadataTableName, StringComparison.OrdinalIgnoreCase));
        if (!metadataTableExists)
        {
            return new ShardInitializationState(false, "Not initialized");
        }

        var rows = db.ExecuteQuery(
            $"SELECT * FROM {EscapeIdentifier(ShardMetadataTableName)} WHERE ShardId = {FormatSqlLiteral(shardId)}");

        return rows.Count > 0
            ? new ShardInitializationState(true, "Healthy")
            : new ShardInitializationState(false, "Shard metadata missing");
    }

    private static async Task<TResult> ExecuteWithShardDatabaseAsync<TResult>(
        string connectionString,
        Func<IDatabase, TResult> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(operation);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var serviceProvider = CreateServiceProvider();
            var options = ParseShardConnectionString(connectionString);
            var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
            var db = factory.Create(options.DatabasePath, options.Password, options.IsReadOnly);

            try
            {
                return operation(db);
            }
            finally
            {
                (db as IDisposable)?.Dispose();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        return services.BuildServiceProvider();
    }

    private static ShardConnectionOptions ParseShardConnectionString(string connectionString)
    {
        if (!connectionString.Contains('=', StringComparison.Ordinal))
        {
            return new ShardConnectionOptions(connectionString, "default", false);
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var databasePath = GetConnectionStringValue(builder, "Path", "Data Source", "DataSource", "Filename", "File", "Database");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new InvalidOperationException("Shard connection string must include a database path.");
        }

        var password = GetConnectionStringValue(builder, "Password", "Pwd") ?? "default";
        var isReadOnly = bool.TryParse(GetConnectionStringValue(builder, "ReadOnly"), out var readOnly) && readOnly;

        return new ShardConnectionOptions(databasePath, password, isReadOnly);
    }

    private static string? GetConnectionStringValue(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static string BuildCreateTableStatement(string tableName, IReadOnlyCollection<ColumnInfo> columns)
    {
        var columnDefinitions = string.Join(", ", columns.OrderBy(static column => column.Ordinal).Select(BuildColumnDefinition));
        return $"CREATE TABLE IF NOT EXISTS {EscapeIdentifier(tableName)} ({columnDefinitions})";
    }

    private static string BuildColumnDefinition(ColumnInfo column)
    {
        var nullabilityClause = column.IsNullable ? string.Empty : " NOT NULL";
        var collationClause = string.IsNullOrWhiteSpace(column.Collation)
            ? string.Empty
            : $" COLLATE {column.Collation}";
        return $"{EscapeIdentifier(column.Name)} {NormalizeColumnType(column.DataType)}{nullabilityClause}{collationClause}";
    }

    private static string NormalizeColumnType(string? dataType)
    {
        return string.IsNullOrWhiteSpace(dataType) ? "TEXT" : dataType.Trim().ToUpperInvariant();
    }

    private static string BuildInsertStatement(string tableName, IReadOnlyList<ColumnInfo> columns, IReadOnlyDictionary<string, object> row)
    {
        var orderedColumns = columns.OrderBy(static column => column.Ordinal).ToArray();
        var columnList = string.Join(", ", orderedColumns.Select(column => EscapeIdentifier(column.Name)));
        var values = string.Join(", ", orderedColumns.Select(column =>
            row.TryGetValue(column.Name, out var value) ? FormatSqlLiteral(value) : "NULL"));
        return $"INSERT INTO {EscapeIdentifier(tableName)} ({columnList}) VALUES ({values})";
    }

    private static string EscapeIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (identifier[0] != '_' && !char.IsLetter(identifier[0]))
        {
            throw new InvalidOperationException($"Identifier '{identifier}' is not supported by SharpCoreDB shard operations.");
        }

        for (var i = 1; i < identifier.Length; i++)
        {
            var character = identifier[i];
            if (character != '_' && !char.IsLetterOrDigit(character))
            {
                throw new InvalidOperationException($"Identifier '{identifier}' is not supported by SharpCoreDB shard operations.");
            }
        }

        return identifier;
    }

    private static string FormatSqlLiteral(object? value)
    {
        return value switch
        {
            null => "NULL",
            string text => $"'{text.Replace("'", "''")}'",
            char character => $"'{character.ToString().Replace("'", "''")}'",
            bool boolean => boolean ? "1" : "0",
            DateTime dateTime => $"'{dateTime.ToUniversalTime():O}'",
            DateTimeOffset dateTimeOffset => $"'{dateTimeOffset.ToUniversalTime():O}'",
            Guid guid => $"'{guid:D}'",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => $"'{value.ToString()?.Replace("'", "''")}'"
        };
    }

    private sealed record ShardConnectionOptions(string DatabasePath, string Password, bool IsReadOnly);

    private sealed record ShardInitializationState(bool IsInitialized, string Status);
}

/// <summary>
/// Represents the health status of all shards.
/// </summary>
public class ShardHealthStatus
{
    /// <summary>Gets the total number of shards.</summary>
    public int TotalShards { get; init; }

    /// <summary>Gets the number of healthy shards.</summary>
    public int HealthyShards { get; init; }

    /// <summary>Gets the number of unhealthy shards.</summary>
    public int UnhealthyShards { get; init; }

    /// <summary>Gets the individual shard health checks.</summary>
    public IReadOnlyCollection<ShardHealthCheck> ShardHealthChecks { get; init; } = [];
}

/// <summary>
/// Represents the health check result for a single shard.
/// </summary>
public class ShardHealthCheck
{
    /// <summary>Gets the shard identifier.</summary>
    public string ShardId { get; init; } = string.Empty;

    /// <summary>Gets whether the shard is healthy.</summary>
    public bool IsHealthy { get; init; }

    /// <summary>Gets the health status description.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the timestamp of the last health check.</summary>
    public DateTimeOffset LastChecked { get; init; }

    /// <summary>Gets the response time for the health check.</summary>
    public TimeSpan? ResponseTime { get; init; }

    /// <summary>Gets the error message if the shard is unhealthy.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Statistics for shard operations.
/// </summary>
public class ShardOperationStats
{
    /// <summary>Gets the total number of configured shards.</summary>
    public int TotalShards { get; init; }

    /// <summary>Gets the number of online shards.</summary>
    public int OnlineShards { get; init; }

    /// <summary>Gets the routing statistics.</summary>
    public ShardRoutingStats RoutingStats { get; init; } = new();
}

/// <summary>
/// Exception thrown when shard connection fails.
/// </summary>
public class ShardConnectionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShardConnectionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ShardConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
