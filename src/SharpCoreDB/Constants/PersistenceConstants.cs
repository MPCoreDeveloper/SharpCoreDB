// <copyright file="PersistenceConstants.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Constants;

/// <summary>
/// Constants for database persistence.
/// Modern C# 14 with binary serialization support.
/// </summary>
public static class PersistenceConstants
{
    /// <summary>The name of the metadata file (binary format).</summary>
    public const string MetaFileName = "meta.dat";  // ✅ Changed from .json to .dat

    /// <summary>The name of the write-ahead log file.</summary>
    public const string WalFileName = "wal.log";

    /// <summary>The file extension for table data files (binary format).</summary>
    public const string TableFileExtension = ".dat";

    /// <summary>The key for tables in metadata.</summary>
    public const string TablesKey = "tables";

    /// <summary>
    /// The name of the auto-generated internal row identifier column.
    /// Injected as primary key when a table is created without an explicit PRIMARY KEY.
    /// Uses ULID type for globally unique, lexicographically sortable identifiers.
    /// Hidden from SELECT * but queryable via explicit column reference.
    /// </summary>
    public const string InternalRowIdColumnName = "_rowid";
}
