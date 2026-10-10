// <copyright file="IArtifactStore.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore store.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Store;

/// <summary>A byte range, half-open.</summary>
public readonly record struct ByteRange(long Start, long End);

/// <summary>
/// Where an artifact's components are read from and written to.
/// </summary>
/// <remarks>
/// Keys are ALWAYS the artifact-relative component paths from the manifest, normalized
/// (see <see cref="Artifacts.ArtifactVerifier.NormalizeComponentPath"/>). An implementation combines
/// them with a tenant- and scope-constrained prefix it was constructed with; no method takes a bare
/// artifact hash as authority to read or delete, because a content hash is not an authorization
/// boundary. The trait is deliberately range-capable from the start: adding
/// <c>range</c> later would be a breaking change to every implementation.
/// </remarks>
public interface IArtifactStore
{
    /// <summary>Write a component at a normalized, artifact-relative path.</summary>
    void PutComponent(string path, ReadOnlySpan<byte> bytes);

    /// <summary>Read a component, or a half-open byte range of it.</summary>
    byte[] GetComponent(string path, ByteRange? range = null);

    /// <summary>The component's length in bytes.</summary>
    long HeadComponent(string path);

    /// <summary>Whether the component exists.</summary>
    bool Exists(string path);
}
