// <copyright file="ITableSchemaApplicator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for applying a parsed table schema to a concrete table implementation.
/// Allows SqlParser.DDL to set schema properties without direct dependencies
/// on Table or SingleFileTable implementations.
/// </summary>
public interface ITableSchemaApplicator
{
    /// <summary>
    /// Applies the given schema definition to this table.
    /// Implementations populate their internal schema fields from the definition.
    /// </summary>
    /// <param name="schema">The parsed table schema.</param>
    void ApplySchema(TableSchemaDefinition schema);
}
