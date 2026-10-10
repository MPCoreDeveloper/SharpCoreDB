// <copyright file="ChunkRecords.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore records.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Records;

using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// One stored chunk, as it will be returned with a hit. Holds the stable source path, source id,
/// node id, ordinal, text and text hash, so a historical exact-version read is answerable from the
/// artifact alone — without consulting mutable source metadata.
/// </summary>
public sealed record ChunkRecord
{
    /// <summary>The chunk identifier.</summary>
    [JsonRequired]
    public string ChunkId { get; init; } = string.Empty;

    /// <summary>The stable source identifier.</summary>
    [JsonRequired]
    public string SourceId { get; init; } = string.Empty;

    /// <summary>The stable source path.</summary>
    [JsonRequired]
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Optional node identifier (e.g. a graph node).</summary>
    public string? NodeId { get; init; }

    /// <summary>The chunk's ordinal within its source.</summary>
    [JsonRequired]
    public uint Ordinal { get; init; }

    /// <summary>The chunk text.</summary>
    [JsonRequired]
    public string Text { get; init; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of <see cref="Text"/>, so a hit can be shown to be the indexed bytes.</summary>
    [JsonRequired]
    public string TextSha256 { get; init; } = string.Empty;
}

/// <summary>The serialized pair: the JSON-Lines body and the fixed-width offset index.</summary>
public sealed record RecordsBlob(byte[] Body, byte[] Index);

/// <summary>
/// The record store, format <c>sharpcoredb-records@1</c>: a length-prefixed JSON-Lines body plus a
/// fixed-width offset index, so record N is reachable in O(1) without parsing the whole body.
/// </summary>
public static class ChunkRecords
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Serialize records in the given order (order is preserved exactly).</summary>
    public static RecordsBlob Write(IReadOnlyList<ChunkRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var body = new List<byte>(records.Count * 64);
        var index = new byte[records.Count * sizeof(long)];

        for (int i = 0; i < records.Count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(index.AsSpan(i * sizeof(long)), body.Count);

            byte[] line = JsonSerializer.SerializeToUtf8Bytes(records[i], Options);
            if (line.AsSpan().IndexOf((byte)'\n') >= 0)
            {
                // A newline here would silently shift every later offset.
                throw ArtifactException.Invalid($"record {records[i].ChunkId} serialized with an embedded newline");
            }

            body.AddRange(line);
            body.Add((byte)'\n');
        }

        return new RecordsBlob([.. body], index);
    }

    /// <summary>
    /// Read every record back. Validates the index against the body rather than trusting either:
    /// both arrive from an untrusted store.
    /// </summary>
    public static IReadOnlyList<ChunkRecord> Read(byte[] body, byte[] index)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(index);

        if (index.Length % sizeof(long) != 0)
        {
            throw ArtifactException.Integrity($"record index is {index.Length} bytes, not a multiple of 8");
        }

        int count = index.Length / sizeof(long);
        var records = new List<ChunkRecord>(Math.Min(count, 4096));

        for (int i = 0; i < count; i++)
        {
            long start = BinaryPrimitives.ReadInt64LittleEndian(index.AsSpan(i * sizeof(long)));
            if (start < 0 || start > body.Length)
            {
                throw ArtifactException.Integrity($"record {i} starts at {start}, past the {body.Length}-byte body");
            }

            int end = Array.IndexOf(body, (byte)'\n', (int)start);
            if (end < 0)
            {
                throw ArtifactException.Integrity($"record {i} has no terminator");
            }

            try
            {
                ChunkRecord record = JsonSerializer.Deserialize<ChunkRecord>(body.AsSpan((int)start, end - (int)start), Options)
                    ?? throw ArtifactException.Integrity($"record {i} is empty");
                records.Add(record);
            }
            catch (JsonException e)
            {
                throw ArtifactException.Integrity($"record {i} does not parse: {e.Message}");
            }
        }

        return records;
    }
}
