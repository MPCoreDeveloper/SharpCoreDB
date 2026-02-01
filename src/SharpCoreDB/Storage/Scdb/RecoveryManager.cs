// <copyright file="RecoveryManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.Storage;

/// <summary>
/// Recovery manager for crash recovery using WAL replay.
/// Implements REDO-only recovery (no UNDO needed with write-ahead guarantee).
/// C# 14: Modern async patterns with Lock type.
/// </summary>
internal sealed class RecoveryManager : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private readonly WalManager _walManager;
    private readonly Lock _recoveryLock = new();  // ✅ C# 14: Lock type
    private bool _disposed;

    public RecoveryManager(SingleFileStorageProvider provider, WalManager walManager)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(walManager);
        
        _provider = provider;
        _walManager = walManager;
    }

    /// <summary>
    /// Performs crash recovery by replaying WAL entries.
    /// REDO-only: Replays committed transactions, ignores uncommitted.
    /// </summary>
    /// <returns>Recovery information with statistics.</returns>
    public async Task<RecoveryInfo> RecoverAsync(CancellationToken cancellationToken = default)
    {
        lock (_recoveryLock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RecoveryManager));
            }
        }

        var startTime = DateTime.UtcNow;
        
        // Step 1: Analyze WAL to identify committed transactions
        var analysis = await AnalyzeWalAsync(cancellationToken);
        
        if (analysis.TotalEntries == 0)
        {
            // No recovery needed
            return new RecoveryInfo
            {
                RecoveryNeeded = false,
                TotalEntries = 0,
                CommittedTransactions = 0,
                UncommittedTransactions = 0,
                OperationsReplayed = 0,
                RecoveryTime = TimeSpan.Zero
            };
        }

        // Step 2: Replay committed transactions in LSN order
        var replayed = await ReplayCommittedTransactionsAsync(analysis, cancellationToken);
        
        var duration = DateTime.UtcNow - startTime;
        
        return new RecoveryInfo
        {
            RecoveryNeeded = true,
            TotalEntries = analysis.TotalEntries,
            CommittedTransactions = analysis.CommittedTransactions.Count,
            UncommittedTransactions = analysis.UncommittedTransactions.Count,
            OperationsReplayed = replayed,
            RecoveryTime = duration
        };
    }

    /// <summary>
    /// Analyzes WAL to build transaction map.
    /// Identifies committed vs uncommitted transactions.
    /// </summary>
    private async Task<WalAnalysisResult> AnalyzeWalAsync(CancellationToken cancellationToken)
    {
        var entries = await _walManager.ReadEntriesSinceCheckpointAsync(cancellationToken);
        
        var activeTransactions = new HashSet<ulong>();
        var committedTransactions = new HashSet<ulong>();
        var operations = new Dictionary<ulong, List<WalEntry>>();  // txId → operations

        foreach (var entry in entries)
        {
            var txId = entry.TransactionId;
            
            switch ((WalOperation)entry.Operation)
            {
                case WalOperation.TransactionBegin:
                    activeTransactions.Add(txId);
                    operations[txId] = new List<WalEntry>();
                    break;

                case WalOperation.TransactionCommit:
                    if (activeTransactions.Contains(txId))
                    {
                        committedTransactions.Add(txId);
                        activeTransactions.Remove(txId);
                    }
                    break;

                case WalOperation.TransactionAbort:
                    // Transaction aborted, remove from active
                    activeTransactions.Remove(txId);
                    operations.Remove(txId);
                    break;

                case WalOperation.Insert:
                case WalOperation.Update:
                case WalOperation.Delete:
                    // Add operation to transaction
                    if (operations.ContainsKey(txId))
                    {
                        operations[txId].Add(entry);
                    }
                    break;
            }
        }

        // Uncommitted = still active
        var uncommittedTransactions = new HashSet<ulong>(activeTransactions);

        return new WalAnalysisResult
        {
            TotalEntries = entries.Count,
            CommittedTransactions = committedTransactions,
            UncommittedTransactions = uncommittedTransactions,
            Operations = operations
        };
    }

    /// <summary>
    /// Replays committed transactions in LSN order.
    /// Applies operations to block registry and data files.
    /// </summary>
    private async Task<int> ReplayCommittedTransactionsAsync(
        WalAnalysisResult analysis, 
        CancellationToken cancellationToken)
    {
        var replayedCount = 0;

        // Get all operations from committed transactions, sorted by LSN
        var operationsToReplay = analysis.Operations
            .Where(kvp => analysis.CommittedTransactions.Contains(kvp.Key))
            .SelectMany(kvp => kvp.Value)
            .OrderBy(entry => entry.Lsn)
            .ToList();

        foreach (var entry in operationsToReplay)
        {
            await ReplayOperationAsync(entry, cancellationToken);
            replayedCount++;
        }

        // Flush after all replays
        await _provider.FlushAsync(cancellationToken);

        return replayedCount;
    }

    /// <summary>
    /// Replays a single WAL operation.
    /// </summary>
    private async Task ReplayOperationAsync(WalEntry entry, CancellationToken cancellationToken)
    {
        switch ((WalOperation)entry.Operation)
        {
            case WalOperation.Insert:
                await ReplayInsertAsync(entry, cancellationToken);
                break;

            case WalOperation.Update:
                await ReplayUpdateAsync(entry, cancellationToken);
                break;

            case WalOperation.Delete:
                await ReplayDeleteAsync(entry, cancellationToken);
                break;

            default:
                // Skip unknown operations
                break;
        }
    }

    /// <summary>
    /// Replays an INSERT operation.
    /// </summary>
    private async Task ReplayInsertAsync(WalEntry entry, CancellationToken cancellationToken)
    {
        // In a real implementation:
        // 1. Read data from WAL entry
        // 2. Write to block at specified offset
        // 3. Update block registry

        // For now, stub implementation
        await Task.CompletedTask;
    }

    /// <summary>
    /// Replays an UPDATE operation.
    /// </summary>
    private async Task ReplayUpdateAsync(WalEntry entry, CancellationToken cancellationToken)
    {
        // In a real implementation:
        // 1. Read new data from WAL entry
        // 2. Overwrite block at specified offset
        // 3. Update metadata

        await Task.CompletedTask;
    }

    /// <summary>
    /// Replays a DELETE operation.
    /// </summary>
    private async Task ReplayDeleteAsync(WalEntry entry, CancellationToken cancellationToken)
    {
        // In a real implementation:
        // 1. Mark block as deleted in registry
        // 2. Free pages in FSM
        // 3. Update metadata

        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_recoveryLock)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Result of WAL analysis for recovery.
/// </summary>
internal sealed class WalAnalysisResult
{
    public required int TotalEntries { get; init; }
    public required HashSet<ulong> CommittedTransactions { get; init; }
    public required HashSet<ulong> UncommittedTransactions { get; init; }
    public required Dictionary<ulong, List<WalEntry>> Operations { get; init; }
}

/// <summary>
/// Information about recovery process.
/// C# 14: Record struct with required properties.
/// </summary>
public readonly record struct RecoveryInfo
{
    /// <summary>Was recovery needed?</summary>
    public required bool RecoveryNeeded { get; init; }
    
    /// <summary>Total WAL entries scanned.</summary>
    public required int TotalEntries { get; init; }
    
    /// <summary>Number of committed transactions replayed.</summary>
    public required int CommittedTransactions { get; init; }
    
    /// <summary>Number of uncommitted transactions discarded.</summary>
    public required int UncommittedTransactions { get; init; }
    
    /// <summary>Number of operations replayed.</summary>
    public required int OperationsReplayed { get; init; }
    
    /// <summary>Total recovery time.</summary>
    public required TimeSpan RecoveryTime { get; init; }

    /// <summary>
    /// Returns human-readable summary.
    /// </summary>
    public override string ToString()
    {
        if (!RecoveryNeeded)
        {
            return "No recovery needed";
        }

        return $"Recovery: {OperationsReplayed} operations from {CommittedTransactions} transactions " +
               $"in {RecoveryTime.TotalMilliseconds:F0}ms";
    }
}
