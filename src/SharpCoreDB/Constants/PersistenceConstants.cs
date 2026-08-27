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

    // ── Table data file format versioning (Known Issue 1) ───────────────────────
    // Legacy (and NoEncryptMode=true) table files are plaintext length-prefixed
    // records: [len:4][data]... with NO magic header — byte-for-byte identical to
    // every existing SharpCoreDB version, so backward compat is guaranteed.
    //
    // When encryption is enabled (DatabaseConfig.Default, NoEncryptMode=false),
    // NEW table files carry this 8-byte magic header, followed by per-record
    // AES-256-GCM ciphertext: [len_cipher:4][nonce(12)][cipher][tag(16)]...
    // The len field stores the ciphertext size so legacy-style parsers can skip
    // records; readers detect the header and decrypt each record.
    //
    // The magic never collides with a valid legacy record because records start
    // with a 4-byte signed length; the first 4 bytes here are 0x53 0x43 0x44 0x42
    // ("SCDB" as ASCII) which as a little-endian Int32 is +1128354611 (>1GB) and
    // therefore treated as invalid-length by all existing parsers — they already
    // break on such records, so no silent misread can occur on legacy tools.
    /// <summary>The 8-byte magic header marking an encrypted per-record table data file.</summary>
    public static readonly byte[] EncryptedTableMagic = [0x53, 0x43, 0x44, 0x42, 0x01, 0x01, 0x00, 0x00];

    /// <summary>Length of <see cref="EncryptedTableMagic"/>.</summary>
    public const int EncryptedTableMagicLength = 8;

    /// <summary>The key for tables in metadata.</summary>
    public const string TablesKey = "tables";

    /// <summary>
    /// The metadata key marking whether a database stores ULIDs in the ULID-spec-compliant encoding.
    /// Absent in databases created before 1.9.5; those may contain legacy-encoded ULIDs and should be
    /// migrated with <c>Database.MigrateLegacyUlids()</c>.
    /// </summary>
    public const string UlidSpecMarkerKey = "ulidSpec";

    /// <summary>
    /// The name of the auto-generated internal row identifier column.
    /// Injected as primary key when a table is created without an explicit PRIMARY KEY.
    /// Uses ULID type for globally unique, lexicographically sortable identifiers.
    /// Hidden from SELECT * but queryable via explicit column reference.
    /// </summary>
    public const string InternalRowIdColumnName = "_rowid";
}
