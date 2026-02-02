// <copyright file="FilePointer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Overflow;

using System;

/// <summary>
/// Pointer to an external file in the FILESTREAM directory.
/// C# 14: Modern record type with required properties.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 6: FILESTREAM support for unlimited row sizes.
/// 
/// Storage layout:
/// - Inline (database): FilePointer structure (~128 bytes)
/// - External (filesystem): Actual file data
/// 
/// This allows rows of ANY size limited only by filesystem (NTFS: 256TB).
/// </remarks>
public sealed record FilePointer
{
    /// <summary>Gets or sets the unique file identifier.</summary>
    public required Guid FileId { get; init; }
    
    /// <summary>Gets or sets the relative path from blobs directory.</summary>
    /// <example>ab/cd/abcdef1234567890.bin</example>
    public required string RelativePath { get; init; }
    
    /// <summary>Gets or sets the file size in bytes.</summary>
    public required long FileSize { get; init; }
    
    /// <summary>Gets or sets when the file was created.</summary>
    public required DateTime CreatedAt { get; init; }
    
    /// <summary>Gets or sets when the file was last accessed.</summary>
    public DateTime LastAccessedAt { get; init; }
    
    /// <summary>Gets or sets the MIME content type.</summary>
    /// <example>image/jpeg, application/pdf, application/octet-stream</example>
    public string ContentType { get; init; } = "application/octet-stream";
    
    /// <summary>Gets or sets the SHA-256 checksum for integrity validation.</summary>
    public required byte[] Checksum { get; init; }
    
    // ======================================================================
    // Reference tracking for orphan detection
    // ======================================================================
    
    /// <summary>Gets or sets the owning row ID.</summary>
    public required long RowId { get; init; }
    
    /// <summary>Gets or sets the owning table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Gets or sets the column name containing this file.</summary>
    public required string ColumnName { get; init; }
    
    /// <summary>
    /// Generates the relative path for a file ID.
    /// </summary>
    /// <param name="fileId">The file GUID.</param>
    /// <returns>Relative path like "ab/cd/abcdef1234.bin"</returns>
    public static string GenerateRelativePath(Guid fileId)
    {
        var guid = fileId.ToString("N");  // No hyphens
        var sub1 = guid.Substring(0, 2);
        var sub2 = guid.Substring(2, 2);
        return $"{sub1}/{sub2}/{guid}.bin";
    }
}

/// <summary>
/// Storage mode for row data.
/// </summary>
public enum StorageMode
{
    /// <summary>Data stored inline in data page (0-4KB).</summary>
    Inline = 0,
    
    /// <summary>Data stored in overflow page chain (4KB-256KB).</summary>
    Overflow = 1,
    
    /// <summary>Data stored in external file (256KB+).</summary>
    FileStream = 2,
}

/// <summary>
/// Policy for handling missing files.
/// </summary>
public enum MissingFilePolicy
{
    /// <summary>Log warning only, no action.</summary>
    AlertOnly = 0,
    
    /// <summary>Set column to NULL in database.</summary>
    SetNull = 1,
    
    /// <summary>Delete entire row from database.</summary>
    DeleteRow = 2,
}

/// <summary>
/// Options for orphan cleanup.
/// </summary>
public sealed record CleanupOptions
{
    /// <summary>Gets or sets whether to perform dry run (no actual deletion).</summary>
    public bool DryRun { get; init; } = true;
    
    /// <summary>Gets or sets the retention period for orphaned files.</summary>
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(7);
    
    /// <summary>Gets or sets the policy for missing files.</summary>
    public MissingFilePolicy MissingPolicy { get; init; } = MissingFilePolicy.AlertOnly;
}

/// <summary>
/// Options for file recovery from backup.
/// </summary>
public sealed record RecoveryOptions
{
    /// <summary>Gets or sets the backup path to restore from.</summary>
    public required string BackupPath { get; init; }
    
    /// <summary>Gets or sets whether to verify checksums before restoring.</summary>
    public bool VerifyChecksums { get; init; } = true;
    
    /// <summary>Gets or sets whether to skip existing files.</summary>
    public bool SkipExisting { get; init; } = true;
}

/// <summary>
/// Overflow page header structure.
/// </summary>
public struct OverflowPageHeader
{
    /// <summary>Magic number: 0x4F564552 ("OVER").</summary>
    public uint Magic;
    
    /// <summary>Format version.</summary>
    public ushort Version;
    
    /// <summary>This page ID.</summary>
    public ulong PageId;
    
    /// <summary>Parent row ID.</summary>
    public ulong RowId;
    
    /// <summary>Sequence number in chain (0, 1, 2...).</summary>
    public uint SequenceNum;
    
    /// <summary>Next page ID (or 0 if last).</summary>
    public ulong NextPage;
    
    /// <summary>Data length in this page.</summary>
    public uint DataLength;
    
    /// <summary>CRC32 checksum of data.</summary>
    public uint Checksum;
    
    /// <summary>Overflow page magic number.</summary>
    public const uint OVERFLOW_MAGIC = 0x4F564552;  // "OVER"
    
    /// <summary>Current version.</summary>
    public const ushort CURRENT_VERSION = 1;
    
    /// <summary>Header size in bytes (Magic:4 + Version:2 + PageId:8 + RowId:8 + SequenceNum:4 + NextPage:8 + DataLength:4 + Checksum:4 = 42).</summary>
    public const int HEADER_SIZE = 42;
}
