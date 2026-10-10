// <copyright file="DiskAnnIndex.Verify.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

using System.Text;
using System.Text.Json;
using SharpCoreDB.VectorSearch;
using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>Artifact verification, serialization and recall measurement for <see cref="DiskAnnIndex"/>.</summary>
public sealed partial class DiskAnnIndex
{
    /// <summary>
    /// Content-addressed verification: the expected manifest's canonical id is recomputed, so a
    /// manifest whose claimed id no longer matches its own content is reported as tampered/drifted.
    /// </summary>
    /// <param name="expectedManifest">The manifest to verify against.</param>
    /// <returns>A <see cref="BuildResult"/> describing the outcome.</returns>
    public BuildResult Verify(ArtifactManifest expectedManifest)
    {
        ArgumentNullException.ThrowIfNull(expectedManifest);
        ArtifactManifest current = Manifest;

        ArtifactManifest recomputedExpected = expectedManifest.WithComputedId();
        if (current.ArtifactId != recomputedExpected.ArtifactId || current.ArtifactId != expectedManifest.ArtifactId)
        {
            return new BuildResult.VerificationFailed("Artifact ID mismatch - content changed or corrupted", current.ArtifactId);
        }

        if (current.Count != expectedManifest.Count || current.Dimensions != expectedManifest.Dimensions)
        {
            return new BuildResult.LimitExceeded("Count or dimensions mismatch", current.Count, expectedManifest.Count);
        }

        return new BuildResult.Success(current.ArtifactId, 0);
    }

    /// <summary>UTF-8 bytes of the manifest's JSON representation (for storage/persistence).</summary>
    public byte[] SerializeManifest()
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Manifest));

    /// <summary>
    /// Crossover test helper: measures recall@K of this approximate index against the exact
    /// <see cref="FlatIndex"/> oracle over the SAME data, using indexed vectors as queries so the
    /// measurement is deterministic.
    /// </summary>
    /// <param name="exactIndex">The exact index holding the same vectors.</param>
    /// <param name="k">The K in recall@K.</param>
    /// <param name="numQueries">How many stored vectors to use as queries.</param>
    /// <returns>Recall in [0, 1], or 1.0 when there is nothing to measure.</returns>
    public double MeasureRecallAgainstExact(FlatIndex exactIndex, int k, int numQueries = 100)
    {
        ArgumentNullException.ThrowIfNull(exactIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);

        if (_liveCount == 0 || exactIndex.Count == 0)
        {
            return 1.0;
        }

        var liveSlots = new List<int>(_liveCount);
        for (int s = 0; s < _length; s++)
        {
            if (_ids[s] != Tombstone)
            {
                liveSlots.Add(s);
            }
        }

        int queries = Math.Clamp(numQueries, 1, liveSlots.Count);
        var rng = new Random(RngSeed ^ 0x11);
        int dims = _config.Dimensions;
        long hits = 0;
        long total = 0;

        for (int q = 0; q < queries; q++)
        {
            int slot = liveSlots[rng.Next(liveSlots.Count)];
            float[] query = _data.AsSpan(slot * dims, dims).ToArray();

            var approximate = new HashSet<long>();
            foreach (VectorSearchResult r in Search(query, k))
            {
                approximate.Add(r.Id);
            }

            foreach (VectorSearchResult e in exactIndex.Search(query, k))
            {
                total++;
                if (approximate.Contains(e.Id))
                {
                    hits++;
                }
            }
        }

        return total == 0 ? 1.0 : (double)hits / total;
    }
}
