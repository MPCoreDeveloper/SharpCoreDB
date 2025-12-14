// <copyright file="Database.Execution.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.Constants;
using SharpCoreDB.Services;
using System.Text;
using System.Text.Json;

/// <summary>
/// Database implementation - Execution partial class.
/// Handles SQL execution with modern C# 14 patterns.
/// </summary>
public partial class Database
{
    /// <inheritdoc />
    public void ExecuteSQL(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);  // ✅ C# 14
        
        SqlQueryValidator.ValidateQuery(
            sql, 
            null, 
            config?.SqlValidationMode ?? SqlQueryValidator.ValidationMode.Lenient,
            config?.StrictParameterValidation ?? true);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))  // ✅ Modern comparison
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
            sqlParser.Execute(sql, null);
            return;
        }

        if (groupCommitWal is not null)  // ✅ C# 14: not pattern
        {
            ExecuteSQLWithGroupCommit(sql).GetAwaiter().GetResult();
        }
        else
        {
            lock (_walLock)
            {
                var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
                sqlParser.Execute(sql, null);
                
                if (!isReadOnly && IsSchemaChangingCommand(sql))
                {
                    SaveMetadata();
                }
            }
        }
    }

    /// <summary>
    /// Executes a parameterized SQL command.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    public void ExecuteSQL(string sql, Dictionary<string, object?> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        
        SqlQueryValidator.ValidateQuery(
            sql, 
            parameters, 
            config?.SqlValidationMode ?? SqlQueryValidator.ValidationMode.Lenient,
            config?.StrictParameterValidation ?? true);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
            sqlParser.Execute(sql, parameters, null);
            return;
        }

        if (groupCommitWal is not null)
        {
            ExecuteSQLWithGroupCommit(sql, parameters).GetAwaiter().GetResult();
        }
        else
        {
            lock (_walLock)
            {
                var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
                sqlParser.Execute(sql, parameters, null);
                
                if (!isReadOnly && IsSchemaChangingCommand(sql))
                {
                    SaveMetadata();
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() =>
            {
                var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
                sqlParser.Execute(sql, null);
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (groupCommitWal is not null)
        {
            await ExecuteSQLWithGroupCommit(sql, cancellationToken);
        }
        else
        {
            await Task.Run(() => ExecuteSQL(sql), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a parameterized SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() =>
            {
                var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
                sqlParser.Execute(sql, parameters, null);
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (groupCommitWal is not null)
        {
            await ExecuteSQLWithGroupCommit(sql, parameters, cancellationToken);
        }
        else
        {
            await Task.Run(() => ExecuteSQL(sql, parameters), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteSQLWithGroupCommit(string sql, CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
            sqlParser.Execute(sql, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                SaveMetadata();
            }
        }
        
        byte[] walData = Encoding.UTF8.GetBytes(sql);
        await groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    private async Task ExecuteSQLWithGroupCommit(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache);
            sqlParser.Execute(sql, parameters, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                SaveMetadata();
            }
        }
        
        var walEntry = new { Sql = sql, Parameters = parameters };
        byte[] walData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(walEntry));
        await groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        var sqlParser = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache);
        return sqlParser.ExecuteQuery(sql, parameters ?? []);  // ✅ C# 14: collection expression
    }

    /// <summary>
    /// Executes a query and returns the results with optional encryption bypass.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters, bool noEncrypt)
    {
        var sqlParser = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache, noEncrypt);
        return sqlParser.ExecuteQuery(sql, parameters ?? [], noEncrypt);
    }
}
