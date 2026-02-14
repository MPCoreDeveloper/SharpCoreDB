// <copyright file="SqlParser.Triggers.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SqlParser partial class for trigger DDL operations:
/// CREATE TRIGGER, DROP TRIGGER.
///
/// Syntax:
/// CREATE TRIGGER name BEFORE|AFTER INSERT|UPDATE|DELETE ON table
/// BEGIN
///     ... body ...
/// END
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// In-memory registry of trigger definitions, keyed by name.
    /// Static so triggers survive across SqlParser instances within the same process.
    /// </summary>
    private static readonly Dictionary<string, TriggerDefinition> _triggers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _triggerLock = new();

    /// <summary>
    /// Executes CREATE TRIGGER statement.
    /// Syntax: CREATE TRIGGER name BEFORE|AFTER INSERT|UPDATE|DELETE ON table BEGIN ... END
    /// </summary>
    private void ExecuteCreateTrigger(string sql, string[] parts, IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot create trigger in readonly mode");

        // Minimum: CREATE TRIGGER name BEFORE INSERT ON table BEGIN ... END
        if (parts.Length < 7)
            throw new ArgumentException("Invalid CREATE TRIGGER syntax");

        var triggerName = parts[2];

        var timing = parts[3].ToUpperInvariant() switch
        {
            "BEFORE" => TriggerTiming.Before,
            "AFTER" => TriggerTiming.After,
            _ => throw new ArgumentException($"Expected BEFORE or AFTER, got: {parts[3]}")
        };

        var triggerEvent = parts[4].ToUpperInvariant() switch
        {
            "INSERT" => TriggerEvent.Insert,
            "UPDATE" => TriggerEvent.Update,
            "DELETE" => TriggerEvent.Delete,
            _ => throw new ArgumentException($"Expected INSERT, UPDATE, or DELETE, got: {parts[4]}")
        };

        // parts[5] should be "ON"
        if (!parts[5].Equals("ON", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Expected ON keyword, got: {parts[5]}");

        var tableName = parts[6];

        // Extract BEGIN...END body
        var beginIdx = sql.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
        var endIdx = sql.LastIndexOf("END", StringComparison.OrdinalIgnoreCase);
        if (beginIdx < 0 || endIdx < 0 || endIdx <= beginIdx)
            throw new ArgumentException("Trigger must have a BEGIN...END block");

        var body = sql[(beginIdx + 5)..endIdx].Trim();

        var trigger = new TriggerDefinition(triggerName, tableName, timing, triggerEvent, body);
        lock (_triggerLock)
        {
            _triggers[triggerName] = trigger;
        }
    }

    /// <summary>
    /// Executes DROP TRIGGER statement with optional IF EXISTS clause.
    /// Syntax: DROP TRIGGER [IF EXISTS] trigger_name
    /// </summary>
    private void ExecuteDropTrigger(string sql, string[] parts, IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot drop trigger in readonly mode");

        // ✅ Detect IF EXISTS clause
        bool ifExists = false;
        int nameIndex = 2;
        
        if (parts.Length >= 5 && parts[2].Equals("IF", StringComparison.OrdinalIgnoreCase)
            && parts[3].Equals("EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            ifExists = true;
            nameIndex = 4;
        }

        if (nameIndex >= parts.Length)
            throw new ArgumentException("Trigger name is required");

        var name = parts[nameIndex].TrimEnd(';');
        lock (_triggerLock)
        {
            if (!_triggers.Remove(name))
            {
                if (!ifExists)
                    throw new InvalidOperationException($"Trigger '{name}' does not exist");
                // IF EXISTS: silently skip if trigger doesn't exist
            }
        }
        
        wal?.Log(sql);
    }

    /// <summary>
    /// Fires all matching triggers for a given table and event.
    /// Called from INSERT/UPDATE/DELETE handlers.
    /// </summary>
    /// <param name="tableName">The table being modified.</param>
    /// <param name="timing">BEFORE or AFTER the operation.</param>
    /// <param name="triggerEvent">INSERT, UPDATE, or DELETE.</param>
    /// <param name="newRow">NEW row values (null for DELETE).</param>
    /// <param name="oldRow">OLD row values (null for INSERT).</param>
    internal void FireTriggers(string tableName, TriggerTiming timing, TriggerEvent triggerEvent,
        Dictionary<string, object>? newRow = null, Dictionary<string, object>? oldRow = null)
    {
        List<TriggerDefinition> matching;
        lock (_triggerLock)
        {
            matching = _triggers.Values
                .Where(t => t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase)
                         && t.Timing == timing
                         && t.Event == triggerEvent)
                .ToList();
        }

        foreach (var trigger in matching)
        {
            var body = trigger.Body;

            // Substitute NEW.col and OLD.col references
            if (newRow is not null)
            {
                foreach (var (col, val) in newRow)
                {
                    body = body.Replace($"NEW.{col}", val?.ToString() ?? "NULL", StringComparison.OrdinalIgnoreCase);
                }
            }

            if (oldRow is not null)
            {
                foreach (var (col, val) in oldRow)
                {
                    body = body.Replace($"OLD.{col}", val?.ToString() ?? "NULL", StringComparison.OrdinalIgnoreCase);
                }
            }

            // Execute each statement in the trigger body
            var statements = body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var stmt in statements)
            {
                if (string.IsNullOrWhiteSpace(stmt))
                    continue;

                var stmtParts = stmt.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                ExecuteInternal(stmt.Trim(), stmtParts);
            }
        }
    }

    /// <summary>
    /// Gets all registered trigger names.
    /// </summary>
    public IReadOnlyList<string> GetTriggerNames()
    {
        lock (_triggerLock)
        {
            return [.. _triggers.Keys];
        }
    }

    /// <summary>
    /// Gets all triggers for a specific table.
    /// </summary>
    public IReadOnlyList<TriggerDefinition> GetTriggersForTable(string tableName)
    {
        lock (_triggerLock)
        {
            return _triggers.Values
                .Where(t => t.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}

/// <summary>
/// When the trigger fires relative to the operation.
/// </summary>
public enum TriggerTiming
{
    /// <summary>Fires before the operation.</summary>
    Before,

    /// <summary>Fires after the operation.</summary>
    After
}

/// <summary>
/// The DML event that fires the trigger.
/// </summary>
public enum TriggerEvent
{
    /// <summary>Fires on INSERT.</summary>
    Insert,

    /// <summary>Fires on UPDATE.</summary>
    Update,

    /// <summary>Fires on DELETE.</summary>
    Delete
}

/// <summary>
/// In-memory definition of a database trigger.
/// </summary>
/// <param name="Name">Trigger name.</param>
/// <param name="TableName">Table the trigger is attached to.</param>
/// <param name="Timing">BEFORE or AFTER.</param>
/// <param name="Event">INSERT, UPDATE, or DELETE.</param>
/// <param name="Body">SQL body between BEGIN...END.</param>
public sealed record TriggerDefinition(string Name, string TableName, TriggerTiming Timing, TriggerEvent Event, string Body);
