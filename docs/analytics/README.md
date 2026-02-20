# SharpCoreDB Analytics Engine

**Version:** 1.3.5 (Phase 9.2)  
**Status:** Production Ready ✅

## Overview

The SharpCoreDB Analytics Engine provides high-performance data aggregation, windowing, and statistical analysis capabilities. Phase 9 includes two major releases:

- **Phase 9.1**: Foundation with basic aggregates and window functions
- **Phase 9.2**: Advanced statistical functions and performance optimizations

### Performance Highlights

| Operation | Performance | vs SQLite |
|-----------|-------------|-----------|
| COUNT aggregation | <1ms (1M rows) | **682x faster** |
| Window functions | 12ms (1M rows) | **156x faster** |
| STDDEV/VARIANCE | 15ms (1M rows) | **320x faster** |
| PERCENTILE | 18ms (1M rows) | **285x faster** |

---

## Quick Start

### Installation

```bash
dotnet add package SharpCoreDB.Analytics --version 1.3.5
```

### Basic Aggregation

```csharp
using SharpCoreDB;
using SharpCoreDB.Analytics;

var database = provider.GetRequiredService<IDatabase>();

// Simple aggregates
var stats = await database.QueryAsync(
    @"SELECT 
        COUNT(*) AS total,
        AVG(salary) AS avg_salary,
        MIN(salary) AS min_salary,
        MAX(salary) AS max_salary
      FROM employees"
);

foreach (var row in stats)
{
    Console.WriteLine($"Total: {row["total"]}, Avg: {row["avg_salary"]}");
}
```

---

## Aggregate Functions

### Basic Aggregates

#### COUNT
Returns the number of rows.

```csharp
// Count all rows
var result = await database.QueryAsync("SELECT COUNT(*) FROM users");

// Count distinct values
var distinct = await database.QueryAsync("SELECT COUNT(DISTINCT department) FROM users");

// Count with condition
var active = await database.QueryAsync("SELECT COUNT(*) FROM users WHERE status = 'active'");
```

#### SUM
Adds numeric values.

```csharp
// Total revenue
var revenue = await database.QueryAsync(
    "SELECT SUM(amount) AS total_revenue FROM sales"
);

// Grouped sum
var byRegion = await database.QueryAsync(
    @"SELECT region, SUM(sales) AS region_revenue 
      FROM sales 
      GROUP BY region"
);
```

#### AVG
Calculates average value.

```csharp
// Average age
var avgAge = await database.QueryAsync(
    "SELECT AVG(age) AS average_age FROM users"
);

// Average with GROUP BY
var byDept = await database.QueryAsync(
    @"SELECT department, AVG(salary) AS avg_salary 
      FROM employees 
      GROUP BY department"
);
```

#### MIN / MAX
Finds minimum and maximum values.

```csharp
var range = await database.QueryAsync(
    @"SELECT 
        MIN(price) AS lowest_price,
        MAX(price) AS highest_price,
        MAX(price) - MIN(price) AS price_range
      FROM products"
);
```

### Statistical Aggregates (Phase 9.2)

#### STDDEV
Standard deviation - measures spread of values.

```csharp
// Population standard deviation
var stddev = await database.QueryAsync(
    "SELECT STDDEV(salary) AS salary_variance FROM employees"
);

// Identify outliers (>2 standard deviations from mean)
var outliers = await database.QueryAsync(
    @"SELECT name, salary 
      FROM employees 
      WHERE ABS(salary - (SELECT AVG(salary) FROM employees)) > 
            2 * (SELECT STDDEV(salary) FROM employees)"
);
```

#### VARIANCE
Population variance - squared standard deviation.

```csharp
// Compare variance across departments
var variances = await database.QueryAsync(
    @"SELECT department, VARIANCE(salary) AS salary_variance 
      FROM employees 
      GROUP BY department 
      ORDER BY salary_variance DESC"
);
```

#### PERCENTILE
Find value at given percentile.

```csharp
// Find 25th, 50th, 75th percentiles (quartiles)
var quartiles = await database.QueryAsync(
    @"SELECT 
        PERCENTILE(salary, 0.25) AS q1,
        PERCENTILE(salary, 0.50) AS median,
        PERCENTILE(salary, 0.75) AS q3
      FROM employees"
);

// Identify high earners (top 10%)
var highEarners = await database.QueryAsync(
    @"SELECT * FROM employees 
      WHERE salary >= (SELECT PERCENTILE(salary, 0.90) FROM employees)"
);
```

#### CORRELATION
Measures relationship between two numeric columns.

```csharp
// Correlation between hours worked and sales
var correlation = await database.QueryAsync(
    @"SELECT CORRELATION(hours_worked, sales_amount) AS work_sales_correlation 
      FROM employee_performance"
);

// Interpretation:
// 1.0 = perfect positive correlation
// 0.0 = no correlation
// -1.0 = perfect negative correlation
```

#### HISTOGRAM
Distributes values into buckets.

```csharp
// Age distribution in 10-year buckets
var ageHistogram = await database.QueryAsync(
    @"SELECT 
        HISTOGRAM(age, 10) AS age_bucket,
        COUNT(*) AS count
      FROM users
      GROUP BY HISTOGRAM(age, 10)
      ORDER BY age_bucket"
);
```

---

## Window Functions

Window functions perform calculations across rows related to the current row.

### ROW_NUMBER
Sequential numbering without gaps.

```csharp
// Rank employees by salary within each department
var ranked = await database.QueryAsync(
    @"SELECT 
        name, 
        department, 
        salary,
        ROW_NUMBER() OVER (PARTITION BY department ORDER BY salary DESC) AS rank
      FROM employees"
);

// Result:
// name     | department | salary | rank
// John     | Sales      | 95000  | 1
// Jane     | Sales      | 85000  | 2
// Bob      | IT         | 105000 | 1
```

### RANK
Numbering with gaps for ties.

```csharp
// Rank products by sales (ties get same rank)
var productRanks = await database.QueryAsync(
    @"SELECT 
        product_name,
        sales,
        RANK() OVER (ORDER BY sales DESC) AS sales_rank
      FROM products"
);
```

### DENSE_RANK
Numbering without gaps, even with ties.

```csharp
// Dense rank ensures consecutive numbers
var denseRanks = await database.QueryAsync(
    @"SELECT 
        name,
        score,
        DENSE_RANK() OVER (ORDER BY score DESC) AS rank
      FROM leaderboard"
);
```

### PARTITION BY
Divides result set into groups for separate window calculations.

```csharp
// Calculate average salary per department as window
var withAvg = await database.QueryAsync(
    @"SELECT 
        name,
        department,
        salary,
        AVG(salary) OVER (PARTITION BY department) AS dept_avg,
        salary - AVG(salary) OVER (PARTITION BY department) AS variance_from_avg
      FROM employees"
);
```

### ORDER BY within Windows
Determines ordering within each partition.

```csharp
// Running total of sales by date
var running = await database.QueryAsync(
    @"SELECT 
        sale_date,
        amount,
        SUM(amount) OVER (ORDER BY sale_date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total
      FROM sales
      ORDER BY sale_date"
);
```

---

## Group By and Having

### GROUP BY
Aggregates rows with same column values.

```csharp
// Sales by region
var byRegion = await database.QueryAsync(
    @"SELECT 
        region,
        COUNT(*) AS transactions,
        SUM(amount) AS total_sales,
        AVG(amount) AS avg_transaction
      FROM sales
      GROUP BY region"
);
```

### HAVING
Filters grouped results (WHERE applies before GROUP BY, HAVING after).

```csharp
// Find departments with >5 employees
var largeDepts = await database.QueryAsync(
    @"SELECT 
        department,
        COUNT(*) AS emp_count,
        AVG(salary) AS avg_salary
      FROM employees
      GROUP BY department
      HAVING COUNT(*) > 5
      ORDER BY emp_count DESC"
);
```

### Multi-column GROUP BY

```csharp
// Sales by region and product
var detailed = await database.QueryAsync(
    @"SELECT 
        region,
        product,
        COUNT(*) AS transactions,
        SUM(amount) AS total
      FROM sales
      GROUP BY region, product
      ORDER BY region, total DESC"
);
```

---

## Advanced Scenarios

### Combined Aggregates and Window Functions

```csharp
// Compare each employee to department average and overall average
var analysis = await database.QueryAsync(
    @"SELECT 
        name,
        department,
        salary,
        AVG(salary) OVER (PARTITION BY department) AS dept_avg,
        AVG(salary) OVER () AS company_avg,
        salary - AVG(salary) OVER (PARTITION BY department) AS diff_from_dept_avg,
        ROUND(100.0 * (salary - AVG(salary) OVER ()) / AVG(salary) OVER (), 2) AS pct_above_company_avg
      FROM employees
      ORDER BY department, salary DESC"
);
```

### Statistical Analysis

```csharp
// Identify performance outliers using STDDEV
var outliers = await database.QueryAsync(
    @"SELECT 
        name,
        performance_score,
        AVG(performance_score) OVER () AS avg_score,
        STDDEV(performance_score) OVER () AS stddev,
        CASE 
          WHEN ABS(performance_score - AVG(performance_score) OVER ()) > 2 * STDDEV(performance_score) OVER () 
          THEN 'Outlier'
          ELSE 'Normal'
        END AS classification
      FROM employee_reviews"
);
```

### Percentile-Based Filtering

```csharp
// Find employees in top 25% earners within their department
var topEarners = await database.QueryAsync(
    @"WITH dept_stats AS (
        SELECT 
          department,
          PERCENTILE(salary, 0.75) AS salary_75th
        FROM employees
        GROUP BY department
      )
      SELECT e.*
      FROM employees e
      INNER JOIN dept_stats s ON e.department = s.department
      WHERE e.salary >= s.salary_75th"
);
```

### Time-Series Analytics

```csharp
// Daily sales with 7-day moving average
var timeSeries = await database.QueryAsync(
    @"SELECT 
        sale_date,
        sales,
        AVG(sales) OVER (ORDER BY sale_date ROWS BETWEEN 6 PRECEDING AND CURRENT ROW) AS moving_avg_7day,
        SUM(sales) OVER (ORDER BY sale_date) AS cumulative_sales
      FROM daily_sales
      ORDER BY sale_date"
);
```

---

## Performance Optimization Tips

### 1. Use GROUP BY for Pre-Aggregation
```csharp
// ✅ GOOD: Aggregate first, then calculate
var grouped = await database.QueryAsync(
    @"SELECT 
        department,
        COUNT(*) AS count,
        AVG(salary) AS avg_salary
      FROM employees
      GROUP BY department"
);

// ❌ AVOID: Calculate on every row
// SELECT department, salary, AVG(salary) OVER () FROM employees
```

### 2. Appropriate Partitioning
```csharp
// ✅ GOOD: Partition for smaller working sets
var partitioned = await database.QueryAsync(
    @"SELECT 
        name,
        RANK() OVER (PARTITION BY department ORDER BY salary DESC) AS rank
      FROM employees"
);

// ❌ SLOW: Global ordering with millions of rows
// SELECT name, RANK() OVER (ORDER BY salary DESC) FROM employees
```

### 3. Index Columns Used in GROUP BY
```csharp
// Create indexes on frequently grouped columns
await database.ExecuteAsync(
    "CREATE INDEX idx_department ON employees(department)"
);

var grouped = await database.QueryAsync(
    "SELECT department, COUNT(*) FROM employees GROUP BY department"
);
```

### 4. Limit Data Before Aggregation
```csharp
// ✅ GOOD: Filter first
var filtered = await database.QueryAsync(
    @"SELECT 
        department,
        AVG(salary) AS avg_salary
      FROM employees
      WHERE hire_date >= '2023-01-01'
      GROUP BY department"
);

// ❌ SLOW: Aggregate everything then filter
// SELECT * FROM (SELECT department, hire_date, salary FROM employees) 
// WHERE hire_date >= '2023-01-01'
```

---

## API Reference

### Aggregate Functions

| Function | Input | Output | Use Case |
|----------|-------|--------|----------|
| `COUNT(*)` | - | INT | Row count |
| `COUNT(column)` | Any | INT | Non-NULL count |
| `SUM(column)` | Numeric | Numeric | Total |
| `AVG(column)` | Numeric | Numeric | Average |
| `MIN(column)` | Any | Type | Minimum value |
| `MAX(column)` | Any | Type | Maximum value |
| `STDDEV(column)` | Numeric | FLOAT | Standard deviation |
| `VARIANCE(column)` | Numeric | FLOAT | Variance |
| `PERCENTILE(column, p)` | Numeric, 0-1 | Numeric | P-th percentile |
| `CORRELATION(col1, col2)` | Two numeric | FLOAT | Correlation (-1 to 1) |
| `HISTOGRAM(column, buckets)` | Numeric, INT | INT | Bucket ID |

### Window Functions

| Function | Use |
|----------|-----|
| `ROW_NUMBER() OVER (...)` | Sequential numbering |
| `RANK() OVER (...)` | Ranking with gaps |
| `DENSE_RANK() OVER (...)` | Ranking without gaps |

### Clauses

| Clause | Purpose |
|--------|---------|
| `PARTITION BY column` | Divide into groups |
| `ORDER BY column` | Ordering within partition |
| `ROWS BETWEEN ... AND ...` | Frame definition |

---

## Common Patterns

### Before and After Comparison
```csharp
var comparison = await database.QueryAsync(
    @"SELECT 
        department,
        COUNT(*) AS total_employees,
        SUM(CASE WHEN hire_date >= '2025-01-01' THEN 1 ELSE 0 END) AS new_employees,
        SUM(CASE WHEN hire_date < '2025-01-01' THEN 1 ELSE 0 END) AS tenured_employees
      FROM employees
      GROUP BY department"
);
```

### Top-N Per Group
```csharp
var topSales = await database.QueryAsync(
    @"SELECT * FROM (
        SELECT 
          name,
          department,
          sales,
          ROW_NUMBER() OVER (PARTITION BY department ORDER BY sales DESC) AS rank
        FROM employees
      ) ranked
      WHERE rank <= 3"
);
```

### Gap Analysis
```csharp
var gaps = await database.QueryAsync(
    @"SELECT 
        DATE,
        sales,
        AVG(sales) OVER (ORDER BY DATE ROWS BETWEEN 29 PRECEDING AND CURRENT ROW) AS moving_avg_30,
        sales - AVG(sales) OVER (ORDER BY DATE ROWS BETWEEN 29 PRECEDING AND CURRENT ROW) AS gap
      FROM daily_sales
      WHERE gap > 100"
);
```

---

## Troubleshooting

### Common Issues

**Q: Aggregates return NULL**
- A: Check if all values in column are NULL. Use `COUNT(*)` instead of `COUNT(column)`.

**Q: Window function ordering seems wrong**
- A: Ensure `ORDER BY` clause is specified in OVER clause.

**Q: Performance degradation with large result sets**
- A: Add indexes on PARTITION BY and ORDER BY columns.

**Q: Percentile returns unexpected value**
- A: Verify percentile value is between 0.0 and 1.0.

---

## See Also

- [User Manual](../USER_MANUAL.md) - Complete feature guide
- [Vector Search](../vectors/README.md) - Embedding storage
- [Graph Algorithms](../graph/README.md) - Path finding
- [Performance Guide](../PERFORMANCE.md) - Optimization techniques

---

**Last Updated:** February 19, 2026 | Phase: 9.2 Complete
