// <copyright file="ScdbStructureLayoutTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests.Storage;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpCoreDB.Storage.Scdb;
using Xunit;

/// <summary>
/// Offset and size guards for the on-disk structures in <c>ScdbStructures.cs</c>.
/// <para>
/// The layout of these structs is a FORMAT contract rather than an implementation detail: a field that moves
/// changes what every file already on disk means. The guard below covers
/// <see cref="TableMetadataEntry.Reserved"/> specifically, because it stopped being an
/// <c>unsafe fixed byte[22]</c> buffer and became an <see cref="InlineArrayAttribute"/> region (Sonar S6640) —
/// a change that is only acceptable while the 22 bytes stay at 0x92 with the bytes before them untouched.
/// </para>
/// <para>
/// <see cref="TableMetadataEntry.SIZE"/> (256) is the on-disk STRIDE the directory manager reads and writes per
/// entry (<c>TableDirectoryManager</c>); the CLR struct is the packed sum of its fields and is smaller, so the
/// two are asserted separately instead of assumed equal.
/// </para>
/// </summary>
public sealed class ScdbStructureLayoutTests
{
    /// <summary>0x8E: the §4b inline capacity, carved out of what used to be the reserved region.</summary>
    [Fact]
    public void TableMetadataEntry_FixedWidthInlineCapacity_KeepsItsDocumentedOffset()
    {
        Assert.Equal(0x8E, Marshal.OffsetOf<TableMetadataEntry>(nameof(TableMetadataEntry.FixedWidthInlineValueBytes)).ToInt32());
    }

    /// <summary>0x92: the reserved region — same offset it had as a fixed buffer, still 22 bytes wide.</summary>
    [Fact]
    public void TableMetadataEntry_ReservedRegion_KeepsItsOffsetAndIts22Bytes()
    {
        Assert.Equal(0x92, Marshal.OffsetOf<TableMetadataEntry>(nameof(TableMetadataEntry.Reserved)).ToInt32());
        Assert.Equal(22, Unsafe.SizeOf<TableMetadataEntryReservedRegion>());
    }

    /// <summary>Reserved is the last field, so the packed size is 0xA8 and the 256-byte stride still covers it.</summary>
    [Fact]
    public void TableMetadataEntry_PackedSize_StaysWithinTheOnDiskStride()
    {
        var packedSize = Unsafe.SizeOf<TableMetadataEntry>();

        Assert.Equal(0xA8, packedSize);
        Assert.True(
            packedSize <= TableMetadataEntry.SIZE,
            $"TableMetadataEntry now packs to {packedSize} bytes, which no longer fits the {TableMetadataEntry.SIZE}-byte on-disk stride.");
    }
}
