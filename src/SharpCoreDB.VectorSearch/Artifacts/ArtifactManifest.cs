// <copyright file="ArtifactManifest.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore model.rs + canonical.rs.
// </copyright>

using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// Immutable, content-addressed manifest for a vector/search index artifact.
/// Exactly mirrors the spirit of munarium's ArtifactManifest + BuildSpec.
/// The artifact_id IS the SHA-256 of the canonical JSON representation.
/// This enables perfect reproducibility, verification and caching.
/// </summary>
public sealed record ArtifactManifest
{
    /// <summary>Version of this manifest format (currently 1).</summary>
    public int ManifestVersion { get; init; } = 1;

    /// <summary>Logical index version derived from BuildSpec (independent of physical engine).</summary>
    public string IndexVersionId { get; init; } = string.Empty;

    /// <summary>Physical content identifier = SHA256(canonical JSON of this manifest).</summary>
    [JsonIgnore]
    public string ArtifactId { get; private init; } = string.Empty;

    /// <summary>Dimensions of the vectors in this artifact.</summary>
    public int Dimensions { get; init; }

    /// <summary>Number of vectors/documents indexed.</summary>
    public long Count { get; init; }

    /// <summary>Index type (Hnsw, DiskAnn, Flat).</summary>
    public VectorIndexType IndexType { get; init; }

    /// <summary>Distance function used.</summary>
    public DistanceFunction DistanceFunction { get; init; }

    /// <summary>
    /// Hybrid fusion alpha (0.0 = vector only, 1.0 = lexical only), carried as a decimal STRING:
    /// artifact canonicalization refuses floats, so a genuine ratio travels as a string at a
    /// declared scale.
    /// </summary>
    public string HybridFusionAlpha { get; init; } = "0.5";

    /// <summary>Build parameters (M, efConstruction, etc.) serialized for reproducibility.</summary>
    public Dictionary<string, CanonicalParam> BuildParams { get; init; } = new();

    /// <summary>Creation timestamp (UTC) — not part of identity (only for audit).</summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Computes the canonical artifact_id from the current state.
    /// Must be called after construction to finalize the manifest.
    /// </summary>
    public ArtifactManifest WithComputedId()
    {
        // Identity is sha256 of the RFC 8785 canonical bytes, with the id itself and the audit
        // timestamp excluded. The timestamp is *pinned to the default* rather than omitted: a
        // canonical document serializes every member, so "omitted" would be a different document.
        var canonical = this with { ArtifactId = string.Empty, CreatedAtUtc = default };
        var artifactId = CanonicalJson.CanonicalSha256(canonical);

        return this with { ArtifactId = artifactId };
    }

    /// <summary>Canonical bytes the artifact id hashes (id and timestamp pinned out).</summary>
    public byte[] CanonicalBytes()
        => CanonicalJson.CanonicalBytes(this with { ArtifactId = string.Empty, CreatedAtUtc = default });

    /// <summary>
    /// Verifies that this manifest matches the provided canonical bytes (integrity check).
    /// Returns true only on a perfect match.
    /// </summary>
    public bool Verify(byte[] expectedManifestBytes)
    {
        ArgumentNullException.ThrowIfNull(expectedManifestBytes);
        return CryptographicOperations.FixedTimeEquals(CanonicalBytes(), expectedManifestBytes);
    }
}
