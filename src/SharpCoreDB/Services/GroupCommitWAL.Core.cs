// <copyright file="GroupCommitWAL.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// GroupCommitWAL - Core infrastructure.
/// Contains fields, constructor, and initialization logic.
/// Part of the GroupCommitWAL partial class.
/// Modern C# 14 with target-typed new and enhanced patterns.
/// See also: GroupCommitWAL.Batching.cs, GroupCommitWAL.Diagnostics.cs
/// </summary>
public partial class GroupCommitWAL : IDisposable
{
    // Core fields
    private readonly string logPath;
    private readonly string instanceId;
    private readonly FileStream fileStream;
    private readonly DurabilityMode durabilityMode;
    private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
    private readonly Channel<PendingCommit> commitQueue;
    private readonly Task backgroundWorker;
    private readonly CancellationTokenSource cts;
    private bool disposed;  // ✅ C# 14: Removed unnecessary = false

    // Configuration
    private int currentBatchSize;  // Mutable for adaptive scaling
    private readonly TimeSpan maxBatchDelay;
    private readonly bool enableAdaptiveBatching;

    // Statistics
    private long totalCommits;  // ✅ C# 14: Removed unnecessary = 0
    private long totalBatches;
    private long totalBytesWritten;
    private long totalBatchAdjustments;
    private long operationsSinceLastAdjustment;

    /// <summary>
    /// Represents a pending commit request with its completion source.
    /// ✅ C# 14: Target-typed new for TaskCompletionSource.
    /// </summary>
    private sealed class PendingCommit
    {
        public WalRecord Record { get; set; }
        public TaskCompletionSource<bool> CompletionSource { get; set; } = new();  // ✅ C# 14: Target-typed new
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GroupCommitWAL"/> class.
    /// Creates instance-specific WAL file and starts background commit worker.
    /// ✅ C# 14: Target-typed new for UnboundedChannelOptions.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="durabilityMode">The durability mode (FullSync or Async).</param>
    /// <param name="maxBatchSize">Maximum number of commits to batch (default 100). If 0, uses ProcessorCount * 128.</param>
    /// <param name="maxBatchDelayMs">Maximum delay before flushing batch in milliseconds (default 10ms).</param>
    /// <param name="instanceId">Optional instance ID for unique WAL file. If null, generates a new GUID.</param>
    /// <param name="enableAdaptiveBatching">Enable adaptive batch size tuning based on queue depth (default true).</param>
    public GroupCommitWAL(
        string dbPath,
        DurabilityMode durabilityMode = DurabilityMode.FullSync,
        int maxBatchSize = 0,
        int maxBatchDelayMs = 10,
        string? instanceId = null,
        bool enableAdaptiveBatching = true)
    {
        // Generate unique instance ID if not provided (prevents file locking conflicts)
        this.instanceId = instanceId ?? Guid.NewGuid().ToString("N");
        
        // Create instance-specific WAL filename
        this.logPath = Path.Combine(dbPath, $"wal-{this.instanceId}.log");
        
        this.durabilityMode = durabilityMode;
        this.enableAdaptiveBatching = enableAdaptiveBatching;
        
        // Adaptive batch size: Use ProcessorCount-based calculation if maxBatchSize == 0
        int initialBatchSize = maxBatchSize > 0 
            ? maxBatchSize 
            : BufferConstants.GetRecommendedWalBatchSize();
        
        this.currentBatchSize = initialBatchSize;
        this.maxBatchDelay = TimeSpan.FromMilliseconds(maxBatchDelayMs);

        // Create file stream with appropriate options
        var options = FileOptions.Asynchronous;
        if (durabilityMode is DurabilityMode.FullSync)  // ✅ C# 14: is pattern
        {
            options |= FileOptions.WriteThrough; // OS-level write-through
        }

        this.fileStream = new FileStream(
            this.logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,  // Allow concurrent reads for recovery
            bufferSize: 64 * 1024, // 64KB buffer
            options);

        // ✅ C# 14: Target-typed new for UnboundedChannelOptions
        this.commitQueue = Channel.CreateUnbounded<PendingCommit>(new()
        {
            SingleReader = true, // Only background worker reads
            SingleWriter = false, // Multiple threads can enqueue
        });

        // Start background worker
        this.cts = new CancellationTokenSource();
        this.backgroundWorker = Task.Run(() => BackgroundCommitWorker(cts.Token), cts.Token);
    }

    /// <summary>
    /// Gets the instance ID of this WAL.
    /// </summary>
    public string InstanceId => instanceId;
}
