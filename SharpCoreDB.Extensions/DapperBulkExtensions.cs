using Dapper;
using SharpCoreDB.Interfaces;
using System.Data;
using System.Text;

namespace SharpCoreDB.Extensions;

/// <summary>
/// Provides bulk operations extensions for Dapper with SharpCoreDB.
/// </summary>
public static class DapperBulkExtensions
{
    /// <summary>
    /// Performs a bulk insert operation.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">The target table name.</param>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="batchSize">The batch size for bulk operations (default: 1000).</param>
    /// <returns>The number of rows affected.</returns>
    public static int BulkInsert<T>(this IDatabase database, string tableName, IEnumerable<T> entities, int batchSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(entities);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return 0;

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead)
            .ToList();

        if (properties.Count == 0)
            throw new InvalidOperationException($"Type {typeof(T).Name} has no readable properties");

        var totalInserted = 0;
        var batches = entityList.Chunk(batchSize);

        foreach (var batch in batches)
        {
            var sql = BuildBulkInsertSql(tableName, properties, batch);
            database.ExecuteSQL(sql);
            totalInserted += batch.Length;
        }

        return totalInserted;
    }

    /// <summary>
    /// Performs a bulk insert operation asynchronously.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">The target table name.</param>
    /// <param name="entities">The entities to insert.</param>
    /// <param name="batchSize">The batch size for bulk operations (default: 1000).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of rows affected.</returns>
    public static async Task<int> BulkInsertAsync<T>(
        this IDatabase database, 
        string tableName, 
        IEnumerable<T> entities, 
        int batchSize = 1000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(entities);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return 0;

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead)
            .ToList();

        if (properties.Count == 0)
            throw new InvalidOperationException($"Type {typeof(T).Name} has no readable properties");

        var totalInserted = 0;
        var batches = entityList.Chunk(batchSize);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var sql = BuildBulkInsertSql(tableName, properties, batch);
            await database.ExecuteSQLAsync(sql, cancellationToken);
            totalInserted += batch.Length;
        }

        return totalInserted;
    }

    /// <summary>
    /// Performs a bulk update operation.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">The target table name.</param>
    /// <param name="entities">The entities to update.</param>
    /// <param name="keyProperty">The name of the key property.</param>
    /// <param name="batchSize">The batch size for bulk operations (default: 1000).</param>
    /// <returns>The number of rows affected.</returns>
    public static int BulkUpdate<T>(
        this IDatabase database, 
        string tableName, 
        IEnumerable<T> entities, 
        string keyProperty,
        int batchSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyProperty);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return 0;

        var totalUpdated = 0;
        var batches = entityList.Chunk(batchSize);

        foreach (var batch in batches)
        {
            foreach (var entity in batch)
            {
                var sql = BuildUpdateSql(tableName, entity, keyProperty);
                database.ExecuteSQL(sql);
                totalUpdated++;
            }
        }

        return totalUpdated;
    }

    /// <summary>
    /// Performs a bulk delete operation.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <param name="database">The database instance.</param>
    /// <param name="tableName">The target table name.</param>
    /// <param name="keys">The keys of entities to delete.</param>
    /// <param name="keyColumn">The name of the key column.</param>
    /// <param name="batchSize">The batch size for bulk operations (default: 1000).</param>
    /// <returns>The number of rows affected.</returns>
    public static int BulkDelete<TKey>(
        this IDatabase database,
        string tableName,
        IEnumerable<TKey> keys,
        string keyColumn = "Id",
        int batchSize = 1000)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(keys);

        var keyList = keys.ToList();
        if (keyList.Count == 0)
            return 0;

        var totalDeleted = 0;
        var batches = keyList.Chunk(batchSize);

        foreach (var batch in batches)
        {
            var sql = BuildBulkDeleteSql(tableName, batch, keyColumn);
            database.ExecuteSQL(sql);
            totalDeleted += batch.Length;
        }

        return totalDeleted;
    }

    /// <summary>
    /// Performs an upsert (insert or update) operation.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="connection">The database connection.</param>
    /// <param name="tableName">The target table name.</param>
    /// <param name="entities">The entities to upsert.</param>
    /// <param name="keyProperties">The key property names.</param>
    /// <param name="transaction">Optional transaction.</param>
    /// <returns>The number of rows affected.</returns>
    public static int BulkUpsert<T>(
        this IDbConnection connection,
        string tableName,
        IEnumerable<T> entities,
        string[] keyProperties,
        IDbTransaction? transaction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(keyProperties);

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return 0;

        var totalAffected = 0;

        foreach (var entity in entityList)
        {
            var sql = BuildUpsertSql(tableName, entity, keyProperties);
            totalAffected += connection.Execute(sql, entity, transaction);
        }

        return totalAffected;
    }

    private static string BuildBulkInsertSql<T>(string tableName, IList<System.Reflection.PropertyInfo> properties, T[] entities)
    {
        var sb = new StringBuilder();
        var columns = string.Join(", ", properties.Select(p => p.Name));

        sb.AppendLine($"INSERT INTO {tableName} ({columns}) VALUES");

        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            var values = properties.Select(p =>
            {
                var value = p.GetValue(entity);
                return FormatValue(value);
            });

            sb.Append($"  ({string.Join(", ", values)})");
            
            if (i < entities.Length - 1)
                sb.AppendLine(",");
            else
                sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildUpdateSql<T>(string tableName, T entity, string keyProperty)
    {
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.Name != keyProperty)
            .ToList();

        var keyProp = typeof(T).GetProperty(keyProperty)
            ?? throw new ArgumentException($"Property {keyProperty} not found on type {typeof(T).Name}");

        var setClause = string.Join(", ", properties.Select(p =>
            $"{p.Name} = {FormatValue(p.GetValue(entity))}"));

        var keyValue = FormatValue(keyProp.GetValue(entity));

        return $"UPDATE {tableName} SET {setClause} WHERE {keyProperty} = {keyValue}";
    }

    private static string BuildBulkDeleteSql<TKey>(string tableName, TKey[] keys, string keyColumn)
    {
        var keyList = string.Join(", ", keys.Select(k => FormatValue(k)));
        return $"DELETE FROM {tableName} WHERE {keyColumn} IN ({keyList})";
    }

    private static string BuildUpsertSql<T>(string tableName, T entity, string[] keyProperties)
    {
        var properties = typeof(T).GetProperties().Where(p => p.CanRead).ToList();
        var columns = string.Join(", ", properties.Select(p => p.Name));
        var values = string.Join(", ", properties.Select(p => $"@{p.Name}"));
        
        var updateColumns = properties
            .Where(p => !keyProperties.Contains(p.Name))
            .Select(p => $"{p.Name} = @{p.Name}");
        
        var whereClause = string.Join(" AND ", keyProperties.Select(k => $"{k} = @{k}"));

        // Simplified upsert using separate UPDATE/INSERT
        return $@"
            UPDATE {tableName} 
            SET {string.Join(", ", updateColumns)}
            WHERE {whereClause};
            
            INSERT INTO {tableName} ({columns})
            SELECT {values}
            WHERE NOT EXISTS (SELECT 1 FROM {tableName} WHERE {whereClause})";
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            bool b => b ? "1" : "0",
            byte[] bytes => $"'{Convert.ToBase64String(bytes)}'",
            _ => value.ToString() ?? "NULL"
        };
    }
}
