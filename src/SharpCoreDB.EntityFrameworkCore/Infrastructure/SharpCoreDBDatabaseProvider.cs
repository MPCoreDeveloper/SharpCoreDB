using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Update;
using SharpCoreDB.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Database provider implementation for SharpCoreDB.
/// Follows the standard EF Core relational provider pattern (same as SQLite).
/// </summary>
public class SharpCoreDBDatabaseProvider : RelationalDatabase
{
    private readonly IRelationalConnection _connection;

    public SharpCoreDBDatabaseProvider(
        DatabaseDependencies databaseDependencies,
        RelationalDatabaseDependencies relationalDatabaseDependencies,
        IRelationalConnection connection)
        : base(databaseDependencies, relationalDatabaseDependencies)
    {
        _connection = connection;
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
        if (_connection.DbConnection.State != System.Data.ConnectionState.Open)
            _connection.Open();

        foreach (var entry in entries)
            ExecuteUpdateEntry(entry);

        // ✅ FIX: Only flush when NOT inside an explicit transaction.
        // Inside a transaction, defer flush so Rollback() can cancel unflushed writes.
        // After commit, the transaction's own Commit() calls Flush().
        if (_connection.DbConnection is SharpCoreDBConnection conn)
        {
            var isInTransaction = conn.DbInstance?.IsBatchUpdateActive ?? false;
            if (!isInTransaction)
            {
                conn.DbInstance?.Flush();
            }
        }

        return entries.Count;
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = default)
    {
        if (_connection.DbConnection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(cancellationToken);

        System.IO.File.AppendAllText(@"D:\sfd_batch.log", 
            $"[{DateTime.Now:HH:mm:ss.fff}] SaveChangesAsync: Processing {entries.Count} entries\n");

        foreach (var entry in entries)
            ExecuteUpdateEntry(entry);

        // ✅ FIX: Only flush when NOT inside an explicit transaction.
        // Inside a transaction, defer flush so Rollback() can cancel unflushed writes.
        // After commit, the transaction's own Commit() calls Flush().
        if (_connection.DbConnection is SharpCoreDBConnection conn)
        {
            var isInTransaction = conn.DbInstance?.IsBatchUpdateActive ?? false;
            System.IO.File.AppendAllText(@"D:\sfd_batch.log", 
                $"[{DateTime.Now:HH:mm:ss.fff}] SaveChangesAsync: IsInTransaction = {isInTransaction}\n");
            if (!isInTransaction)
            {
                conn.DbInstance?.Flush();
            }
        }

        return entries.Count;
    }

    private void ExecuteUpdateEntry(IUpdateEntry entry)
    {
        if (_connection.DbConnection is not SharpCoreDBConnection conn || conn.DbInstance is null)
            return;

        var tableName = entry.EntityType.GetTableName() ?? entry.EntityType.ClrType.Name;
        var state = entry.EntityState;

        if (state == EntityState.Added)
        {
            var cols = new List<string>();
            var vals = new List<string>();
            IProperty? skippedAutoKeyProp = null;

            foreach (var prop in entry.EntityType.GetProperties())
            {
                var col = prop.GetColumnName();
                if (prop.ValueGenerated == ValueGenerated.OnAdd &&
                    prop.IsPrimaryKey() &&
                    IsDefaultValue(entry.GetCurrentValue(prop), prop.ClrType))
                {
                    skippedAutoKeyProp = prop;
                    continue;
                }
                var val = entry.GetCurrentValue(prop);
                // ✅ FIX: Remove quotes from column names - SharpCoreDB parser doesn't handle them
                cols.Add(col);
                vals.Add(FormatSqlValue(val));
            }
            if (cols.Count == 0) return;

            // ✅ FIX: Remove quotes from table name - SharpCoreDB parser doesn't handle them
            var sql = $"INSERT INTO {tableName} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)})";

            // DEBUG: Log INSERT SQL
            try
            {
                System.IO.File.AppendAllText("D:\\ef_dml_provider.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] INSERT: {sql}\n");
            }
            catch { /* Intentionally empty */ }

            conn.DbInstance.ExecuteSQL(sql);

            if (skippedAutoKeyProp != null)
            {
                var rowId = conn.DbInstance.GetLastInsertRowId();
                if (rowId <= 0)
                {
                    try
                    {
                        var rows = conn.DbInstance.ExecuteQuery("SELECT last_insert_rowid() AS rowid");
                        if (rows.Count > 0 && rows[0].TryGetValue("rowid", out var val) && val != null)
                            rowId = Convert.ToInt64(val);
                    }
                    catch { /* Intentionally empty */ }
                }
                if (rowId > 0)
                    entry.SetStoreGeneratedValue(skippedAutoKeyProp, Convert.ChangeType(rowId, skippedAutoKeyProp.ClrType));
            }
        }
        else if (state == EntityState.Modified)
        {
            var setClauses = new List<string>();
            var whereClauses = new List<string>();
            foreach (var prop in entry.EntityType.GetProperties())
            {
                // ✅ FIX: Remove quotes from column names
                var col = prop.GetColumnName();
                if (prop.IsPrimaryKey())
                    whereClauses.Add($"{col} = {FormatSqlValue(entry.GetCurrentValue(prop))}");
                else if (entry.IsModified(prop))
                    setClauses.Add($"{col} = {FormatSqlValue(entry.GetCurrentValue(prop))}");
            }
            if (setClauses.Count == 0 || whereClauses.Count == 0) return;

            // ✅ FIX: Remove quotes from table name
            var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

            // DEBUG: Log UPDATE SQL
            try
            {
                System.IO.File.AppendAllText("D:\\ef_dml_provider.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] UPDATE: {sql}\n");
            }
            catch { /* Intentionally empty */ }

            conn.DbInstance.ExecuteSQL(sql);
        }
        else if (state == EntityState.Deleted)
        {
            var whereClauses = new List<string>();
            foreach (var prop in entry.EntityType.FindPrimaryKey()?.Properties ?? [])
            {
                // ✅ FIX: Remove quotes from column names
                whereClauses.Add($"{prop.GetColumnName()} = {FormatSqlValue(entry.GetCurrentValue(prop))}");
            }
            if (whereClauses.Count == 0) return;

            // ✅ FIX: Remove quotes from table name
            var sql = $"DELETE FROM {tableName} WHERE {string.Join(" AND ", whereClauses)}";

            // DEBUG: Log DELETE SQL
            try
            {
                System.IO.File.AppendAllText("D:\\ef_dml_provider.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] DELETE: {sql}\n");
            }
            catch { /* Intentionally empty */ }

            conn.DbInstance.ExecuteSQL(sql);
        }
    }

    private static bool IsDefaultValue(object? value, Type type)
    {
        if (value is null) return true;
        if (type == typeof(int) || type == typeof(long))
            return Convert.ToInt64(value) <= 0;
        if (type == typeof(Guid)) return (Guid)value == Guid.Empty;
        return false;
    }

    private static string FormatSqlValue(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "1" : "0",
        string s => $"'{s.Replace("'", "''")}'",
        DateTime dt => $"'{dt:O}'",
        Guid g => $"'{g}'",
        _ => value.ToString()
    };
}

