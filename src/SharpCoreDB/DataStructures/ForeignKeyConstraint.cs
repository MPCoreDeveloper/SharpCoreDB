// <copyright file="ForeignKeyConstraint.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.DataStructures;

/// <summary>
/// Represents a foreign key constraint.
/// </summary>
public class ForeignKeyConstraint
{
    /// <summary>
    /// Gets or sets the column name in this table.
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the referenced table name.
    /// </summary>
    public string ReferencedTable { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the referenced column name.
    /// </summary>
    public string ReferencedColumn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ON DELETE action.
    /// </summary>
    public FkAction OnDelete { get; set; }

    /// <summary>
    /// Gets or sets the ON UPDATE action.
    /// </summary>
    public FkAction OnUpdate { get; set; }
}

/// <summary>
/// Foreign key actions.
/// </summary>
public enum FkAction
{
    /// <summary>No action.</summary>
    NoAction,

    /// <summary>Restrict action.</summary>
    Restrict,

    /// <summary>Set NULL action.</summary>
    SetNull,

    /// <summary>Cascade action.</summary>
    Cascade
}
