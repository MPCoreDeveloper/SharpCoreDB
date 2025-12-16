// <copyright file="WalOperationType.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Defines the types of operations that can be logged in the hybrid WAL.
/// Each operation type has specific handling during recovery.
/// 
/// Operation Type Byte Values:
/// - 0x01-0x0F: Columnar storage operations
/// - 0x11-0x1F: Page-based storage operations  
/// - 0x20-0x2F: System operations (checkpoint, etc.)
/// </summary>
public enum WalOperationType : byte
{
    /// <summary>
    /// Invalid/unknown operation - should never appear in valid WAL.
    /// </summary>
    Invalid = 0x00,

    // ==================== COLUMNAR OPERATIONS (0x01-0x0F) ====================

    /// <summary>
    /// INSERT operation for columnar storage.
    /// Data: Serialized record to append to .dat file
    /// Recovery: Append record to table data file
    /// </summary>
    InsertColumnar = 0x01,

    /// <summary>
    /// UPDATE operation for columnar storage.
    /// Data: Record identifier + new record data
    /// Recovery: Append updated record, mark old as deleted
    /// Note: Columnar updates are append-only
    /// </summary>
    UpdateColumnar = 0x02,

    /// <summary>
    /// DELETE operation for columnar storage.
    /// Data: Record identifier (position or primary key)
    /// Recovery: Mark record as deleted in index
    /// Note: Actual data remains in file (tombstone)
    /// </summary>
    DeleteColumnar = 0x03,

    // ==================== PAGE-BASED OPERATIONS (0x11-0x1F) ====================

    /// <summary>
    /// INSERT operation for page-based storage.
    /// Data: Page ID + slot number + serialized record
    /// Recovery: Re-insert record into specified page
    /// </summary>
    InsertPage = 0x11,

    /// <summary>
    /// In-place UPDATE operation for page-based storage.
    /// Data: Page ID + slot number + new record data (or delta)
    /// Recovery: Update record in place within page
    /// </summary>
    UpdatePageInPlace = 0x12,

    /// <summary>
    /// DELETE operation for page-based storage.
    /// Data: Page ID + slot number
    /// Recovery: Mark slot as deleted, add to free space
    /// </summary>
    DeletePage = 0x13,

    /// <summary>
    /// Page allocation operation.
    /// Data: Page ID + table ID + page type
    /// Recovery: Re-allocate page for table
    /// </summary>
    AllocatePage = 0x14,

    /// <summary>
    /// Page deallocation/free operation.
    /// Data: Page ID
    /// Recovery: Add page to free list
    /// </summary>
    FreePage = 0x15,

    /// <summary>
    /// Page compaction operation.
    /// Data: Page ID + compacted page data
    /// Recovery: Replace page with compacted version
    /// </summary>
    CompactPage = 0x16,

    // ==================== SYSTEM OPERATIONS (0x20-0x2F) ====================

    /// <summary>
    /// CHECKPOINT marker.
    /// Data: Checkpoint metadata (LSN, dirty pages flushed, etc.)
    /// Recovery: All operations before this LSN are guaranteed on disk
    /// </summary>
    Checkpoint = 0x20,

    /// <summary>
    /// Transaction BEGIN marker.
    /// Data: Transaction ID + isolation level
    /// Recovery: Used for transaction rollback
    /// </summary>
    TransactionBegin = 0x21,

    /// <summary>
    /// Transaction COMMIT marker.
    /// Data: Transaction ID
    /// Recovery: Ensure all transaction operations are applied
    /// </summary>
    TransactionCommit = 0x22,

    /// <summary>
    /// Transaction ROLLBACK marker.
    /// Data: Transaction ID
    /// Recovery: Undo all operations for this transaction
    /// </summary>
    TransactionRollback = 0x23,

    /// <summary>
    /// Schema change operation (CREATE TABLE, ALTER TABLE, etc.).
    /// Data: DDL statement + table metadata
    /// Recovery: Re-apply schema change
    /// </summary>
    SchemaChange = 0x24,

    /// <summary>
    /// Index creation/modification.
    /// Data: Index metadata + index type
    /// Recovery: Rebuild index
    /// </summary>
    IndexOperation = 0x25,

    // ==================== FUTURE OPERATIONS (0x30-0xFF) ====================

    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Reserved = 0xFF
}

/// <summary>
/// Extension methods for WalOperationType.
/// </summary>
public static class WalOperationTypeExtensions
{
    /// <summary>
    /// Returns whether this operation type is for columnar storage.
    /// </summary>
    public static bool IsColumnar(this WalOperationType opType)
    {
        return opType >= WalOperationType.InsertColumnar && opType <= WalOperationType.DeleteColumnar;
    }

    /// <summary>
    /// Returns whether this operation type is for page-based storage.
    /// </summary>
    public static bool IsPageBased(this WalOperationType opType)
    {
        return opType >= WalOperationType.InsertPage && opType <= WalOperationType.CompactPage;
    }

    /// <summary>
    /// Returns whether this operation type is a system operation.
    /// </summary>
    public static bool IsSystemOperation(this WalOperationType opType)
    {
        return opType >= WalOperationType.Checkpoint && opType <= WalOperationType.IndexOperation;
    }

    /// <summary>
    /// Returns whether this operation type modifies data.
    /// </summary>
    public static bool IsDataModification(this WalOperationType opType)
    {
        return opType switch
        {
            WalOperationType.InsertColumnar => true,
            WalOperationType.UpdateColumnar => true,
            WalOperationType.DeleteColumnar => true,
            WalOperationType.InsertPage => true,
            WalOperationType.UpdatePageInPlace => true,
            WalOperationType.DeletePage => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns a human-readable description of the operation type.
    /// </summary>
    public static string GetDescription(this WalOperationType opType)
    {
        return opType switch
        {
            WalOperationType.InsertColumnar => "INSERT (Columnar)",
            WalOperationType.UpdateColumnar => "UPDATE (Columnar)",
            WalOperationType.DeleteColumnar => "DELETE (Columnar)",
            WalOperationType.InsertPage => "INSERT (Page-Based)",
            WalOperationType.UpdatePageInPlace => "UPDATE (Page-Based In-Place)",
            WalOperationType.DeletePage => "DELETE (Page-Based)",
            WalOperationType.AllocatePage => "ALLOCATE PAGE",
            WalOperationType.FreePage => "FREE PAGE",
            WalOperationType.CompactPage => "COMPACT PAGE",
            WalOperationType.Checkpoint => "CHECKPOINT",
            WalOperationType.TransactionBegin => "BEGIN TRANSACTION",
            WalOperationType.TransactionCommit => "COMMIT TRANSACTION",
            WalOperationType.TransactionRollback => "ROLLBACK TRANSACTION",
            WalOperationType.SchemaChange => "SCHEMA CHANGE",
            WalOperationType.IndexOperation => "INDEX OPERATION",
            _ => $"Unknown (0x{(byte)opType:X2})"
        };
    }

    /// <summary>
    /// Returns whether this operation requires page ID to be set.
    /// </summary>
    public static bool RequiresPageId(this WalOperationType opType)
    {
        return opType switch
        {
            WalOperationType.InsertPage => true,
            WalOperationType.UpdatePageInPlace => true,
            WalOperationType.DeletePage => true,
            WalOperationType.AllocatePage => true,
            WalOperationType.FreePage => true,
            WalOperationType.CompactPage => true,
            _ => false
        };
    }

    /// <summary>
    /// Returns the expected minimum data size for this operation (0 = variable).
    /// </summary>
    public static int MinimumDataSize(this WalOperationType opType)
    {
        return opType switch
        {
            WalOperationType.Checkpoint => 8,  // At least LSN
            WalOperationType.TransactionBegin => 8,  // Transaction ID
            WalOperationType.TransactionCommit => 8,
            WalOperationType.TransactionRollback => 8,
            WalOperationType.FreePage => 0,  // Page ID is in header
            _ => 0  // Variable size
        };
    }
}
