// <copyright file="StorageMode.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Hybrid;

/// <summary>
/// Defines the storage mode for a table in SharpCoreDB.
/// Supports hybrid architecture with columnar and page-based storage.
/// </summary>
public enum StorageMode : byte
{
    /// <summary>
    /// Columnar append-only storage (default).
    /// Optimized for analytics, bulk inserts, and compression.
    /// Uses .dat files with sequential append operations.
    /// </summary>
    Columnar = 0,

    /// <summary>
    /// Page-based storage with in-place updates.
    /// Optimized for OLTP workloads with frequent updates.
    /// Uses .pages files with 8KB pages and B-tree indexes.
    /// </summary>
    PageBased = 1,

    /// <summary>
    /// Hybrid mode (future): mix of columnar and page-based within single table.
    /// Hot data in pages, cold data in columnar format.
    /// </summary>
    Hybrid = 2
}

/// <summary>
/// Extended table metadata with storage mode information.
/// </summary>
public class TableMetadataExtended
{
    /// <summary>
    /// Gets or sets the table ID.
    /// </summary>
    public uint TableId { get; set; }

    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the storage mode for this table.
    /// </summary>
    public StorageMode StorageMode { get; set; } = StorageMode.Columnar;

    /// <summary>
    /// Gets or sets the column definitions.
    /// </summary>
    public List<ColumnMetadata> Columns { get; set; } = new();

    /// <summary>
    /// Gets or sets the total record count.
    /// </summary>
    public long RecordCount { get; set; }

    /// <summary>
    /// Gets or sets the file path for the table data.
    /// For columnar: table_name.dat
    /// For page-based: table_name.pages
    /// </summary>
    public string DataFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary key column names.
    /// </summary>
    public List<string> PrimaryKeyColumns { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this table has indexes.
    /// </summary>
    public bool HasIndexes { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the last modification timestamp.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Serializes metadata to binary format.
    /// </summary>
    /// <returns>Binary representation of metadata.</returns>
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(TableId);
        writer.Write(TableName);
        writer.Write((byte)StorageMode);
        writer.Write(Columns.Count);

        foreach (var col in Columns)
        {
            writer.Write(col.ColumnName);
            writer.Write(col.DataType);
            writer.Write(col.IsNullable);
            writer.Write(col.IsPrimaryKey);
        }

        writer.Write(RecordCount);
        writer.Write(DataFilePath);
        writer.Write(PrimaryKeyColumns.Count);
        foreach (var pk in PrimaryKeyColumns)
        {
            writer.Write(pk);
        }

        writer.Write(HasIndexes);
        writer.Write(CreatedAt.ToBinary());
        writer.Write(ModifiedAt.ToBinary());

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes metadata from binary format.
    /// </summary>
    /// <param name="data">Binary data to deserialize.</param>
    /// <returns>Deserialized metadata object.</returns>
    public static TableMetadataExtended FromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        var metadata = new TableMetadataExtended
        {
            TableId = reader.ReadUInt32(),
            TableName = reader.ReadString(),
            StorageMode = (StorageMode)reader.ReadByte()
        };

        var columnCount = reader.ReadInt32();
        for (int i = 0; i < columnCount; i++)
        {
            metadata.Columns.Add(new ColumnMetadata
            {
                ColumnName = reader.ReadString(),
                DataType = reader.ReadString(),
                IsNullable = reader.ReadBoolean(),
                IsPrimaryKey = reader.ReadBoolean()
            });
        }

        metadata.RecordCount = reader.ReadInt64();
        metadata.DataFilePath = reader.ReadString();

        var pkCount = reader.ReadInt32();
        for (int i = 0; i < pkCount; i++)
        {
            metadata.PrimaryKeyColumns.Add(reader.ReadString());
        }

        metadata.HasIndexes = reader.ReadBoolean();
        metadata.CreatedAt = DateTime.FromBinary(reader.ReadInt64());
        metadata.ModifiedAt = DateTime.FromBinary(reader.ReadInt64());

        return metadata;
    }
}

/// <summary>
/// Column metadata definition.
/// </summary>
public class ColumnMetadata
{
    /// <summary>Gets or sets the column name.</summary>
    public string ColumnName { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the data type.</summary>
    public string DataType { get; set; } = "TEXT";
    
    /// <summary>Gets or sets whether the column is nullable.</summary>
    public bool IsNullable { get; set; } = true;
    
    /// <summary>Gets or sets whether this is a primary key column.</summary>
    public bool IsPrimaryKey { get; set; } = false;
    
    /// <summary>Gets or sets the maximum length for text columns.</summary>
    public int MaxLength { get; set; } = -1;
    
    /// <summary>Gets or sets the default value.</summary>
    public object? DefaultValue { get; set; }
}
