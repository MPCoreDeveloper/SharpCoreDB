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
    /// Default storage mode is determined by DatabaseConfig.StorageEngineType if present,
    /// otherwise COLUMNAR for backward compatibility.
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
        
        // ✅ NEW: For Phase 1 DDL features
        var isNotNull = new List<bool>();
        var defaultValues = new List<object?>();
        var uniqueConstraints = new List<List<string>>();
        var foreignKeys = new List<ForeignKeyConstraint>();  // Added for Phase 1.2
        
        // ✅ NEW: For Phase 2 integrity features
        var defaultExpressions = new List<string?>();
        var columnCheckExpressions = new List<string?>();
        var tableCheckConstraints = new List<string>();
        
        // ✅ FIX: Determine default storage mode from DatabaseConfig if present
        // This ensures encrypted PageBased databases create tables with PageBased mode
        var storageMode = this.config?.StorageEngineType switch
        {
            StorageEngineType.PageBased => StorageMode.PageBased,
            StorageEngineType.Columnar => StorageMode.Columnar,
            StorageEngineType.AppendOnly => StorageMode.Columnar, // AppendOnly uses Columnar mode
            _ => StorageMode.Columnar // Default for backward compatibility
        };
        
        // Parse explicit STORAGE clause (overrides config default)
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
            var isNotNullCol = def.Contains("NOT NULL");
            var isUniqueCol = def.Contains("UNIQUE");
            
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
            isNotNull.Add(isNotNullCol);
            defaultValues.Add(null); // Default to null, would need more parsing for actual DEFAULT values
            defaultExpressions.Add(null); // Phase 2: Default expressions
            columnCheckExpressions.Add(null); // Phase 2: Column CHECK constraints
            
            if (isPrimary)
            {
                primaryKeyIndex = i;
            }
            
            if (isUniqueCol)
            {
                uniqueConstraints.Add([colName]);
            }
        }

        // ✅ NEW: Parse FOREIGN KEY constraints (Phase 1.2)
        // Look for FOREIGN KEY definitions in the column definitions
        foreignKeys.AddRange(colDefs
            .Where(def => def.Trim().ToUpper().StartsWith("FOREIGN KEY"))
            .Select(def => ParseForeignKeyFromString(def.Trim()))
            .Where(fk => fk is not null)!);

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
            
            // ✅ IMPROVED: Retry file deletion with validation and exponential backoff
            // This defense-in-depth approach handles edge cases on slow machines or antivirus interference
            foreach (var ext in new[] { PersistenceConstants.TableFileExtension, ".pages", ".pages.freelist" })
            {
                var fileToDelete = Path.Combine(this.dbPath, tableName + ext);
                if (File.Exists(fileToDelete))
                {
                    bool deleted = false;
                    
                    // Retry with exponential backoff (5 attempts over ~1.5 seconds max)
                    for (int attempt = 0; attempt < 5 && !deleted; attempt++)
                    {
                        try
                        {
                            // ✅ VALIDATION: Verify file is not locked by opening exclusively first
                            // This confirms the file handle is truly released by Dispose()
                            using (var testStream = new FileStream(
                                fileToDelete, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                            {
                                // If we can open exclusively, file is not locked
                            }
                            
                            File.Delete(fileToDelete);
                            deleted = true;
                        }
                        catch (IOException) when (attempt < 4)
                        {
                            // File still locked - wait with exponential backoff
                            // 10, 20, 40, 80, 160 ms (total: 310ms max)
                            int delayMs = 10 * (1 << attempt);
                            System.Threading.Thread.Sleep(delayMs);
                        }
                        catch (UnauthorizedAccessException) when (attempt < 4)
                        {
                            // Permission issue (rare) - linear backoff
                            System.Threading.Thread.Sleep(50 * (attempt + 1));
                        }
                    }
                    
                    // ✅ VALIDATION: Verify deletion succeeded
                    if (!deleted && File.Exists(fileToDelete))
                    {
                        throw new InvalidOperationException(
                            $"Failed to delete '{fileToDelete}' after 5 attempts. " +
                            "File may be locked by another process or antivirus software.");
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
        var table = new Table(this.storage, this.isReadOnly, this.config)
        {
            Name = tableName,
            Columns = columns,
            ColumnTypes = columnTypes,
            IsAuto = isAuto,
            PrimaryKeyIndex = primaryKeyIndex,
            DataFile = dataFilePath,
            StorageMode = storageMode,
            IsNotNull = isNotNull,
            DefaultValues = defaultValues,
            UniqueConstraints = uniqueConstraints,
            ForeignKeys = foreignKeys,  // Added for Phase 1.2
            DefaultExpressions = defaultExpressions,
            ColumnCheckExpressions = columnCheckExpressions,
            TableCheckConstraints = tableCheckConstraints
        };
        
        this.tables[tableName] = table;
        
        // ✅ CRITICAL: Initialize storage engine IMMEDIATELY after creating table
        // This ensures the engine is ready before any INSERT/SELECT operations
        table.InitializeStorageEngine();
        
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
    /// ENHANCED: Supports BTREE and HASH index types, and UNIQUE indexes.
    /// Syntax: CREATE [UNIQUE] INDEX idx_name ON table(column) [USING BTREE|HASH]
    /// </summary>
    private void ExecuteCreateIndex(string sql, string[] parts, IWAL? wal)
    {
        if (this.isReadOnly)
        {
            throw new InvalidOperationException("Cannot create index in readonly mode");
        }

        // Parse: CREATE INDEX idx_name ON table_name (column_name) [USING BTREE|HASH]
        // or CREATE UNIQUE INDEX idx_name ON table_name (column_name) [USING BTREE|HASH]
        var onIdx = Array.FindIndex(parts, p => p.Equals("ON", StringComparison.OrdinalIgnoreCase));
        if (onIdx < 0)
        {
            throw new InvalidOperationException("CREATE INDEX requires ON clause");
        }

        // Determine if this is CREATE UNIQUE INDEX (parts[1] == "UNIQUE") or CREATE INDEX
        // For CREATE INDEX: parts[2] is the index name
        // For CREATE UNIQUE INDEX: parts[3] is the index name
        var isUnique = parts.Length > 1 && parts[1].Equals("UNIQUE", StringComparison.OrdinalIgnoreCase);
        var indexNamePosition = isUnique ? 3 : 2;
        
        if (indexNamePosition >= parts.Length)
        {
            throw new InvalidOperationException("CREATE INDEX requires an index name");
        }
        
        var indexName = parts[indexNamePosition];

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

        // ✅ NEW: Determine index type (BTREE or HASH)
        // Default to HASH for backward compatibility, use BTREE if specified
        var indexType = IndexType.Hash; // Default
        
        // Check for USING clause after closing parenthesis
        var afterParenIndex = columnEnd + 1;
        if (afterParenIndex < sql.Length)
        {
            var remainingSQL = sql.Substring(afterParenIndex).Trim().ToUpperInvariant();
            if (remainingSQL.StartsWith("USING"))
            {
                if (remainingSQL.Contains("BTREE"))
                {
                    indexType = IndexType.BTree;
                }
                else if (remainingSQL.Contains("HASH"))
                {
                    indexType = IndexType.Hash;
                }
            }
            // Also support shorthand: CREATE INDEX ... BTREE ON ...
            else if (sql.Contains("BTREE", StringComparison.OrdinalIgnoreCase))
            {
                indexType = IndexType.BTree;
            }
        }

        // Create the appropriate index type with uniqueness constraint
        if (indexType == IndexType.BTree)
        {
            this.tables[tableName].CreateBTreeIndex(indexName, columnName, isUnique);
        }
        else
        {
            this.tables[tableName].CreateHashIndex(indexName, columnName, isUnique);
        }
        
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

        // ✅ FK dependency check: Ensure no other tables reference this table
        foreach (var otherTable in this.tables.Values)
        {
            if (otherTable.Name != tableName)
            {
                foreach (var fk in otherTable.ForeignKeys)
                {
                    if (fk.ReferencedTable == tableName)
                    {
                        throw new InvalidOperationException(
                            $"Cannot drop table '{tableName}' because it is referenced by foreign key in table '{otherTable.Name}' (column '{fk.ColumnName}')");
                    }
                }
            }
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
        
        // ✅ OPTIMIZED: Same retry pattern as CREATE TABLE for consistency
        // PageManager.Dispose() now guarantees synchronous file handle closure
        if (File.Exists(dataFile))
        {
            bool deleted = false;
            
            // Retry with exponential backoff (5 attempts over ~310ms max)
            for (int attempt = 0; attempt < 5 && !deleted; attempt++)
            {
                try
                {
                    // ✅ VALIDATION: Verify file is not locked by opening exclusively first
                    // This confirms the file handle is truly released by Dispose()
                    using (var testStream = new FileStream(
                        dataFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                        // If we can open exclusively, file is not locked
                    }
                    
                    File.Delete(dataFile);
                    deleted = true;
                }
                catch (IOException) when (attempt < 4)
                {
                    // File still locked - wait with exponential backoff
                    // 10, 20, 40, 80, 160 ms (total: 310ms max)
                    int delayMs = 10 * (1 << attempt);
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    // Permission issue (rare) - linear backoff
                    System.Threading.Thread.Sleep(50 * (attempt + 1));
                }
            }
            
            // ✅ VALIDATION: Verify deletion succeeded
            if (!deleted && File.Exists(dataFile))
            {
                throw new InvalidOperationException(
                    $"Failed to delete data file '{dataFile}' after DROP TABLE. " +
                    "File may be locked by another process or antivirus software.");
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
            // Parse the column definition from SQL
            var columnDefStr = string.Join(" ", parts.Skip(5));
            var columnDef = ParseColumnDefinitionFromSql(columnDefStr);

            // Add column to table
            table.AddColumn(columnDef);

            // Mark metadata as dirty for persistence
            // Note: Database.ExecuteSQL will handle SaveMetadata

            wal?.Log(sql);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported ALTER TABLE operation: {string.Join(" ", parts.Skip(3))}");
        }
    }

    /// <summary>
    /// Parses a column definition string into a ColumnDefinition object.
    /// Used for ALTER TABLE ADD COLUMN parsing.
    /// </summary>
    private static ColumnDefinition ParseColumnDefinitionFromSql(string columnDefStr)
    {
        var parts = columnDefStr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Invalid column definition");
        }

        var column = new ColumnDefinition
        {
            Name = parts[0],
            DataType = parts[1]
        };

        // Parse constraints
        int i = 2;
        while (i < parts.Length)
        {
            var constraint = parts[i].ToUpperInvariant();
            switch (constraint)
            {
                case "PRIMARY":
                    if (i + 1 < parts.Length && parts[i + 1].ToUpperInvariant() == "KEY")
                    {
                        column.IsPrimaryKey = true;
                        i += 2; // Skip PRIMARY KEY
                    }
                    else
                    {
                        i++;
                    }
                    break;
                case "AUTO":
                case "AUTOINCREMENT":
                    column.IsAutoIncrement = true;
                    i++;
                    break;
                case "NOT":
                    if (i + 1 < parts.Length && parts[i + 1].ToUpperInvariant() == "NULL")
                    {
                        column.IsNotNull = true;
                        i += 2; // Skip NOT NULL
                    }
                    else
                    {
                        i++;
                    }
                    break;
                case "UNIQUE":
                    column.IsUnique = true;
                    i++;
                    break;
                case "DEFAULT":
                    if (i + 1 < parts.Length)
                    {
                        column.DefaultValue = ParseDefaultValue(parts[i + 1]);
                        i += 2; // Skip DEFAULT value
                    }
                    else
                    {
                        i++;
                    }
                    break;
                default:
                    i++;
                    break;
            }
        }

        return column;
    }

    /// <summary>
    /// Parses a default value string.
    /// </summary>
    private static object? ParseDefaultValue(string valueStr)
    {
        if (string.IsNullOrEmpty(valueStr))
            return null;

        // Remove quotes for strings
        if (valueStr.StartsWith('\'') && valueStr.EndsWith('\''))
        {
            return valueStr.Substring(1, valueStr.Length - 2);
        }

        // Handle NULL
        if (valueStr.ToUpperInvariant() == "NULL")
        {
            return null;
        }

        // Handle CURRENT_TIMESTAMP
        if (valueStr.ToUpperInvariant() == "CURRENT_TIMESTAMP")
        {
            return DateTime.Now; // Or a special marker
        }

        // Try to parse as number
        if (int.TryParse(valueStr, out var intVal))
            return intVal;
        if (long.TryParse(valueStr, out var longVal))
            return longVal;
        if (decimal.TryParse(valueStr, out var decimalVal))
            return decimalVal;
        if (bool.TryParse(valueStr, out var boolVal))
            return boolVal;

        // Default to string
        return valueStr;
    }

    /// <summary>
    /// Parses a foreign key constraint from a string definition.
    /// Used for CREATE TABLE parsing.
    /// </summary>
    private static ForeignKeyConstraint ParseForeignKeyFromString(string fkDef)
    {
        // Expected format: FOREIGN KEY (column_name) REFERENCES table_name(column_name) [ON DELETE action] [ON UPDATE action]
        var parts = fkDef.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 6 || !parts[0].ToUpper().Equals("FOREIGN") || !parts[1].ToUpper().Equals("KEY"))
        {
            throw new InvalidOperationException($"Invalid FOREIGN KEY definition: {fkDef}");
        }

        // Extract column name from (column_name)
        var columnPart = parts[2];
        if (!columnPart.StartsWith('(') || !columnPart.EndsWith(')'))
        {
            throw new InvalidOperationException($"Invalid FOREIGN KEY column reference: {columnPart}");
        }
        var columnName = columnPart.Trim('(', ')');

        // Extract referenced table and column
        var referencesIdx = Array.FindIndex(parts, p => p.ToUpper().Equals("REFERENCES"));
        if (referencesIdx < 0 || referencesIdx + 1 >= parts.Length)
        {
            throw new InvalidOperationException($"Missing REFERENCES clause in FOREIGN KEY: {fkDef}");
        }

        var refTableAndCol = parts[referencesIdx + 1];
        var parenIdx = refTableAndCol.IndexOf('(');
        if (parenIdx < 0 || !refTableAndCol.EndsWith(')'))
        {
            throw new InvalidOperationException($"Invalid REFERENCES format: {refTableAndCol}");
        }
        var referencedTable = refTableAndCol.Substring(0, parenIdx);
        var referencedColumn = refTableAndCol.Substring(parenIdx + 1, refTableAndCol.Length - parenIdx - 2);

        // Parse ON DELETE and ON UPDATE actions
        var onDelete = FkAction.Restrict; // Default
        var onUpdate = FkAction.Restrict; // Default

        for (int i = referencesIdx + 2; i < parts.Length; i++)
        {
            if (parts[i].ToUpper().Equals("ON") && i + 2 < parts.Length)
            {
                var actionType = parts[i + 1].ToUpper();
                var action = parts[i + 2].ToUpper();
                var fkAction = action switch
                {
                    "CASCADE" => FkAction.Cascade,
                    "SET" => FkAction.SetNull, // Assuming next part is NULL
                    "RESTRICT" => FkAction.Restrict,
                    "NO" => FkAction.NoAction, // Assuming next part is ACTION
                    _ => FkAction.Restrict
                };

                if (actionType.Equals("DELETE"))
                {
                    onDelete = fkAction;
                }
                else if (actionType.Equals("UPDATE"))
                {
                    onUpdate = fkAction;
                }
            }
        }

        return new ForeignKeyConstraint
        {
            ColumnName = columnName,
            ReferencedTable = referencedTable,
            ReferencedColumn = referencedColumn,
            OnDelete = onDelete,
            OnUpdate = onUpdate
        };
    }
}
