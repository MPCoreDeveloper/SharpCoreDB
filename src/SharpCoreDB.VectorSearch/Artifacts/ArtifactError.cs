// <copyright file="ArtifactError.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore lib.rs Error taxonomy.
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// The class of an artifact failure. Variants are the CLASSES a caller acts on
/// differently, not one per call site: <see cref="Integrity"/> means quarantine the
/// artifact, <see cref="Unsupported"/> means this reader cannot serve it but a newer one
/// might, <see cref="Limit"/> means the artifact is bigger than this node was configured to
/// accept. A single opaque error would collapse three different operator responses into one.
/// </summary>
public enum ArtifactErrorKind
{
    /// <summary>A hash or a declared length did not match the bytes. Quarantine.</summary>
    Integrity,

    /// <summary>Well-formed but not servable by THIS reader.</summary>
    Unsupported,

    /// <summary>Refused before allocating, because a declared size exceeded a limit.</summary>
    Limit,

    /// <summary>A path that is not a normalized relative path.</summary>
    Path,

    /// <summary>Structurally invalid: a manifest that contradicts itself.</summary>
    Invalid,

    /// <summary>Canonicalization refused the value (e.g. a float in a hashed document).</summary>
    Canonical,

    /// <summary>An underlying I/O failure.</summary>
    Io,
}

/// <summary>
/// A typed artifact failure. Mirrors the six operator-actionable classes of the
/// munarium-datastore crate so a caller can branch on <see cref="Kind"/> instead of parsing
/// a message.
/// </summary>
public sealed class ArtifactException : Exception
{
    /// <summary>Gets the failure class.</summary>
    public ArtifactErrorKind Kind { get; }

    /// <summary>Initializes a new <see cref="ArtifactException"/>.</summary>
    public ArtifactException(ArtifactErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>A hash/length mismatch. Quarantine.</summary>
    public static ArtifactException Integrity(string message) => new(ArtifactErrorKind.Integrity, message);

    /// <summary>Well-formed but not servable by this reader.</summary>
    public static ArtifactException Unsupported(string message) => new(ArtifactErrorKind.Unsupported, message);

    /// <summary>A declared size exceeded a configured limit (checked before allocation).</summary>
    public static ArtifactException Limit(string message) => new(ArtifactErrorKind.Limit, message);

    /// <summary>A path that is not a normalized relative path.</summary>
    public static ArtifactException Path(string message) => new(ArtifactErrorKind.Path, message);

    /// <summary>Structurally invalid (a manifest that contradicts itself).</summary>
    public static ArtifactException Invalid(string message) => new(ArtifactErrorKind.Invalid, message);

    /// <summary>Canonicalization refused the value.</summary>
    public static ArtifactException Canonical(string message) => new(ArtifactErrorKind.Canonical, message);

    /// <summary>An underlying I/O failure.</summary>
    public static ArtifactException Io(string message, Exception? innerException = null)
        => new(ArtifactErrorKind.Io, message, innerException);
}
