// <copyright file="DeltaCompactor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Delta compaction manager for merging delta updates into full records (Phase 3.3).
/// Prevents excessive delta chains that degrade read performance.
/// C# 14: Uses modern async patterns and Lock type.
/// </summary>
public sealed class DeltaCompactor : IDisposable
{
    private readonly Lock _compactionLock = new();
    private readonly Dictionary<string, DeltaChainInfo> _deltaChains = [];
    private bool _disposed;
    
    /// <summary>
    /// Maximum delta chain length before forcing compaction.
    /// </summary>
    public int MaxDeltaChainLength { get; init; } = 10;
    
    /// <summary>
    /// Delta compaction threshold (0-100%).
    /// Triggers compaction when delta size exceeds this percentage of full record size.
    /// </summary>
    public int CompactionThresholdPercent { get; init; } = 75;

    /// <summary>
    /// Tracks a delta update for future compaction.
    /// </summary>
    /// <param name="tableName">Table name</param>
    /// <param name="recordId">Record identifier</param>
    /// <param name="deltaSize">Size of delta update</param>
    public void TrackDelta(string tableName, long recordId, int deltaSize)
    {
        lock (_compactionLock)
        {
            var key = $"{tableName}:{recordId}";
            
            if (!_deltaChains.TryGetValue(key, out var info))
            {
                info = new DeltaChainInfo
                {
                    TableName = tableName,
                    RecordId = recordId
                };
                _deltaChains[key] = info;
            }
            
            info.DeltaCount++;
            info.TotalDeltaSize += deltaSize;
            info.LastUpdateTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Checks if a record needs compaction based on delta chain length and size.
    /// </summary>
    /// <param name="tableName">Table name</param>
    /// <param name="recordId">Record identifier</param>
    /// <param name="fullRecordSize">Size of full record</param>
    /// <returns>True if compaction is needed</returns>
    public bool NeedsCompaction(string tableName, long recordId, int fullRecordSize)
    {
        lock (_compactionLock)
        {
            var key = $"{tableName}:{recordId}";
            
            if (!_deltaChains.TryGetValue(key, out var info))
                return false;
            
            // Compaction needed if:
            // 1. Delta chain is too long
            if (info.DeltaCount >= MaxDeltaChainLength)
                return true;
            
            // 2. Delta size exceeds threshold
            int threshold = fullRecordSize * CompactionThresholdPercent / 100;
            if (info.TotalDeltaSize >= threshold)
                return true;
            
            return false;
        }
    }

    /// <summary>
    /// Marks a record as compacted and clears its delta chain.
    /// </summary>
    /// <param name="tableName">Table name</param>
    /// <param name="recordId">Record identifier</param>
    public void MarkCompacted(string tableName, long recordId)
    {
        lock (_compactionLock)
        {
            var key = $"{tableName}:{recordId}";
            _deltaChains.Remove(key);
        }
    }

    /// <summary>
    /// Gets all records that need compaction.
    /// </summary>
    /// <param name="fullRecordSizeGetter">Function to get full record size</param>
    /// <returns>List of (tableName, recordId) tuples</returns>
    public List<(string tableName, long recordId)> GetCompactionCandidates(
        Func<string, long, int> fullRecordSizeGetter)
    {
        var candidates = new List<(string, long)>();
        
        lock (_compactionLock)
        {
            foreach (var (key, info) in _deltaChains)
            {
                int fullSize = fullRecordSizeGetter(info.TableName, info.RecordId);
                if (NeedsCompaction(info.TableName, info.RecordId, fullSize))
                {
                    candidates.Add((info.TableName, info.RecordId));
                }
            }
        }
        
        return candidates;
    }

    /// <summary>
    /// Performs asynchronous compaction of delta chains.
    /// </summary>
    /// <param name="compactionAction">Action to perform compaction for a record</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of records compacted</returns>
    public async Task<int> CompactAsync(
        Func<string, long, Task> compactionAction,
        CancellationToken ct = default)
    {
        var candidates = new List<(string tableName, long recordId)>();
        
        lock (_compactionLock)
        {
            foreach (var (_, info) in _deltaChains)
            {
                if (info.DeltaCount >= MaxDeltaChainLength)
                {
                    candidates.Add((info.TableName, info.RecordId));
                }
            }
        }
        
        int compacted = 0;
        foreach (var (tableName, recordId) in candidates)
        {
            if (ct.IsCancellationRequested) break;
            
            try
            {
                await compactionAction(tableName, recordId).ConfigureAwait(false);
                MarkCompacted(tableName, recordId);
                compacted++;
            }
            catch
            {
                // Continue with other records if one fails
                continue;
            }
        }
        
        return compacted;
    }

    /// <summary>
    /// Gets compaction statistics.
    /// </summary>
    /// <returns>Compaction stats</returns>
    public DeltaCompactionStats GetStats()
    {
        lock (_compactionLock)
        {
            int totalChains = _deltaChains.Count;
            int totalDeltas = 0;
            long totalDeltaBytes = 0;
            
            foreach (var info in _deltaChains.Values)
            {
                totalDeltas += info.DeltaCount;
                totalDeltaBytes += info.TotalDeltaSize;
            }
            
            return new DeltaCompactionStats
            {
                TotalDeltaChains = totalChains,
                TotalDeltas = totalDeltas,
                TotalDeltaBytes = totalDeltaBytes,
                AvgDeltasPerChain = totalChains > 0 ? (double)totalDeltas / totalChains : 0
            };
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_compactionLock)
        {
            _deltaChains.Clear();
        }
        
        _disposed = true;
    }
}

/// <summary>
/// Information about a delta chain for a specific record.
/// </summary>
internal sealed class DeltaChainInfo
{
    public string TableName { get; init; } = string.Empty;
    public long RecordId { get; init; }
    public int DeltaCount { get; set; }
    public long TotalDeltaSize { get; set; }
    public DateTimeOffset LastUpdateTime { get; set; }
}

/// <summary>
/// Statistics about delta compaction.
/// </summary>
public record DeltaCompactionStats
{
    public int TotalDeltaChains { get; init; }
    public int TotalDeltas { get; init; }
    public long TotalDeltaBytes { get; init; }
    public double AvgDeltasPerChain { get; init; }
}
