// <copyright file="HydrationCacheTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Cases ported from munarium-datastore hydrate.rs.
// </copyright>

using SharpCoreDB.VectorSearch.Artifacts;
using SharpCoreDB.VectorSearch.Storage;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>L1 hydration: sealing, quarantine, single-flight and crash reconciliation.</summary>
public class HydrationCacheTests
{
    private static ArtifactComponent Component(string path, byte[] bytes)
        => new()
        {
            Path = path,
            BytesLen = bytes.Length,
            Sha256 = ArtifactVerifier.Sha256Hex(bytes),
            Purpose = ArtifactComponentPurpose.Records,
        };

    [Fact]
    public void Hydration_seals_the_artifact_and_marks_it_complete()
    {
        string root = TempDir();
        try
        {
            var cache = new HydrationCache(root);
            var key = new ArtifactCacheKey("dom", "idx2-v1", "abcdef123456");
            byte[] bytes = "hello records"u8.ToArray();
            ArtifactComponent component = Component("records/chunks.bin", bytes);

            Assert.Equal(Residency.Absent, cache.StateOf(key));

            ResidentArtifact resident = cache.Hydrate(key, () => [new ComponentBytes(component, bytes)]);

            Assert.Equal(Residency.Resident, cache.StateOf(key));
            Assert.True(File.Exists(Path.Combine(resident.Path, HydrationCache.CompleteMarker)));
            Assert.True(File.Exists(Path.Combine(resident.Path, "records", "chunks.bin")));
            Assert.False(Directory.Exists(resident.Path + ".partial"));
            Assert.Equal(bytes.Length, resident.Bytes);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_component_that_fails_verification_is_quarantined()
    {
        string root = TempDir();
        try
        {
            var cache = new HydrationCache(root);
            var key = new ArtifactCacheKey("dom", "idx2-v1", "tampered00001");
            byte[] declared = "trusted"u8.ToArray();
            ArtifactComponent component = Component("records/chunks.bin", declared);
            byte[] tampered = "EVIL"u8.ToArray();

            Assert.Throws<ArtifactException>(() => cache.Hydrate(key, () => [new ComponentBytes(component, tampered)]));

            Assert.Equal(Residency.Quarantined, cache.StateOf(key));
            Assert.False(Directory.Exists(cache.Leaf(key) + ".partial"));

            // A quarantined key is refused, not silently retried.
            Assert.Throws<ArtifactException>(() => cache.Hydrate(key, () => [new ComponentBytes(component, declared)]));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Two_concurrent_hydrations_of_one_key_share_a_single_fetch()
    {
        string root = TempDir();
        try
        {
            var cache = new HydrationCache(root);
            var key = new ArtifactCacheKey("dom", "idx2-v1", "concurrent001");
            byte[] bytes = "payload"u8.ToArray();
            ArtifactComponent component = Component("records/chunks.bin", bytes);

            int calls = 0;
            using var barrier = new Barrier(8);
            var results = new ResidentArtifact[8];

            Parallel.For(0, 8, i =>
            {
                barrier.SignalAndWait();
                results[i] = cache.Hydrate(key, () =>
                {
                    Interlocked.Increment(ref calls);
                    Thread.Sleep(50);
                    return [new ComponentBytes(component, bytes)];
                });
            });

            Assert.Equal(1, calls);
            Assert.All(results, r => Assert.NotNull(r));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Reconcile_removes_partial_directories_left_by_a_crash()
    {
        string root = TempDir();
        try
        {
            string partial = Path.Combine(root, "dom", "idx2-v1", "deadbeef.partial");
            Directory.CreateDirectory(partial);
            File.WriteAllText(Path.Combine(partial, "half-written.bin"), "x");

            var cache = new HydrationCache(root);
            int removed = cache.ReconcilePartials();

            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(partial));
        }
        finally
        {
            Delete(root);
        }
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "scdb-hydrate-" + Guid.NewGuid().ToString("N"));
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
