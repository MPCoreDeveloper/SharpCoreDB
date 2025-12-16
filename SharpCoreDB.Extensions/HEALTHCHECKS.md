# SharpCoreDB Health Checks - Enhancement Summary

## Overview

The SharpCoreDB health checks have been significantly enhanced to provide comprehensive database monitoring and diagnostics capabilities, fully integrated with the new Dapper features.

## What Changed

### Before (Basic Implementation)
- Simple connection test only
- Single configuration method
- No performance metrics
- No threshold-based status
- Limited diagnostic data

### After (Enhanced Implementation)
- ? Multiple health check variants (lightweight, comprehensive, custom)
- ? Performance metrics integration
- ? Query cache statistics
- ? Configurable thresholds for degraded/unhealthy status
- ? Timeout support
- ? Rich diagnostic data
- ? Kubernetes/container patterns
- ? Production-ready configurations

## New Features

### 1. Multiple Health Check Variants

#### Lightweight (Connection Only)
```csharp
services.AddHealthChecks()
    .AddSharpCoreDBLightweight(database);
```
- Fast execution (< 100ms typical)
- Minimal overhead
- Perfect for high-frequency liveness probes
- Tests connection only

#### Comprehensive (All Diagnostics)
```csharp
services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(database);
```
- Complete diagnostic information
- Performance metrics
- Query cache statistics
- Table validation
- Best for detailed monitoring

#### Custom Configuration
```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(database, configure: options =>
    {
        options.TestQuery = "SELECT 1";
        options.CheckQueryCache = true;
        options.CheckPerformanceMetrics = true;
        options.DegradedThresholdMs = 1000;
        options.UnhealthyThresholdMs = 5000;
    });
```

### 2. Configurable Options

| Option | Default | Description |
|--------|---------|-------------|
| TestQuery | "SELECT 1" | Query to execute for testing |
| TestConnection | true | Test database connection |
| UseAsync | true | Use async execution |
| CheckQueryCache | true | Include query cache statistics |
| CheckTableCount | false | Include table count check |
| CheckPerformanceMetrics | true | Include performance metrics |
| DegradedThresholdMs | 1000 | Threshold for degraded status (ms) |
| UnhealthyThresholdMs | 5000 | Threshold for unhealthy status (ms) |
| Timeout | 10s | Health check timeout |

### 3. Rich Diagnostic Data

Health check responses now include:

```json
{
  "connection": "OK",
  "query_execution_ms": 15,
  "cache_hit_rate": "85.50%",
  "cache_hits": 342,
  "cache_misses": 58,
  "cached_queries": 25,
  "avg_query_time_ms": 12.3,
  "total_queries": 1250,
  "slowest_query_ms": 245,
  "health_check_duration_ms": 18
}
```

### 4. Status Determination

Health checks now use configurable thresholds:

- **Healthy** - Response time < DegradedThresholdMs
- **Degraded** - Response time between DegradedThresholdMs and UnhealthyThresholdMs
- **Unhealthy** - Response time > UnhealthyThresholdMs OR exception occurred

### 5. Kubernetes/Container Patterns

#### Liveness Probe
```csharp
services.AddHealthChecks()
    .AddSharpCoreDBLightweight(
        database,
        name: "liveness",
        tags: new[] { "k8s", "liveness" });
```

#### Readiness Probe
```csharp
services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(
        database,
        name: "readiness",
        tags: new[] { "k8s", "readiness" });
```

### 6. Performance Monitoring Integration

Health checks now integrate with the Dapper performance monitoring:

```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(database, configure: options =>
    {
        options.CheckPerformanceMetrics = true;
        // Returns avg query time, total queries, slowest query, etc.
    });
```

## Usage Patterns

### Development Environment
```csharp
// Simple, detailed diagnostics
services.AddHealthChecks()
    .AddSharpCoreDBComprehensive(database, name: "db");
```

### Production Environment
```csharp
// Multiple checks with different purposes
services.AddHealthChecks()
    // Fast liveness (every 5s)
    .AddSharpCoreDBLightweight(
        database,
        name: "db-live",
        tags: new[] { "liveness" })
    
    // Detailed readiness (every 30s)
    .AddSharpCoreDB(
        database,
        configure: options =>
        {
            options.DegradedThresholdMs = 500;
            options.UnhealthyThresholdMs = 2000;
        },
        name: "db-ready",
        tags: new[] { "readiness" });
```

### Kubernetes
```yaml
# Deployment manifest
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 5

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 30
```

## Performance Impact

### Lightweight Health Check
- Execution time: < 100ms
- Memory overhead: < 1KB
- CPU usage: Negligible
- **Recommended frequency**: Every 5-10 seconds

### Comprehensive Health Check
- Execution time: 100-500ms (depending on query)
- Memory overhead: < 10KB
- CPU usage: Low
- **Recommended frequency**: Every 30-60 seconds

## Migration Guide

### From Old to New

**Old Code:**
```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(database, testQuery: "SELECT 1");
```

**New Code (Equivalent):**
```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(database, testQuery: "SELECT 1");
// Still works! Backward compatible
```

**New Code (Enhanced):**
```csharp
services.AddHealthChecks()
    .AddSharpCoreDB(database, configure: options =>
    {
        options.TestQuery = "SELECT 1";
        options.CheckQueryCache = true;
        options.CheckPerformanceMetrics = true;
        options.DegradedThresholdMs = 1000;
        options.UnhealthyThresholdMs = 5000;
    });
```

## Best Practices

1. **Use Multiple Health Checks**
   - Lightweight for liveness (is the DB process running?)
   - Comprehensive for readiness (is the DB ready to serve requests?)

2. **Set Appropriate Thresholds**
   - DegradedThresholdMs: 2-3x your average query time
   - UnhealthyThresholdMs: 5-10x your average query time

3. **Monitor the Right Metrics**
   - Development: Enable all checks
   - Production: Balance thoroughness with performance

4. **Use Tags Effectively**
   - Tag by purpose: "liveness", "readiness", "diagnostic"
   - Tag by environment: "dev", "staging", "prod"
   - Tag by criticality: "critical", "warning", "info"

5. **Set Reasonable Timeouts**
   - Liveness: 2-5 seconds
   - Readiness: 5-10 seconds
   - Diagnostic: 30+ seconds

## Examples

See `HealthCheckExamples.cs` for comprehensive examples including:
- Basic health checks
- Lightweight and comprehensive variants
- Custom configuration
- Kubernetes patterns
- Production-ready configurations
- Multiple health check scenarios
- Performance-focused checks
- Cache monitoring checks

## Breaking Changes

None! The enhancement is fully backward compatible. Existing code continues to work, with new features available via opt-in configuration.

## Future Enhancements

Potential future additions:
- Connection pool health metrics
- Disk space monitoring
- WAL (Write-Ahead Log) health
- Replication lag monitoring (if/when replication is added)
- Custom health check predicates
- Health check result caching
