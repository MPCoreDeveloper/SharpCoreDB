// <copyright file="DiskAnnIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. Full implementation inspired by munarium-datastore vector_diskann.rs.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

using SharpCoreDB.VectorSearch;
using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>
/// DiskANN-style approximate nearest-neighbour index: a <b>Vamana</b> graph with greedy beam
/// search (Subramanya et al., "DiskANN: Fast Accurate Billion-point Nearest Neighbor Search on a
/// Single Node"). The graph is built lazily and frozen until the store changes.
/// </summary>
/// <remarks>
/// Design choices mirroring the munarium datastore:
/// <list type="bullet">
/// <item><b>Full precision throughout.</b> Vectors are stored as given and traversal uses the true
/// distance, so the ONLY approximation is which nodes the beam visits.</item>
/// <item><b>Robust prune with alpha &gt; 1.0</b> keeps longer-range edges and improves recall at a
/// small build cost.</item>
/// <item><b>The entry point is the medoid</b> (the live vertex closest to the centroid).</item>
/// <item><b>Deterministic construction</b> — the random initialization is seeded, candidates are
/// ordered by (distance, id), so two builds of the same data agree.</item>
/// </list>
/// Storage is a contiguous struct-of-arrays, so traversal touches one linear buffer.
/// </remarks>
public sealed partial class DiskAnnIndex : IVectorIndex, IVerifiableIndex
{
    /// <summary>Engine id recorded in an artifact manifest.</summary>
    public const string EngineId = "diskann";

    /// <summary>Engine revision (this pure-C# Vamana implementation).</summary>
    public const string EngineRevision = "1.0.0-csharp-vamana";

    /// <summary>The envelope feature bit an approximate artifact requires.</summary>
    public const string FeatureBit = "vector.diskann.v1";

    private const long Tombstone = long.MinValue;
    private const int RngSeed = 0x5EED;

    private readonly DiskAnnConfig _config;
    private readonly Lock _writeLock = new();
    private readonly Dictionary<long, int> _idToSlot = new();

    private float[] _data;
    private long[] _ids;
    private int _capacity;
    private int _length;
    private int _liveCount;

    private volatile Graph? _graph;
    private ArtifactManifest? _manifest;

    /// <summary>Initializes a new DiskANN (Vamana) index.</summary>
    /// <param name="config">Graph and vector parameters.</param>
    public DiskAnnIndex(DiskAnnConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _config = config;

        _capacity = 4;
        _data = new float[_capacity * config.Dimensions];
        _ids = new long[_capacity];
    }

    /// <inheritdoc />
    public VectorIndexType IndexType => VectorIndexType.DiskAnn;

    /// <inheritdoc />
    public int Count => _liveCount;

    /// <inheritdoc />
    public int Dimensions => _config.Dimensions;

    /// <inheritdoc />
    public DistanceFunction DistanceFunction => _config.DistanceFunction;

    /// <summary>Gets the graph/vector parameters this index was created with.</summary>
    public DiskAnnConfig Config => _config;

    /// <inheritdoc />
    public long EstimatedMemoryBytes
    {
        get
        {
            long vectors = (long)_length * ((_config.Dimensions * sizeof(float)) + sizeof(long));
            Graph? graph = _graph;
            long edges = graph is null ? 0 : graph.NeighborSlots.Sum(static n => (long)n.Count) * sizeof(int);
            return vectors + edges + 256;
        }
    }

    /// <summary>
    /// An immutable, consistently-published view of the built graph. Neighbours are SLOT indices,
    /// not ids, so traversal never touches the id-to-slot map.
    /// </summary>
    private sealed record Graph(List<int>[] NeighborSlots, int EntrySlot, int Length);
}
