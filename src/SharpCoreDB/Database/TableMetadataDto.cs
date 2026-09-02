// <copyright file="TableMetadataDto.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB;

using SharpCoreDB.DataStructures;

/// <summary>
/// Strongly-typed metadata DTO for table persistence (Native AOT / source-generated JSON).
/// Mirrors the legacy anonymous-type JSON shape exactly, so existing databases remain
/// fully compatible when metadata is written or read through the source-generated
/// <see cref="SharpCoreDBJsonContext"/>.
/// </summary>
public sealed class TableMetadataDto
{
    /// <summary>Gets or sets the table name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the column names.</summary>
    public List<string>? Columns { get; set; }

    /// <summary>Gets or sets the column types.</summary>
    public List<DataType>? ColumnTypes { get; set; }

    /// <summary>Gets or sets the primary key column index.</summary>
    public int PrimaryKeyIndex { get; set; }

    /// <summary>Gets or sets whether the table has the internal _rowid column.</summary>
    public bool HasInternalRowId { get; set; }

    /// <summary>Gets or sets the data file path.</summary>
    public string? DataFile { get; set; }

    /// <summary>Gets or sets the storage mode.</summary>
    public SharpCoreDB.Storage.Hybrid.StorageMode StorageMode { get; set; }

    /// <summary>
    /// Gets or sets whether the table uses the fixed-width record layout (out-of-line overflow).
    /// B5: persisted so a reopened database keeps the record format without needing the config flag.
    /// </summary>
    public bool IsFixedWidthRecords { get; set; }

    /// <summary>Gets or sets auto-increment flags per column.</summary>
    public List<bool>? IsAuto { get; set; }

    /// <summary>Gets or sets NOT NULL flags per column.</summary>
    public List<bool>? IsNotNull { get; set; }

    /// <summary>Gets or sets default values per column.</summary>
    public List<object?>? DefaultValues { get; set; }

    /// <summary>Gets or sets unique constraints.</summary>
    public List<List<string>>? UniqueConstraints { get; set; }

    /// <summary>Gets or sets foreign key constraints.</summary>
    public List<ForeignKeyConstraint>? ForeignKeys { get; set; }

    /// <summary>Gets or sets per-column collations.</summary>
    public List<CollationType>? ColumnCollations { get; set; }

    /// <summary>Gets or sets persisted auto-increment counters.</summary>
    public Dictionary<int, long>? AutoIncrementCounters { get; set; }
}
