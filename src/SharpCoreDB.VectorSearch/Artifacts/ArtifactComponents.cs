// <copyright file="ArtifactComponents.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore model.rs + verify.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

/// <summary>What a sealed artifact component is for.</summary>
public enum ArtifactComponentPurpose
{
    /// <summary>The manifest sidecar.</summary>
    ManifestSidecar,

    /// <summary>The records/chunk bodies.</summary>
    Records,

    /// <summary>The lexical (inverted) index.</summary>
    Lexical,

    /// <summary>The vector index (graph + vectors).</summary>
    Vector,

    /// <summary>Filter/postings sidecars.</summary>
    Filters,

    /// <summary>The block range map.</summary>
    RangeMap,
}

/// <summary>
/// One sealed component: a path, its purpose, its declared length and its SHA-256. Every field is
/// checked against the bytes rather than trusted (see <see cref="ArtifactVerifier"/>).
/// </summary>
public sealed record ArtifactComponent
{
    /// <summary>Normalized, artifact-relative POSIX path (never host-shaped).</summary>
    public required string Path { get; init; }

    /// <summary>What the component is for.</summary>
    public ArtifactComponentPurpose Purpose { get; init; }

    /// <summary>Declared byte length.</summary>
    public long BytesLen { get; init; }

    /// <summary>Lowercase-hex SHA-256 of the component bytes.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Whether a reader must have this component to serve the artifact.</summary>
    public bool Required { get; init; } = true;
}

/// <summary>A block range map reference (block size + block count over one component).</summary>
public sealed record ArtifactRangeMapRef
{
    /// <summary>The component path the range map describes.</summary>
    public required string Path { get; init; }

    /// <summary>Block size in bytes.</summary>
    public long BlockBytes { get; init; }

    /// <summary>Number of blocks.</summary>
    public long Blocks { get; init; }
}

/// <summary>
/// Limits applied to declared values BEFORE any allocation. A count in a manifest is an allocation
/// instruction unless it is bounded first, so a hostile or corrupt manifest becomes a refusal
/// instead of an out-of-memory kill. An operator can raise any of them knowingly.
/// </summary>
public sealed record ArtifactLimits
{
    /// <summary>Maximum number of components.</summary>
    public int MaxComponents { get; init; } = 10_000;

    /// <summary>Maximum bytes per component (64 GiB).</summary>
    public long MaxComponentBytes { get; init; } = 64L * 1024 * 1024 * 1024;

    /// <summary>Maximum total bytes across components (256 GiB).</summary>
    public long MaxTotalBytes { get; init; } = 256L * 1024 * 1024 * 1024;

    /// <summary>Maximum chunk count.</summary>
    public long MaxChunks { get; init; } = 1_000_000_000;

    /// <summary>Maximum vector dimensions.</summary>
    public int MaxDimensions { get; init; } = 65_536;

    /// <summary>The default, deliberately finite limit set.</summary>
    public static ArtifactLimits Default { get; } = new();
}

/// <summary>
/// The reader's own supported envelope range. An unsupported artifact is refused BEFORE traffic is
/// switched to it, so an approximate artifact is rejected by a reader that cannot open it.
/// </summary>
public sealed class ReaderCapabilities
{
    /// <summary>Minimum supported format version.</summary>
    public required int FormatMin { get; init; }

    /// <summary>Maximum supported format version.</summary>
    public required int FormatMax { get; init; }

    /// <summary>Feature bits this reader advertises.</summary>
    public required IReadOnlySet<string> Features { get; init; }

    /// <summary>The v1 reader: format 1..1, <c>records.v1</c>, plus <c>vector.diskann.v1</c> when the engine is compiled in.</summary>
    public static ReaderCapabilities V1(bool vectorDiskAnn = true)
    {
        var features = new HashSet<string>(StringComparer.Ordinal) { "records.v1" };
        if (vectorDiskAnn)
        {
            features.Add("vector.diskann.v1");
        }

        return new ReaderCapabilities { FormatMin = 1, FormatMax = 1, Features = features };
    }
}
