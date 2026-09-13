// <copyright file="IVerifiableIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore verify.rs + store.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Artifacts;

using SharpCoreDB.VectorSearch;

/// <summary>
/// Interface for indexes that support content-verified immutable artifacts
/// (munarium-datastore style). Enables verify-before-use, quarantine on mismatch,
/// and reproducible RAG caches.
/// </summary>
public interface IVerifiableIndex : IVectorIndex
{
    /// <summary>
    /// Gets the immutable manifest for this index (contains canonical artifact_id).
    /// </summary>
    ArtifactManifest Manifest { get; }

    /// <summary>
    /// Verifies the index against an expected manifest (or bytes).
    /// Throws VerificationFailed or returns BuildResult.VerificationFailed on mismatch.
    /// </summary>
    BuildResult Verify(ArtifactManifest expectedManifest);

    /// <summary>
    /// Serializes the current manifest to bytes for storage/persistence.
    /// </summary>
    byte[] SerializeManifest();
}
