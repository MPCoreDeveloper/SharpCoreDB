// <copyright file="TableSchemaDefinition.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

using SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Immutable schema definition produced by DDL parsing and passed to
/// <see cref="ITable.ApplySchema"/> during <c>CREATE TABLE</c>.
/// Using a record keeps the DDL path free of individual property setters on <see cref="ITable"/>.
/// </summary>
public sealed record TableSchemaDefinition
{
    /// <summary>Gets the column names.</summary>
    public required List<string> Columns { get; init; }

    /// <summary>Gets the column data types.</summary>
    public required List<DataType> ColumnTypes { get; init; }

    /// <summary>Gets the auto-generation flags per column.</summary>
    public required List<bool> IsAuto { get; init; }

    /// <summary>Gets the NOT NULL constraint flags per column.</summary>
    public required List<bool> IsNotNull { get; init; }

    /// <summary>Gets the default values per column.</summary>
    public required List<object?> DefaultValues { get; init; }

    /// <summary>Gets the DEFAULT expression strings per column (may be null per entry).</summary>
    public required List<string?> DefaultExpressions { get; init; }

    /// <summary>Gets the inline CHECK expressions per column (may be null per entry).</summary>
    public required List<string?> ColumnCheckExpressions { get; init; }

    /// <summary>Gets the table-level CHECK constraint expressions.</summary>
    public required List<string> TableCheckConstraints { get; init; }

    /// <summary>Gets the foreign key constraints.</summary>
    public required List<ForeignKeyConstraint> ForeignKeys { get; init; }

    /// <summary>Gets the unique constraints (each inner list is one unique set of columns).</summary>
    public required List<List<string>> UniqueConstraints { get; init; }

    /// <summary>Gets the collation type per column.</summary>
    public required List<CollationType> ColumnCollations { get; init; }

    /// <summary>Gets the locale name per column (null for non-Locale collations).</summary>
    public required List<string?> ColumnLocaleNames { get; init; }

    /// <summary>Gets the index of the primary key column (-1 if none).</summary>
    public required int PrimaryKeyIndex { get; init; }

    /// <summary>Gets whether an internal ULID <c>_rowid</c> column was injected.</summary>
    public required bool HasInternalRowId { get; init; }

    /// <summary>Gets the storage engine mode requested for this table.</summary>
    public required StorageMode StorageMode { get; init; }

    /// <summary>Gets the data file path resolved by the DDL engine.</summary>
    public required string DataFilePath { get; init; }
}
