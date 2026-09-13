// <copyright file="DiskAnnIndex.Store.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.VectorSearch.Index;

using SharpCoreDB.VectorSearch;
using SharpCoreDB.VectorSearch.Artifacts;

/// <summary>Vector store, lifecycle and manifest for <see cref="DiskAnnIndex"/>.</summary>
public sealed partial class DiskAnnIndex
{
    /// <inheritdoc />
    public ArtifactManifest Manifest
    {
        get
        {
            if (_manifest is null)
            {
                _manifest = new ArtifactManifest
                {
                    IndexVersionId = $"idx-diskann-{_config.Dimensions}-{_config.MaxNeighbors}-{_config.ConstructionSearchListSize}",
                    Dimensions = Dimensions,
                    Count = Count,
                    IndexType = IndexType,
                    DistanceFunction = DistanceFunction,
                    HybridFusionAlpha = "0.5",
                    BuildParams = new()
                    {
                        ["Engine"] = EngineId,
                        ["EngineRevision"] = EngineRevision,
                        ["FeatureBit"] = FeatureBit,
                        ["MaxNeighbors"] = _config.MaxNeighbors,
                        ["ConstructionSearchListSize"] = _config.ConstructionSearchListSize,
                        ["QuerySearchListSize"] = _config.QuerySearchListSize,
                        ["Alpha"] = CanonicalParam.Ratio(_config.Alpha),
                        ["TargetRecall"] = CanonicalParam.Ratio(_config.TargetRecall),
                    },
                }.WithComputedId();
            }

            return _manifest;
        }
    }

    /// <inheritdoc />
    public void Add(long id, ReadOnlySpan<float> vector)
    {
        if (vector.Length != _config.Dimensions)
        {
            throw new ArgumentException(
                $"Vector has {vector.Length} dimensions, index requires {_config.Dimensions}");
        }

        for (int i = 0; i < vector.Length; i++)
        {
            if (!float.IsFinite(vector[i]))
            {
                throw new ArgumentException($"Vector for id {id} holds a non-finite value at index {i}");
            }
        }

        lock (_writeLock)
        {
            if (_idToSlot.TryGetValue(id, out int existing))
            {
                vector.CopyTo(_data.AsSpan(existing * _config.Dimensions, _config.Dimensions));
                Invalidate();
                return;
            }

            if (_length == _capacity)
            {
                Grow();
            }

            int slot = _length;
            vector.CopyTo(_data.AsSpan(slot * _config.Dimensions, _config.Dimensions));
            _ids[slot] = id;
            _idToSlot[id] = slot;
            _length++;
            _liveCount++;
            Invalidate();
        }
    }

    /// <inheritdoc />
    public bool Remove(long id)
    {
        lock (_writeLock)
        {
            if (!_idToSlot.Remove(id, out int slot))
            {
                return false;
            }

            _ids[slot] = Tombstone;
            _liveCount--;
            Invalidate();
            return true;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_writeLock)
        {
            _idToSlot.Clear();
            _capacity = 4;
            _data = new float[_capacity * _config.Dimensions];
            _ids = new long[_capacity];
            _length = 0;
            _liveCount = 0;
            _graph = null;
            _manifest = null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Clear();

    private void Invalidate()
    {
        _graph = null;
        _manifest = null;
    }

    private void Grow()
    {
        int newCapacity = _capacity * 2;
        var newData = new float[newCapacity * _config.Dimensions];
        var newIds = new long[newCapacity];
        Array.Copy(_data, newData, (long)_length * _config.Dimensions);
        Array.Copy(_ids, newIds, _length);
        _data = newData;
        _ids = newIds;
        _capacity = newCapacity;
    }
}
