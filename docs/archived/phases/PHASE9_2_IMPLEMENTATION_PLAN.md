# 🚀 PHASE 9.2 IMPLEMENTATION PLAN: Advanced Aggregates

**Phase:** 9.2 — Advanced Aggregate Functions  
**Status:** 📅 **READY TO START**  
**Target Duration:** 3-5 days  
**Target Completion:** 2025-02-21  
**Assigned:** GitHub Copilot Agent  

---

## 🎯 Phase 9.2 Objectives

Implement **statistical and advanced aggregate functions** that complement the basic aggregates from Phase 9.1:

### Deliverables
1. ✅ 7 Advanced Aggregate Implementations
2. ✅ 24+ Comprehensive Test Cases
3. ✅ XML Documentation
4. ✅ Performance Validation
5. ✅ Integration with AggregateFactory

---

## 📋 Implementation Checklist

### Core Aggregates

#### 1. StandardDeviationAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/StatisticalAggregates.cs`
- [ ] Support both population and sample standard deviation
- [ ] Use Welford's online algorithm for numerical stability
- [ ] Formula: σ = √(Σ(xi - μ)² / N)
- **Tests:** 3 test cases
  - [ ] Population standard deviation
  - [ ] Sample standard deviation
  - [ ] Handle null values

#### 2. VarianceAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/StatisticalAggregates.cs`
- [ ] Support both population and sample variance
- [ ] Use same algorithm as StandardDeviation (without sqrt)
- [ ] Formula: σ² = Σ(xi - μ)² / N
- **Tests:** 3 test cases
  - [ ] Population variance
  - [ ] Sample variance
  - [ ] Single value edge case

#### 3. MedianAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/PercentileAggregates.cs`
- [ ] Collect all values (requires buffering)
- [ ] Use efficient sorting (Array.Sort)
- [ ] Handle even/odd count (average middle values if even)
- [ ] Formula: Middle value or avg of two middle values
- **Tests:** 4 test cases
  - [ ] Odd number of values
  - [ ] Even number of values
  - [ ] Single value
  - [ ] Null handling

#### 4. PercentileAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/PercentileAggregates.cs`
- [ ] Generic percentile calculation (P50, P90, P95, P99)
- [ ] Use linear interpolation between ranks
- [ ] Support custom percentile values (0.0 - 1.0)
- [ ] Formula: Interpolated value at rank = percentile * (count - 1)
- **Tests:** 5 test cases
  - [ ] P50 (median)
  - [ ] P95 (common SLA metric)
  - [ ] P99 (tail latency)
  - [ ] Boundary values (P0, P100)
  - [ ] Null handling

#### 5. ModeAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/FrequencyAggregates.cs`
- [ ] Track frequency of each value (Dictionary)
- [ ] Return most frequent value
- [ ] Handle ties (return first occurrence)
- [ ] Support multi-modal (future enhancement)
- **Tests:** 3 test cases
  - [ ] Single mode
  - [ ] Multiple values with clear mode
  - [ ] Null handling

#### 6. CorrelationAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/BivariatеAggregates.cs`
- [ ] Pearson correlation coefficient
- [ ] Requires two input series (x, y)
- [ ] Use online algorithm to avoid buffering
- [ ] Formula: r = Σ((xi - x̄)(yi - ȳ)) / √(Σ(xi - x̄)² * Σ(yi - ȳ)²)
- **Tests:** 3 test cases
  - [ ] Perfect positive correlation (r = 1)
  - [ ] Perfect negative correlation (r = -1)
  - [ ] No correlation (r ≈ 0)

#### 7. CovarianceAggregate
- [ ] **File:** `src/SharpCoreDB.Analytics/Aggregation/BivariateAggregates.cs`
- [ ] Covariance between two series
- [ ] Support population and sample covariance
- [ ] Use online algorithm
- [ ] Formula: Cov(X,Y) = Σ((xi - x̄)(yi - ȳ)) / N
- **Tests:** 3 test cases
  - [ ] Population covariance
  - [ ] Sample covariance
  - [ ] Null handling

---

## 🏗️ File Structure

```
src/SharpCoreDB.Analytics/Aggregation/
├── AggregateFunction.cs             (Existing - Phase 9.1)
├── StandardAggregates.cs            (Existing - Phase 9.1)
├── StatisticalAggregates.cs         ⬅️ NEW (StdDev, Variance)
├── PercentileAggregates.cs          ⬅️ NEW (Median, Percentile)
├── FrequencyAggregates.cs           ⬅️ NEW (Mode)
└── BivariateAggregates.cs           ⬅️ NEW (Correlation, Covariance)

tests/SharpCoreDB.Analytics.Tests/
├── AggregateTests.cs                (Existing - Phase 9.1)
├── StatisticalAggregateTests.cs     ⬅️ NEW
├── PercentileAggregateTests.cs      ⬅️ NEW
├── FrequencyAggregateTests.cs       ⬅️ NEW
└── BivariateAggregateTests.cs       ⬅️ NEW
```

---

## 🔧 Implementation Details

### 1. StatisticalAggregates.cs

```csharp
namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates standard deviation using Welford's online algorithm.
/// Supports both population and sample standard deviation.
/// C# 14: Uses primary constructor for immutable configuration.
/// </summary>
public sealed class StandardDeviationAggregate(bool isSample = true) : IAggregateFunction
{
    private int _count = 0;
    private double _mean = 0.0;
    private double _m2 = 0.0; // Sum of squared differences
    
    public string FunctionName => isSample ? "STDDEV_SAMP" : "STDDEV_POP";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        var numValue = Convert.ToDouble(value);
        _count++;
        
        // Welford's online algorithm
        var delta = numValue - _mean;
        _mean += delta / _count;
        var delta2 = numValue - _mean;
        _m2 += delta * delta2;
    }
    
    public object? GetResult()
    {
        if (_count == 0) return null;
        if (_count == 1 && isSample) return null; // Sample stddev undefined for n=1
        
        var divisor = isSample ? _count - 1 : _count;
        var variance = _m2 / divisor;
        return Math.Sqrt(variance);
    }
    
    public void Reset()
    {
        _count = 0;
        _mean = 0.0;
        _m2 = 0.0;
    }
}

/// <summary>
/// Calculates variance (standard deviation squared).
/// </summary>
public sealed class VarianceAggregate(bool isSample = true) : IAggregateFunction
{
    private int _count = 0;
    private double _mean = 0.0;
    private double _m2 = 0.0;
    
    public string FunctionName => isSample ? "VAR_SAMP" : "VAR_POP";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        var numValue = Convert.ToDouble(value);
        _count++;
        
        var delta = numValue - _mean;
        _mean += delta / _count;
        var delta2 = numValue - _mean;
        _m2 += delta * delta2;
    }
    
    public object? GetResult()
    {
        if (_count == 0) return null;
        if (_count == 1 && isSample) return null;
        
        var divisor = isSample ? _count - 1 : _count;
        return _m2 / divisor;
    }
    
    public void Reset()
    {
        _count = 0;
        _mean = 0.0;
        _m2 = 0.0;
    }
}
```

### 2. PercentileAggregates.cs

```csharp
namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates median (50th percentile).
/// Requires buffering all values.
/// </summary>
public sealed class MedianAggregate : IAggregateFunction
{
    private readonly List<double> _values = [];
    
    public string FunctionName => "MEDIAN";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        _values.Add(Convert.ToDouble(value));
    }
    
    public object? GetResult()
    {
        if (_values.Count == 0) return null;
        
        var sorted = _values.ToArray();
        Array.Sort(sorted);
        
        var mid = sorted.Length / 2;
        
        if (sorted.Length % 2 == 0)
        {
            // Even count: average of two middle values
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
        else
        {
            // Odd count: middle value
            return sorted[mid];
        }
    }
    
    public void Reset() => _values.Clear();
}

/// <summary>
/// Calculates arbitrary percentile (0.0 - 1.0).
/// Uses linear interpolation for accuracy.
/// </summary>
public sealed class PercentileAggregate(double percentile) : IAggregateFunction
{
    private readonly List<double> _values = [];
    
    public string FunctionName => $"PERCENTILE_{percentile * 100:F0}";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        _values.Add(Convert.ToDouble(value));
    }
    
    public object? GetResult()
    {
        if (_values.Count == 0) return null;
        
        var sorted = _values.ToArray();
        Array.Sort(sorted);
        
        // Calculate rank (0-based)
        var rank = percentile * (sorted.Length - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        
        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }
        
        // Linear interpolation
        var weight = rank - lowerIndex;
        return sorted[lowerIndex] * (1 - weight) + sorted[upperIndex] * weight;
    }
    
    public void Reset() => _values.Clear();
}
```

### 3. FrequencyAggregates.cs

```csharp
namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Finds the most frequent value (mode).
/// </summary>
public sealed class ModeAggregate : IAggregateFunction
{
    private readonly Dictionary<object, int> _frequencies = [];
    
    public string FunctionName => "MODE";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        if (_frequencies.ContainsKey(value))
            _frequencies[value]++;
        else
            _frequencies[value] = 1;
    }
    
    public object? GetResult()
    {
        if (_frequencies.Count == 0) return null;
        
        var maxFrequency = _frequencies.Values.Max();
        return _frequencies.First(kvp => kvp.Value == maxFrequency).Key;
    }
    
    public void Reset() => _frequencies.Clear();
}
```

### 4. BivariateAggregates.cs

```csharp
namespace SharpCoreDB.Analytics.Aggregation;

/// <summary>
/// Calculates Pearson correlation coefficient between two series.
/// Requires paired (x, y) values.
/// </summary>
public sealed class CorrelationAggregate : IAggregateFunction
{
    private int _count = 0;
    private double _sumX = 0.0, _sumY = 0.0;
    private double _sumXY = 0.0;
    private double _sumX2 = 0.0, _sumY2 = 0.0;
    
    public string FunctionName => "CORR";
    
    /// <summary>
    /// Aggregate a pair of values (x, y).
    /// Pass as Tuple<double, double> or array [x, y].
    /// </summary>
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        double x, y;
        
        if (value is Tuple<double, double> tuple)
        {
            x = tuple.Item1;
            y = tuple.Item2;
        }
        else if (value is double[] array && array.Length >= 2)
        {
            x = array[0];
            y = array[1];
        }
        else
        {
            throw new ArgumentException("Value must be Tuple<double,double> or double[2]");
        }
        
        _count++;
        _sumX += x;
        _sumY += y;
        _sumXY += x * y;
        _sumX2 += x * x;
        _sumY2 += y * y;
    }
    
    public object? GetResult()
    {
        if (_count == 0) return null;
        
        var numerator = _count * _sumXY - _sumX * _sumY;
        var denominator = Math.Sqrt(
            (_count * _sumX2 - _sumX * _sumX) * 
            (_count * _sumY2 - _sumY * _sumY)
        );
        
        if (denominator == 0) return null; // Undefined
        
        return numerator / denominator;
    }
    
    public void Reset()
    {
        _count = 0;
        _sumX = _sumY = _sumXY = _sumX2 = _sumY2 = 0.0;
    }
}

/// <summary>
/// Calculates covariance between two series.
/// </summary>
public sealed class CovarianceAggregate(bool isSample = true) : IAggregateFunction
{
    private int _count = 0;
    private double _meanX = 0.0, _meanY = 0.0;
    private double _cov = 0.0;
    
    public string FunctionName => isSample ? "COVAR_SAMP" : "COVAR_POP";
    
    public void Aggregate(object? value)
    {
        if (value is null) return;
        
        double x, y;
        
        if (value is Tuple<double, double> tuple)
        {
            x = tuple.Item1;
            y = tuple.Item2;
        }
        else if (value is double[] array && array.Length >= 2)
        {
            x = array[0];
            y = array[1];
        }
        else
        {
            throw new ArgumentException("Value must be Tuple<double,double> or double[2]");
        }
        
        _count++;
        
        var deltaX = x - _meanX;
        _meanX += deltaX / _count;
        var deltaY = y - _meanY;
        _meanY += deltaY / _count;
        
        _cov += deltaX * (y - _meanY);
    }
    
    public object? GetResult()
    {
        if (_count == 0) return null;
        if (_count == 1 && isSample) return null;
        
        var divisor = isSample ? _count - 1 : _count;
        return _cov / divisor;
    }
    
    public void Reset()
    {
        _count = 0;
        _meanX = _meanY = _cov = 0.0;
    }
}
```

---

## 🧪 Test Plan

### Test File Structure

Each aggregate gets its own test class with comprehensive coverage:

```csharp
namespace SharpCoreDB.Analytics.Tests;

public class StatisticalAggregateTests
{
    [Fact]
    public void StandardDeviation_Population_ShouldCalculateCorrectly()
    {
        // Arrange
        var stdDev = new StandardDeviationAggregate(isSample: false);
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
            stdDev.Aggregate(value);
        
        var result = (double?)stdDev.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2.0, result.Value, precision: 2);
    }
    
    [Fact]
    public void StandardDeviation_Sample_ShouldCalculateCorrectly()
    {
        // Arrange
        var stdDev = new StandardDeviationAggregate(isSample: true);
        var values = new[] { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        
        // Act
        foreach (var value in values)
            stdDev.Aggregate(value);
        
        var result = (double?)stdDev.GetResult();
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal(2.14, result.Value, precision: 2);
    }
    
    [Fact]
    public void StandardDeviation_WithNulls_ShouldIgnoreNulls()
    {
        // Arrange
        var stdDev = new StandardDeviationAggregate();
        
        // Act
        stdDev.Aggregate(10.0);
        stdDev.Aggregate(null);
        stdDev.Aggregate(20.0);
        stdDev.Aggregate(null);
        stdDev.Aggregate(30.0);
        
        var result = stdDev.GetResult();
        
        // Assert
        Assert.NotNull(result);
        // StdDev of [10, 20, 30] with sample correction
    }
}
```

---

## 📊 Success Criteria

### Must Have
- [ ] All 7 aggregates implemented
- [ ] 24+ tests passing (100% pass rate)
- [ ] Zero compiler warnings
- [ ] XML documentation on all public APIs
- [ ] Consistent API with Phase 9.1

### Performance Targets
- [ ] StandardDeviation/Variance: O(n) time, O(1) space
- [ ] Median/Percentile: O(n log n) time (sorting), O(n) space
- [ ] Mode: O(n) time, O(k) space (k = unique values)
- [ ] Correlation/Covariance: O(n) time, O(1) space

### Code Quality
- [ ] Follow C# 14 coding standards
- [ ] Use primary constructors where applicable
- [ ] Null safety enabled
- [ ] No allocations in hot paths (except buffering aggregates)

---

## 🚀 Implementation Order

### Day 1: Statistical Aggregates
1. Create `StatisticalAggregates.cs`
2. Implement `StandardDeviationAggregate`
3. Implement `VarianceAggregate`
4. Create `StatisticalAggregateTests.cs`
5. Write 6 tests (3 per aggregate)

### Day 2: Percentile Aggregates
1. Create `PercentileAggregates.cs`
2. Implement `MedianAggregate`
3. Implement `PercentileAggregate`
4. Create `PercentileAggregateTests.cs`
5. Write 9 tests (4 median + 5 percentile)

### Day 3: Frequency & Bivariate
1. Create `FrequencyAggregates.cs`
2. Implement `ModeAggregate`
3. Create `BivariateAggregates.cs`
4. Implement `CorrelationAggregate`
5. Implement `CovarianceAggregate`
6. Create test files
7. Write 9 tests (3 per aggregate)

### Day 4: Integration & Polish
1. Update `AggregateFactory` to include new aggregates
2. Add factory tests
3. Performance validation
4. Documentation review
5. Final testing

---

## 🔗 Integration Points

### AggregateFactory Updates

```csharp
public static class AggregateFactory
{
    public static IAggregateFunction Create(string functionName) => functionName.ToUpperInvariant() switch
    {
        // Phase 9.1 (existing)
        "SUM" => new SumAggregate(),
        "COUNT" => new CountAggregate(),
        "AVG" or "AVERAGE" => new AverageAggregate(),
        "MIN" => new MinAggregate(),
        "MAX" => new MaxAggregate(),
        
        // Phase 9.2 (new)
        "STDDEV" or "STDDEV_SAMP" => new StandardDeviationAggregate(isSample: true),
        "STDDEV_POP" => new StandardDeviationAggregate(isSample: false),
        "VAR" or "VAR_SAMP" or "VARIANCE" => new VarianceAggregate(isSample: true),
        "VAR_POP" => new VarianceAggregate(isSample: false),
        "MEDIAN" => new MedianAggregate(),
        "MODE" => new ModeAggregate(),
        "CORR" or "CORRELATION" => new CorrelationAggregate(),
        "COVAR" or "COVAR_SAMP" => new CovarianceAggregate(isSample: true),
        "COVAR_POP" => new CovarianceAggregate(isSample: false),
        
        _ => throw new ArgumentException($"Unknown aggregate function: {functionName}")
    };
    
    public static IAggregateFunction CreatePercentile(double percentile)
        => new PercentileAggregate(percentile);
}
```

---

## 📝 Documentation Requirements

### Each Aggregate Needs:
- [ ] Class-level XML summary
- [ ] Parameter descriptions (constructors)
- [ ] Return value descriptions
- [ ] Example usage
- [ ] Performance characteristics (time/space complexity)
- [ ] Thread-safety notes

### Example:
```csharp
/// <summary>
/// Calculates the 50th percentile (median) of a dataset.
/// Requires buffering all values in memory.
/// </summary>
/// <remarks>
/// <para>Time Complexity: O(n log n) due to sorting</para>
/// <para>Space Complexity: O(n) - stores all values</para>
/// <para>Thread Safety: Not thread-safe. Use separate instances per thread.</para>
/// </remarks>
/// <example>
/// <code>
/// var median = new MedianAggregate();
/// median.Aggregate(10);
/// median.Aggregate(20);
/// median.Aggregate(30);
/// var result = median.GetResult(); // 20
/// </code>
/// </example>
public sealed class MedianAggregate : IAggregateFunction
```

---

## ⚠️ Known Challenges

### 1. Memory for Percentile Aggregates
- **Issue:** Median/Percentile require buffering all values
- **Solution:** Accept the O(n) space requirement; document clearly
- **Future:** Consider approximate algorithms (T-Digest, Q-Digest) in Phase 9.7

### 2. Numerical Stability
- **Issue:** Variance calculation can lose precision
- **Solution:** Use Welford's algorithm (proven numerically stable)
- **Reference:** https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance

### 3. Bivariate Input Format
- **Issue:** Correlation/Covariance need paired values
- **Solution:** Accept Tuple<double,double> or double[2]
- **Future:** May need custom API for LINQ integration

### 4. Mode Ties
- **Issue:** Multiple values may have same max frequency
- **Solution:** Return first occurrence; document behavior
- **Future:** Support multi-modal results in Phase 9.7

---

## 🎯 Next Steps After Phase 9.2

1. **Update Factory:** Add all new aggregates to `AggregateFactory`
2. **Update Progress:** Mark Phase 9.2 as complete
3. **Start Phase 9.4:** Time-Series Analytics (skip 9.3 - already done)
4. **Documentation:** Update main README with examples

---

**Status:** 📅 Ready to implement  
**Estimated Start:** Immediately after kickoff approval  
**Target Completion:** 2025-02-21  
**Blocked By:** None  
**Dependencies:** Phase 9.1 (complete ✅)
