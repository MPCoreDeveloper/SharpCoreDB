// <copyright file="ISqlParser.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for parsing and executing SQL commands.
/// </summary>
public interface ISqlParser
{
    /// <summary>
    /// Executes a SQL command.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <param name="wal">The WAL for logging.</param>
    void Execute(string sql, IWAL? wal = null);

    /// <summary>
    /// Executes a parameterized SQL command.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="wal">The WAL for logging.</param>
    void Execute(string sql, Dictionary<string, object?> parameters, IWAL? wal = null);
}
