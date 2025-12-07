// <copyright file="AutoMaintenanceService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Timers;

/// <summary>
/// Provides automatic VACUUM and WAL checkpointing functionality.
/// </summary>
public class AutoMaintenanceService : IDisposable
{
    private readonly IDatabase database;
    private readonly System.Timers.Timer timer;
    private int writeCount = 0;
    private readonly int writeThreshold;
    private readonly object @lock = new();
    private bool disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoMaintenanceService"/> class.
    /// </summary>
    /// <param name="database">The database instance to maintain.</param>
    /// <param name="intervalSeconds">Interval in seconds for automatic maintenance (default 300 = 5 minutes).
    /// For read-heavy workloads, consider increasing this interval (e.g., 1800 = 30 minutes) to reduce checkpoint frequency.</param>
    /// <param name="writeThreshold">Number of writes before triggering maintenance (default 1000).</param>
    public AutoMaintenanceService(IDatabase database, int intervalSeconds = 300, int writeThreshold = 1000)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
        this.writeThreshold = writeThreshold;

        this.timer = new System.Timers.Timer(intervalSeconds * 1000);
        this.timer.Elapsed += this.OnTimerElapsed;
        this.timer.AutoReset = true;
        this.timer.Start();
    }

    /// <summary>
    /// Increments the write count. Call this after each write operation.
    /// </summary>
    public void IncrementWriteCount()
    {
        if (this.disposed)
        {
            return;
        }

        lock (this.@lock)
        {
            this.writeCount++;
            if (this.writeCount >= this.writeThreshold)
            {
                this.PerformMaintenance();
            }
        }
    }

    /// <summary>
    /// Performs maintenance tasks (VACUUM and WAL checkpoint).
    /// </summary>
    private void PerformMaintenance()
    {
        try
        {
            // In a production system, these would execute actual VACUUM and CHECKPOINT commands
            // For now, we'll log that maintenance was triggered
            Console.WriteLine($"[AutoMaintenance] Performing maintenance at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

            // Reset write count
            lock (this.@lock)
            {
                this.writeCount = 0;
            }

            // Clear query cache at WAL checkpoint to ensure consistency
            this.database.ClearQueryCache();

            // Note: Actual VACUUM and CHECKPOINT implementation would go here
            // This would typically involve:
            // 1. WAL checkpoint to flush changes to main database
            // 2. VACUUM to reclaim space from deleted records
            // 3. ANALYZE to update query optimizer statistics
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AutoMaintenance] Error during maintenance: {ex.Message}");
        }
    }

    /// <summary>
    /// Timer elapsed event handler.
    /// </summary>
    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        this.PerformMaintenance();
    }

    /// <summary>
    /// Manually triggers maintenance.
    /// </summary>
    public void TriggerMaintenance()
    {
        if (this.disposed)
        {
            return;
        }

        this.PerformMaintenance();
    }

    /// <summary>
    /// Gets the current write count.
    /// </summary>
    public int WriteCount
    {
        get
        {
            lock (this.@lock)
            {
                return this.writeCount;
            }
        }
    }

    /// <summary>
    /// Disposes the auto maintenance service.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.timer?.Stop();
        this.timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
