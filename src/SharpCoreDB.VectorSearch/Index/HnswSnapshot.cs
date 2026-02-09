// <copyright file="HnswSnapshot.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Read-only snapshot of the HNSW graph for serialization.
/// </summary>
/// <param name="EntryPointId">The entry point node ID (-1 if empty).</param>
/// <param name="Nodes">All nodes in the graph.</param>
public readonly record struct HnswSnapshot(long EntryPointId, IReadOnlyList<HnswNodeSnapshot> Nodes);

/// <summary>
/// Read-only snapshot of a single HNSW node for serialization.
/// </summary>
/// <param name="Id">Node identifier.</param>
/// <param name="Vector">Vector data.</param>
/// <param name="MaxLayer">Maximum layer for this node.</param>
/// <param name="Neighbors">Neighbor arrays per layer.</param>
public readonly record struct HnswNodeSnapshot(long Id, float[] Vector, int MaxLayer, long[][] Neighbors);
