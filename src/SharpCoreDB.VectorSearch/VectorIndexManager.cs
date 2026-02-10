// <copyright file="VectorIndexManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

using System.Collections.Concurrent;
using SharpCoreDB.Interfaces;

/// <summary>
/// Manages live <see cref="IVectorIndex"/> instances for all vector-indexed columns.
/// Indexes are built on demand (lazy) or explicitly via <see cref="BuildIndex"/>.
/// Thread-safe: concurrent reads are lock-free, builds are serialized via <see cref="Lock"/>.
/// </summary>
public sealed class VectorIndexManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IVectorIndex> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _buildLock = new();
    private readonly VectorSearchOptions _options;

    /// <summary>
    /// Initializes a new <see cref="VectorIndexManager"/> with the given options.
    /// </summary>
    /// <param name="options">Vector search configuration.</param>
    public VectorIndexManager(VectorSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Gets the number of live indexes.</summary>
    public int IndexCount => _indexes.Count;

    /// <summary>Gets the total estimated memory usage of all indexes in bytes.</summary>
    public long TotalMemoryBytes => _indexes.Values.Sum(idx => idx.EstimatedMemoryBytes);

    /// <summary>
    /// Gets a live index for the given table and column, or null if none exists.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The VECTOR column name.</param>
    /// <returns>The live index, or null.</returns>
    public IVectorIndex? GetIndex(string tableName, string columnName)
    {
        var key = MakeKey(tableName, columnName);
        return _indexes.TryGetValue(key, out var index) ? index : null;
    }

    /// <summary>
    /// Checks whether an index exists for the given table and column.
    /// </summary>
    public bool HasIndex(string tableName, string columnName)
        => _indexes.ContainsKey(MakeKey(tableName, columnName));

    /// <summary>
    /// Builds a vector index by reading all existing vector data from the table.
    /// If an index already exists for this column, it is replaced.
    /// </summary>
    /// <param name="table">The table containing vector data.</param>
    /// <param name="tableName">The table name (for registry keying).</param>
    /// <param name="columnName">The VECTOR column containing float[] or byte[] data.</param>
    /// <param name="indexType">The index type to build.</param>
    /// <param name="config">Optional HNSW config (uses defaults from options if null).</param>
    /// <returns>The built index.</returns>
    public IVectorIndex BuildIndex(
        ITable table,
        string tableName,
        string columnName,
        VectorIndexType indexType,
        HnswConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);

        var key = MakeKey(tableName, columnName);

        lock (_buildLock)
        {
            // Dispose old index if replacing
            if (_indexes.TryRemove(key, out var oldIndex))
            {
                oldIndex.Dispose();
            }

            // Read all rows to determine dimensions and populate index
            var rows = table.Select();
            int dimensions = DetectDimensions(rows, columnName);

            var index = CreateIndex(indexType, dimensions, config);

            // Add all existing vectors with sequential IDs
            long id = 0;
            foreach (var row in rows)
            {
                if (row.TryGetValue(columnName, out var val) && val is not null)
                {
                    var vec = CoerceToFloatArray(val);
                    if (vec.Length == dimensions)
                    {
                        index.Add(id, vec);
                    }
                }

                id++;
            }

            _indexes[key] = index;
            return index;
        }
    }

    /// <summary>
    /// Drops (removes and disposes) the index for the given table and column.
    /// </summary>
    /// <returns>True if the index was found and removed.</returns>
    public bool DropIndex(string tableName, string columnName)
    {
        var key = MakeKey(tableName, columnName);
        if (_indexes.TryRemove(key, out var index))
        {
            index.Dispose();
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var (_, index) in _indexes)
        {
            index.Dispose();
        }

        _indexes.Clear();
    }

    private IVectorIndex CreateIndex(VectorIndexType indexType, int dimensions, HnswConfig? config)
    {
        return indexType switch
        {
            VectorIndexType.Flat => new FlatIndex(dimensions),
            VectorIndexType.Hnsw => new HnswIndex(config ?? new HnswConfig
            {
                Dimensions = dimensions,
                M = _options.DefaultM,
                EfConstruction = _options.DefaultEfConstruction,
                EfSearch = _options.DefaultEfSearch,
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(indexType)),
        };
    }

    /// <summary>
    /// Detects vector dimensions from the first non-null vector in the result set.
    /// Falls back to 1536 (OpenAI embedding size) if no vectors are found.
    /// </summary>
    private static int DetectDimensions(List<Dictionary<string, object>> rows, string columnName)
    {
        foreach (var row in rows)
        {
            if (row.TryGetValue(columnName, out var val) && val is not null)
            {
                return CoerceToFloatArray(val).Length;
            }
        }

        return 1536;
    }

    /// <summary>
    /// Converts various vector representations to float[].
    /// </summary>
    internal static float[] CoerceToFloatArray(object value)
    {
        return value switch
        {
            float[] arr => arr,
            byte[] bytes => VectorSerializer.Deserialize(bytes),
            string json => VectorSerializer.FromJson(json),
            _ => throw new InvalidOperationException(
                $"Cannot convert {value.GetType().Name} to float vector"),
        };
    }

    private static string MakeKey(string tableName, string columnName)
        => $"{tableName}:{columnName}";
}
