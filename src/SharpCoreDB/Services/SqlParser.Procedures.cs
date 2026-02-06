// <copyright file="SqlParser.Procedures.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SqlParser partial class for stored procedure DDL operations:
/// CREATE PROCEDURE, DROP PROCEDURE, EXEC.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// In-memory registry of stored procedures, keyed by name.
    /// Static so procedures survive across SqlParser instances within the same process.
    /// </summary>
    private static readonly Dictionary<string, StoredProcedureDefinition> _procedures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _procedureLock = new();

    /// <summary>
    /// Executes CREATE PROCEDURE statement.
    /// Syntax: CREATE PROCEDURE name @p1 TYPE [MODE], @p2 TYPE BEGIN ... END
    /// </summary>
    private void ExecuteCreateProcedure(string sql, string[] parts, IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot create procedure in readonly mode");

        if (parts.Length < 3)
            throw new ArgumentException("Procedure name is required");

        var procedureName = parts[2];

        var beginIdx = sql.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
        var endIdx = sql.LastIndexOf("END", StringComparison.OrdinalIgnoreCase);
        if (beginIdx < 0 || endIdx < 0 || endIdx <= beginIdx)
            throw new ArgumentException("Procedure must have a BEGIN...END block");

        // Parameter text sits between procedure name and BEGIN
        var nameEnd = sql.IndexOf(procedureName, StringComparison.OrdinalIgnoreCase) + procedureName.Length;
        var paramText = sql[nameEnd..beginIdx].Trim();
        var parameters = ParseProcedureParams(paramText);

        var body = sql[(beginIdx + 5)..endIdx].Trim();

        var proc = new StoredProcedureDefinition(procedureName, parameters, body);
        lock (_procedureLock)
        {
            _procedures[procedureName] = proc;
        }
    }

    /// <summary>
    /// Executes DROP PROCEDURE statement.
    /// </summary>
    private void ExecuteDropProcedure(string sql, string[] parts, IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot drop procedure in readonly mode");

        if (parts.Length < 3)
            throw new ArgumentException("Procedure name is required");

        var name = parts[2];
        lock (_procedureLock)
        {
            if (!_procedures.Remove(name))
                throw new InvalidOperationException($"Procedure '{name}' does not exist");
        }
    }

    /// <summary>
    /// Executes EXEC statement by looking up the procedure and running its body.
    /// Syntax: EXEC ProcName @p1=value, @p2=value
    /// </summary>
    private void ExecuteExecProcedure(string sql, string[] parts)
    {
        if (parts.Length < 2)
            throw new ArgumentException("Procedure name is required for EXEC");

        var procName = parts[1].TrimEnd(';');
        StoredProcedureDefinition proc;
        lock (_procedureLock)
        {
            if (!_procedures.TryGetValue(procName, out proc!))
                throw new InvalidOperationException($"Procedure '{procName}' not found");
        }

        // Parse inline arguments: everything after the procedure name
        var argsStart = sql.IndexOf(procName, StringComparison.OrdinalIgnoreCase) + procName.Length;
        var argsText = sql[argsStart..].Trim().TrimEnd(';');
        var argValues = ParseExecArguments(argsText);

        // Substitute parameters into the body and execute each statement
        var body = proc.Body;
        foreach (var param in proc.Parameters)
        {
            if (argValues.TryGetValue(param.Name, out var val))
            {
                body = body.Replace(param.Name, val, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Split body on semicolons and execute each statement
        var statements = body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var stmt in statements)
        {
            if (string.IsNullOrWhiteSpace(stmt))
                continue;

            var stmtParts = stmt.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            ExecuteInternal(stmt.Trim(), stmtParts);
        }
    }

    /// <summary>
    /// Gets all registered procedure names.
    /// </summary>
    public IReadOnlyList<string> GetProcedureNames()
    {
        lock (_procedureLock)
        {
            return [.. _procedures.Keys];
        }
    }

    /// <summary>
    /// Parses procedure parameter definitions.
    /// Format: @p1 TYPE [MODE], @p2 TYPE [MODE]
    /// </summary>
    private static List<ProcedureParameter> ParseProcedureParams(string text)
    {
        List<ProcedureParameter> result = [];
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var segment in text.Split(','))
        {
            var tokens = segment.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            var name = tokens[0];
            if (!name.StartsWith('@'))
                throw new ArgumentException($"Parameter name must start with @: {name}");

            var mode = ParameterMode.In;
            if (tokens.Length > 2)
            {
                mode = tokens[2].ToUpperInvariant() switch
                {
                    "IN" => ParameterMode.In,
                    "OUT" => ParameterMode.Out,
                    "INOUT" => ParameterMode.InOut,
                    _ => ParameterMode.In
                };
            }

            result.Add(new ProcedureParameter(name, tokens[1], mode));
        }

        return result;
    }

    /// <summary>
    /// Parses EXEC inline arguments: @p1=value, @p2='text'
    /// </summary>
    private static Dictionary<string, string> ParseExecArguments(string text)
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var segment in text.Split(','))
        {
            var eqIdx = segment.IndexOf('=');
            if (eqIdx < 0)
                continue;

            var paramName = segment[..eqIdx].Trim();
            var paramValue = segment[(eqIdx + 1)..].Trim().TrimEnd(';');
            result[paramName] = paramValue;
        }

        return result;
    }
}

/// <summary>
/// Stored procedure parameter mode.
/// </summary>
public enum ParameterMode
{
    /// <summary>Input parameter (default).</summary>
    In = 0,

    /// <summary>Output parameter.</summary>
    Out = 1,

    /// <summary>Input/output parameter.</summary>
    InOut = 2
}

/// <summary>
/// Parameter definition for a stored procedure.
/// </summary>
/// <param name="Name">Parameter name including @ prefix.</param>
/// <param name="TypeName">SQL type name (INTEGER, TEXT, etc.).</param>
/// <param name="Mode">Parameter direction.</param>
public sealed record ProcedureParameter(string Name, string TypeName, ParameterMode Mode);

/// <summary>
/// In-memory definition of a stored procedure.
/// </summary>
/// <param name="Name">Procedure name.</param>
/// <param name="Parameters">Parameter definitions.</param>
/// <param name="Body">SQL body between BEGIN...END.</param>
public sealed record StoredProcedureDefinition(string Name, List<ProcedureParameter> Parameters, string Body);
