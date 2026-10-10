// <copyright file="CanonicalJsonTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Contract vectors ported from munarium-datastore canonical.rs.
// </copyright>

using System.Text;
using System.Text.Json;
using SharpCoreDB.VectorSearch.Artifacts;
using Xunit;

namespace SharpCoreDB.VectorSearch.Tests;

/// <summary>
/// Contract vectors for the RFC 8785 (JCS) canonical writer, ported one-to-one from the
/// munarium-datastore crate's <c>canonical.rs</c> tests so the two implementations cannot drift in
/// intent.
/// </summary>
public class CanonicalJsonTests
{
    private static string Canon(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return CanonicalJson.Canonicalize(doc.RootElement);
    }

    private static string CanonHash(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return CanonicalJson.CanonicalSha256(doc.RootElement);
    }

    [Fact]
    public void Object_members_sort_by_utf16_code_unit()
    {
        Assert.Equal("{\"a\":2,\"b\":1}", Canon("{\"b\":1,\"a\":2}"));
        Assert.Equal("{\"z\":{\"x\":2,\"y\":1}}", Canon("{\"z\":{\"y\":1,\"x\":2}}"));
    }

    [Fact]
    public void Member_order_does_not_change_the_hash()
    {
        Assert.Equal(
            CanonHash("{\"kind\":\"collection\",\"id\":\"col-1\"}"),
            CanonHash("{\"id\":\"col-1\",\"kind\":\"collection\"}"));
    }

    [Fact]
    public void Array_order_does_change_the_hash()
    {
        Assert.NotEqual(CanonHash("[\"x\",\"y\"]"), CanonHash("[\"y\",\"x\"]"));
    }

    [Fact]
    public void Floats_are_refused_and_the_message_says_what_to_do()
    {
        var ex = Assert.Throws<ArtifactException>(() => Canon("{\"ratio\":0.5}"));
        Assert.Equal(ArtifactErrorKind.Canonical, ex.Kind);
        Assert.Contains("forbids", ex.Message);
        Assert.Contains("decimal STRING", ex.Message);
    }

    [Fact]
    public void Integers_keep_their_sign_and_zero()
    {
        Assert.Equal("{\"m\":0,\"n\":-12,\"p\":148213}", Canon("{\"n\":-12,\"m\":0,\"p\":148213}"));
    }

    [Fact]
    public void Strings_escape_minimally_and_do_not_normalize_unicode()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", Canon("\"a\\\"b\\\\c\""));
        Assert.Equal("\"tab\\there\"", Canon("\"tab\\there\""));
        // A C0 control IS escaped, lowercase \u00XX — minimal means minimal, not absent.
        Assert.Equal("\"bell\\u0007\"", Canon("\"bell\\u0007\""));
        // Accented and CJK text is emitted literally, not escaped and not normalized.
        Assert.Equal("\"Café 東京\"", Canon("\"Café 東京\""));
    }

    [Fact]
    public void Nested_empties_are_stable()
    {
        Assert.Equal("{\"a\":[],\"b\":{},\"c\":null}", Canon("{\"a\":[],\"b\":{},\"c\":null}"));
    }

    [Fact]
    public void Manifest_id_is_deterministic_and_tamper_sensitive()
    {
        static ArtifactManifest New() => new()
        {
            IndexVersionId = "idx-hnsw-8-16-200",
            Dimensions = 8,
            Count = 3,
            IndexType = VectorIndexType.Hnsw,
            DistanceFunction = DistanceFunction.Cosine,
        };

        var a = New().WithComputedId();
        var b = New().WithComputedId();

        Assert.Equal(a.ArtifactId, b.ArtifactId);
        Assert.NotEqual(a.ArtifactId, (a with { Count = 4 }).WithComputedId().ArtifactId);
    }

    [Fact]
    public void Ratios_travel_as_decimal_strings_not_floats()
    {
        var manifest = new ArtifactManifest
        {
            Count = 1,
            BuildParams = new() { ["TargetRecall"] = CanonicalParam.Ratio(0.95) },
        }.WithComputedId();

        var json = Encoding.UTF8.GetString(manifest.CanonicalBytes());
        Assert.Contains("\"TargetRecall\":\"0.95\"", json);
    }
}
