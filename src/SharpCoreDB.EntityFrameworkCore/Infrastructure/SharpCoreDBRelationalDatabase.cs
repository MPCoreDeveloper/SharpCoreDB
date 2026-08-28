using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using SharpCoreDB.EntityFrameworkCore.Storage;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// The actual database implementation for SharpCoreDB.
/// Contains SaveChanges logic, key propagation, and DML execution.
/// Follows the same pattern as SqliteRelationalDatabase / RelationalDatabase.
/// </summary>
public class SharpCoreDBRelationalDatabase : RelationalDatabase
{
    private readonly IRelationalConnection _connection;

    public SharpCoreDBRelationalDatabase(
        DatabaseDependencies dependencies,
        RelationalDatabaseDependencies relationalDependencies,
        IRelationalConnection connection)
        : base(dependencies, relationalDependencies)
    {
        _connection = connection;
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
        if (_connection.DbConnection.State != System.Data.ConnectionState.Open)
            _connection.Open();

        foreach (var entry in entries)
            ExecuteUpdateEntry(entry);

        if (_connection.DbConnection is SharpCoreDBConnection c && !(c.DbInstance?.IsBatchUpdateActive ?? false))
            c.DbInstance?.Flush();

        return entries.Count;
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = default)
    {
        if (_connection.DbConnection.State != System.Data.ConnectionState.Open)
            await _connection.OpenAsync(cancellationToken);

        foreach (var entry in entries)
            ExecuteUpdateEntry(entry);

        if (_connection.DbConnection is SharpCoreDBConnection c && !(c.DbInstance?.IsBatchUpdateActive ?? false))
            c.DbInstance?.Flush();

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
                System.IO.File.AppendAllText("D:\\ef_insert.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] INSERT SQL: {sql}\n");
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
                    catch
                    {
                        // Intentionally empty: best-effort last-insert-rowid lookup.
                    }
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
                // ✅ FIX: Remove quotes from column names - SharpCoreDB parser doesn't handle them
                var col = prop.GetColumnName();
                if (prop.IsPrimaryKey())
                    whereClauses.Add($"{col} = {FormatSqlValue(entry.GetCurrentValue(prop))}");
                else if (entry.IsModified(prop))
                    setClauses.Add($"{col} = {FormatSqlValue(entry.GetCurrentValue(prop))}");
            }
            if (setClauses.Count == 0 || whereClauses.Count == 0) return;

            // ✅ FIX: Remove quotes from table name - SharpCoreDB parser doesn't handle them
            var sql = $"UPDATE {tableName} SET {string.Join(", ", setClauses)} WHERE {string.Join(" AND ", whereClauses)}";

            // DEBUG: Log UPDATE SQL
            try
            {
                System.IO.File.AppendAllText("D:\\ef_update.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] UPDATE SQL: {sql}\n");
            }
            catch { /* Intentionally empty */ }

            conn.DbInstance.ExecuteSQL(sql);
        }
        else if (state == EntityState.Deleted)
        {
            var whereClauses = new List<string>();
            foreach (var prop in entry.EntityType.FindPrimaryKey()?.Properties ?? [])
            {
                // ✅ FIX: Remove quotes from column names - SharpCoreDB parser doesn't handle them
                var colName = prop.GetColumnName();
                whereClauses.Add($"{colName} = {FormatSqlValue(entry.GetCurrentValue(prop))}");
            }
            if (whereClauses.Count == 0) return;

            // ✅ FIX: Remove quotes from table name - SharpCoreDB parser doesn't handle them
            var sql = $"DELETE FROM {tableName} WHERE {string.Join(" AND ", whereClauses)}";

            // DEBUG: Log DELETE SQL
            try
            {
                System.IO.File.AppendAllText("D:\\ef_delete.log",
                    $"[{DateTime.Now:HH:mm:ss.fff}] DELETE SQL: {sql}\n");
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
        _ => value.ToString()!
    };
}
