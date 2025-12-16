// <copyright file="SqlParser.DDL.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// SqlParser partial class containing DDL (Data Definition Language) operations:
/// CREATE TABLE, CREATE INDEX, DROP TABLE, DROP INDEX, ALTER TABLE.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Executes CREATE TABLE statement with optional STORAGE clause.
    /// Syntax: CREATE TABLE name (columns...) [STORAGE = COLUMNAR|PAGE_BASED]
    /// Default storage mode is COLUMNAR for backward compatibility.
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
        
        // ✅ NEW: Parse STORAGE clause (defaults to COLUMNAR)
        var storageMode = StorageMode.Columnar;
        var afterParenIndex = colsEnd + 1;
        if (afterParenIndex < sql.Length)
        {
            var remainingSQL = sql.Substring(afterParenIndex).Trim().ToUpperInvariant();
            if (remainingSQL.StartsWith("STORAGE"))
            {
                // Extract: STORAGE = COLUMNAR|PAGE_BASED
                var storageParts = remainingSQL.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (storageParts.Length >= 2)
                {
                    var modeStr = storageParts[1].Trim();
                    storageMode = modeStr switch
                    {
                        "COLUMNAR" => StorageMode.Columnar,
                        "PAGE_BASED" => StorageMode.PageBased,
                        "HYBRID" => StorageMode.Hybrid,
                        _ => throw new InvalidOperationException(
                            $"Invalid storage mode '{modeStr}'. Valid modes: COLUMNAR, PAGE_BASED, HYBRID")
                    };
                }
            }
        }
        
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

        // ✅ NEW: Choose file extension based on storage mode
        var fileExtension = storageMode == StorageMode.PageBased 
            ? ".pages" 
            : PersistenceConstants.TableFileExtension;
        var dataFilePath = Path.Combine(this.dbPath, tableName + fileExtension);

        // ✅ CRITICAL FIX: Complete cleanup of existing table + indexes + data file
        if (this.tables.TryGetValue(tableName, out var existingTable))
        {
            // 🔥 PERFORMANCE CRITICAL: Clear ALL indexes FIRST to prevent reading stale data
            existingTable.ClearAllIndexes();
            
            // Remove from dictionary and dispose to release file handles
            this.tables.Remove(tableName);
            
            if (existingTable is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            // Give OS time to close file handles
            System.Threading.Thread.Sleep(100);
            
            // Delete old data file explicitly (both .dat and .pages files)
            foreach (var ext in new[] { PersistenceConstants.TableFileExtension, ".pages", ".pages.freelist" })
            {
                var fileToDelete = Path.Combine(this.dbPath, tableName + ext);
                if (File.Exists(fileToDelete))
                {
                    try
                    {
                        File.Delete(fileToDelete);
                        
                        // Wait for deletion to complete
                        for (int attempt = 0; attempt < 10 && File.Exists(fileToDelete); attempt++)
                        {
                            System.Threading.Thread.Sleep(50);
                        }
                    }
                    catch (IOException)
                    {
                        // If deletion fails, truncation below will handle it
                    }
                }
            }
        }
        
        // ✅ ALWAYS use FileMode.Create to guarantee 0-byte file
        using (var fs = File.Create(dataFilePath))
        {
            fs.Flush(true); // Force flush to disk
        }
        
        // ✅ VALIDATION: Verify file is truly empty
        var fileInfo = new FileInfo(dataFilePath);
        if (fileInfo.Length != 0)
        {
            throw new InvalidOperationException(
                $"Failed to create empty data file for table '{tableName}'. " +
                $"File '{dataFilePath}' has {fileInfo.Length} bytes instead of 0.");
        }
        
        // Create brand new table with clean state
        var table = new Table(this.storage, this.isReadOnly)
        {
            Name = tableName,
            Columns = columns,
            ColumnTypes = columnTypes,
            IsAuto = isAuto,
            PrimaryKeyIndex = primaryKeyIndex,
            DataFile = dataFilePath,
            StorageMode = storageMode  // ✅ NEW: Set storage mode
        };
        
        this.tables[tableName] = table;
        wal?.Log(sql);
        
        // ✅ NEW: Only create indexes for columnar mode (page-based uses B-trees)
        if (storageMode == StorageMode.Columnar)
        {
            // Auto-create hash indexes (will be built lazily on first query)
            if (primaryKeyIndex >= 0)
            {
                table.CreateHashIndex(table.Columns[primaryKeyIndex]);
            }
            
            for (int i = 0; i < columns.Count; i++)
            {
                if (i != primaryKeyIndex)
                {
                    table.CreateHashIndex(columns[i]);
                }
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

        // 🔥 CRITICAL FIX: Extract index NAME (not just column name)
        // SQL: CREATE INDEX idx_email ON users(email)
        //      parts[2] = "idx_email" (the index NAME)
        var indexName = parts[2]; // Extract index name (e.g., "idx_email")

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

        // 🔥 CRITICAL FIX: Create named hash index with BOTH index name and column name
        // This allows DROP INDEX idx_email to work correctly
        this.tables[tableName].CreateHashIndex(indexName, columnName);
        
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
        
        // 🔥 CRITICAL: Clear ALL indexes FIRST to prevent any references to data
        table.ClearAllIndexes();
        
        // Remove from dictionary and dispose to release file handles
        this.tables.Remove(tableName);
        
        if (table is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        // ✅ ROBUST file deletion with retry and verification
        if (File.Exists(dataFile))
        {
            bool deleted = false;
            
            // Try deletion with exponential backoff (max 5 attempts over ~1.5 seconds)
            for (int attempt = 0; attempt < 5 && !deleted; attempt++)
            {
                try
                {
                    // Verify file is not locked by opening exclusively first
                    using (var testStream = new FileStream(dataFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // If we can open exclusively, we can delete
                    }
                    
                    File.Delete(dataFile);
                    deleted = true;
                }
                catch (IOException) when (attempt < 4)
                {
                    // File is locked - wait with exponential backoff
                    int delayMs = 50 * (1 << attempt); // 50, 100, 200, 400, 800 ms
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    // Permission issue - wait and retry
                    System.Threading.Thread.Sleep(50 * (attempt + 1));
                }
            }
            
            // ✅ VALIDATION: Verify deletion succeeded
            if (File.Exists(dataFile))
            {
                throw new InvalidOperationException(
                    $"Failed to delete data file '{dataFile}' after DROP TABLE. " +
                    $"The file may be locked by another process.");
            }
        }
        
        wal?.Log(sql);
    }

    /// <summary>
    /// Executes DROP INDEX statement.
    /// FIXED: Now correctly handles named indexes (e.g., DROP INDEX idx_email).
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
        
        // 🔥 CRITICAL FIX: Search ALL tables and use RemoveHashIndex which supports index names
        // Previously used HasHashIndex which only checked column names
        bool found = this.tables.Values.Any(table => table.RemoveHashIndex(indexName));
        
        if (!found && !ifExists)
            throw new InvalidOperationException($"{indexName} is not a table index.");
        
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
