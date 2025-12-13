// <copyright file="SqlParser.DDL.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

/// <summary>
/// SqlParser partial class containing DDL (Data Definition Language) operations:
/// CREATE TABLE, CREATE INDEX, DROP TABLE, DROP INDEX, ALTER TABLE.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Executes CREATE TABLE statement.
    /// </summary>
    private void ExecuteCreateTable(string sql, string[] parts, IWAL? wal)
    {
        if (this.isReadOnly)
        {
            throw new InvalidOperationException("Cannot create table in readonly mode");
        }

        var tableName = parts[2];
        var colsStart = sql.IndexOf('(');
        var colsEnd = sql.LastIndexOf(')');
        var colsStr = sql.Substring(colsStart + 1, colsEnd - colsStart - 1);
        List<string> colDefs = colsStr.Split(',').Select(c => c.Trim()).ToList();
        var columns = new List<string>();
        var columnTypes = new List<DataType>();
        var isAuto = new List<bool>();
        var primaryKeyIndex = -1;
        
        for (int i = 0; i < colDefs.Count; i++)
        {
            var def = colDefs[i];
            var partsDef = def.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var colName = partsDef[0];
            var typeStr = partsDef[1].ToUpper();
            var isPrimary = def.Contains(SqlConstants.PRIMARYKEY);
            var isAutoGen = def.Contains("AUTO");
            columns.Add(colName);
            columnTypes.Add(typeStr switch
            {
                "INTEGER" => DataType.Integer,
                "TEXT" => DataType.String,
                "REAL" => DataType.Real,
                "BLOB" => DataType.Blob,
                "BOOLEAN" => DataType.Boolean,
                "DATETIME" => DataType.DateTime,
                "LONG" => DataType.Long,
                "DECIMAL" => DataType.Decimal,
                "ULID" => DataType.Ulid,
                "GUID" => DataType.Guid,
                _ => DataType.String,
            });
            isAuto.Add(isAutoGen);
            if (isPrimary)
            {
                primaryKeyIndex = i;
            }
        }

        // FIXED: Properly dispose existing table to release file handles
        if (this.tables.TryGetValue(tableName, out var existingTable))
        {
            this.tables.Remove(tableName);
            
            // Dispose the table to release all resources
            if (existingTable is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            // Force garbage collection to release file handles
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            // Give OS time to close file handles
            System.Threading.Thread.Sleep(100);
        }

        var dataFilePath = Path.Combine(this.dbPath, tableName + PersistenceConstants.TableFileExtension);
        
        // FIXED: Use FileMode.Create which truncates existing files atomically
        // This ensures we always start with a truly empty file
        using (var fs = File.Create(dataFilePath))
        {
            // File created and immediately flushed to disk
        }
        
        var table = new Table(this.storage, this.isReadOnly)
        {
            Name = tableName,
            Columns = columns,
            ColumnTypes = columnTypes,
            IsAuto = isAuto,
            PrimaryKeyIndex = primaryKeyIndex,
            DataFile = dataFilePath,
        };
        
        this.tables[tableName] = table;
        wal?.Log(sql);
        
        // Auto-create hash index on primary key for faster lookups
        if (primaryKeyIndex >= 0)
        {
            table.CreateHashIndex(table.Columns[primaryKeyIndex]);
        }
        
        // Auto-create hash indexes on all columns for faster WHERE lookups
        for (int i = 0; i < columns.Count; i++)
        {
            if (i != primaryKeyIndex)
            {
                table.CreateHashIndex(columns[i]);
            }
        }
    }

    /// <summary>
    /// Executes CREATE INDEX statement.
    /// </summary>
    private void ExecuteCreateIndex(string sql, string[] parts, IWAL? wal)
    {
        if (this.isReadOnly)
        {
            throw new InvalidOperationException("Cannot create index in readonly mode");
        }

        // CREATE INDEX idx_name ON table_name (column_name)
        // or CREATE UNIQUE INDEX idx_name ON table_name (column_name)
        var onIdx = Array.IndexOf(parts.Select(p => p.ToUpper()).ToArray(), "ON");
        if (onIdx < 0)
        {
            throw new InvalidOperationException("CREATE INDEX requires ON clause");
        }

        // Extract table name - handle case where parenthesis is attached (e.g., "users(email)")
        var tableNameRaw = parts[onIdx + 1];
        var parenIdx = tableNameRaw.IndexOf('(');
        var tableName = parenIdx >= 0 ? tableNameRaw.Substring(0, parenIdx) : tableNameRaw;
        
        if (!this.tables.ContainsKey(tableName))
        {
            throw new InvalidOperationException($"Table {tableName} does not exist");
        }

        // Extract column name from parentheses
        var columnStart = sql.IndexOf('(');
        var columnEnd = sql.IndexOf(')');
        if (columnStart < 0 || columnEnd < 0)
        {
            throw new InvalidOperationException("CREATE INDEX requires column name in parentheses");
        }

        var columnName = sql.Substring(columnStart + 1, columnEnd - columnStart - 1).Trim();

        // Create hash index on the table (registers it for lazy loading)
        // Index will be built automatically when first SELECT query uses it
        this.tables[tableName].CreateHashIndex(columnName);
        
        wal?.Log(sql);
    }

    /// <summary>
    /// Executes DROP TABLE statement.
    /// </summary>
    private void ExecuteDropTable(string[] parts, string sql, IWAL? wal)
    {
        if (this.isReadOnly)
            throw new InvalidOperationException("Cannot drop table in readonly mode");
        
        bool ifExists = false;
        string tableName;
        
        if (parts.Length >= 5 && parts[2].ToUpper() == "IF" && parts[3].ToUpper() == "EXISTS")
        {
            ifExists = true;
            tableName = parts[4];
        }
        else
        {
            tableName = parts[2];
        }
        
        if (!this.tables.ContainsKey(tableName))
        {
            if (ifExists)
                return;
            throw new InvalidOperationException($"Table {tableName} does not exist");
        }
        
        var table = this.tables[tableName];
        var dataFile = table.DataFile;
        
        // FIXED: Remove from dictionary and dispose FIRST to release file handles
        this.tables.Remove(tableName);
        
        // Dispose the table to release all resources including file handles
        if (table is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        // Force garbage collection to release file handles immediately
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        // FIXED: Robust file deletion with proper retry and verification
        if (File.Exists(dataFile))
        {
            bool deleted = false;
            
            // Try deletion with exponential backoff (max 5 attempts over ~1.5 seconds)
            for (int attempt = 0; attempt < 5 && !deleted; attempt++)
            {
                try
                {
                    // Verify file is not locked by trying to open it exclusively first
                    using (var testStream = new FileStream(dataFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // If we can open exclusively, we can delete
                    }
                    
                    File.Delete(dataFile);
                    deleted = true;
                }
                catch (IOException) when (attempt < 4)
                {
                    // File is locked or in use - wait with exponential backoff
                    int delayMs = 50 * (1 << attempt); // 50, 100, 200, 400, 800 ms
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    // Permission issue - wait and retry
                    System.Threading.Thread.Sleep(50 * (attempt + 1));
                }
            }
            
            // FIXED: Verify deletion succeeded
            if (File.Exists(dataFile))
            {
                // File still exists after all retry attempts
                // This is a serious error - don't allow silent failure
                throw new InvalidOperationException(
                    $"Failed to delete data file '{dataFile}' after DROP TABLE. " +
                    $"The file may be locked by another process. " +
                    $"Close all connections to this database and retry.");
            }
        }
        
        wal?.Log(sql);
    }

    /// <summary>
    /// Executes DROP INDEX statement.
    /// </summary>
    private void ExecuteDropIndex(string[] parts, string sql, IWAL? wal)
    {
        bool ifExists = false;
        string indexName;
        
        if (parts.Length >= 5 && parts[2].ToUpper() == "IF" && parts[3].ToUpper() == "EXISTS")
        {
            ifExists = true;
            indexName = parts[4];
        }
        else
        {
            indexName = parts[2];
        }
        
        bool found = false;
        foreach (var table in this.tables.Values.Where(t => t.HasHashIndex(indexName)))
        {
            table.RemoveHashIndex(indexName);
            found = true;
            break;
        }
        
        if (!found && !ifExists)
            throw new InvalidOperationException($"Index {indexName} does not exist");
        
        if (found)
        {
            wal?.Log(sql);
        }
    }

    /// <summary>
    /// Executes ALTER TABLE statement.
    /// </summary>
    private void ExecuteAlterTable(string[] parts, string sql, IWAL? wal)
    {
        var tableName = parts[2];
        if (!this.tables.ContainsKey(tableName))
            throw new InvalidOperationException($"Table {tableName} does not exist");
        
        var table = this.tables[tableName];
        
        if (parts.Length > 4 && parts[3].ToUpper() == "RENAME" && parts[4].ToUpper() == "TO")
        {
            var newName = parts[5];
            
            if (this.tables.ContainsKey(newName))
                throw new InvalidOperationException($"Table {newName} already exists");
            
            // FIXED: Store old file path before modifying table
            var oldFile = table.DataFile;
            var newFile = Path.Combine(this.dbPath, newName + PersistenceConstants.TableFileExtension);
            
            // FIXED: Verify source file exists
            if (!File.Exists(oldFile))
            {
                throw new InvalidOperationException(
                    $"Cannot rename table '{tableName}': data file '{oldFile}' does not exist");
            }
            
            // FIXED: If target file exists, delete it first (should not happen in normal operation)
            if (File.Exists(newFile))
            {
                try
                {
                    File.Delete(newFile);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Cannot rename table to '{newName}': target file '{newFile}' exists and cannot be deleted: {ex.Message}", 
                        ex);
                }
            }
            
            // FIXED: Update table metadata BEFORE file rename to maintain consistency
            table.Name = newName;
            table.DataFile = newFile;
            
            // FIXED: Remove old name from dictionary and add new name BEFORE file operations
            this.tables.Remove(tableName);
            this.tables[newName] = table;
            
            // FIXED: Rename file with retry logic for robustness
            bool renamed = false;
            Exception? lastException = null;
            
            for (int attempt = 0; attempt < 3 && !renamed; attempt++)
            {
                try
                {
                    File.Move(oldFile, newFile);
                    renamed = true;
                }
                catch (IOException ex) when (attempt < 2)
                {
                    lastException = ex;
                    // File may be temporarily locked - wait and retry
                    System.Threading.Thread.Sleep(100 * (attempt + 1));
                }
            }
            
            if (!renamed)
            {
                // ROLLBACK: Restore original table name
                this.tables.Remove(newName);
                this.tables[tableName] = table;
                table.Name = tableName;
                table.DataFile = oldFile;
                
                throw new InvalidOperationException(
                    $"Failed to rename data file from '{oldFile}' to '{newFile}': {lastException?.Message}", 
                    lastException);
            }
            
            // FIXED: Verify rename succeeded
            if (!File.Exists(newFile))
            {
                // ROLLBACK: Restore original table name
                this.tables.Remove(newName);
                this.tables[tableName] = table;
                table.Name = tableName;
                table.DataFile = oldFile;
                
                throw new InvalidOperationException(
                    $"File rename appeared to succeed but target file '{newFile}' does not exist");
            }
            
            wal?.Log(sql);
        }
        else if (parts.Length > 4 && parts[3].ToUpper() == "ADD" && parts[4].ToUpper() == "COLUMN")
        {
            throw new NotImplementedException("ALTER TABLE ADD COLUMN not yet implemented");
        }
        else
        {
            throw new InvalidOperationException($"Unsupported ALTER TABLE operation: {string.Join(" ", parts.Skip(3))}");
        }
    }
}
