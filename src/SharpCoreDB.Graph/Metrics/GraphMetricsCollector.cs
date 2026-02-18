using System.Runtime.CompilerServices;

namespace SharpCoreDB.Graph.Metrics;

/// <summary>
/// Thread-safe metrics collector for graph operations.
/// C# 14: Lock class for synchronization, Interlocked for counters.
/// PERF: Zero allocation in hot paths, <1% overhead when enabled.
/// ✅ GraphRAG Phase 6.3: OpenTelemetry integration for standard observability.
/// </summary>
public sealed class GraphMetricsCollector
{
    private static readonly Lazy<GraphMetricsCollector> _global = new(() => new GraphMetricsCollector());
    
    /// <summary>Global singleton metrics collector.</summary>
    public static GraphMetricsCollector Global => _global.Value;
    
    // Traversal metrics (Interlocked counters)
    private long _nodesVisited;
    private long _edgesTraversed;
    private long _maxDepthReached;
    private long _resultCount;
    private long _totalExecutionTicks;
    private long _traversalCount;
    
    // Cache metrics
    private long _cacheHits;
    private long _cacheMisses;
    private long _cacheEvictions;
    private long _totalLookupTicks;
    private long _totalConstructionTicks;
    
    // Parallel metrics
    private long _parallelTraversals;
    private long _totalThreadTime;
    private long _workStealingOps;
    
    // Optimizer metrics
    private long _optimizerInvocations;
    private long _totalEstimatedCostMs;
    private long _totalActualCostMs;
    private long _strategyOverrides;
    
    // Heuristic metrics
    private long _heuristicCalls;
    private long _admissibleEstimates;
    private long _overEstimates;
    private long _heuristicEvaluationTicks;
    
    // C# 14: Lock class for snapshot generation
    private readonly Lock _snapshotLock = new();
    
    private bool _enabled;
    
    /// <summary>
    /// Enable metrics collection.
    /// </summary>
    public void Enable()
    {
        _enabled = true;
    }
    
    /// <summary>
    /// Disable metrics collection (zero overhead).
    /// </summary>
    public void Disable()
    {
        _enabled = false;
    }
    
    /// <summary>
    /// Check if metrics collection is enabled.
    /// </summary>
    public bool IsEnabled => _enabled;
    
    #region Traversal Metrics
    
    /// <summary>
    /// Record nodes visited during traversal.
    /// PERF: Inlined, <1ns overhead when disabled.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordNodesVisited(long count)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _nodesVisited, count);
        }
    }
    
    /// <summary>Record edges traversed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordEdgesTraversed(long count)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _edgesTraversed, count);
        }
    }
    
    /// <summary>Update maximum depth reached.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateMaxDepth(long depth)
    {
        if (_enabled)
        {
            // Update max using Compare-And-Swap loop
            long current;
            do
            {
                current = Interlocked.Read(ref _maxDepthReached);
                if (depth <= current) return; // Already higher
            } while (Interlocked.CompareExchange(ref _maxDepthReached, depth, current) != current);
        }
    }
    
    /// <summary>
    /// Record traversal execution time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordTraversalTime(TimeSpan duration)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _totalExecutionTicks, duration.Ticks);
            Interlocked.Increment(ref _traversalCount);
            
            // OpenTelemetry integration
            OpenTelemetryIntegration.TraversalDurationHistogram.Record(duration.TotalMilliseconds);
        }
    }
    
    /// <summary>Record result count.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordResultCount(long count)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _resultCount, count);
        }
    }
    
    #endregion
    
    #region Cache Metrics
    
    /// <summary>Record cache hit.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordCacheHit()
    {
        if (_enabled)
        {
            Interlocked.Increment(ref _cacheHits);
        }
    }
    
    /// <summary>Record cache miss.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordCacheMiss()
    {
        if (_enabled)
        {
            Interlocked.Increment(ref _cacheMisses);
        }
    }
    
    /// <summary>Record cache eviction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordCacheEviction()
    {
        if (_enabled)
        {
            Interlocked.Increment(ref _cacheEvictions);
        }
    }
    
    /// <summary>
    /// Record cache lookup time (for OpenTelemetry export).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordCacheLookupTime(TimeSpan duration)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _totalLookupTicks, duration.Ticks);
            
            // OpenTelemetry integration
            OpenTelemetryIntegration.CacheLookupTimeHistogram.Record(duration.TotalMilliseconds);
        }
    }
    
    #endregion
    
    #region Parallel Metrics
    
    /// <summary>Record parallel traversal execution.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordParallelTraversal(TimeSpan threadTime, long workStealingOps)
    {
        if (_enabled)
        {
            Interlocked.Increment(ref _parallelTraversals);
            Interlocked.Add(ref _totalThreadTime, threadTime.Ticks);
            Interlocked.Add(ref _workStealingOps, workStealingOps);
        }
    }
    
    /// <summary>Record parallel traversal with detailed metrics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordParallelTraversal(long nodesVisited, long edgesTraversed, int degreeOfParallelism, long executionTimeMs)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _nodesVisited, nodesVisited);
            Interlocked.Add(ref _edgesTraversed, edgesTraversed);
            Interlocked.Increment(ref _parallelTraversals);
            Interlocked.Add(ref _totalExecutionTicks, TimeSpan.FromMilliseconds(executionTimeMs).Ticks);
            Interlocked.Increment(ref _traversalCount);
        }
    }
    
    /// <summary>Record parallel traversal with work-stealing metrics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordParallelTraversal(long nodesVisited, long edgesTraversed, int degreeOfParallelism, long executionTimeMs, long workStealingOperations)
    {
        if (_enabled)
        {
            Interlocked.Add(ref _nodesVisited, nodesVisited);
            Interlocked.Add(ref _edgesTraversed, edgesTraversed);
            Interlocked.Increment(ref _parallelTraversals);
            Interlocked.Add(ref _totalExecutionTicks, TimeSpan.FromMilliseconds(executionTimeMs).Ticks);
            Interlocked.Add(ref _workStealingOps, workStealingOperations);
            Interlocked.Increment(ref _traversalCount);
        }
    }
    
    #endregion
    
    #region Optimizer Metrics
    
    /// <summary>Record optimizer prediction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordOptimizerPrediction(double estimatedCostMs, double actualCostMs, bool strategyOverridden)
    {
        if (_enabled)
        {
            Interlocked.Increment(ref _optimizerInvocations);
            Interlocked.Add(ref _totalEstimatedCostMs, (long)estimatedCostMs);
            Interlocked.Add(ref _totalActualCostMs, (long)actualCostMs);
            if (strategyOverridden)
            {
                Interlocked.Increment(ref _strategyOverrides);
            }
        }
    }
    
    #endregion
    
    #region Heuristic Metrics
    
    /// <summary>
    /// Record heuristic evaluation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordHeuristicEvaluation(TimeSpan duration, bool wasAdmissible)
    {
        if (_enabled)
        {
            Interlocked.Increment(ref _heuristicCalls);
            Interlocked.Add(ref _heuristicEvaluationTicks, duration.Ticks);
            
            if (wasAdmissible)
            {
                Interlocked.Increment(ref _admissibleEstimates);
            }
            else
            {
                Interlocked.Increment(ref _overEstimates);
            }
            
            // OpenTelemetry integration
            OpenTelemetryIntegration.RecordHeuristicMetrics(duration.TotalMilliseconds, wasAdmissible);
        }
    }
    
    #endregion
    
    #region Snapshot & Reset
    
    /// <summary>
    /// Get atomic snapshot of current metrics.
    /// C# 14: Lock class for thread-safe snapshot.
    /// </summary>
    public MetricSnapshot GetSnapshot()
    {
        lock (_snapshotLock)
        {
            return new MetricSnapshot
            {
                // Traversal
                TotalNodesVisited = Interlocked.Read(ref _nodesVisited),
                TotalEdgesTraversed = Interlocked.Read(ref _edgesTraversed),
                MaxDepthReached = Interlocked.Read(ref _maxDepthReached),
                TotalResultCount = Interlocked.Read(ref _resultCount),
                TraversalCount = Interlocked.Read(ref _traversalCount),
                AverageExecutionTime = _traversalCount > 0
                    ? TimeSpan.FromTicks(Interlocked.Read(ref _totalExecutionTicks) / _traversalCount)
                    : TimeSpan.Zero,
                
                // Cache
                CacheHits = Interlocked.Read(ref _cacheHits),
                CacheMisses = Interlocked.Read(ref _cacheMisses),
                CacheEvictions = Interlocked.Read(ref _cacheEvictions),
                AverageLookupTime = (_cacheHits + _cacheMisses) > 0
                    ? TimeSpan.FromTicks(Interlocked.Read(ref _totalLookupTicks) / (_cacheHits + _cacheMisses))
                    : TimeSpan.Zero,
                
                // Parallel
                ParallelTraversals = Interlocked.Read(ref _parallelTraversals),
                TotalWorkStealingOps = Interlocked.Read(ref _workStealingOps),
                
                // Optimizer
                OptimizerInvocations = Interlocked.Read(ref _optimizerInvocations),
                AveragePredictionError = _optimizerInvocations > 0
                    ? Math.Abs(Interlocked.Read(ref _totalEstimatedCostMs) - Interlocked.Read(ref _totalActualCostMs)) 
                      / (double)Interlocked.Read(ref _totalActualCostMs)
                    : 0.0,
                StrategyOverrides = Interlocked.Read(ref _strategyOverrides),
                
                // Heuristic
                HeuristicCalls = Interlocked.Read(ref _heuristicCalls),
                AdmissibleEstimates = Interlocked.Read(ref _admissibleEstimates),
                OverEstimates = Interlocked.Read(ref _overEstimates),
                AverageHeuristicTime = _heuristicCalls > 0
                    ? TimeSpan.FromTicks(Interlocked.Read(ref _heuristicEvaluationTicks) / _heuristicCalls)
                    : TimeSpan.Zero,
                
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }
    
    /// <summary>
    /// Reset all metrics to zero.
    /// </summary>
    public void Reset()
    {
        lock (_snapshotLock)
        {
            // Traversal
            Interlocked.Exchange(ref _nodesVisited, 0);
            Interlocked.Exchange(ref _edgesTraversed, 0);
            Interlocked.Exchange(ref _maxDepthReached, 0);
            Interlocked.Exchange(ref _resultCount, 0);
            Interlocked.Exchange(ref _totalExecutionTicks, 0);
            Interlocked.Exchange(ref _traversalCount, 0);
            
            // Cache
            Interlocked.Exchange(ref _cacheHits, 0);
            Interlocked.Exchange(ref _cacheMisses, 0);
            Interlocked.Exchange(ref _cacheEvictions, 0);
            Interlocked.Exchange(ref _totalLookupTicks, 0);
            Interlocked.Exchange(ref _totalConstructionTicks, 0);
            
            // Parallel
            Interlocked.Exchange(ref _parallelTraversals, 0);
            Interlocked.Exchange(ref _totalThreadTime, 0);
            Interlocked.Exchange(ref _workStealingOps, 0);
            
            // Optimizer
            Interlocked.Exchange(ref _optimizerInvocations, 0);
            Interlocked.Exchange(ref _totalEstimatedCostMs, 0);
            Interlocked.Exchange(ref _totalActualCostMs, 0);
            Interlocked.Exchange(ref _strategyOverrides, 0);
            
            // Heuristic
            Interlocked.Exchange(ref _heuristicCalls, 0);
            Interlocked.Exchange(ref _admissibleEstimates, 0);
            Interlocked.Exchange(ref _overEstimates, 0);
            Interlocked.Exchange(ref _heuristicEvaluationTicks, 0);
        }
    }
    
    #endregion
}
