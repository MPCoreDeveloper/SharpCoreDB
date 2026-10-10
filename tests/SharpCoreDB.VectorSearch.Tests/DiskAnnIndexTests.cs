// <copyright file="DiskAnnIndexTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Tests for munarium-inspired DiskAnnIndex (Phase 6).
// </copyright>

using SharpCoreDB.VectorSearch.Artifacts;
using SharpCoreDB.VectorSearch.Index;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

public class DiskAnnIndexTests
{
    [Fact]
    public void DiskAnnIndex_BuildsAndSearches_WithHighRecall()
    {
        // Arrange
        var config = DiskAnnConfig.HighRecall(8); // small dims for test speed
        using var diskAnn = new DiskAnnIndex(config);
        using var flat = new FlatIndex(8, DistanceFunction.Cosine);

        var rng = new Random(42);
        const int count = 1000;
        const int k = 10;

        float[][] vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[8];
            for (int d = 0; d < 8; d++)
                vectors[i][d] = (float)rng.NextDouble() * 2 - 1;

            diskAnn.Add(i, vectors[i]);
            flat.Add(i, vectors[i]);
        }

        // Act — measure recall vs exact flat index (crossover test from munarium)
        double recall = diskAnn.MeasureRecallAgainstExact(flat, k, 50);

        // Assert — should meet target recall from config
        Assert.True(recall >= config.TargetRecall - 0.05, 
            $"Recall was {recall:P2}, expected at least {config.TargetRecall:P2}");
    }

    [Fact]
    public void Verify_ReturnsSuccess_OnMatchingManifest()
    {
        var config = DiskAnnConfig.Default(4);
        using var index = new DiskAnnIndex(config);

        // Add some data to build manifest
        index.Add(1, new float[] { 0.1f, 0.2f, 0.3f, 0.4f });

        var manifest = index.Manifest;
        var result = index.Verify(manifest);

        Assert.IsType<BuildResult.Success>(result);
        var success = (BuildResult.Success)result;
        Assert.Equal(manifest.ArtifactId, success.ArtifactId);
    }

    [Fact]
    public void Verify_ReturnsVerificationFailed_OnMismatchedManifest()
    {
        var config = DiskAnnConfig.Default(4);
        using var index = new DiskAnnIndex(config);
        index.Add(1, new float[] { 0.1f, 0.2f, 0.3f, 0.4f });

        var goodManifest = index.Manifest;
        var badManifest = goodManifest with { Count = 999 }; // tamper count

        var result = index.Verify(badManifest);

        var failed = Assert.IsType<BuildResult.VerificationFailed>(result);
        Assert.Contains("mismatch", failed.Reason.ToLower());
    }

    [Fact]
    public void BuildResult_SupportsExhaustivePatternMatching()
    {
        BuildResult result = new BuildResult.Success("abc123", 42);

        var message = result switch
        {
            BuildResult.Success s => $"Success: {s.ArtifactId} in {s.BuildTimeMs}ms",
            BuildResult.VerificationFailed f => $"Failed: {f.Reason}",
            BuildResult.LimitExceeded l => $"Limit {l.LimitType}: {l.Actual}/{l.Max}",
            _ => "Unknown"
        };

        Assert.Contains("Success", message);
    }

    [Fact]
    public void DiskAnnIndex_ImplementsIVerifiableIndex()
    {
        using var index = new DiskAnnIndex(DiskAnnConfig.Default(8));
        Assert.IsAssignableFrom<IVerifiableIndex>(index);
        Assert.NotNull(index.Manifest);
        Assert.NotEmpty(index.Manifest.ArtifactId);
    }
}
