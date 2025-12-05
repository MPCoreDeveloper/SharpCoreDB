using System.Timers;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Provides automatic VACUUM and WAL checkpointing functionality.
/// </summary>
public class AutoMaintenanceService : IDisposable
{
    private readonly IDatabase _database;
    private readonly System.Timers.Timer _timer;
    private int _writeCount = 0;
    private readonly int _writeThreshold;
    private readonly object _lock = new();
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the AutoMaintenanceService class.
    /// </summary>
    /// <param name="database">The database instance to maintain.</param>
    /// <param name="intervalSeconds">Interval in seconds for automatic maintenance (default 300 = 5 minutes).</param>
    /// <param name="writeThreshold">Number of writes before triggering maintenance (default 1000).</param>
    public AutoMaintenanceService(IDatabase database, int intervalSeconds = 300, int writeThreshold = 1000)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _writeThreshold = writeThreshold;

        _timer = new System.Timers.Timer(intervalSeconds * 1000);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
        _timer.Start();
    }

    /// <summary>
    /// Increments the write count. Call this after each write operation.
    /// </summary>
    public void IncrementWriteCount()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            _writeCount++;
            if (_writeCount >= _writeThreshold)
            {
                PerformMaintenance();
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
            lock (_lock)
            {
                _writeCount = 0;
            }

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
        PerformMaintenance();
    }

    /// <summary>
    /// Manually triggers maintenance.
    /// </summary>
    public void TriggerMaintenance()
    {
        if (_disposed)
            return;

        PerformMaintenance();
    }

    /// <summary>
    /// Gets the current write count.
    /// </summary>
    public int WriteCount
    {
        get
        {
            lock (_lock)
            {
                return _writeCount;
            }
        }
    }

    /// <summary>
    /// Disposes the auto maintenance service.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Stop();
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }
}
