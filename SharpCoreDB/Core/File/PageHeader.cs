// <copyright file="PageHeader.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.File;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Page header structure for zero-allocation serialization using MemoryMarshal.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PageHeader
{
    /// <summary>
    /// Magic number for page validation (0x5343DB = "SCDB").
    /// </summary>
    public uint MagicNumber;

    /// <summary>
    /// Page version number for schema evolution.
    /// </summary>
    public ushort Version;

    /// <summary>
    /// Page type (0=Data, 1=Index, 2=Overflow).
    /// </summary>
    public byte PageType;

    /// <summary>
    /// Flags for page state (0x01=Dirty, 0x02=Compressed, 0x04=Encrypted).
    /// </summary>
    public byte Flags;

    /// <summary>
    /// Number of rows/entries in this page.
    /// </summary>
    public ushort EntryCount;

    /// <summary>
    /// Free space offset in bytes from start of page.
    /// </summary>
    public ushort FreeSpaceOffset;

    /// <summary>
    /// Checksum (CRC32 or FNV-1a hash of page data).
    /// </summary>
    public uint Checksum;

    /// <summary>
    /// Transaction ID that last modified this page.
    /// </summary>
    public ulong TransactionId;

    /// <summary>
    /// Link to next page (for overflow or linked lists).
    /// </summary>
    public uint NextPageId;

    /// <summary>
    /// Reserved space for future use.
    /// </summary>
    public uint Reserved1;

    /// <summary>
    /// Reserved space for future use.
    /// </summary>
    public uint Reserved2;

    /// <summary>
    /// The size of the page header in bytes (40 bytes).
    /// </summary>
    public const int Size = 40;

    /// <summary>
    /// Magic number constant for validation.
    /// </summary>
    public const uint MagicConst = 0x5343_4442; // "SCDB" in hex

    /// <summary>
    /// Current page format version.
    /// </summary>
    public const ushort CurrentVersion = 1;

    /// <summary>
    /// Creates a new page header with default values.
    /// </summary>
    /// <param name="pageType">The page type.</param>
    /// <param name="transactionId">The transaction ID.</param>
    /// <returns>A new PageHeader.</returns>
    public static PageHeader Create(byte pageType, ulong transactionId)
    {
        return new PageHeader
        {
            MagicNumber = MagicConst,
            Version = CurrentVersion,
            PageType = pageType,
            Flags = 0,
            EntryCount = 0,
            FreeSpaceOffset = Size,
            Checksum = 0,
            TransactionId = transactionId,
            NextPageId = 0,
            Reserved1 = 0,
            Reserved2 = 0,
        };
    }

    /// <summary>
    /// Validates the page header.
    /// </summary>
    /// <returns>True if header is valid.</returns>
    public readonly bool IsValid()
    {
        return MagicNumber == MagicConst && Version == CurrentVersion;
    }
}

/// <summary>
/// Page type enumeration.
/// </summary>
public enum PageType : byte
{
    /// <summary>
    /// Data page containing rows.
    /// </summary>
    Data = 0,

    /// <summary>
    /// Index page (B-tree node).
    /// </summary>
    Index = 1,

    /// <summary>
    /// Overflow page for large values.
    /// </summary>
    Overflow = 2,

    /// <summary>
    /// Free list page.
    /// </summary>
    FreeList = 3,
}

/// <summary>
/// Page flags.
/// </summary>
[Flags]
public enum PageFlags : byte
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Page has been modified since last checkpoint.
    /// </summary>
    Dirty = 0x01,

    /// <summary>
    /// Page data is compressed.
    /// </summary>
    Compressed = 0x02,

    /// <summary>
    /// Page data is encrypted.
    /// </summary>
    Encrypted = 0x04,
}
