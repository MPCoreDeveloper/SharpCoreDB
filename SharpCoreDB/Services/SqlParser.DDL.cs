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

        var table = new Table(this.storage, this.isReadOnly)
        {
            Name = tableName,
            Columns = columns,
            ColumnTypes = columnTypes,
            IsAuto = isAuto,
            PrimaryKeyIndex = primaryKeyIndex,
            DataFile = Path.Combine(this.dbPath, tableName + PersistenceConstants.TableFileExtension),
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

        var tableName = parts[onIdx + 1];
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

        // Create hash index on the table
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
        this.tables.Remove(tableName);
        
        if (File.Exists(table.DataFile))
            File.Delete(table.DataFile);
        
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
            
            table.Name = newName;
            
            var oldFile = table.DataFile;
            var newFile = Path.Combine(this.dbPath, newName + PersistenceConstants.TableFileExtension);
            
            if (File.Exists(oldFile))
                File.Move(oldFile, newFile);
            
            table.DataFile = newFile;
            
            this.tables.Remove(tableName);
            this.tables[newName] = table;
            
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
