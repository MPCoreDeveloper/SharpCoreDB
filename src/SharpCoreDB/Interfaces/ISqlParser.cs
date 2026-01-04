// <copyright file="ISqlParser.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
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
