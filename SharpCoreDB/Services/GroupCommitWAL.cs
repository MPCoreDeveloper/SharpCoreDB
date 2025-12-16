// <copyright file="GroupCommitWAL.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Append-only Write-Ahead Log with group commits for high throughput.
/// Uses a background worker thread to batch multiple pending commits into a single fsync operation.
/// Supports FullSync and Async durability modes.
/// Each instance uses a unique WAL file to avoid file locking conflicts.
/// 
/// REFACTORED TO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - GroupCommitWAL.Core.cs: Fields, constructor, initialization
/// - GroupCommitWAL.Batching.cs: CommitAsync, background worker, adaptive batching
/// - GroupCommitWAL.Diagnostics.cs: Statistics, recovery, utilities
/// - GroupCommitWAL.cs (this file): Dispose pattern
/// 
/// MODERN C# 14 FEATURES USED:
/// - ObjectDisposedException.ThrowIf: Modern disposal checking
/// - Target-typed new: new() for known types
/// - Enhanced pattern matching: is null, is not null, is pattern
/// - Range operator: [..8] instead of Substring(0, 8)
/// </summary>
public partial class GroupCommitWAL : IDisposable
{
    /// <summary>
    /// Disposes the WAL and stops the background worker.
    /// Cleans up the instance-specific WAL file.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for cleanup.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (disposing)
        {
            // Signal shutdown
            commitQueue.Writer.Complete();
            _ = cts.CancelAsync();  // Fire and forget for dispose

            // Wait for background worker to finish
            try
            {
                backgroundWorker.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore timeout
            }

            // Cleanup resources
            cts.Dispose();
            fileStream.Dispose();
        }
        
        // Delete instance-specific WAL file (it's been committed to main database)
        try
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
        catch
        {
            // Ignore deletion errors - file might be locked or already deleted
        }
    }

    /// <summary>
    /// Asynchronously disposes the WAL and stops the background worker.
    /// Cleans up the instance-specific WAL file.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        // Signal shutdown
        commitQueue.Writer.Complete();
        await cts.CancelAsync();

        // Wait for background worker to finish
        try
        {
            await backgroundWorker.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Ignore timeout
        }

        // Cleanup resources
        cts.Dispose();
        await fileStream.DisposeAsync();
        
        // Delete instance-specific WAL file (it's been committed to main database)
        try
        {
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
        catch
        {
            // Ignore deletion errors - file might be locked or already deleted
        }
    }
}
