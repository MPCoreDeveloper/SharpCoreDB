// <copyright file="DiskAnnTuningTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Tests for the DiskANN recall/latency tuning knobs:
// the per-query search-list override and the build-pass count.
// </copyright>

using SharpCoreDB.VectorSearch.Index;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

public class DiskAnnTuningTests
{
    private const int Dims = 16;
    private const int Count = 2000;
    private const int K = 10;

    /// <summary>
    /// A wider search list lets the beam explore more of the graph, so recall must never regress.
    /// </summary>
    [Fact]
    public void Search_WithWiderSearchList_DoesNotReduceRecall()
    {
        (DiskAnnIndex disk, FlatIndex flat, float[][] queries) = Build(passes: 2, querySearchList: 16);

        double narrow = Recall(disk, flat, queries, beam: 16);
        double wide = Recall(disk, flat, queries, beam: 256);

        Assert.True(
            wide >= narrow,
            $"A wider beam lost recall: beam=16 gave {narrow:P1}, beam=256 gave {wide:P1}");
    }

    /// <summary>
    /// The configured <see cref="DiskAnnConfig.QuerySearchListSize"/> is a floor, not a ceiling: a
    /// caller-supplied beam may be larger or smaller than it.
    /// </summary>
    [Fact]
    public void Search_WithExplicitSearchListSize_OverridesConfiguredFloor()
    {
        (DiskAnnIndex disk, FlatIndex flat, float[][] queries) = Build(passes: 2, querySearchList: 128);

        Assert.Equal(128, disk.Config.QuerySearchListSize);

        // A beam well above the floor must be honoured, and one below it must still work.
        double above = Recall(disk, flat, queries, beam: 512);
        double below = Recall(disk, flat, queries, beam: 8);

        Assert.True(above >= below, $"Beam 512 ({above:P1}) should beat beam 8 ({below:P1})");
        Assert.NotEmpty(disk.Search(queries[0], K, 8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Search_WithNonPositiveSearchListSize_Throws(int beam)
    {
        (DiskAnnIndex disk, _, float[][] queries) = Build(passes: 1, querySearchList: 32);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => disk.Search(queries[0], K, beam));
    }

    /// <summary>
    /// An extra Vamana pass re-evaluates long-range links against the grown graph, so it must not
    /// make the graph worse — this is the cheapest recall lever (build time is linear in passes).
    /// </summary>
    [Fact]
    public void Build_WithMorePasses_DoesNotReduceRecall()
    {
        (DiskAnnIndex onePass, FlatIndex flat1, float[][] queries) = Build(passes: 1, querySearchList: 32);
        (DiskAnnIndex threePass, FlatIndex flat3, _) = Build(passes: 3, querySearchList: 32);
        using (onePass)
        using (flat1)
        using (threePass)
        using (flat3)
        {
            double one = Recall(onePass, flat1, queries, beam: 32);
            double three = Recall(threePass, flat3, queries, beam: 32);

            Assert.True(
                three >= one,
                $"Three passes ({three:P1}) should not be worse than one pass ({one:P1})");
        }
    }

    /// <summary>
    /// The back-edge phase runs in parallel, so this guards the determinism of a rebuild: two
    /// indexes over identical input must return identical ids and distances, bit for bit.
    /// </summary>
    [Fact]
    public void Build_IsDeterministic_AcrossRebuilds()
    {
        (DiskAnnIndex a, FlatIndex flatA, float[][] queries) = Build(passes: 2, querySearchList: 64);
        (DiskAnnIndex b, FlatIndex flatB, _) = Build(passes: 2, querySearchList: 64);
        using (a)
        using (flatA)
        using (b)
        using (flatB)
        {
            foreach (float[] query in queries)
            {
                var first = a.Search(query, K, 64);
                var second = b.Search(query, K, 64);

                Assert.Equal(first.Count, second.Count);
                for (int i = 0; i < first.Count; i++)
                {
                    Assert.Equal(first[i].Id, second[i].Id);
                    Assert.Equal(first[i].Distance, second[i].Distance);
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Config_WithNonPositiveBuildPasses_Throws(int passes)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiskAnnIndex(new DiskAnnConfig { Dimensions = 4, BuildPasses = passes }));
    }

    private static (DiskAnnIndex Disk, FlatIndex Flat, float[][] Queries) Build(int passes, int querySearchList)
    {
        var rng = new Random(11);
        var disk = new DiskAnnIndex(new DiskAnnConfig
        {
            Dimensions = Dims,
            MaxNeighbors = 32,
            ConstructionSearchListSize = 64,
            QuerySearchListSize = querySearchList,
            BuildPasses = passes,
        });

        var flat = new FlatIndex(Dims, DistanceFunction.Cosine);
        var vectors = new float[Count][];
        for (int i = 0; i < Count; i++)
        {
            var v = new float[Dims];
            for (int d = 0; d < Dims; d++)
            {
                v[d] = (float)(rng.NextDouble() * 2 - 1);
            }

            vectors[i] = v;
            disk.Add(i, v);
            flat.Add(i, v);
        }

        var queries = new float[40][];
        for (int q = 0; q < queries.Length; q++)
        {
            queries[q] = vectors[rng.Next(Count)];
        }

        return (disk, flat, queries);
    }

    private static double Recall(DiskAnnIndex disk, FlatIndex flat, float[][] queries, int beam)
    {
        int matched = 0;
        foreach (float[] query in queries)
        {
            var truth = new HashSet<long>();
            foreach (VectorSearchResult exact in flat.Search(query, K))
            {
                truth.Add(exact.Id);
            }

            foreach (VectorSearchResult approx in disk.Search(query, K, beam))
            {
                if (truth.Contains(approx.Id))
                {
                    matched++;
                }
            }
        }

        return (double)matched / (queries.Length * K);
    }
}
