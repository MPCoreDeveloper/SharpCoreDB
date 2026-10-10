// <copyright file="Candidate.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Inspired by munarium-datastore vector.rs Candidate.
// </copyright>

namespace SharpCoreDB.VectorSearch.Fusion;

/// <summary>
/// A candidate from one search leg, before fusion.
/// </summary>
/// <param name="ChunkId">The indexed chunk/document identifier.</param>
/// <param name="Score">
/// Leg-native score. Lexical: higher is better. Vector: a cosine DISTANCE, lower is better. The two
/// are never compared numerically — fusion works on ranks precisely because these scales are
/// incommensurable (a BM25 score against a cosine distance is a merge hazard).
/// </param>
public readonly record struct Candidate(string ChunkId, float Score);
