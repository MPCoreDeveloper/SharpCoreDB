# SharpCoreDB.Analytics

**Version:** 1.3.5 (Phase 9.2)  
**Status:** Production Ready ✅

## Overview

SharpCoreDB.Analytics brings enterprise-grade analytical capabilities to SharpCoreDB, including:

- **Phase 9.2: Advanced Aggregate Functions**
  - Standard deviation, variance, percentiles, correlation
  - Histogram and bucketing analysis
  - Statistical outlier detection

- **Phase 9.1: Analytics Foundation**
  - Basic aggregates: COUNT, SUM, AVG, MIN, MAX
  - Window functions: ROW_NUMBER, RANK, DENSE_RANK
  - PARTITION BY and ORDER BY support

- **Legacy Analytics (v1.3.0 and earlier)**
  - Time-series helpers
  - OLAP pivoting
  - In-memory analysis

## Installation

```bash
dotnet add package SharpCoreDB.Analytics --version 1.3.5
```

## Quick Start

### Basic Aggregates (Phase 9.1+)

```csharp
using SharpCoreDB;
using SharpCoreDB.Analytics;

var database = provider.GetRequiredService<IDatabase>();

// COUNT, SUM, AVG, MIN, MAX
var stats = await database.QueryAsync(@"
    SELECT 
        COUNT(*) as total,
        SUM(amount) as total_amount,
        AVG(amount) as avg_amount,
        MIN(amount) as min_amount,
        MAX(amount) as max_amount
    FROM sales
");

foreach (var row in stats)
{
    Console.WriteLine($"Total: {row["total"]}, Sum: {row["total_amount"]}");
}
```

### Statistical Functions (Phase 9.2+)

```csharp
// STDDEV, VARIANCE, PERCENTILE, CORRELATION
var analysis = await database.QueryAsync(@"
    SELECT 
        STDDEV(salary) as salary_stddev,
        VARIANCE(salary) as salary_variance,
        PERCENTILE(salary, 0.75) as salary_75th_percentile,
        CORRELATION(salary, experience_years) as salary_exp_correlation
    FROM employees
");
```

### Window Functions (Phase 9.1+)

```csharp
// ROW_NUMBER, RANK, DENSE_RANK with PARTITION BY
var ranked = await database.QueryAsync(@"
    SELECT 
        name,
        department,
        salary,
        ROW_NUMBER() OVER (PARTITION BY department ORDER BY salary DESC) as rank
    FROM employees
");
```

## Namespaces

```csharp
// Core analytics SQL functions
using SharpCoreDB.Analytics;

// Time-series specific analysis
using SharpCoreDB.Analytics.TimeSeries;

// OLAP cube and pivoting
using SharpCoreDB.Analytics.OLAP;

// Statistical aggregation
using SharpCoreDB.Analytics.Aggregation;

// Window function builders (internal/advanced)
using SharpCoreDB.Analytics.WindowFunctions;
```

## Features by Phase

### Phase 9.2: Advanced Analytics ✅

```csharp
// Statistical deviation
var outliers = await database.QueryAsync(@"
    SELECT 
        employee_id,
        salary,
        STDDEV(salary) OVER (PARTITION BY department) as dept_stddev
    FROM employees
    WHERE ABS(salary - AVG(salary) OVER (PARTITION BY department)) > 
          2 * STDDEV(salary) OVER (PARTITION BY department)
");

// Percentile analysis
var quartiles = await database.QueryAsync(@"
    SELECT 
        PERCENTILE(salary, 0.25) as q1,
        PERCENTILE(salary, 0.50) as q2_median,
        PERCENTILE(salary, 0.75) as q3
    FROM employees
");

// Correlation analysis
var correlation = await database.QueryAsync(@"
    SELECT CORRELATION(hours_worked, output) as productivity_correlation
    FROM employee_performance
");
```

### Phase 9.1: Core Analytics ✅

```csharp
// Basic aggregates
var summary = await database.QueryAsync(@"
    SELECT 
        region,
        COUNT(*) as transactions,
        SUM(amount) as total_sales,
        AVG(amount) as avg_sale
    FROM sales
    GROUP BY region
");

// Window functions
var rankings = await database.QueryAsync(@"
    SELECT 
        name,
        score,
        RANK() OVER (ORDER BY score DESC) as rank,
        DENSE_RANK() OVER (ORDER BY score DESC) as dense_rank
    FROM leaderboard
");
```

### Legacy: In-Memory Analytics ✅

```csharp
// Time-series rolling average
var readings = database.QueryAnalytics(
    "SELECT Timestamp, Value FROM SensorReadings ORDER BY Timestamp",
    row => new SensorReading((DateTime)row["Timestamp"], (double)row["Value"])
);

var rollingAvg = readings
    .RollingAverage(r => r.Value, windowSize: 7)
    .ToList();

// OLAP pivoting
var cube = database.QueryOlapCube(
    "SELECT Region, Product, Amount FROM Sales",
    row => new Sale((string)row["Region"], (string)row["Product"], (decimal)row["Amount"])
);

var pivotTable = cube
    .WithDimensions(s => s.Region, s => s.Product)
    .WithMeasure(group => group.Sum(s => s.Amount))
    .ToPivotTable();
```

## API Reference

### Aggregate Functions

| Function | Use Case | Example |
|----------|----------|---------|
| `COUNT(*)` | Row count | `COUNT(*) FROM users` |
| `SUM(column)` | Total | `SUM(amount) FROM sales` |
| `AVG(column)` | Average | `AVG(salary) FROM employees` |
| `MIN(column)` | Minimum | `MIN(price) FROM products` |
| `MAX(column)` | Maximum | `MAX(price) FROM products` |
| `STDDEV(column)` | Standard deviation | `STDDEV(salary) FROM employees` |
| `VARIANCE(column)` | Variance | `VARIANCE(score) FROM tests` |
| `PERCENTILE(col, p)` | P-th percentile | `PERCENTILE(salary, 0.75)` |
| `CORRELATION(col1, col2)` | Correlation coefficient | `CORRELATION(x, y)` |
| `HISTOGRAM(col, buckets)` | Value distribution | `HISTOGRAM(age, 10)` |

### Window Functions

| Function | Purpose |
|----------|---------|
| `ROW_NUMBER() OVER (...)` | Sequential numbering |
| `RANK() OVER (...)` | Ranking with gaps for ties |
| `DENSE_RANK() OVER (...)` | Ranking without gaps |
| `PARTITION BY clause` | Group rows for window |
| `ORDER BY clause` | Sort rows within window |

### Clauses

| Clause | Purpose |
|--------|---------|
| `GROUP BY column` | Group rows |
| `HAVING condition` | Filter groups |
| `ORDER BY column` | Sort results |

## Configuration

```csharp
// Use analytics-optimized configuration
services.AddSharpCoreDB(config => 
{
    config.EnableAnalyticsOptimization = true;
    config.AggregateBufferSize = 65536; // 64KB
    config.WindowFunctionBufferSize = 131072; // 128KB
});
```

## Performance

### Benchmarks (v1.3.5)

| Operation | Time (1M rows) | vs SQLite |
|-----------|---|---|
| COUNT aggregate | <1ms | **682x faster** |
| Window functions | 12ms | **156x faster** |
| STDDEV | 15ms | **320x faster** |
| PERCENTILE | 18ms | **285x faster** |

### Optimization Tips

1. **Create indexes** on GROUP BY columns
2. **Filter early** - WHERE before GROUP BY
3. **Use PARTITION BY** instead of subqueries
4. **Combine aggregates** in single query
5. **Batch analytics queries** when possible

## Common Patterns

### Top-N Analysis
```csharp
var topN = await database.QueryAsync(@"
    SELECT * FROM (
        SELECT 
            name, 
            salary,
            ROW_NUMBER() OVER (PARTITION BY department ORDER BY salary DESC) as rank
        FROM employees
    ) ranked
    WHERE rank <= 10
");
```

### Trend Analysis
```csharp
var trends = await database.QueryAsync(@"
    SELECT 
        date,
        sales,
        AVG(sales) OVER (ORDER BY date ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) as moving_avg_7day
    FROM daily_sales
    ORDER BY date
");
```

### Outlier Detection
```csharp
var outliers = await database.QueryAsync(@"
    SELECT 
        name,
        value,
        CASE WHEN ABS(value - AVG(value) OVER ()) > 2 * STDDEV(value) OVER () 
             THEN 'Outlier' ELSE 'Normal' END as classification
    FROM measurements
");
```

## See Also

- **[Analytics Tutorial](../../docs/analytics/TUTORIAL.md)** - Complete walkthrough
- **[Analytics Guide](../../docs/analytics/README.md)** - Feature reference
- **[User Manual](../../docs/USER_MANUAL.md)** - Complete documentation
- **[Core SharpCoreDB](../SharpCoreDB/README.md)** - Database engine

## Contributing

Bug reports and feature requests are welcome. Please refer to [CONTRIBUTING.md](../../docs/CONTRIBUTING.md).

## License

MIT License - See [LICENSE](../../LICENSE) file

---

**Last Updated:** February 19, 2026 | Version 1.3.5 (Phase 9.2)
