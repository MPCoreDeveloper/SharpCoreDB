// <copyright file="FormatConverter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB;
using SharpCoreDB.Data.Provider;
using SharpCoreDB.Viewer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharpCoreDB.Viewer.Services;

/// <summary>
/// Service for converting database formats between Directory and SingleFile modes
/// </summary>
public class FormatConverter
{
    /// <summary>
    /// Event raised when conversion progress changes
    /// </summary>
    public event EventHandler<double>? ProgressChanged;
    
    /// <summary>
    /// Converts a database from one format to another
    /// </summary>
    /// <param name="sourcePath">Source database path</param>
    /// <param name="targetPath">Target database path</param>
    /// <param name="password">Database password</param>
    /// <param name="sourceFormat">Source format</param>
    /// <param name="targetFormat">Target format</param>
    public async Task ConvertAsync(
        string sourcePath,
        string targetPath,
        string password,
        DatabaseFormatType sourceFormat,
        DatabaseFormatType targetFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        
        if (sourceFormat == targetFormat)
        {
            throw new InvalidOperationException("Source and target formats must be different");
        }
        
        ReportProgress(0);
        
        // Open source database
        ReportProgress(5);
        var sourceConnection = new SharpCoreDBConnection(
            $"Path={sourcePath};Password={password};StorageMode={sourceFormat}");
        
        await sourceConnection.OpenAsync();
        
        try
        {
            // Get list of tables
            ReportProgress(10);
            var tables = await GetTablesAsync(sourceConnection);
            
            if (tables.Count == 0)
            {
                throw new InvalidOperationException("Source database contains no tables");
            }
            
            // Create target database
            ReportProgress(15);
            var targetConnection = new SharpCoreDBConnection(
                $"Path={targetPath};Password={password};StorageMode={targetFormat}");
            
            await targetConnection.OpenAsync();
            
            try
            {
                var progressPerTable = 75.0 / tables.Count;
                var currentProgress = 15.0;
                
                // Copy each table
                foreach (var tableName in tables)
                {
                    await CopyTableAsync(sourceConnection, targetConnection, tableName);
                    
                    currentProgress += progressPerTable;
                    ReportProgress(currentProgress);
                }
                
                // Finalize
                ReportProgress(95);
                await targetConnection.CloseAsync();
                ReportProgress(100);
            }
            finally
            {
                targetConnection.Dispose();
            }
        }
        finally
        {
            sourceConnection.Dispose();
        }
    }
    
    private void ReportProgress(double progress)
    {
        ProgressChanged?.Invoke(this, Math.Max(0, Math.Min(100, progress)));
    }
    
    private static async Task<List<string>> GetTablesAsync(SharpCoreDBConnection connection)
    {
        var tables = new List<string>();
        
        using var command = new SharpCoreDBCommand(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
            connection);
        
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        
        return tables;
    }
    
    private static async Task CopyTableAsync(
        SharpCoreDBConnection source,
        SharpCoreDBConnection target,
        string tableName)
    {
        // Get table schema
        var schema = await GetTableSchemaAsync(source, tableName);
        
        // Create table in target
        using (var createCommand = new SharpCoreDBCommand(schema, target))
        {
            await createCommand.ExecuteNonQueryAsync();
        }
        
        // Copy data
        using var selectCommand = new SharpCoreDBCommand($"SELECT * FROM {tableName}", source);
        using var reader = await selectCommand.ExecuteReaderAsync();
        
        var columns = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }
        
        var insertSql = $"INSERT INTO {tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
        
        while (await reader.ReadAsync())
        {
            using var insertCommand = new SharpCoreDBCommand(insertSql, target);
            
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var param = new SharpCoreDBParameter($"@p{i}", reader.GetValue(i));
                insertCommand.Parameters.Add(param);
            }
            
            await insertCommand.ExecuteNonQueryAsync();
        }
    }
    
    private static async Task<string> GetTableSchemaAsync(SharpCoreDBConnection connection, string tableName)
    {
        using var command = new SharpCoreDBCommand(
            $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{tableName}'",
            connection);
        
        using var reader = await command.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return reader.GetString(0);
        }
        
        throw new InvalidOperationException($"Table '{tableName}' not found");
    }
}
