// <copyright file="ArtifactStoreTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Cases ported from munarium-datastore records.rs/store.rs/lib.rs.
// </copyright>

using System.Buffers.Binary;
using SharpCoreDB.VectorSearch.Artifacts;
using SharpCoreDB.VectorSearch.Records;
using SharpCoreDB.VectorSearch.Store;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>Record store, artifact store and cache key.</summary>
public class ArtifactStoreTests
{
    private static ChunkRecord Rec(string id) => new()
    {
        ChunkId = id,
        SourceId = "src-1",
        SourcePath = "a/b.md",
        Ordinal = 0,
        Text = $"body of {id}",
        TextSha256 = new string('0', 64),
    };

    [Fact]
    public void Records_round_trip_in_order()
    {
        var records = new List<ChunkRecord> { Rec("c1"), Rec("c2"), Rec("c3") };
        RecordsBlob blob = ChunkRecords.Write(records);

        Assert.Equal(records, ChunkRecords.Read(blob.Body, blob.Index));
    }

    [Fact]
    public void An_empty_set_round_trips()
    {
        RecordsBlob blob = ChunkRecords.Write([]);
        Assert.Empty(ChunkRecords.Read(blob.Body, blob.Index));
    }

    [Fact]
    public void A_truncated_index_is_refused_rather_than_guessed()
    {
        RecordsBlob blob = ChunkRecords.Write([Rec("c1")]);
        var ex = Assert.Throws<ArtifactException>(() => ChunkRecords.Read(blob.Body, blob.Index[..7]));
        Assert.Contains("multiple of 8", ex.Message);
    }

    [Fact]
    public void An_offset_past_the_body_is_refused()
    {
        RecordsBlob blob = ChunkRecords.Write([Rec("c1")]);
        var bad = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bad, 9_999);
        var ex = Assert.Throws<ArtifactException>(() => ChunkRecords.Read(blob.Body, bad));
        Assert.Contains("past the", ex.Message);
    }

    [Fact]
    public void A_corrupt_body_is_refused()
    {
        RecordsBlob blob = ChunkRecords.Write([Rec("c1")]);
        byte[] body = (byte[])blob.Body.Clone();
        body[3] = (byte)'!';

        Assert.Throws<ArtifactException>(() => ChunkRecords.Read(body, blob.Index));
    }

    [Fact]
    public void Unicode_text_survives_the_round_trip_byte_for_byte()
    {
        ChunkRecord record = Rec("c1") with { Text = "Café 東京 — “quoted”…" };
        RecordsBlob blob = ChunkRecords.Write([record]);

        Assert.Equal(record.Text, ChunkRecords.Read(blob.Body, blob.Index)[0].Text);
    }

    [Fact]
    public void Local_store_round_trips_and_reads_a_range()
    {
        string dir = TempDir();
        try
        {
            var store = new LocalFileStore(dir);
            store.PutComponent("records/chunks.bin", "0123456789"u8);

            Assert.True(store.Exists("records/chunks.bin"));
            Assert.Equal(10, store.HeadComponent("records/chunks.bin"));
            Assert.Equal("234"u8.ToArray(), store.GetComponent("records/chunks.bin", new ByteRange(2, 5)));
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void The_store_refuses_traversal_even_though_the_caller_should_have_checked()
    {
        string dir = TempDir();
        try
        {
            var store = new LocalFileStore(dir);
            foreach (string bad in new[] { "../escape.bin", "/etc/passwd", "a/../../b" })
            {
                Assert.Throws<ArtifactException>(() => store.PutComponent(bad, "x"u8));
                Assert.Throws<ArtifactException>(() => store.GetComponent(bad));
            }
        }
        finally
        {
            Delete(dir);
        }
    }

    [Fact]
    public void The_cache_key_refuses_path_traversal_in_every_element()
    {
        foreach (ArtifactCacheKey bad in new[]
        {
            new ArtifactCacheKey("..", "idx2-abc", "deadbeef"),
            new ArtifactCacheKey("dom", "../../etc", "deadbeef"),
            new ArtifactCacheKey("dom", "idx2-abc", "a/b"),
            new ArtifactCacheKey(string.Empty, "idx2-abc", "deadbeef"),
        })
        {
            Assert.Throws<ArtifactException>(() => bad.L1RelativePath());
        }
    }

    [Fact]
    public void A_good_cache_key_produces_a_three_element_path()
    {
        var key = new ArtifactCacheKey("dom-1", "idx2-abc", "deadbeef");
        Assert.Equal("dom-1/idx2-abc/deadbeef", key.L1RelativePath());
    }

    [Fact]
    public void Identical_content_in_two_domains_is_two_keys()
    {
        var a = new ArtifactCacheKey("dom-a", "idx2-v1", "samehash");
        var b = new ArtifactCacheKey("dom-b", "idx2-v1", "samehash");

        Assert.NotEqual(a, b);
        Assert.NotEqual(a.L1RelativePath(), b.L1RelativePath());
    }

    [Fact]
    public void Cache_key_display_does_not_leak_the_whole_isolation_domain()
    {
        var key = new ArtifactCacheKey(
            "tenant-with-a-long-identifying-name",
            "idx2-v1",
            "0123456789abcdef0123456789abcdef");

        string shown = key.ToString();
        Assert.DoesNotContain("identifying-name", shown);
        Assert.Contains("idx2-v1", shown);
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "scdb-artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Delete(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
