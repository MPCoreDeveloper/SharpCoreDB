// <copyright file="VectorStorageFormat.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Binary format constants for vector storage and index persistence.
/// All formats use little-endian byte order (matching .NET default).
/// </summary>
public static class VectorStorageFormat
{
    // ── Vector Blob Format ──

    /// <summary>Magic bytes for vector data blobs: "VEC\0".</summary>
    public static ReadOnlySpan<byte> VectorMagic => "VEC\0"u8;

    /// <summary>Current vector blob format version.</summary>
    public const byte VectorFormatVersion = 1;

    /// <summary>Vector blob header size: 4 (magic) + 1 (version) + 1 (flags) + 2 (reserved) + 4 (dims) = 12 bytes.</summary>
    public const int VectorHeaderSize = 12;

    // ── HNSW Index Format ──

    /// <summary>Magic bytes for HNSW index files: "HNSW".</summary>
    public static ReadOnlySpan<byte> HnswMagic => "HNSW"u8;

    /// <summary>Current HNSW index format version.</summary>
    public const byte HnswFormatVersion = 1;

    /// <summary>HNSW index header size in bytes.</summary>
    public const int HnswHeaderSize = 52;

    // ── Quantization Format ──

    /// <summary>Magic bytes for scalar quantization calibration: "SQ8\0".</summary>
    public static ReadOnlySpan<byte> ScalarQuantMagic => "SQ8\0"u8;

    /// <summary>Magic bytes for binary quantization calibration: "BQ1\0".</summary>
    public static ReadOnlySpan<byte> BinaryQuantMagic => "BQ1\0"u8;

    // ── Table Metadata Keys ──

    /// <summary>Metadata key prefix for vector index definitions on a table.</summary>
    public const string VectorIndexPrefix = "vector_index:";

    /// <summary>Metadata key suffix for the index name.</summary>
    public const string IndexNameSuffix = ":name";

    /// <summary>Metadata key suffix for the index type (FLAT or HNSW).</summary>
    public const string IndexTypeSuffix = ":type";

    /// <summary>Metadata key suffix for the original CREATE VECTOR INDEX SQL.</summary>
    public const string IndexSqlSuffix = ":sql";

    // ── Limits ──

    /// <summary>Maximum supported vector dimensions.</summary>
    public const int MaxDimensions = 65536;

    /// <summary>Maximum HNSW M parameter.</summary>
    public const int MaxM = 256;

    /// <summary>Maximum HNSW ef parameter.</summary>
    public const int MaxEf = 10000;
}
