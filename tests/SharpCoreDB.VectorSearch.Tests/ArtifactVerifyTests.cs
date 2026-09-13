// <copyright file="ArtifactVerifyTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Contract cases ported from munarium-datastore verify.rs.
// </copyright>

using SharpCoreDB.VectorSearch.Artifacts;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>
/// Verification hardening: path normalization, manifest limits, fetched-bytes hashing and
/// per-component integrity.
/// </summary>
public class ArtifactVerifyTests
{
    private static ArtifactComponent Comp(string path, long len = 10, string? sha = null)
        => new()
        {
            Path = path,
            BytesLen = len,
            Sha256 = sha ?? new string('a', 64),
            Purpose = ArtifactComponentPurpose.Records,
        };

    [Fact]
    public void Good_paths_normalize_to_themselves()
    {
        foreach (string p in new[] { "manifest.json", "records/chunks.bin", "lexical/meta.json" })
        {
            Assert.Equal(p, ArtifactVerifier.NormalizeComponentPath(p));
        }
    }

    [Theory]
    [InlineData("../../etc/passwd", "'..'")]
    [InlineData("a/../../b", "'..'")]
    [InlineData("/etc/passwd", "absolute")]
    [InlineData("C:/windows", "drive")]
    [InlineData("c:", "drive")]
    [InlineData("lexical\\meta.json", "backslash")]
    [InlineData("a//b", "empty path segment")]
    [InlineData("./a", "'.'")]
    [InlineData("a/./b", "'.'")]
    [InlineData("file.txt:stream", "colon")]
    [InlineData("", "empty component path")]
    public void Hostile_paths_are_refused(string path, string expectedFragment)
    {
        var ex = Assert.Throws<ArtifactException>(() => ArtifactVerifier.NormalizeComponentPath(path));
        Assert.Equal(ArtifactErrorKind.Path, ex.Kind);
        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Control_characters_are_refused()
    {
        Assert.Throws<ArtifactException>(() => ArtifactVerifier.NormalizeComponentPath("a\u0007b"));
        Assert.Throws<ArtifactException>(() => ArtifactVerifier.NormalizeComponentPath("a\nb"));
    }

    [Fact]
    public void Manifest_bytes_must_hash_to_the_claimed_id()
    {
        byte[] bytes = "{}"u8.ToArray();
        string real = ArtifactVerifier.Sha256Hex(bytes);

        ArtifactVerifier.VerifyManifestBytes(bytes, real);

        var ex = Assert.Throws<ArtifactException>(
            () => ArtifactVerifier.VerifyManifestBytes(bytes, new string('0', 64)));
        Assert.Equal(ArtifactErrorKind.Integrity, ex.Kind);
        Assert.Contains("hash to", ex.Message);
    }

    [Fact]
    public void Component_length_and_hash_are_both_checked()
    {
        byte[] bytes = "hello"u8.ToArray();
        var good = Comp("records/chunks.bin", bytes.Length, ArtifactVerifier.Sha256Hex(bytes));
        ArtifactVerifier.VerifyComponent(good, bytes);

        Assert.Throws<ArtifactException>(() => ArtifactVerifier.VerifyComponent(good with { BytesLen = 4 }, bytes));
        Assert.Throws<ArtifactException>(() => ArtifactVerifier.VerifyComponent(good with { Sha256 = new string('b', 64) }, bytes));
    }

    [Fact]
    public void A_valid_manifest_passes()
    {
        ArtifactVerifier.ValidateManifest(
            1,
            [Comp("manifest.json"), Comp("records/chunks.bin")],
            new ArtifactRangeMapRef { Path = "records/chunks.bin", BlockBytes = 4096, Blocks = 2 },
            ArtifactLimits.Default,
            ReaderCapabilities.V1());
    }

    [Fact]
    public void An_unsupported_format_is_refused()
    {
        var ex = Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            2, [Comp("manifest.json")], null, ArtifactLimits.Default, ReaderCapabilities.V1()));
        Assert.Equal(ArtifactErrorKind.Unsupported, ex.Kind);
    }

    [Fact]
    public void A_manifest_with_no_components_is_invalid()
    {
        var ex = Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            1, [], null, ArtifactLimits.Default, ReaderCapabilities.V1()));
        Assert.Equal(ArtifactErrorKind.Invalid, ex.Kind);
    }

    [Fact]
    public void Component_count_and_size_limits_are_enforced_before_allocation()
    {
        var countEx = Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            1, [Comp("a.bin"), Comp("b.bin")], null, new ArtifactLimits { MaxComponents = 1 }, ReaderCapabilities.V1()));
        Assert.Equal(ArtifactErrorKind.Limit, countEx.Kind);

        var sizeEx = Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            1, [Comp("a.bin", len: 10)], null, new ArtifactLimits { MaxComponentBytes = 5 }, ReaderCapabilities.V1()));
        Assert.Equal(ArtifactErrorKind.Limit, sizeEx.Kind);
    }

    [Fact]
    public void Duplicate_paths_and_malformed_hashes_are_invalid()
    {
        Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            1, [Comp("a.bin"), Comp("a.bin")], null, ArtifactLimits.Default, ReaderCapabilities.V1()));

        Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            1, [Comp("a.bin", sha: "short")], null, ArtifactLimits.Default, ReaderCapabilities.V1()));
    }

    [Fact]
    public void A_range_map_must_point_at_a_listed_component()
    {
        var ex = Assert.Throws<ArtifactException>(() => ArtifactVerifier.ValidateManifest(
            1,
            [Comp("records/chunks.bin")],
            new ArtifactRangeMapRef { Path = "records/chunks.idx", BlockBytes = 4096, Blocks = 1 },
            ArtifactLimits.Default,
            ReaderCapabilities.V1()));
        Assert.Equal(ArtifactErrorKind.Invalid, ex.Kind);
    }
}
